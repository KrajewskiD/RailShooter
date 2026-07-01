using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Serialization;
using System.Collections.Generic;

public enum NoiseHashMode : byte
{
    Hash = 0,
    PermutationTable512 = 1
}

public class NoiseProvider : MonoBehaviour
{
    public static NoiseProvider Instance;
    private const float SimplexTerrainHeightScale = 0.8f;

    public enum NoiseType { Perlin = 0, SimplexNoise = 1, Worley = 2 }
    public enum FractalType { None, FBm, Ridged }
    public enum BaseNoiseBlendMode { Add = 0, Subtract = 1, Multiply = 2, Max = 3, Min = 4 }

    [System.Serializable]
    public class BaseNoiseLayer
    {
        public bool enabled = true;
        public BaseNoiseBlendMode blendMode = BaseNoiseBlendMode.Add;
        [Range(0f, 2f)] public float strength = 1f;
        public NoiseType type = NoiseType.SimplexNoise;
        public FractalType fractal = FractalType.FBm;
        public float frequency = 0.002f;
        [Range(1, 8)] public int octaves = 2;
        public float lacunarity = 2f;
        public float persistence = 0.5f;
        public CellularDistance cellDistance = CellularDistance.EuclideanSq;
        public CellularReturn cellReturn = CellularReturn.Distance;
        [Range(0f, 1f)] public float jitter = 1f;
    }

    public struct BaseNoiseLayerRuntime
    {
        public int enabled;
        public int blendMode;
        public float strength;
    }

    [Header("Execution")]
    public bool useBurst = true;

    [Header("Seed")]
    public int seed = 1337;

    [Header("Hash Mode")]
    public NoiseHashMode hashMode = NoiseHashMode.Hash;

    [System.NonSerialized] private NativeArray<int> _perm512;
    [System.NonSerialized] private int _cachedPermSeed;
    [System.NonSerialized] private bool _hasPermCache;

    public NativeArray<int> Perm512 => _perm512;
    public NoiseHashMode HashMode => hashMode;

    [Header("Terrain Layer — Foundation")]
    public bool baseEnabled = true;
    public float baseAmplitude = 40f;
    public AnimationCurve heightShapeCurve = AnimationCurve.Linear(-1f, -1f, 1f, 1f);
    public List<BaseNoiseLayer> baseNoiseLayers = new List<BaseNoiseLayer>();

    [HideInInspector] public NoiseType baseType = NoiseType.SimplexNoise;
    [HideInInspector] public FractalType baseFractal = FractalType.FBm;
    [HideInInspector] public float baseFrequency = 0.002f;
    [HideInInspector, Range(1, 8)] public int baseOctaves = 2;
    [HideInInspector] public CellularDistance baseCellDistance = CellularDistance.EuclideanSq;
    [HideInInspector] public CellularReturn baseCellReturn = CellularReturn.Distance;
    [HideInInspector, Range(0, 1f)] public float baseJitter = 1f;
    [FormerlySerializedAs("lacunarity"), SerializeField, HideInInspector] private float legacyBaseLacunarity = 2f;
    [FormerlySerializedAs("persistence"), SerializeField, HideInInspector] private float legacyBasePersistence = 0.5f;

    private RewriteFastNoiseLite _temperatureNoiseBurst;
    private RewriteFastNoiseLite _moistureNoiseBurst;
    [System.NonSerialized] private NativeArray<RewriteFastNoiseLite> _baseNoiseLayersBurst;
    [System.NonSerialized] private NativeArray<BaseNoiseLayerRuntime> _baseNoiseLayerSettingsBurst;

    public NativeArray<RewriteFastNoiseLite> BaseNoiseLayersBurst => _baseNoiseLayersBurst;
    public NativeArray<BaseNoiseLayerRuntime> BaseNoiseLayerSettingsBurst => _baseNoiseLayerSettingsBurst;
    public RewriteFastNoiseLite TemperatureNoiseBurst => _temperatureNoiseBurst;
    public RewriteFastNoiseLite MoistureNoiseBurst    => _moistureNoiseBurst;

    [Header("Temperature")]
    public NoiseType temperatureType = NoiseType.Worley;
    public FractalType temperatureFractal = FractalType.None;
    public float temperatureFrequency = 0.003f;
    public int temperatureOctaves = 1;
    public float temperatureLacunarity = 2f;
    public float temperaturePersistence = 0.5f;
    public float temperatureJitter = 1f;
    public CellularDistance temperatureCellDistance = CellularDistance.EuclideanSq;
    public CellularReturn   temperatureCellReturn   = CellularReturn.Distance;
    public bool temperatureEnabled = true;

