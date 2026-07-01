using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1200)]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EntityHealth))]
[RequireComponent(typeof(EnemyStatProvider))]
public class EnemyAI : MonoBehaviour
{
    [Header("Position On Spline")]
    [Tooltip("How many meters ahead of the player this enemy should stay on the spline.")]
    [Min(0f)] [SerializeField] private float desiredDistanceAhead = 120f;
    [Tooltip("Hard guard: the player is not allowed to pass closer than this arc distance.")]
    [Min(0f)] [SerializeField] private float minimumDistanceAhead = 35f;
    [Min(0f)] [SerializeField] private float arrivalTolerance = 3f;
    [SerializeField] private bool preserveSpawnOffset = true;
    [SerializeField] private float maxPreservedLateralOffset = 18f;
    [SerializeField] private float maxPreservedVerticalOffset = 10f;

    [Header("Automatic Separation")]
    [Tooltip("Minimum preferred distance between enemies in the same cross-section of the spline.")]
    [Min(0f)] [SerializeField] private float spacingBetweenEnemies = 10f;
    [Tooltip("Arc distance in which nearby enemies push each other to different lanes.")]
    [Min(0f)] [SerializeField] private float avoidanceArcWindow = 35f;
    [Tooltip("How quickly the enemy moves sideways/upwards to avoid another enemy.")]
    [Min(0f)] [SerializeField] private float offsetMoveSpeed = 12f;

    [Header("Combat Strafe")]
    [SerializeField] private bool followPlayerOffset = true;
    [Range(0f, 1f)] [SerializeField] private float playerOffsetInfluence = 0.7f;
    [Min(0f)] [SerializeField] private float lateralStrafeAmplitude = 8f;
    [Min(0f)] [SerializeField] private float verticalStrafeAmplitude = 0f;
    [Tooltip("Full left-right cycles per second.")]
    [Min(0f)] [SerializeField] private float strafeFrequency = 0.18f;

    [Header("Drift")]
    [SerializeField] private float lateralDriftAmplitude = 0f;
    [SerializeField] private float verticalDriftAmplitude = 0f;
    [Min(0f)] [SerializeField] private float driftFrequency = 0.45f;

    [Header("Speed")]
    [Tooltip("Minimum enemy spline speed, used before the player/level speed is available.")]
    [Min(0f)] [SerializeField] private float minimumMoveSpeed = 40f;
    [Tooltip("Enemy speed is at least player/level speed multiplied by this value.")]
    [Min(1f)] [SerializeField] private float speedMultiplier = 1.1f;
    [Tooltip("Flat speed added on top of player/level speed, so the enemy can catch up as the level accelerates.")]
    [Min(0f)] [SerializeField] private float speedAdvantage = 15f;
    [Tooltip("Lower values make the enemy recover its protected distance faster after the player boosts.")]
    [Min(0.05f)] [SerializeField] private float catchUpResponseTime = 0.35f;

    [Header("Rotation")]
    [SerializeField] private bool lookAtPlayer = true;
    [Min(0f)] [SerializeField] private float rotationLerpSpeed = 12f;

    [Header("Physics Cost")]
    [Tooltip("Use Rigidbody.MovePosition/MoveRotation. Keep off when enemies only need raycast-hit colliders, not physical contact response.")]
    [SerializeField] private bool usePhysicsMovePosition = false;

    private SplineGenerator _splineGenerator;
    private PathMath _pathMath;
    private Transform _target;
    private IFlightVehicle _playerVehicle;

    private Rigidbody _rb;
    private EntityHealth _health;
    private EnemyStatProvider _stats;
    private float _currentArc;
    private Vector2 _baseOffset;
    private Vector2 _currentOffset;
    private float _lastMoveSign = 1f;
    private bool _hasSplinePosition;
    private bool _started;
    private int _spawnOrder;
    private float _driftPhase;
    private float _nextReferenceResolveTime;
    private Vector3 _lookTarget;
    private float _lookTargetUntilTime;
    private bool _hasLookTarget;
    private Transform _aimForwardReference;

