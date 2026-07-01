using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Profiling;

[DefaultExecutionOrder(-100)]
public class SplineGenerator : MonoBehaviour
{
    [Header("References")]
    private bool _hasStartedPlaying = false;
    public SplineContainer targetSpline;
    public SplineAnimate splineAnimate;
    public Transform player;

    [Header("Generation")]
    public float segmentLength = 30f;
    public float initialLength = 2500f;
    public float extendTrigger = 1500f;
    public float extendBy = 500f;

    [Header("Spawning Segments")]
    [Tooltip("Co ile knotów emitowany jest segment dla SplineSpawnManager. 8 × segmentLength = długość arc segmentu.")]
    [SerializeField] private int knotsPerSegment = 8;
    [SerializeField] private int spawnSeed = 1337;
    [SerializeField] private float corridorRadiusOverride = 0f;

    [Header("Player Reach (dla placement'ów pickup'ów)")]
    [Tooltip("Referencja do Controllera gracza — placement'y czytają z niego FrameHalfWidth/Height.")]
    [SerializeField] private SplinePlayerController playerController;
    [Tooltip("Fallback gdy playerController == null albo jeszcze niezinicjalizowany.")]
    [SerializeField] private float fallbackReachHalfWidth = 8f;
    [SerializeField] private float fallbackReachHalfHeight = 5f;
    [Tooltip("Margines bezpieczeństwa — 0.85 = pickup'y w 85% maksymalnego zasięgu.")]
    [Range(0.1f, 1f)] [SerializeField] private float reachSafetyMargin = 0.85f;
    private float _accumulatedLength;
    public event System.Action<SplineSegment, int, int> OnSegmentSpawned;
    public float CurrentSplineLength { get; private set; }
    private NativeArray<float3> _outPoints;
    private JobHandle _genJobHandle;
    private bool _isJobRunning = false;
    private int _pointsBeingGenerated = 0;
    public float CorridorRadius
    {
        get
        {
            if (corridorRadiusOverride > 0f) return corridorRadiusOverride;
            return PathMath.Instance != null ? PathMath.Instance.corridorWidth * 0.5f : 30f;
        }
    }

    public float ReachHalfWidth
    {
        get
        {
            float w = playerController != null ? playerController.FrameHalfWidth : 0f;
            if (w <= 0f) w = fallbackReachHalfWidth;
            return w * reachSafetyMargin;
        }
    }

    public float ReachHalfHeight
    {
        get
        {
            float h = playerController != null ? playerController.FrameHalfHeight : 0f;
            if (h <= 0f) h = fallbackReachHalfHeight;
            return h * reachSafetyMargin;
        }
    }
    int _currentSegmentIndex;
    int _knotsInCurrentSegment;
    SplineSegment _currentSegment;
    Transform _segmentsRoot;

    [Header("Path Shape")]
    [Tooltip("Maksymalna wysokość spline nad terenem (m).")]
    public float maxHeightAboveTerrain = 30f;
    [Tooltip("Maksymalny kąt skrętu w poziomie na segment (deg).")]
    [Range(0f, 30f)] public float maxTurnAngle = 6f;
    [Tooltip("Maksymalny kąt wzlotu/spadku w pionie (deg).")]
    [Range(0f, 60f)] public float maxVerticalAngle = 20f;
    [Tooltip("Częstość występowania zakrętów i wzniesień (0 = prosto, 1 = często).")]
    [Range(0f, 1f)] public float variationFrequency = 0.4f;
    [Tooltip("Łagodzenie zmian wysokości spline'a (większe = bardziej liniowy lot, mniej szarpań pionowych). " +
             "Wyrażone w 'knotach' — czas potrzebny by walkPos.y dogonił target.")]
    [Range(0.1f, 8f)] public float altitudeSmoothTime = 3f;

    [Tooltip("Bias steerujący spline w stronę dolin. 0 = wyłączone (losowy walk). " +
             "1-2 = subtelne preferowanie niższego terenu. 3-5 = mocne unikanie gór, spline aktywnie kluczy między pasmami.")]
    [Range(0f, 5f)] public float valleySeekingStrength = 0f;