    [Header("Moisture")]
    public NoiseType moistureType = NoiseType.Worley;
    public FractalType moistureFractal = FractalType.None;
    public float moistureFrequency = 0.003f;
    public int moistureOctaves = 1;
    public float moistureLacunarity = 2f;
    public float moisturePersistence = 0.5f;
    public float moistureJitter = 1f;
    public CellularDistance moistureCellDistance = CellularDistance.EuclideanSq;
    public CellularReturn   moistureCellReturn   = CellularReturn.Distance;
    public bool moistureEnabled = true;

    [Header("Biome Heights (ułamki baseAmplitude)")]
    [Range(0f, 1f)] public float beachTopFraction      = 0.05f;
    [Range(0f, 1f)] public float biomeTopFraction      = 0.10f;
    [Range(0f, 1f)] public float rockStartFraction     = 0.20f;
    [Range(0f, 1f)] public float rockEndFraction       = 0.70f;
    [Range(0f, 1f)] public float snowLineColdFraction  = 0.50f;
    [Range(0f, 1f)] public float snowLineHotFraction   = 1.00f;
    [Range(0f, 1f)] public float snowBandWidthFraction = 0.15f;
    [Range(0f, 1f)] public float valleyTopFraction     = 0.10f;
    [Range(0f, 1f)] public float peakHeightFraction    = 0.85f;

    [Header("Climate Terrain Effects")]
    [Range(0f, 1f)] public float altitudeTemperatureCooling = 0.25f;
    [Range(0f, 1f)] public float valleyMoistureBoost = 0.30f;
    [Range(0f, 1f)] public float peakMoistureDryness = 1.00f;

