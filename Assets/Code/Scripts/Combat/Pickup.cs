using UnityEngine;

public enum PickupEffect
{
    Fuel,
    HP,
    Shield,
    SpeedBoost,
    Exp,
    WeaponChange
}

[RequireComponent(typeof(SphereCollider))]
public class Pickup : MonoBehaviour
{
    [Header("Effect")]
    public PickupEffect effect = PickupEffect.Exp;
    [Tooltip("Wartość: ilość paliwa / HP / exp, albo mnożnik prędkości.")]
    public float value = 10f;
    [Tooltip("Czas trwania dla efektów chwilowych (Shield, SpeedBoost).")]
    public float duration = 3f;
    [Tooltip("Tylko dla WeaponChange — broń do której gracz przełącza się.")]
    public WeaponData weaponTarget;

    [Header("Visual & Animation")]
    [ColorUsage(true, true)]
    public Color visualColor = Color.yellow;
    [Tooltip("Rozmiar placeholder'a (sphere). Ignoruje jeśli prefab ma własny visual.")]
    public float visualScale = 5f;
    public float rotationSpeed = 90f;
    public float bobAmplitude = 0.6f;
    public float bobFrequency = 1.5f;
    private MeshRenderer _renderer;
    private Transform _visual;
    private Vector3 _visualBasePos;
    private float _phase;
    private MaterialPropertyBlock _propertyBlock;
    private static Mesh _cachedSphereMesh;
    private static Shader _cachedShader;
    private static Material _cachedMaterial;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Collection Feel")]
    [Tooltip("Zasięg w którym magnes się aktywuje — pickup zacznie homing'ować do gracza w tym dystansie.")]
    [SerializeField] private float magnetDistance = 12f;
    [Tooltip("Bazowa prędkość przyciągania przed dodaniem bonusu z aktualnej prędkości gracza.")]
    [SerializeField] private float magnetSpeed = 40f;
    [Tooltip("Dodatkowe przyspieszenie zależne od dystansu do centrum gracza (units/sec per meter).")]
    [SerializeField] private float homingSpeedPerMeter = 6f;
    [Tooltip("Ile aktualnej prędkości lotu dodać do prędkości homingu.")]
    [SerializeField] private float playerSpeedInfluence = 1.5f;
    [Tooltip("Wyprzedzenie aktywacji w sekundach lotu. 0.18 przy 50 u/s daje +9m zasięgu.")]
    [SerializeField] private float speedLeadTime = 0.18f;
    [Tooltip("Limit bonusowego zasięgu z prędkości lotu.")]
    [SerializeField] private float maxSpeedDistanceBonus = 14f;
    [Tooltip("Jak szybko pickup dochodzi do docelowej prędkości homingu.")]
    [SerializeField] private float magnetAcceleration = 10f;
    [Tooltip("Prędkość shrink animacji po zebraniu (scale/sec).")]
    [SerializeField] private float collectScaleSpeed = 12f;
    [Tooltip("Margines zbliżenia względem widocznego promienia pickupa. 0.03 = 3%, 0.05 = 5%.")]
    [Range(0.01f, 0.1f)]
    [SerializeField] private float collectTolerancePercent = 0.05f;
    [Tooltip("Minimalny dystans zebrania w world units, zabezpiecza przed zbyt małą tolerancją.")]
    [SerializeField] private float minimumCollectDistance = 1.5f;
    [Tooltip("Warstwy na których pickup szuka gracza w OnTrigger*. Zostaw Everything jeśli player nie ma dedykowanej warstwy.")]
    [SerializeField] private LayerMask playerMask = ~0;
    [SerializeField] private ParticleSystem collectParticles;
    [SerializeField] private AudioClip collectSound;

    private bool _isCollected;
    private bool _magnetActive;
    private Transform _player;
    private Transform _magnetTarget;
    private float _targetForwardSpeed;
    private float _currentHomingSpeed;
    private bool _detachedForMagnet;
    private SphereCollider _collider;
    private Collider _lastResolvedCollider;
    private Transform _lastResolvedPlayer;
    private Transform _lastResolvedTarget;
    private Transform _initialParent;
    private Vector3 _initialLocalPosition;
    private Quaternion _initialLocalRotation;
    private Vector3 _initialLocalScale;
    private bool _hasInitialTransform;
    private bool _restoreInitialTransformQueued;
    private float _cachedCollectDistance;
    private bool _visualMetricsDirty = true;