    const float MinHeightAboveTerrain = 5f;
    const float LookAheadDistance = 250f;
    const float TerrainSampleRadius = 40f;
    const int MinStraightSegments = 4;
    const int MaxStraightSegments = 30;
    const int VariationSegments = 6;
    const float MaxCumulativeYaw = 60f;

    Spline _spline;
    Vector3 _walkPos;
    Vector3 _walkDir = Vector3.forward;
    float _targetHeightOffset;
    NoiseProvider _np;
    NativeArray<RewriteFastNoiseLite> _baseNoises;
    NativeArray<NoiseProvider.BaseNoiseLayerRuntime> _baseLayerSettings;
    NativeArray<int> _permutation;
    NativeArray<float> _heightLUT;
    float _baseAmplitude;
    int _lookaheadSamples;
    float _maxStepPerSegment;
    float _peakSampleRadius;

    void Start()
    {
        if (targetSpline == null) { enabled = false; return; }

        RefreshCaches();

        if (splineAnimate != null)
        {
            splineAnimate.Container = null;
            splineAnimate.enabled = false;
        }

        _spline = targetSpline.Spline;
        _spline.Clear();

        _walkDir = Vector3.forward;
        _walkPos = targetSpline.transform.position;

        float terrainHere = NoiseReady ? SamplePeak(_walkPos.x, _walkPos.z) : 0f;
        _walkPos.y = terrainHere + (maxHeightAboveTerrain * 0.5f);

        CurrentSplineLength = 0f;
        _currentSegmentIndex = 0;
        _knotsInCurrentSegment = 0;

        if (_segmentsRoot == null)
        {
            _segmentsRoot = new GameObject("SplineSegments").transform;
            _segmentsRoot.SetParent(transform, false);
        }

        StartNewSegment();
        Vector3 localStart = targetSpline.transform.InverseTransformPoint(_walkPos);
        _spline.Add(new BezierKnot(new float3(localStart.x, localStart.y, localStart.z)), TangentMode.AutoSmooth);
        StartInitialGeneration(initialLength);
    }

    void StartInitialGeneration(float length)
    {
        _pointsBeingGenerated = Mathf.CeilToInt(length / segmentLength);
        if (_outPoints.IsCreated) _outPoints.Dispose();
        _outPoints = new NativeArray<float3>(_pointsBeingGenerated, Allocator.Persistent);
        var sampler = CreateNativeSampler();

        var job = new GenerateSplinePointsJob
        {
            StartPos = _walkPos,
            StartDir = _walkDir,
            SegmentLength = segmentLength,
            PointsToGenerate = _pointsBeingGenerated,
            Sampler = sampler,
            RandomGen = new Unity.Mathematics.Random((uint)spawnSeed),
            
            MaxHeightAboveTerrain = maxHeightAboveTerrain,
            MaxTurnAngle = maxTurnAngle,
            MaxVerticalAngle = maxVerticalAngle,
            LookAheadDistance = LookAheadDistance,
            VariationFrequency = variationFrequency,
            ValleySeekingStrength = valleySeekingStrength,
            AltitudeSmoothTime = altitudeSmoothTime,
            OutPoints = _outPoints
        };

        _genJobHandle = job.Schedule();
        if (_np != null)
        {
            _np.RegisterHeightLutReader(_genJobHandle);
        }
        _isJobRunning = true;
    }

    private NativeHeightSampler CreateNativeSampler()
    {
        return new NativeHeightSampler
        {
            baseNoises = _baseNoises,
            baseLayerSettings = _baseLayerSettings,
            permutation = _permutation,
            baseAmplitude = _baseAmplitude,
            heightLUT = _heightLUT
        };
    }
    void LateUpdate()
    {
        if (_isJobRunning && _genJobHandle.IsCompleted)
        {
            CompleteGenerationJob();
        }

        if (player == null || targetSpline == null || _spline == null || _spline.Count == 0) return;

        if (splineAnimate != null && splineAnimate.Container != null && !_hasStartedPlaying)
        {
            if (IsInGameState())
            {
                splineAnimate.Play();
                _hasStartedPlaying = true;
            }
        }

        if (!_isJobRunning)
        {
            Vector3 endWorld = targetSpline.transform.TransformPoint((Vector3)_spline[_spline.Count - 1].Position);
            if ((player.position - endWorld).sqrMagnitude < extendTrigger * extendTrigger)
            {
                StartGenerationJob();
            }
        }
    }