    private static int _nextSpawnOrder;
    private static int _targetArcCacheFrame = -1;
    private static bool _targetArcCacheValid;
    private static float _targetArcCacheValue;
    private static Transform _targetArcCacheTarget;
    private static SplineGenerator _targetArcCacheSplineGenerator;
    private static PathMath _targetArcCachePathMath;
    private static readonly List<EnemyAI> ActiveEnemies = new List<EnemyAI>();

    public EntityHealth Health => _health;
    public EnemyStatProvider Stats => _stats;
    public float CurrentArc => _currentArc;
    public static int ActiveCount
    {
        get
        {
            PruneActiveEnemies();
            return ActiveEnemies.Count;
        }
    }

    public static bool CanSpawnMore(int maxActiveEnemies)
    {
        return maxActiveEnemies <= 0 || ActiveCount < maxActiveEnemies;
    }

    public void SetLookTarget(Vector3 worldTarget, float holdSeconds = 0.2f)
    {
        if (float.IsNaN(worldTarget.x) || float.IsNaN(worldTarget.y) || float.IsNaN(worldTarget.z)) return;

        _lookTarget = worldTarget;
        _lookTargetUntilTime = Time.time + Mathf.Max(0.02f, holdSeconds);
        _hasLookTarget = true;
    }

    public void FaceTargetNow(Vector3 worldTarget)
    {
        SetLookTarget(worldTarget);
        Transform aimReference = ResolveAimForwardReference();
        Vector3 origin = aimReference != null ? aimReference.position : transform.position;
        Vector3 direction = worldTarget - origin;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = ResolveAimedRootRotation(direction.normalized);

        if (_rb != null) _rb.rotation = targetRotation;
        transform.rotation = targetRotation;
    }

    private Transform ResolveAimForwardReference()
    {
        if (_aimForwardReference != null) return _aimForwardReference;

        FirePointMarker[] markers = GetComponentsInChildren<FirePointMarker>(true);
        FirePointMarker best = null;

        for (int i = 0; i < markers.Length; i++)
        {
            FirePointMarker marker = markers[i];
            if (marker == null || (marker.roles & FirePointRole.Primary) == 0) continue;

            bool better = best == null ||
                          marker.IsActiveAtLevel(1) && !best.IsActiveAtLevel(1) ||
                          marker.IsActiveAtLevel(1) == best.IsActiveAtLevel(1) && marker.order < best.order;

            if (better) best = marker;
        }

        _aimForwardReference = best != null ? best.transform : transform;
        return _aimForwardReference;
    }

    private void Awake()
    {
        _spawnOrder = _nextSpawnOrder++;
        _driftPhase = _spawnOrder * 1.618f;

        _rb = GetComponent<Rigidbody>();
        _health = GetComponent<EntityHealth>();
        _stats = GetComponent<EnemyStatProvider>();

        ConfigureRigidbody();
    }

    private void OnEnable()
    {
        if (!ActiveEnemies.Contains(this))
            ActiveEnemies.Add(this);

        if (_health != null)
            _health.OnDied += HandleDied;
    }

    private void OnDisable()
    {
        if (_health != null)
            _health.OnDied -= HandleDied;

        ActiveEnemies.Remove(this);
    }

    private void Start()
    {
        _started = true;
        ResolveSceneReferences();
        InitializeSplinePosition();
    }

    public void ConfigureSpline(SplineGenerator generator, PathMath math = null)
    {
        if (generator != null) _splineGenerator = generator;
        if (math != null) _pathMath = math;

        _hasSplinePosition = false;
        if (_started && isActiveAndEnabled)
            InitializeSplinePosition();
    }