    private void Awake()
    {
        _collider = GetComponent<SphereCollider>();
        EnsureCollider();
        CaptureInitialTransform();
        EnsureVisual();
        _visual = _renderer != null ? _renderer.transform : transform;
        _visualBasePos = _visual.localPosition;
        _phase = Random.value * Mathf.PI * 2f;
        RefreshVisualMetrics();
        ApplyVisualColor();
    }

    private void OnValidate()
    {
        magnetDistance = Mathf.Max(0.01f, magnetDistance);
        magnetSpeed = Mathf.Max(0.01f, magnetSpeed);
        homingSpeedPerMeter = Mathf.Max(0f, homingSpeedPerMeter);
        playerSpeedInfluence = Mathf.Max(0f, playerSpeedInfluence);
        speedLeadTime = Mathf.Max(0f, speedLeadTime);
        maxSpeedDistanceBonus = Mathf.Max(0f, maxSpeedDistanceBonus);
        magnetAcceleration = Mathf.Max(0.01f, magnetAcceleration);
        collectScaleSpeed = Mathf.Max(0.01f, collectScaleSpeed);
        collectTolerancePercent = Mathf.Clamp(collectTolerancePercent, 0.01f, 0.1f);
        minimumCollectDistance = Mathf.Max(0.01f, minimumCollectDistance);

        if (Application.isPlaying) return;

        if (_collider == null) _collider = GetComponent<SphereCollider>();
        if (_collider != null) EnsureCollider();
    }

    private void OnEnable()
    {
        ResetRuntimeState();
        _restoreInitialTransformQueued = true;
        var mgr = PickupManager.GetOrCreate();
        if (mgr != null) mgr.Register(this);
    }

    private void OnDisable()
    {
        var mgr = PickupManager.Instance;
        if (mgr != null) mgr.Unregister(this);

        ResetRuntimeState();
        _restoreInitialTransformQueued = false;
    }

    private void ResetRuntimeState()
    {
        _isCollected = false;
        _magnetActive = false;
        _detachedForMagnet = false;
        _player = null;
        _magnetTarget = null;
        _targetForwardSpeed = 0f;
        _currentHomingSpeed = 0f;
        _lastResolvedCollider = null;
        _lastResolvedPlayer = null;
        _lastResolvedTarget = null;
        if (_collider != null) _collider.enabled = true;
        if (_visual != null) _visual.localPosition = _visualBasePos;
    }

    private void CaptureInitialTransform()
    {
        _initialParent = transform.parent;
        _initialLocalPosition = transform.localPosition;
        _initialLocalRotation = transform.localRotation;
        _initialLocalScale = transform.localScale;
        _hasInitialTransform = true;
    }

    private void RestoreInitialTransform()
    {
        if (!_hasInitialTransform) CaptureInitialTransform();

        transform.SetParent(_initialParent, false);
        transform.SetLocalPositionAndRotation(_initialLocalPosition, _initialLocalRotation);
        transform.localScale = _initialLocalScale;
    }

    public void Tick(
        float dt,
        float time,
        bool hasPlayer,
        Vector3 playerPos,
        Transform playerTransform,
        Transform pickupTarget,
        float playerForwardSpeed)
    {
        if (_restoreInitialTransformQueued)
        {
            RestoreInitialTransform();
            _restoreInitialTransformQueued = false;
        }

        if (hasPlayer)
        {
            RefreshMagnetTarget(playerTransform, pickupTarget, playerForwardSpeed);
        }

        if (_isCollected)
        {
            if (HasActiveTarget())
            {
                Vector3 magnetTarget = GetMagnetTarget();
                float dist = Vector3.Distance(transform.position, magnetTarget);
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    magnetTarget,
                    GetSmoothedHomingSpeed(dist, dt) * dt);
            }

            transform.localScale = Vector3.MoveTowards(
                transform.localScale,
                Vector3.zero,
                collectScaleSpeed * dt);
            return;
        }


        if (_magnetActive && !HasActiveTarget())
        {
            Destroy(gameObject);
            return;
        }