    private void OnDestroy()
    {
        if (_isJobRunning) _genJobHandle.Complete();
        if (_outPoints.IsCreated) _outPoints.Dispose();
    }

    void StartGenerationJob()
    {
        RefreshCaches();

        _pointsBeingGenerated = Mathf.CeilToInt(extendBy / segmentLength);
        if (_outPoints.IsCreated) _outPoints.Dispose();
        _outPoints = new NativeArray<float3>(_pointsBeingGenerated, Allocator.Persistent);


        var sampler = new NativeHeightSampler
        {
            baseNoises = _baseNoises,
            baseLayerSettings = _baseLayerSettings,
            permutation = _permutation,
            baseAmplitude = _baseAmplitude,
            heightLUT = _heightLUT
        };

        var job = new GenerateSplinePointsJob
        {
            StartPos = _walkPos,
            StartDir = _walkDir,
            SegmentLength = segmentLength,
            PointsToGenerate = _pointsBeingGenerated,
            Sampler = sampler,
            RandomGen = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 10000)),
            
            MaxHeightAboveTerrain = maxHeightAboveTerrain,
            MaxTurnAngle = maxTurnAngle,
            MaxVerticalAngle = maxVerticalAngle,
            LookAheadDistance = LookAheadDistance,
            VariationFrequency = variationFrequency,
            ValleySeekingStrength = valleySeekingStrength,
            AltitudeSmoothTime = altitudeSmoothTime,
            OutPoints = _outPoints
        };

        _genJobHandle = job.Schedule();
        if (_np != null)
        {
            _np.RegisterHeightLutReader(_genJobHandle);
        }
        _isJobRunning = true;
    }

    void CompleteGenerationJob()
    {
       _genJobHandle.Complete();
        _isJobRunning = false;

        bool isInitialGen = (_spline.Count <= 1);
        bool shouldPreserveAnimatedPosition = !isInitialGen && splineAnimate != null && splineAnimate.Container != null;
        Vector3 animatedPositionBeforeRebuild = shouldPreserveAnimatedPosition ? splineAnimate.transform.position : Vector3.zero;

        int startKnotIndex = _spline.Count;

        for (int i = 0; i < _outPoints.Length; i++)
        {
            float3 worldPos = _outPoints[i];
            
            _walkPos = worldPos;
            if (i > 0) _walkDir = math.normalize(_outPoints[i] - _outPoints[i-1]);

            Vector3 local = targetSpline.transform.InverseTransformPoint(worldPos);
            
            _spline.Add(new BezierKnot(local), TangentMode.AutoSmooth);

            _accumulatedLength += segmentLength;
            CurrentSplineLength = _accumulatedLength;

            _knotsInCurrentSegment++;
            if (_knotsInCurrentSegment >= knotsPerSegment)
            {
                SyncSplineLength();
                FinalizeCurrentSegment();
                StartNewSegment();
            }
        }

        for (int i = startKnotIndex; i < _spline.Count; i++)
        {
            _spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

        SyncSplineLength();

        if (splineAnimate != null)
        {
            if (isInitialGen)
            {
                splineAnimate.Container = targetSpline; 
                splineAnimate.enabled = true;
                splineAnimate.Restart(false);
                if (IsInGameState())
                {
                    splineAnimate.Play();
                    _hasStartedPlaying = true;
                }
                else
                {
                    splineAnimate.Pause(); 
                    _hasStartedPlaying = false;
                }
            }
            else
            {
                if (TryGetNearestSplineTime(animatedPositionBeforeRebuild, out float nextTime))
                    splineAnimate.NormalizedTime = nextTime;

                if (_hasStartedPlaying && IsInGameState() && !splineAnimate.IsPlaying)
                    splineAnimate.Play();
            }
        }

        RebuildNativeSpline();
    }

    private static bool IsInGameState()
    {
        GameStateManager gameStateManager = GameStateManager.Instance;
        return gameStateManager != null && gameStateManager.CurrentState == GameState.InGame;
    }

    void RefreshCaches()
    {
        if (_np == null) _np = NoiseProvider.Instance;
        if (_np != null)
        {
            _np.EnsureReadyForTerrainJobs();
            _baseNoises = _np.BaseNoiseLayersBurst;
            _baseLayerSettings = _np.BaseNoiseLayerSettingsBurst;
            _permutation = _np.Perm512;
            _heightLUT = _np.heightCurveLUT;
            _baseAmplitude = _np.baseAmplitude;
        }
        _lookaheadSamples = Mathf.Max(1, Mathf.CeilToInt(LookAheadDistance / segmentLength));
        _maxStepPerSegment = Mathf.Tan(maxVerticalAngle * Mathf.Deg2Rad) * segmentLength;
        _peakSampleRadius = TerrainSampleRadius * 0.5f;
    }

    bool NoiseReady =>
        _np != null &&
        _heightLUT.IsCreated &&
        _heightLUT.Length > 1 &&
        _baseNoises.IsCreated &&
        _baseLayerSettings.IsCreated &&
        _permutation.IsCreated &&
        _baseNoises.Length > 0;
    void StartNewSegment()
    {
        var go = new GameObject($"Segment_{_currentSegmentIndex}");
        go.transform.SetParent(_segmentsRoot, false);
        go.transform.position = _walkPos;
        _currentSegment = go.AddComponent<SplineSegment>();
        _currentSegment.SegmentIndex = _currentSegmentIndex;
        _currentSegment.StartArc = CurrentSplineLength;
        _knotsInCurrentSegment = 0;
    }

    void FinalizeCurrentSegment()
    {
        if (_currentSegment == null) return;
        _currentSegment.EndArc = CurrentSplineLength;
        int segSeed = unchecked(spawnSeed + _currentSegmentIndex * 31);
        OnSegmentSpawned?.Invoke(_currentSegment, _currentSegmentIndex, segSeed);
        _currentSegmentIndex++;
    }

    public Vector3 GetPositionAtArc(float arc)
    {
        if (_spline == null || _spline.Count < 2 || CurrentSplineLength <= 0f) return _walkPos;
        float t = GetNormalizedTimeAtArc(arc);
        float3 localPos = SplineUtility.EvaluatePosition(_spline, t);
        return targetSpline.transform.TransformPoint((Vector3)localPos);
    }

    public Vector3 GetTangentAtArc(float arc)
    {
        if (_spline == null || _spline.Count < 2 || CurrentSplineLength <= 0f) return _walkDir;
        float t = GetNormalizedTimeAtArc(arc);
        float3 localTan = SplineUtility.EvaluateTangent(_spline, t);
        Vector3 worldTan = targetSpline.transform.TransformDirection((Vector3)localTan);
        return worldTan.sqrMagnitude > 1e-8f ? worldTan.normalized : Vector3.forward;
    }

    public bool TryGetPlayerArc(out float arc)
    {
        arc = 0f;
        if (_spline == null || _spline.Count < 2 || CurrentSplineLength <= 0f) return false;

        if (splineAnimate != null && splineAnimate.Container != null)
        {
            arc = GetArcAtNormalizedTime(splineAnimate.NormalizedTime);
            return true;
        }

        if (player != null && TryGetNearestSplineTime(player.position, out float nearestT))
        {
            arc = GetArcAtNormalizedTime(nearestT);
            return true;
        }

        return false;
    }

    float GetNormalizedTimeAtArc(float arc)
    {
        float length = CurrentSplineLength > 0f ? CurrentSplineLength : GetActualSplineLength();
        if (length <= 0.01f) return 0f;

        float clampedArc = Mathf.Clamp(arc, 0f, length);
        float t = _spline.ConvertIndexUnit(clampedArc, PathIndexUnit.Distance, PathIndexUnit.Normalized);
        return float.IsNaN(t) || float.IsInfinity(t) ? Mathf.Clamp01(clampedArc / length) : Mathf.Clamp01(t);
    }

    float GetArcAtNormalizedTime(float normalizedTime)
    {
        if (_spline == null || _spline.Count < 2) return 0f;

        float t = SanitizeNormalizedTime(normalizedTime);
        float arc = _spline.ConvertIndexUnit(t, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        return float.IsNaN(arc) || float.IsInfinity(arc) ? 0f : Mathf.Clamp(arc, 0f, CurrentSplineLength);
    }

    bool TryGetNearestSplineTime(Vector3 worldPosition, out float t)
    {
        t = 0f;
        if (_spline == null || _spline.Count < 2 || targetSpline == null) return false;

        float3 localPoint = targetSpline.transform.InverseTransformPoint(worldPosition);
        SplineUtility.GetNearestPoint(_spline, localPoint, out _, out t);
        t = Mathf.Clamp01(t);
        return !(float.IsNaN(t) || float.IsInfinity(t));
    }

    float GetActualSplineLength()
    {
        if (_spline == null || _spline.Count < 2 || targetSpline == null) return 0f;
        return _spline.CalculateLength(targetSpline.transform.localToWorldMatrix);
    }

    void SyncSplineLength()
    {
        float actualLength = GetActualSplineLength();
        if (actualLength <= 0f) return;

        _accumulatedLength = actualLength;
        CurrentSplineLength = actualLength;
    }

    static float SanitizeNormalizedTime(float normalizedTime)
    {
        if (float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime)) return 0f;
        if (normalizedTime >= 0f && normalizedTime <= 1f) return normalizedTime;

        float t = normalizedTime % 1f;
        if (t < 0f) t += 1f;
        return Mathf.Approximately(t, 0f) ? 1f : t;
    }

    float SamplePeak(float x, float z)
    {
        float r = _peakSampleRadius;
        float h0 = HeightAt(x, z);
        float h1 = HeightAt(x + r, z);
        float h2 = HeightAt(x - r, z);
        float h3 = HeightAt(x, z + r);
        float h4 = HeightAt(x, z - r);
        return math.max(h0, math.max(math.max(h1, h2), math.max(h3, h4)));
    }

    float HeightAt(float x, float z)
    {
        float baseHeight = NoiseProvider.SampleBaseNoiseStack(_baseNoises, _baseLayerSettings, _permutation, x, z);
        float t = math.saturate((baseHeight + 1f) * 0.5f);
        if (!_heightLUT.IsCreated || _heightLUT.Length < 2)
        {
            return math.max(0f, baseHeight * _baseAmplitude);
        }

        int lenMinus1 = _heightLUT.Length - 1;
        float idx = t * lenMinus1;
        int i0 = (int)idx;
        int i1 = math.min(i0 + 1, lenMinus1);
        float fract = idx - i0;
        float shaped = math.lerp(_heightLUT[i0], _heightLUT[i1], fract);
        float h = shaped * _baseAmplitude;

        return h < 0f ? 0f : h;
    }




    private static readonly ProfilerMarker s_MarkerRebuild      = new ProfilerMarker("SplineGen.Rebuild.Total");
    private static readonly ProfilerMarker s_MarkerRebuildAsync = new ProfilerMarker("SplineGen.Rebuild.UpdateNativeSplineAsync");

    void RebuildNativeSpline()
    {
        var pm = PathMath.Instance;
        if (pm == null || pm.pathSpline == null) return;







        s_MarkerRebuild.Begin();

        s_MarkerRebuildAsync.Begin();
        pm.UpdateNativeSplineAsync(default);
        s_MarkerRebuildAsync.End();

        s_MarkerRebuild.End();
    }
}