    [System.NonSerialized] public NativeArray<float> heightCurveLUT;
    private float _cachedAbsoluteMaxHeight;
    private bool _isSetup;
    private const int HeightLutReaderPruneThreshold = 256;
    private readonly List<JobHandle> _heightLutReaders = new List<JobHandle>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SetupNoise();
    }

    private void OnValidate()
    {
        if (heightShapeCurve != null) SetupNoise();
    }

    public void RegisterHeightLutReader(JobHandle reader)
    {
        if (_heightLutReaders.Count >= HeightLutReaderPruneThreshold)
        {
            PruneCompletedHeightLutReaders();
        }

        _heightLutReaders.Add(reader);
    }

    void OnDestroy() {
        CompleteHeightLutReaders();
        DisposeBaseNoiseArrays();
        if (heightCurveLUT.IsCreated) heightCurveLUT.Dispose();
        if (_perm512.IsCreated) _perm512.Dispose();
        _hasPermCache = false;
        _isSetup = false;
        if (Instance == this) Instance = null;
    }

    private void EnsurePermutationCache()
    {
        if (!_perm512.IsCreated)
        {
            _perm512 = new NativeArray<int>(512, Allocator.Persistent);
            _hasPermCache = false;
        }

        if (hashMode != NoiseHashMode.PermutationTable512)
        {
            _hasPermCache = false;
            return;
        }

        if (!_hasPermCache || _cachedPermSeed != seed)
        {
            PermutationTable.BuildPermutation512(seed, _perm512);
            _cachedPermSeed = seed;
            _hasPermCache = true;
        }
    }

    private NativeArray<int> ActivePermutation
    {
        get
        {
            return hashMode == NoiseHashMode.PermutationTable512 ? _perm512 : default;
        }
    }

    public bool IsReadyForTerrainJobs =>
        _isSetup &&
        heightCurveLUT.IsCreated &&
        heightCurveLUT.Length > 1 &&
        _perm512.IsCreated &&
        _perm512.Length >= 512 &&
        _baseNoiseLayersBurst.IsCreated &&
        _baseNoiseLayerSettingsBurst.IsCreated &&
        _baseNoiseLayersBurst.Length > 0 &&
        _baseNoiseLayersBurst.Length == _baseNoiseLayerSettingsBurst.Length;

    public void EnsureReadyForTerrainJobs()
    {
        if (!IsReadyForTerrainJobs)
        {
            SetupNoise();
        }
    }

    public void SetupNoise()
    {
        if (heightShapeCurve == null)
        {
            heightShapeCurve = AnimationCurve.Linear(-1f, -1f, 1f, 1f);
        }

        CompleteHeightLutReaders();
        EnsureBaseNoiseLayers();
        EnsurePermutationCache();
        BuildBaseNoiseStack();

        _temperatureNoiseBurst = BuildBurstLayer(
            temperatureType, temperatureFractal, temperatureFrequency, temperatureOctaves,
            temperatureCellDistance, temperatureCellReturn, temperatureJitter,
            temperatureLacunarity, temperaturePersistence, 2);
        _moistureNoiseBurst = BuildBurstLayer(
            moistureType, moistureFractal, moistureFrequency, moistureOctaves,
            moistureCellDistance, moistureCellReturn, moistureJitter,
            moistureLacunarity, moisturePersistence, 3);

        if (heightCurveLUT.IsCreated) heightCurveLUT.Dispose();
        heightCurveLUT = new NativeArray<float>(256, Allocator.Persistent);

        float maxCurveValue = 0f;
        for (int i = 0; i < 256; i++) {
            float t = i / 255f;
            heightCurveLUT[i] = heightShapeCurve.Evaluate(t * 2f - 1f);

            if (heightCurveLUT[i] > maxCurveValue)
            {
                maxCurveValue = heightCurveLUT[i];
            }
        }

        _cachedAbsoluteMaxHeight = maxCurveValue * baseAmplitude;
        _isSetup = true;
    }

    private void CompleteHeightLutReaders()
    {
        for (int i = 0; i < _heightLutReaders.Count; i++)
        {
            _heightLutReaders[i].Complete();
        }
        _heightLutReaders.Clear();
    }

    private void PruneCompletedHeightLutReaders()
    {
        for (int i = _heightLutReaders.Count - 1; i >= 0; i--)
        {
            if (_heightLutReaders[i].IsCompleted)
            {
                _heightLutReaders.RemoveAt(i);
            }
        }
    }

    private void DisposeBaseNoiseArrays()
    {
        if (_baseNoiseLayersBurst.IsCreated) _baseNoiseLayersBurst.Dispose();
        if (_baseNoiseLayerSettingsBurst.IsCreated) _baseNoiseLayerSettingsBurst.Dispose();
    }

    private void EnsureBaseNoiseLayers()
    {
        if (baseNoiseLayers == null)
        {
            baseNoiseLayers = new List<BaseNoiseLayer>(1);
        }

        if (baseNoiseLayers.Count == 0)
        {
            baseNoiseLayers.Add(CreateLegacyBaseLayer());
        }

        for (int i = 0; i < baseNoiseLayers.Count; i++)
        {
            if (baseNoiseLayers[i] == null)
            {
                baseNoiseLayers[i] = CreateDefaultBaseLayer(i);
            }

            SanitizeBaseLayer(baseNoiseLayers[i]);
        }
    }

    private void BuildBaseNoiseStack()
    {
        DisposeBaseNoiseArrays();

        int layerCount = math.max(1, baseNoiseLayers.Count);
        _baseNoiseLayersBurst = new NativeArray<RewriteFastNoiseLite>(layerCount, Allocator.Persistent);
        _baseNoiseLayerSettingsBurst = new NativeArray<BaseNoiseLayerRuntime>(layerCount, Allocator.Persistent);

        for (int i = 0; i < layerCount; i++)
        {
            BaseNoiseLayer layer = i < baseNoiseLayers.Count ? baseNoiseLayers[i] : CreateDefaultBaseLayer(i);
            SanitizeBaseLayer(layer);

            _baseNoiseLayersBurst[i] = BuildBurstLayer(
                layer.type, layer.fractal, layer.frequency, layer.octaves,
                layer.cellDistance, layer.cellReturn, layer.jitter,
                layer.lacunarity, layer.persistence, i * 1013);

            _baseNoiseLayerSettingsBurst[i] = new BaseNoiseLayerRuntime
            {
                enabled = baseEnabled && layer.enabled ? 1 : 0,
                blendMode = i == 0 ? (int)BaseNoiseBlendMode.Add : (int)layer.blendMode,
                strength = layer.strength
            };
        }
    }

    private BaseNoiseLayer CreateLegacyBaseLayer()
    {
        return new BaseNoiseLayer
        {
            enabled = true,
            blendMode = BaseNoiseBlendMode.Add,
            strength = 1f,
            type = baseType,
            fractal = baseFractal,
            frequency = baseFrequency,
            octaves = baseOctaves,
            lacunarity = legacyBaseLacunarity,
            persistence = legacyBasePersistence,
            cellDistance = baseCellDistance,
            cellReturn = baseCellReturn,
            jitter = baseJitter
        };
    }

    public static BaseNoiseLayer CreateDefaultBaseLayer(int index)
    {
        return new BaseNoiseLayer
        {
            enabled = true,
            blendMode = BaseNoiseBlendMode.Add,
            strength = index == 0 ? 1f : 0.25f,
            type = NoiseType.SimplexNoise,
            fractal = FractalType.FBm,
            frequency = index == 0 ? 0.002f : 0.006f,
            octaves = index == 0 ? 2 : 3,
            lacunarity = 2f,
            persistence = 0.5f,
            cellDistance = CellularDistance.EuclideanSq,
            cellReturn = CellularReturn.Distance,
            jitter = 1f
        };
    }

    private static void SanitizeBaseLayer(BaseNoiseLayer layer)
    {
        layer.frequency = math.max(0.000001f, layer.frequency);
        layer.octaves = math.clamp(layer.octaves, 1, 8);
        layer.lacunarity = math.max(1f, layer.lacunarity);
        layer.persistence = math.clamp(layer.persistence, 0f, 1f);
        layer.strength = math.max(0f, layer.strength);
        layer.jitter = math.clamp(layer.jitter, 0f, 1f);
    }

    public float SampleBase(float x, float z)
    {
        EnsureReadyForTerrainJobs();
        return SampleBaseNoiseStack(_baseNoiseLayersBurst, _baseNoiseLayerSettingsBurst, ActivePermutation, x, z) * baseAmplitude;
    }

    public float SampleTemperature(float x, float z)
    {
        EnsureReadyForTerrainJobs();
        if (!temperatureEnabled)
        {
            return ChunkGenerator.ClimateDisabledValue;
        }

        return ChunkGenerator.NormalizeTemperatureNoise(_temperatureNoiseBurst.GetNoise2D(x, z, ActivePermutation));
    }
    public float SampleMoisture(float x, float z)
    {
        EnsureReadyForTerrainJobs();
        if (!moistureEnabled)
        {
            return ChunkGenerator.ClimateDisabledValue;
        }

        return ChunkGenerator.NormalizeMoistureNoise(_moistureNoiseBurst.GetNoise2D(x, z, ActivePermutation));
    }

    public ChunkGenerator.ClimateSample SampleEffectiveClimate(float x, float z)
    {
        EnsureReadyForTerrainJobs();
        float terrainHeight = SampleVisualHeight(x, z);
        return ChunkGenerator.SampleClimate(
            _temperatureNoiseBurst,
            _moistureNoiseBurst,
            ActivePermutation,
            x,
            z,
            terrainHeight,
            ChunkGenerator.BuildBiomeParams(this),
            true);
    }


    public float GetBiomeHeight(float x, float z)
    {
        float h = SampleVisualHeight(x, z);
        ChunkGenerator.ClimateSample climate = ChunkGenerator.SampleClimate(
            _temperatureNoiseBurst,
            _moistureNoiseBurst,
            ActivePermutation,
            x,
            z,
            h,
            ChunkGenerator.BuildBiomeParams(this),
            true);
        float biomeHeightModulator = Mathf.Lerp(0.8f, 1.2f, climate.temperature * climate.moisture);
        
        return h * biomeHeightModulator;
    }

    public float SampleVisualHeight(float x, float z)
    {
        EnsureReadyForTerrainJobs();
        float baseHeight = SampleBaseNoiseStack(_baseNoiseLayersBurst, _baseNoiseLayerSettingsBurst, ActivePermutation, x, z);
        float t = Mathf.Clamp01((baseHeight + 1f) * 0.5f);
        float shaped;
        if (heightCurveLUT.IsCreated && heightCurveLUT.Length > 1)
        {
            float idx = t * (heightCurveLUT.Length - 1);
            int i0 = (int)idx;
            int i1 = Mathf.Min(i0 + 1, heightCurveLUT.Length - 1);
            float fract = idx - i0;
            shaped = Mathf.Lerp(heightCurveLUT[i0], heightCurveLUT[i1], fract);
        }
        else
        {
            shaped = heightShapeCurve.Evaluate(t * 2f - 1f);
        }
        float h = shaped * baseAmplitude;

        if (h < 0f) h = 0f;
        return h;
    }

    public float GetAbsoluteMaxHeight()
    {
        return _cachedAbsoluteMaxHeight;
    }

    private RewriteFastNoiseLite BuildBurstLayer(
        NoiseType type, FractalType fractal,
        float frequency, int octaves,
        CellularDistance cellDist, CellularReturn cellRet, float cellJitter,
        float layerLacunarity, float layerPersistence, int seedOffset)
    {
        return new RewriteFastNoiseLite
        {
            seed = seed + seedOffset,
            noiseType = MapNoiseType(type),
            fractalType = fractal switch {
                FractalType.None   => RewriteFastNoiseLite.FractalType.None,
                FractalType.FBm    => RewriteFastNoiseLite.FractalType.FBm,
                FractalType.Ridged => RewriteFastNoiseLite.FractalType.Ridged,
                _                  => RewriteFastNoiseLite.FractalType.None
            },
            cellularDistance = (RewriteFastNoiseLite.CellularDistanceFunction)(int)cellDist,
            cellularReturn = (RewriteFastNoiseLite.CellularReturnType)(int)NormalizeWorleyReturn(cellRet),
            frequency = frequency,
            octaves = octaves,
            lacunarity = layerLacunarity,
            gain = layerPersistence,
            cellularJitter = cellJitter,
            hashMode = hashMode
        };
    }

    public static float SampleBaseNoiseStack(
        NativeArray<RewriteFastNoiseLite> noises,
        NativeArray<BaseNoiseLayerRuntime> layers,
        float x,
        float z)
    {
        return SampleBaseNoiseStack(noises, layers, default, x, z);
    }

    public static float SampleBaseNoiseStack(
        NativeArray<RewriteFastNoiseLite> noises,
        NativeArray<BaseNoiseLayerRuntime> layers,
        NativeArray<int> permutation,
        float x,
        float z)
    {
        int count = math.min(noises.Length, layers.Length);
        if (count <= 0)
        {
            return 0f;
        }

        float value = 0f;
        bool hasValue = false;

        for (int i = 0; i < count; i++)
        {
            BaseNoiseLayerRuntime layer = layers[i];
            if (layer.enabled == 0 || layer.strength <= 0f)
            {
                continue;
            }

            float sample = NormalizeBaseNoiseSample(noises[i], noises[i].GetNoise2D(x, z, permutation));
            float weighted = sample * layer.strength;

            if (!hasValue)
            {
                value = weighted;
                hasValue = true;
                continue;
            }

            switch (layer.blendMode)
            {
                case (int)BaseNoiseBlendMode.Subtract:
                    value -= weighted;
                    break;

                case (int)BaseNoiseBlendMode.Multiply:
                    float factor = 1f + sample * layer.strength;
                    value *= math.max(0f, factor);
                    break;

                case (int)BaseNoiseBlendMode.Max:
                    value = math.max(value, weighted);
                    break;

                case (int)BaseNoiseBlendMode.Min:
                    value = math.min(value, weighted);
                    break;

                default:
                    value += weighted;
                    break;
            }
        }

        return hasValue ? math.clamp(value, -1f, 1f) : 0f;
    }

    private static float NormalizeBaseNoiseSample(RewriteFastNoiseLite noise, float sample)
    {
        if (noise.noiseType == RewriteFastNoiseLite.NoiseType.SimplexNoise)
        {
            sample *= SimplexTerrainHeightScale;
        }

        return math.clamp(sample, -1f, 1f);
    }

    private static RewriteFastNoiseLite.NoiseType MapNoiseType(NoiseType type) => type switch {
        NoiseType.Perlin => RewriteFastNoiseLite.NoiseType.Perlin,
        NoiseType.Worley => RewriteFastNoiseLite.NoiseType.Worley,
        _ => RewriteFastNoiseLite.NoiseType.SimplexNoise
    };

    public static CellularReturn NormalizeWorleyReturn(CellularReturn cellReturn)
    {
        switch (cellReturn)
        {
            case CellularReturn.Distance2:
            case CellularReturn.Distance2Add:
            case CellularReturn.Distance2Sub:
                return cellReturn;
            case CellularReturn.Distance:
            case CellularReturn.CellValue:
            default:
                return CellularReturn.Distance;
        }
    }

}