        _visual.Rotate(0f, rotationSpeed * dt, 0f, Space.Self);

        if (_magnetActive)
        {
            Vector3 magnetTarget = GetMagnetTarget();

            if (IsAtPlayerCenter(magnetTarget))
            {
                StartCollect();
                return;
            }

            float dist = Vector3.Distance(transform.position, magnetTarget);
            float pull = GetSmoothedHomingSpeed(dist, dt);

            transform.position = Vector3.MoveTowards(
                transform.position,
                magnetTarget,
                pull * dt);

            if (IsAtPlayerCenter(magnetTarget))
            {
                StartCollect();
            }

            return;
        }

        if (bobAmplitude > 0f)
        {
            float y = Mathf.Sin(time * bobFrequency + _phase) * bobAmplitude;
            _visual.localPosition = _visualBasePos + new Vector3(0f, y, 0f);
        }

        if (!hasPlayer) return;

        Vector3 toPlayer = playerPos - transform.position;
        float activationDistance = GetMagnetActivationDistance();
        if (toPlayer.sqrMagnitude <= activationDistance * activationDistance)
        {
            ActivateMagnet(playerTransform, pickupTarget, playerForwardSpeed);
        }
    }

    private void RefreshMagnetTarget(Transform player, Transform pickupTarget, float playerForwardSpeed)
    {
        if (player != null) _player = player;
        if (pickupTarget != null) _magnetTarget = pickupTarget;
        if (_magnetTarget == null) _magnetTarget = _player;
        _targetForwardSpeed = Mathf.Max(0f, playerForwardSpeed);
    }

    private void ActivateMagnet(Transform player, Transform pickupTarget, float playerForwardSpeed)
    {
        if (player == null && pickupTarget == null) return;
        bool alreadyFollowingSameTarget = _magnetActive && _player == player && _magnetTarget == (pickupTarget != null ? pickupTarget : player);

        RefreshMagnetTarget(player, pickupTarget, playerForwardSpeed);
        _magnetActive = true;

        if (!_detachedForMagnet)
        {
            _detachedForMagnet = true;
            transform.SetParent(null, true);
        }

        if (_visual != null) _visual.localPosition = _visualBasePos;

        if (!alreadyFollowingSameTarget)
        {
            float dist = Vector3.Distance(transform.position, GetMagnetTarget());
            _currentHomingSpeed = Mathf.Max(_currentHomingSpeed, GetTargetHomingSpeed(dist) * 0.35f);
        }
    }

    private Vector3 GetMagnetTarget()
    {
        if (_magnetTarget != null) return _magnetTarget.position;
        return _player != null ? _player.position : transform.position;
    }

    private bool HasActiveTarget()
    {
        return _magnetTarget != null || _player != null;
    }

    private float GetTargetHomingSpeed(float distance)
    {
        float speedBonus = _targetForwardSpeed * playerSpeedInfluence;
        return magnetSpeed + speedBonus + Mathf.Max(0f, distance) * homingSpeedPerMeter;
    }

    private float GetSmoothedHomingSpeed(float distance, float dt)
    {
        float targetSpeed = GetTargetHomingSpeed(distance);
        if (_currentHomingSpeed <= 0f) _currentHomingSpeed = targetSpeed * 0.35f;

        float acceleration = Mathf.Max(targetSpeed, magnetSpeed) * magnetAcceleration;
        _currentHomingSpeed = Mathf.MoveTowards(_currentHomingSpeed, targetSpeed, acceleration * dt);
        return _currentHomingSpeed;
    }

    private bool IsAtPlayerCenter() => IsAtPlayerCenter(GetMagnetTarget());

    private bool IsAtPlayerCenter(Vector3 center)
    {
        float collectRadius = GetCollectDistance();
        return (center - transform.position).sqrMagnitude <= collectRadius * collectRadius;
    }

    private float GetCollectDistance()
    {
        if (_visualMetricsDirty) RefreshVisualMetrics();
        return _cachedCollectDistance;
    }

    private float GetMagnetActivationDistance()
    {
        float speedBonus = Mathf.Min(maxSpeedDistanceBonus, _targetForwardSpeed * speedLeadTime);
        return Mathf.Max(magnetDistance + speedBonus, minimumCollectDistance);
    }

    private float GetPickupVisualRadius()
    {
        if (_renderer != null)
        {
            Vector3 extents = _renderer.bounds.extents;
            return Mathf.Max(0.05f, extents.x, extents.y, extents.z);
        }

        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        return Mathf.Max(0.05f, visualScale * 0.5f * scale);
    }

    private void OnTriggerEnter(Collider other) => TryCollect(other);

    private void OnTriggerStay(Collider other) => TryCollect(other);

    private void TryCollect(Collider other)
    {
        if (_isCollected) return;
        if (other == null) return;
        if ((playerMask.value & (1 << other.gameObject.layer)) == 0) return;

        Transform player;
        Transform target;
        float forwardSpeed;
        if (other == _lastResolvedCollider)
        {
            player = _lastResolvedPlayer;
            target = _lastResolvedTarget;
            forwardSpeed = ResolveForwardSpeed(player);
        }
        else
        {
            player = ResolvePlayerTransform(other);
            target = ResolvePickupTarget(player);
            forwardSpeed = ResolveForwardSpeed(player);
            _lastResolvedCollider = other;
            _lastResolvedPlayer = player;
            _lastResolvedTarget = target;
        }

        if (player == null && target == null) return;

        ActivateMagnet(player, target, forwardSpeed);

        if (IsAtPlayerCenter()) StartCollect();
    }

    private static Transform ResolvePlayerTransform(Collider other)
    {
        if (other == null) return null;

        var railCtrl = other.GetComponentInParent<PlayerController>();
        if (railCtrl != null) return railCtrl.transform;

        var splineCtrl = other.GetComponentInParent<SplinePlayerController>();
        if (splineCtrl != null) return splineCtrl.transform;

        var stats = other.GetComponentInParent<PlayerStatsManager>();
        if (stats != null) return stats.transform;

        Transform root = other.transform.root;
        if (root != null && root.CompareTag("Player")) return root;

        return other.CompareTag("Player") ? other.transform : null;
    }

    private static Transform ResolvePickupTarget(Transform player)
    {
        return PlayerReferenceResolver.ResolvePickupTarget(player);
    }

    private static float ResolveForwardSpeed(Transform player)
    {
        return PlayerReferenceResolver.ResolveForwardSpeed(player);
    }

    private void StartCollect()
    {
        if (_isCollected) return;
        _isCollected = true;
        _collider.enabled = false;

        ApplyEffect(_player);
        PlayFeedback();

        Destroy(gameObject, 0.25f);
    }

    private void ApplyEffect(Transform playerTransform)
    {
        PickupEffectApplier.Apply(effect, value, duration, weaponTarget, playerTransform);
    }

    private void PlayFeedback()
    {
        if (collectParticles != null)
            HitEffectPool.Spawn(collectParticles.gameObject, transform.position, Quaternion.identity);

        if (collectSound != null)
            OneShotAudioPool.Play(collectSound, transform.position, 0.5f);
    }

    private void EnsureCollider()
    {
        _collider.isTrigger = true;
        _collider.radius = GetPickupLocalActivationRadius();
    }

    private void EnsureVisual()
    {

        for (int i = 0; i < transform.childCount; i++)
        {
            var mr = transform.GetChild(i).GetComponentInChildren<MeshRenderer>();
            if (mr != null) { _renderer = mr; return; }
        }

        var visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * visualScale;

        var mf = visual.AddComponent<MeshFilter>();
        mf.sharedMesh = GetSphereMesh();

        var mrNew = visual.AddComponent<MeshRenderer>();
        mrNew.sharedMaterial = GetSharedMaterial();
        _renderer = mrNew;
    }

    public void ConfigureCollectionFeel(
        float magnetDistance,
        float magnetSpeed,
        float homingSpeedPerMeter,
        float playerSpeedInfluence,
        float speedLeadTime,
        float maxSpeedDistanceBonus,
        float magnetAcceleration,
        float collectScaleSpeed,
        float collectTolerancePercent,
        float minimumCollectDistance)
    {
        this.magnetDistance = Mathf.Max(0.01f, magnetDistance);
        this.magnetSpeed = Mathf.Max(0.01f, magnetSpeed);
        this.homingSpeedPerMeter = Mathf.Max(0f, homingSpeedPerMeter);
        this.playerSpeedInfluence = Mathf.Max(0f, playerSpeedInfluence);
        this.speedLeadTime = Mathf.Max(0f, speedLeadTime);
        this.maxSpeedDistanceBonus = Mathf.Max(0f, maxSpeedDistanceBonus);
        this.magnetAcceleration = Mathf.Max(0.01f, magnetAcceleration);
        this.collectScaleSpeed = Mathf.Max(0.01f, collectScaleSpeed);
        this.collectTolerancePercent = Mathf.Clamp(collectTolerancePercent, 0.01f, 0.1f);
        this.minimumCollectDistance = Mathf.Max(0.01f, minimumCollectDistance);

        _visualMetricsDirty = true;
        if (_collider != null) _collider.radius = GetPickupLocalActivationRadius();
    }

    public void RefreshVisual()
    {
        if (_renderer == null) EnsureVisual();
        var visual = _renderer.transform;
        if (visual != null) visual.localScale = Vector3.one * visualScale;
        _visualMetricsDirty = true;
        RefreshVisualMetrics();
        if (_collider != null) _collider.radius = GetPickupLocalActivationRadius();
        ApplyVisualColor();

        if (!_magnetActive && !_isCollected)
        {
            CaptureInitialTransform();
        }
    }

    private float GetPickupLocalActivationRadius()
    {
        float percent = Mathf.Clamp(collectTolerancePercent, 0.01f, 0.1f);
        return Mathf.Max(0.05f, visualScale * 0.5f * (1f + percent));
    }

    private void RefreshVisualMetrics()
    {
        float pickupRadius = GetPickupVisualRadius();
        float percent = Mathf.Clamp(collectTolerancePercent, 0.01f, 0.1f);
        float pickupContactRadius = pickupRadius * (1f + percent);
        _cachedCollectDistance = Mathf.Max(minimumCollectDistance, pickupContactRadius);
        _visualMetricsDirty = false;
    }

    private void ApplyVisualColor()
    {
        if (_renderer == null) return;
        if (_renderer.sharedMaterial == null) return;
        if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

        _renderer.GetPropertyBlock(_propertyBlock);

        var mat = _renderer.sharedMaterial;
        if (mat.HasProperty(BaseColorId)) _propertyBlock.SetColor(BaseColorId, visualColor);
        if (mat.HasProperty(ColorId)) _propertyBlock.SetColor(ColorId, visualColor);
        if (mat.HasProperty(EmissionColorId)) _propertyBlock.SetColor(EmissionColorId, visualColor);

        _renderer.SetPropertyBlock(_propertyBlock);
    }

    private static Mesh GetSphereMesh()
    {
        if (_cachedSphereMesh != null) return _cachedSphereMesh;
        var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _cachedSphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(temp); else DestroyImmediate(temp);
        return _cachedSphereMesh;
    }

    private static Shader GetPickupShader()
    {
        if (_cachedShader != null) return _cachedShader;
        _cachedShader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");
        return _cachedShader;
    }



    private static Material GetSharedMaterial()
    {
        if (_cachedMaterial != null) return _cachedMaterial;
        var shader = GetPickupShader();
        if (shader == null) return null;
        _cachedMaterial = new Material(shader);
        _cachedMaterial.EnableKeyword("_EMISSION");
        _cachedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return _cachedMaterial;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _cachedSphereMesh = null;
        _cachedShader = null;
        _cachedMaterial = null;
    }

    public static Color DefaultColorFor(PickupEffect e) => e switch
    {
        PickupEffect.Fuel         => new Color(0.20f, 0.80f, 1.00f) * 2.5f,
        PickupEffect.HP           => new Color(0.20f, 1.00f, 0.30f) * 2.5f,
        PickupEffect.Shield       => new Color(0.70f, 0.40f, 1.00f) * 3.0f,
        PickupEffect.SpeedBoost   => new Color(1.00f, 0.50f, 0.10f) * 3.0f,
        PickupEffect.Exp          => new Color(1.00f, 0.85f, 0.20f) * 1.8f,
        PickupEffect.WeaponChange => new Color(1.00f, 0.30f, 0.30f) * 4.0f,
        _                         => Color.white,
    };
}