[BurstCompile]
public struct NativeHeightSampler
{
    [ReadOnly] public NativeArray<RewriteFastNoiseLite> baseNoises;
    [ReadOnly] public NativeArray<NoiseProvider.BaseNoiseLayerRuntime> baseLayerSettings;
    [ReadOnly] public NativeArray<int> permutation;
    public float baseAmplitude;
    
    [ReadOnly] public NativeArray<float> heightLUT;

    public float HeightAt(float x, float z)
    {
        float baseH = NoiseProvider.SampleBaseNoiseStack(baseNoises, baseLayerSettings, permutation, x, z);
        float t = math.saturate((baseH + 1f) * 0.5f);
        int len = heightLUT.Length;
        if (len < 2)
        {
            return math.max(0f, baseH * baseAmplitude);
        }

        float idx = t * (len - 1);
        int i0 = (int)idx;
        int i1 = math.min(i0 + 1, len - 1);
        float shaped = math.lerp(heightLUT[i0], heightLUT[i1], idx - i0);
        
        float h = shaped * baseAmplitude;
        return math.max(0f, h);
    }

    public float SamplePeak(float x, float z, float r)
    {
        float h0 = HeightAt(x, z);
        float h1 = HeightAt(x + r, z);
        float h2 = HeightAt(x - r, z);
        float h3 = HeightAt(x, z + r);
        float h4 = HeightAt(x, z - r);
        return math.max(h0, math.max(math.max(h1, h2), math.max(h3, h4)));
    }
}