    private void LateUpdate()
    {
        if (_health != null && _health.IsDead) return;
        if (GameStateManager.Instance != null &&
            GameStateManager.Instance.CurrentState != GameState.InGame) return;

        ResolveSceneReferences();
        if (_target == null) return;
        if (!_hasSplinePosition && !InitializeSplinePosition()) return;

        float length = GetSplineLength();
        if (length <= 0.01f) return;

        if (!TryGetTargetArc(out float targetArc)) return;

        float previousArc = _currentArc;
        AdvanceAlongSpline(targetArc, length, Time.deltaTime);
        float deltaArc = _currentArc - previousArc;
        if (Mathf.Abs(deltaArc) > 0.0001f)
            _lastMoveSign = Mathf.Sign(deltaArc);

        ApplySplinePose();
    }

    private void ResolveSceneReferences()
    {
        bool canRunExpensiveLookup = Time.unscaledTime >= _nextReferenceResolveTime;

        if (_splineGenerator == null && canRunExpensiveLookup)
        {
            _nextReferenceResolveTime = Time.unscaledTime + 0.5f;
            _splineGenerator = FindFirstObjectByType<SplineGenerator>();
        }

        if (_pathMath == null)
        {
            _pathMath = PathMath.Instance;
            if (_pathMath == null && canRunExpensiveLookup)
            {
                _nextReferenceResolveTime = Time.unscaledTime + 0.5f;
                _pathMath = FindFirstObjectByType<PathMath>();
            }
        }

        if (_target == null && _splineGenerator != null && _splineGenerator.player != null)
        {
            _target = _splineGenerator.player;
            _playerVehicle = PlayerReferenceResolver.ResolveVehicle(_target);
        }

        if (_target == null && canRunExpensiveLookup)
        {
            _nextReferenceResolveTime = Time.unscaledTime + 0.5f;
            Transform player = PlayerReferenceResolver.ResolvePlayerTransform();
            if (player != null)
            {
                _target = player;
                _playerVehicle = PlayerReferenceResolver.ResolveVehicle(player);
            }
        }

        if (_playerVehicle == null && _target != null && canRunExpensiveLookup)
        {
            _nextReferenceResolveTime = Time.unscaledTime + 0.5f;
            _playerVehicle = PlayerReferenceResolver.ResolveVehicle(_target);
        }

        if (_playerVehicle == null && canRunExpensiveLookup)
        {
            _nextReferenceResolveTime = Time.unscaledTime + 0.5f;
            _playerVehicle = PlayerReferenceResolver.FindAnyVehicle();
        }
    }

    private bool InitializeSplinePosition()
    {
        ResolveSceneReferences();

        if (!TryGetNearestArc(transform.position, out _currentArc)) return false;

        float spawnArc = _currentArc;
        float length = GetSplineLength();
        if (length > 0.01f && TryGetTargetArc(out float targetArc))
            _currentArc = ResolveInitialArc(_currentArc, targetArc, length);
        bool arcWasAdjusted = Mathf.Abs(_currentArc - spawnArc) > 0.01f;

        _hasSplinePosition = true;

        if (TryGetSplineFrame(_currentArc, out Vector3 center, out _, out Vector3 right, out Vector3 up))
        {
            Vector3 delta = transform.position - center;
            Vector2 spawnOffset = new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, up));

            _baseOffset = !arcWasAdjusted && (preserveSpawnOffset || spawnOffset.sqrMagnitude > 0.01f)
                ? ClampOffset(spawnOffset)
                : ResolveAutomaticSpawnOffset(_currentArc);
        }
        else
        {
            _baseOffset = ResolveAutomaticSpawnOffset(_currentArc);
        }

        _currentOffset = _baseOffset;
        ApplySplinePose(true);
        return true;
    }

    private float ResolveInitialArc(float spawnArc, float playerArc, float length)
    {
        float protectedGap = Mathf.Max(0f, minimumDistanceAhead);
        float preferredGap = Mathf.Max(desiredDistanceAhead, protectedGap);
        float preferredArc = Mathf.Clamp(playerArc + preferredGap, 0f, length);
        float protectedArc = Mathf.Clamp(playerArc + protectedGap, 0f, length);
        float currentGap = spawnArc - playerArc;

        if (currentGap < protectedGap || currentGap > preferredGap + avoidanceArcWindow)
            return Mathf.Max(protectedArc, preferredArc);

        return Mathf.Clamp(spawnArc, protectedArc, length);
    }

    private void AdvanceAlongSpline(float playerArc, float length, float dt)
    {
        float protectedGap = Mathf.Max(0f, minimumDistanceAhead);
        float preferredGap = Mathf.Max(desiredDistanceAhead, protectedGap);
        float protectedArc = Mathf.Clamp(playerArc + protectedGap, 0f, length);
        float gap = _currentArc - playerArc;
        float speed = ResolveMoveSpeed(gap, preferredGap);

        _currentArc = Mathf.Clamp(_currentArc + speed * dt, 0f, length);

        if (_currentArc < protectedArc)
            _currentArc = protectedArc;
    }

    private float ResolveMoveSpeed(float gapToPlayer, float preferredGap)
    {
        float referenceSpeed = ResolveReferenceSpeed();

        if (gapToPlayer > preferredGap + arrivalTolerance)
            return Mathf.Max(0f, referenceSpeed - speedAdvantage);

        float catchUpDeficit = Mathf.Max(0f, preferredGap - gapToPlayer);
        float catchUpSpeed = catchUpDeficit / Mathf.Max(0.05f, catchUpResponseTime);
        return referenceSpeed * Mathf.Max(1f, speedMultiplier) + speedAdvantage + catchUpSpeed;
    }

    private float ResolveReferenceSpeed()
    {
        float referenceSpeed = 0f;

        if (LevelManager.Instance != null)
        {
            referenceSpeed = Mathf.Max(referenceSpeed, LevelManager.Instance.CurrentTargetSpeed);
            referenceSpeed = Mathf.Max(referenceSpeed, LevelManager.Instance.baseSpeed);
        }

        if (_playerVehicle != null)
            referenceSpeed = Mathf.Max(referenceSpeed, _playerVehicle.CurrentForwardSpeed);

        return Mathf.Max(minimumMoveSpeed, referenceSpeed);
    }

    private bool TryGetTargetArc(out float targetArc)
    {
        targetArc = 0f;
        if (_target == null) return false;

        int frame = Time.frameCount;
        if (_targetArcCacheFrame == frame &&
            _targetArcCacheTarget == _target &&
            _targetArcCacheSplineGenerator == _splineGenerator &&
            _targetArcCachePathMath == _pathMath)
        {
            targetArc = _targetArcCacheValue;
            return _targetArcCacheValid;
        }

        _targetArcCacheFrame = frame;
        _targetArcCacheTarget = _target;
        _targetArcCacheSplineGenerator = _splineGenerator;
        _targetArcCachePathMath = _pathMath;

        if (_splineGenerator != null && _splineGenerator.TryGetPlayerArc(out targetArc))
        {
            _targetArcCacheValue = targetArc;
            _targetArcCacheValid = true;
            return true;
        }

        _targetArcCacheValid = TryGetNearestArc(_target.position, out targetArc);
        _targetArcCacheValue = targetArc;
        return _targetArcCacheValid;
    }

    private Vector2 ResolveAutomaticSpawnOffset(float arc)
    {
        float separation = Mathf.Max(1f, spacingBetweenEnemies);
        Vector2 best = Vector2.zero;
        float bestScore = ScoreOffsetCandidate(best, arc);
        const int candidateCount = 16;

        for (int i = 0; i < candidateCount; i++)
        {
            float angle = i * 137.508f * Mathf.Deg2Rad;
            float radius = separation * (1f + (i / 8));
            Vector2 candidate = ClampOffset(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            float score = ScoreOffsetCandidate(candidate, arc) - candidate.sqrMagnitude * 0.01f;

            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private float ScoreOffsetCandidate(Vector2 candidate, float arc)
    {
        float score = 0f;
        bool foundNeighbour = false;

        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            EnemyAI other = ActiveEnemies[i];
            if (!IsAvoidanceNeighbour(other, arc)) continue;

            foundNeighbour = true;
            float sqrDistance = (candidate - other._currentOffset).sqrMagnitude;
            score += sqrDistance;
        }

        return foundNeighbour ? score : -candidate.sqrMagnitude;
    }

    private Vector2 ResolveDriftOffset()
    {
        if (driftFrequency <= 0f) return Vector2.zero;

        float phase = Time.time * driftFrequency + _driftPhase;
        float lateral = Mathf.Sin(phase) * lateralDriftAmplitude;
        float vertical = Mathf.Cos(phase * 0.83f) * verticalDriftAmplitude;
        return new Vector2(lateral, vertical);
    }

    private Vector2 ResolveCombatStrafeOffset()
    {
        if (strafeFrequency <= 0f) return Vector2.zero;

        float phase = (_spawnOrder * 1.618f) + (Time.time * strafeFrequency * Mathf.PI * 2f);
        float lateral = Mathf.Sin(phase) * lateralStrafeAmplitude;
        float vertical = Mathf.Sin(phase * 0.5f + 1.3f) * verticalStrafeAmplitude;
        return new Vector2(lateral, vertical);
    }

    private Vector2 ResolveCombatBaseOffset()
    {
        Vector2 desired = Vector2.zero;

        if (followPlayerOffset && TryGetPlayerSplineOffset(out Vector2 playerOffset))
            desired = Vector2.Lerp(desired, playerOffset, Mathf.Clamp01(playerOffsetInfluence));
        else
            desired = _baseOffset;

        desired += ResolveCombatStrafeOffset();
        return desired;
    }

    private Vector2 ResolveSmartOffset(float arc)
    {
        Vector2 desired = ClampOffset(ResolveCombatBaseOffset() + ResolveDriftOffset());
        float separation = Mathf.Max(0f, spacingBetweenEnemies);
        if (separation <= 0.01f) return desired;

        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            EnemyAI other = ActiveEnemies[i];
            if (!IsAvoidanceNeighbour(other, arc)) continue;

            Vector2 away = desired - other._currentOffset;
            float distance = away.magnitude;
            if (distance < 0.001f)
            {
                float angle = (_spawnOrder - other._spawnOrder) * 137.508f * Mathf.Deg2Rad;
                away = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                distance = 1f;
            }

            if (distance < separation)
                desired += away.normalized * (separation - distance);
        }

        return ClampOffset(desired);
    }

    private bool TryGetPlayerSplineOffset(out Vector2 offset)
    {
        offset = Vector2.zero;

        Transform playerTarget = ResolvePlayerOffsetTarget();
        if (playerTarget == null) return false;
        if (!TryGetTargetArc(out float playerArc)) return false;
        if (!TryGetSplineFrame(playerArc, out Vector3 center, out _, out Vector3 right, out Vector3 up)) return false;

        Vector3 delta = playerTarget.position - center;
        offset = ClampOffset(new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, up)));
        return true;
    }

    private Transform ResolvePlayerOffsetTarget()
    {
        if (_playerVehicle != null && _playerVehicle.PickupTargetTransform != null)
            return _playerVehicle.PickupTargetTransform;

        return _target;
    }

    private bool IsAvoidanceNeighbour(EnemyAI other, float arc)
    {
        if (other == null || other == this) return false;
        if (!other.isActiveAndEnabled) return false;
        if (other._health != null && other._health.IsDead) return false;
        if (!SharesSplineWith(other)) return false;
        return Mathf.Abs(other._currentArc - arc) <= avoidanceArcWindow;
    }

    private bool SharesSplineWith(EnemyAI other)
    {
        if (other == null) return false;

        if (_splineGenerator != null || other._splineGenerator != null)
            return _splineGenerator != null && _splineGenerator == other._splineGenerator;

        SplineContainer myContainer = GetSplineContainer();
        SplineContainer otherContainer = other.GetSplineContainer();
        return myContainer != null && myContainer == otherContainer;
    }

    private void ApplySplinePose(bool immediate = false)
    {
        if (!TryGetSplineFrame(_currentArc, out Vector3 center, out Vector3 tangent, out Vector3 right, out Vector3 up))
            return;

        Vector2 desiredOffset = ResolveSmartOffset(_currentArc);
        if (immediate || offsetMoveSpeed <= 0f)
        {
            _currentOffset = desiredOffset;
        }
        else
        {
            _currentOffset = Vector2.MoveTowards(_currentOffset, desiredOffset, offsetMoveSpeed * Time.deltaTime);
        }

        Vector3 targetPosition = center + right * _currentOffset.x + up * _currentOffset.y;
        Quaternion targetRotation = ResolveRotation(targetPosition, tangent);

        if (immediate)
        {
            _rb.position = targetPosition;
            _rb.rotation = targetRotation;
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        Quaternion currentRotation = usePhysicsMovePosition ? _rb.rotation : transform.rotation;
        Quaternion smoothed = Quaternion.Slerp(currentRotation, targetRotation, 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime));
        if (usePhysicsMovePosition)
        {
            _rb.MovePosition(targetPosition);
            _rb.MoveRotation(smoothed);
            transform.SetPositionAndRotation(targetPosition, smoothed);
        }
        else
        {
            _rb.position = targetPosition;
            _rb.rotation = smoothed;
            transform.SetPositionAndRotation(targetPosition, smoothed);
        }
    }

    private Vector2 ClampOffset(Vector2 offset)
    {
        offset.x = Mathf.Clamp(offset.x, -maxPreservedLateralOffset, maxPreservedLateralOffset);
        offset.y = Mathf.Clamp(offset.y, -maxPreservedVerticalOffset, maxPreservedVerticalOffset);
        return offset;
    }

    private Quaternion ResolveRotation(Vector3 position, Vector3 tangent)
    {
        Vector3 forward = tangent * Mathf.Sign(_lastMoveSign);
        bool aimingAtTarget = false;

        if (_hasLookTarget && Time.time <= _lookTargetUntilTime)
        {
            forward = _lookTarget - position;
            aimingAtTarget = true;
        }
        else
        {
            _hasLookTarget = false;
        }

        if (!_hasLookTarget && lookAtPlayer && _target != null)
        {
            forward = _target.position - position;
            aimingAtTarget = true;
        }

        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;

        Vector3 normalizedForward = forward.normalized;
        return aimingAtTarget
            ? ResolveAimedRootRotation(normalizedForward)
            : Quaternion.LookRotation(normalizedForward, Vector3.up);
    }

    private Quaternion ResolveAimedRootRotation(Vector3 desiredAimDirection)
    {
        if (desiredAimDirection.sqrMagnitude < 0.0001f)
            return transform.rotation;

        Transform aimReference = ResolveAimForwardReference();
        if (aimReference != null && aimReference != transform)
            return Quaternion.FromToRotation(aimReference.forward, desiredAimDirection.normalized) * transform.rotation;

        return Quaternion.LookRotation(desiredAimDirection.normalized, Vector3.up);
    }

    private bool TryGetSplineFrame(float arc, out Vector3 position, out Vector3 tangent, out Vector3 right, out Vector3 up)
    {
        position = transform.position;
        tangent = Vector3.forward;
        up = Vector3.up;
        right = Vector3.right;

        if (_splineGenerator != null && _splineGenerator.CurrentSplineLength > 0.01f)
        {
            position = _splineGenerator.GetPositionAtArc(arc);
            tangent = _splineGenerator.GetTangentAtArc(arc);
            return BuildFrame(tangent, out right, out up);
        }

        SplineContainer container = GetSplineContainer();
        if (container == null || container.Spline == null || container.Spline.Count < 2) return false;

        float length = GetSplineLength();
        if (length <= 0.01f) return false;

        float t = container.Spline.ConvertIndexUnit(Mathf.Clamp(arc, 0f, length), PathIndexUnit.Distance, PathIndexUnit.Normalized);
        container.Evaluate(t, out float3 localPosition, out float3 localTangent, out float3 localUp);

        position = container.transform.TransformPoint((Vector3)localPosition);
        tangent = container.transform.TransformDirection((Vector3)localTangent);
        up = container.transform.TransformDirection((Vector3)localUp);

        if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
        tangent.Normalize();

        right = Vector3.Cross(up, tangent);
        if (right.sqrMagnitude < 0.0001f) return BuildFrame(tangent, out right, out up);

        right.Normalize();
        up = Vector3.Cross(tangent, right).normalized;
        return true;
    }

    private static bool BuildFrame(Vector3 tangent, out Vector3 right, out Vector3 up)
    {
        up = Vector3.up;

        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.forward;

        tangent.Normalize();
        right = Vector3.Cross(up, tangent);

        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();

        return true;
    }

    private bool TryGetNearestArc(Vector3 worldPosition, out float arc)
    {
        arc = 0f;

        SplineContainer container = GetSplineContainer();
        if (container == null || container.Spline == null || container.Spline.Count < 2) return false;

        float3 localPoint = (float3)container.transform.InverseTransformPoint(worldPosition);
        SplineUtility.GetNearestPoint(container.Spline, localPoint, out _, out float t);

        if (float.IsNaN(t) || float.IsInfinity(t)) return false;

        arc = container.Spline.ConvertIndexUnit(Mathf.Clamp01(t), PathIndexUnit.Normalized, PathIndexUnit.Distance);
        return !(float.IsNaN(arc) || float.IsInfinity(arc));
    }

    private float GetSplineLength()
    {
        if (_splineGenerator != null && _splineGenerator.CurrentSplineLength > 0.01f)
            return _splineGenerator.CurrentSplineLength;

        SplineContainer container = GetSplineContainer();
        return container != null && container.Spline != null
            ? container.Spline.CalculateLength(container.transform.localToWorldMatrix)
            : 0f;
    }

    private SplineContainer GetSplineContainer()
    {
        if (_splineGenerator != null && _splineGenerator.targetSpline != null)
            return _splineGenerator.targetSpline;

        return _pathMath != null ? _pathMath.pathSpline : null;
    }

    private static void PruneActiveEnemies()
    {
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            EnemyAI enemy = ActiveEnemies[i];
            if (enemy == null || !enemy.isActiveAndEnabled || (enemy._health != null && enemy._health.IsDead))
            {
                ActiveEnemies.RemoveAt(i);
            }
        }
    }

    private void HandleDied()
    {
        ActiveEnemies.Remove(this);
    }

    private void ConfigureRigidbody()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb == null) return;

        _rb.useGravity = false;
        _rb.isKinematic = true;
        _rb.interpolation = usePhysicsMovePosition ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    private void OnValidate()
    {
        desiredDistanceAhead = Mathf.Max(0f, desiredDistanceAhead);
        minimumDistanceAhead = Mathf.Max(0f, minimumDistanceAhead);
        spacingBetweenEnemies = Mathf.Max(0f, spacingBetweenEnemies);
        avoidanceArcWindow = Mathf.Max(0f, avoidanceArcWindow);
        offsetMoveSpeed = Mathf.Max(0f, offsetMoveSpeed);
        playerOffsetInfluence = Mathf.Clamp01(playerOffsetInfluence);
        lateralStrafeAmplitude = Mathf.Max(0f, lateralStrafeAmplitude);
        verticalStrafeAmplitude = Mathf.Max(0f, verticalStrafeAmplitude);
        strafeFrequency = Mathf.Max(0f, strafeFrequency);
        catchUpResponseTime = Mathf.Max(0.05f, catchUpResponseTime);
        maxPreservedLateralOffset = Mathf.Max(0f, maxPreservedLateralOffset);
        maxPreservedVerticalOffset = Mathf.Max(0f, maxPreservedVerticalOffset);

        if (Application.isPlaying)
            ConfigureRigidbody();
    }
}