[BurstCompile]
public struct GenerateSplinePointsJob : IJob
{

    public float3 StartPos;
    public float3 StartDir;
    public float SegmentLength;
    public int PointsToGenerate;
    public NativeHeightSampler Sampler;
    public Unity.Mathematics.Random RandomGen;

    public float MaxHeightAboveTerrain;
    public float MaxTurnAngle;
    public float MaxVerticalAngle;
    public float LookAheadDistance;
    public float VariationFrequency;
    public float ValleySeekingStrength;
    public float AltitudeSmoothTime;


    public NativeArray<float3> OutPoints;

    public void Execute()
    {
        float3 currentPos = StartPos;
        float3 currentDir = StartDir;
        float3 up = new float3(0, 1, 0);

        float maxClimbPerSegment = math.tan(math.radians(MaxVerticalAngle)) * SegmentLength;
        float smoothFactor = math.saturate(1.0f / AltitudeSmoothTime);


        const float safetyBuffer = 6f; 

        for (int i = 0; i < PointsToGenerate; i++)
            {
                float totalSteerAngle = 0f;

                if (ValleySeekingStrength > 0f)
                {
                    float3 right = math.normalize(math.cross(up, currentDir));
                    float sideDist = SegmentLength * 2f;

                    float3 ahead = currentPos + currentDir * LookAheadDistance;
                    float hLeft = Sampler.HeightAt(ahead.x - right.x * sideDist, ahead.z - right.z * sideDist);
                    float hRight = Sampler.HeightAt(ahead.x + right.x * sideDist, ahead.z + right.z * sideDist);

                    float diff = (hLeft - hRight) / math.max(Sampler.baseAmplitude, 1f);
                    totalSteerAngle += diff * ValleySeekingStrength;
                }

                if (RandomGen.NextFloat() < VariationFrequency)
                {
                    totalSteerAngle += RandomGen.NextFloat(-MaxTurnAngle, MaxTurnAngle);
                }

                totalSteerAngle = math.clamp(totalSteerAngle, -MaxTurnAngle, MaxTurnAngle);

                if (math.abs(totalSteerAngle) > 0.01f)
                {
                    quaternion rotation = quaternion.AxisAngle(up, math.radians(totalSteerAngle));
                    currentDir = math.rotate(rotation, currentDir);
                }

                currentPos += currentDir * SegmentLength;
                

                float terrainH = Sampler.SamplePeak(currentPos.x, currentPos.z, 20f);
                float targetY = terrainH + (MaxHeightAboveTerrain * 0.5f);

                float3 futurePos = currentPos;



                for (int j = 1; j <= 12; j++)
                {
                    futurePos += currentDir * SegmentLength;
                    float hAhead = Sampler.HeightAt(futurePos.x, futurePos.z);
                    float requiredNow = hAhead + safetyBuffer - (j * maxClimbPerSegment);

                    if (requiredNow > targetY) 
                    {
                        targetY = requiredNow;
                    }
                }


                currentPos.y = math.lerp(currentPos.y, targetY, smoothFactor); 
                



                if (currentPos.y < terrainH + safetyBuffer)
                {
                    currentPos.y = terrainH + safetyBuffer;
                }

                OutPoints[i] = currentPos;
            }
    }
}
