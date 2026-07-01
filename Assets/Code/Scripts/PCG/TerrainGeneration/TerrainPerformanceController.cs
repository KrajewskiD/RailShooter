using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class TerrainPerformanceController : MonoBehaviour
{
    private const string UiAssetPath = "UI/TerrainPerformance";
    private const string PanelSettingsPath = "UI/TerrainPerformancePanelSettings";
    private static readonly string[] SpinnerFrames = { "|", "/", "-", "\\" };
    private static readonly DimensionalNoiseBenchmarkCase[] DimensionalNoiseCases =
    {
        new DimensionalNoiseBenchmarkCase("Perlin 2D [Hash]", RewriteFastNoiseLite.NoiseType.Perlin, 2),
        new DimensionalNoiseBenchmarkCase("Simplex 2D [Hash]", RewriteFastNoiseLite.NoiseType.SimplexNoise, 2),
        new DimensionalNoiseBenchmarkCase("Perlin 2D [PermutationTable512]", RewriteFastNoiseLite.NoiseType.Perlin, 2, NoiseHashMode.PermutationTable512),
        new DimensionalNoiseBenchmarkCase("Simplex 2D [PermutationTable512]", RewriteFastNoiseLite.NoiseType.SimplexNoise, 2, NoiseHashMode.PermutationTable512),
        new DimensionalNoiseBenchmarkCase("Perlin 3D [Hash]", RewriteFastNoiseLite.NoiseType.Perlin, 3),
        new DimensionalNoiseBenchmarkCase("Simplex 3D [Hash]", RewriteFastNoiseLite.NoiseType.SimplexNoise, 3),
        new DimensionalNoiseBenchmarkCase("Perlin 3D [PermutationTable512]", RewriteFastNoiseLite.NoiseType.Perlin, 3, NoiseHashMode.PermutationTable512),
        new DimensionalNoiseBenchmarkCase("Simplex 3D [PermutationTable512]", RewriteFastNoiseLite.NoiseType.SimplexNoise, 3, NoiseHashMode.PermutationTable512)
    };

    [Serializable]
    public class ChunkBenchmarkConfig
    {
        public bool enabled = true;
        public string label = "Chunk 256 / Res 1";
        [Min(1f)] public float chunkSize = 256f;
        [Min(0.1f)] public float resolution = 1f;
        public Vector2 sampleOrigin = Vector2.zero;
    }

    [Serializable]
    public class NoiseBenchmarkConfig
    {
        public bool enabled = true;
        public string label = "Simplex None";
        public bool useLayerStack;
        public NoiseProvider.NoiseType type = NoiseProvider.NoiseType.SimplexNoise;
        public NoiseProvider.FractalType fractal = NoiseProvider.FractalType.None;
        public NoiseHashMode hashMode = NoiseHashMode.Hash;
        [Range(0f, 2f)] public float strength = 1f;
        public float frequency = 0.002f;
        [Range(1, 8)] public int octaves = 4;
        public float lacunarity = 2f;
        [Range(0f, 1f)] public float persistence = 0.5f;
        public CellularDistance cellDistance = CellularDistance.EuclideanSq;
        public CellularReturn cellReturn = CellularReturn.Distance;
        [Range(0f, 1f)] public float jitter = 1f;
        public List<NoiseProvider.BaseNoiseLayer> baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
    }

    private struct DimensionalNoiseBenchmarkCase
    {
        public readonly string label;
        public readonly RewriteFastNoiseLite.NoiseType noiseType;
        public readonly int dimensions;
        public readonly NoiseHashMode hashMode;

        public DimensionalNoiseBenchmarkCase(
            string label,
            RewriteFastNoiseLite.NoiseType noiseType,
            int dimensions,
            NoiseHashMode hashMode = NoiseHashMode.Hash)
        {
            this.label = label;
            this.noiseType = noiseType;
            this.dimensions = dimensions;
            this.hashMode = hashMode;
        }
    }

    [Header("Scene References")]
    [SerializeField] private VisualTreeAsset visualTreeAsset;
    [SerializeField] private PanelSettings panelSettings;
    [SerializeField] private NoiseProvider noiseProvider;
    [SerializeField] private ChunkGenerator chunkGenerator;
    [SerializeField] private Material chunkMaterial;

    [Header("Benchmark")]
    [SerializeField, Min(1)] private int repetitions = 30;
    [SerializeField, Min(1)] private int noiseEvalSamples = 1000000;
    [SerializeField, Min(1f)] private float chunkSize = 256f;
    [SerializeField, Min(0.1f)] private float resolution = 1f;
    [SerializeField] private Vector2 sampleOrigin = Vector2.zero;
    [SerializeField] private string csvFilePrefix = "terrain_performance";

    [Header("Batch")]
    [SerializeField] private List<ChunkBenchmarkConfig> chunkValues = new List<ChunkBenchmarkConfig>();
    [SerializeField] private List<NoiseBenchmarkConfig> noiseCases = new List<NoiseBenchmarkConfig>();

    private UIDocument _document;
    private Button _startButton;
    private Label _statusLabel;
    private Label _spinnerLabel;
    private Label _progressLabel;
    private Label _configLabel;
    private Label _mathResultLabel;
    private Label _chunkResultLabel;
    private VisualElement _progressFill;
    private Coroutine _benchmarkRoutine;
    private Material _runtimeChunkMaterial;
    private bool _isRunning;
    private float _spinnerTimer;
    private int _spinnerIndex;
    private static float s_NoiseEvalSink;
    private static float s_DimensionalNoiseEvalSink;

    private void Reset()
    {
        EnsureBatchDefaults();
    }

    private void OnValidate()
    {
        EnsureBatchDefaults();
        SanitizeBatchValues();
    }

    private void Awake()
    {
        EnsureBatchDefaults();
        SanitizeBatchValues();
        _document = GetComponent<UIDocument>();
        EnsureDocumentAssets();
        ResolveNoiseProvider();
        EnsureChunkGenerator();
    }

    private void OnEnable()
    {
        CacheUi();
        if (_startButton != null)
        {
            _startButton.clicked += StartBenchmark;
        }

        SetIdleUi();
    }

    private void OnDisable()
    {
        if (_startButton != null)
        {
            _startButton.clicked -= StartBenchmark;
        }
    }

    private void OnDestroy()
    {
        if (_runtimeChunkMaterial != null)
        {
            Destroy(_runtimeChunkMaterial);
            _runtimeChunkMaterial = null;
        }
    }

    private void Update()
    {
        if (!_isRunning || _spinnerLabel == null)
        {
            return;
        }

        _spinnerTimer += Time.unscaledDeltaTime;
        if (_spinnerTimer < 0.12f)
        {
            return;
        }

        _spinnerTimer = 0f;
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        _spinnerLabel.text = SpinnerFrames[_spinnerIndex];
    }

    private void EnsureDocumentAssets()
    {
        if (visualTreeAsset == null)
        {
            visualTreeAsset = Resources.Load<VisualTreeAsset>(UiAssetPath);
        }

        if (_document.visualTreeAsset == null && visualTreeAsset != null)
        {
            _document.visualTreeAsset = visualTreeAsset;
        }

        PanelSettings source = _document.panelSettings != null
            ? _document.panelSettings
            : (panelSettings != null
                ? panelSettings
                : Resources.Load<PanelSettings>(PanelSettingsPath));

        panelSettings = source != null
            ? Instantiate(source)
            : ScriptableObject.CreateInstance<PanelSettings>();

        panelSettings.sortingOrder = 50;
        _document.panelSettings = panelSettings;
    }

    private void CacheUi()
    {
        VisualElement root = _document.rootVisualElement?.Q<VisualElement>("terrain-performance-root");
        if (root == null)
        {
            return;
        }

        _startButton = root.Q<Button>("start-button");
        _statusLabel = root.Q<Label>("status-label");
        _spinnerLabel = root.Q<Label>("spinner-label");
        _progressLabel = root.Q<Label>("progress-label");
        _configLabel = root.Q<Label>("config-label");
        _mathResultLabel = root.Q<Label>("math-result-label");
        _chunkResultLabel = root.Q<Label>("chunk-result-label");
        _progressFill = root.Q<VisualElement>("progress-fill");
    }

    private void ResolveNoiseProvider()
    {
        if (NoiseProvider.Instance != null)
        {
            noiseProvider = NoiseProvider.Instance;
            return;
        }

        if (noiseProvider == null)
        {
            noiseProvider = GetComponent<NoiseProvider>();
        }

        if (noiseProvider == null)
        {
            noiseProvider = FindObjectOfType<NoiseProvider>();
        }

        if (noiseProvider == null)
        {
            GameObject providerObject = new GameObject("NoiseProvider");
            noiseProvider = providerObject.AddComponent<NoiseProvider>();
        }
    }

    private void EnsureChunkGenerator()
    {
        if (chunkGenerator == null)
        {
            GameObject chunkObject = new GameObject("PerformanceChunk");
            chunkObject.transform.SetParent(transform);
            chunkObject.transform.localPosition = Vector3.zero;
            chunkObject.transform.localRotation = Quaternion.identity;
            chunkObject.transform.localScale = Vector3.one;

            chunkObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveChunkMaterial();
            chunkGenerator = chunkObject.AddComponent<ChunkGenerator>();
        }

        chunkGenerator.transform.position = new Vector3(-chunkSize * 0.5f, 0f, -chunkSize * 0.5f);
        chunkGenerator.SetPhysicsEnabled(false);

        MeshRenderer chunkRenderer = chunkGenerator.GetComponent<MeshRenderer>();
        if (chunkRenderer != null && chunkRenderer.sharedMaterial == null)
        {
            chunkRenderer.sharedMaterial = ResolveChunkMaterial();
        }
    }

    private void EnsureBatchDefaults()
    {
        if (chunkValues == null)
        {
            chunkValues = new List<ChunkBenchmarkConfig>();
        }

        if (chunkValues.Count == 0)
        {
            chunkValues.Add(new ChunkBenchmarkConfig
            {
                enabled = true,
                label = "Chunk 256 / Res 1",
                chunkSize = chunkSize,
                resolution = resolution,
                sampleOrigin = sampleOrigin
            });
        }

        if (noiseCases == null)
        {
            noiseCases = new List<NoiseBenchmarkConfig>();
        }
    }

    private static bool UsesLayerStack(NoiseBenchmarkConfig noiseCase)
    {
        return noiseCase != null &&
            noiseCase.useLayerStack &&
            noiseCase.baseNoiseLayers != null &&
            noiseCase.baseNoiseLayers.Count > 0;
    }

    private void SanitizeBatchValues()
    {
        repetitions = Mathf.Max(1, repetitions);
        noiseEvalSamples = Mathf.Max(1, noiseEvalSamples);
        chunkSize = Mathf.Max(1f, chunkSize);
        resolution = Mathf.Max(0.1f, resolution);

        if (chunkValues != null)
        {
            for (int i = 0; i < chunkValues.Count; i++)
            {
                ChunkBenchmarkConfig chunkConfig = chunkValues[i];
                if (chunkConfig == null)
                {
                    continue;
                }

                chunkConfig.chunkSize = Mathf.Max(1f, chunkConfig.chunkSize);
                chunkConfig.resolution = Mathf.Max(0.1f, chunkConfig.resolution);
                if (string.IsNullOrWhiteSpace(chunkConfig.label))
                {
                    chunkConfig.label = $"Chunk {chunkConfig.chunkSize:0.#} / Res {chunkConfig.resolution:0.###}";
                }
            }
        }

        if (noiseCases != null)
        {
            for (int i = 0; i < noiseCases.Count; i++)
            {
                NoiseBenchmarkConfig noiseCase = noiseCases[i];
                if (noiseCase == null)
                {
                    continue;
                }

                noiseCase.strength = Mathf.Max(0f, noiseCase.strength);
                noiseCase.frequency = Mathf.Max(0.000001f, noiseCase.frequency);
                noiseCase.octaves = Mathf.Clamp(noiseCase.octaves, 1, 8);
                noiseCase.lacunarity = Mathf.Max(1f, noiseCase.lacunarity);
                noiseCase.persistence = Mathf.Clamp01(noiseCase.persistence);
                noiseCase.jitter = Mathf.Clamp01(noiseCase.jitter);
                if (!noiseCase.useLayerStack && noiseCase.type == NoiseProvider.NoiseType.Worley)
                {
                    noiseCase.hashMode = NoiseHashMode.Hash;
                }
                if (noiseCase.useLayerStack)
                {
                    SanitizeBenchmarkLayerStack(noiseCase);
                }
                if (string.IsNullOrWhiteSpace(noiseCase.label))
                {
                    noiseCase.label = noiseCase.useLayerStack
                        ? "Mixed Terrain Stack"
                        : $"{noiseCase.type} {noiseCase.fractal}";
                }
            }

            RemoveDuplicateNoiseCases(noiseCases);
        }
    }

    private static void SanitizeBenchmarkLayerStack(NoiseBenchmarkConfig noiseCase)
    {
        if (noiseCase.baseNoiseLayers == null)
        {
            noiseCase.baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
        }

        if (noiseCase.baseNoiseLayers.Count == 0)
        {
            noiseCase.baseNoiseLayers.Add(NoiseProvider.CreateDefaultBaseLayer(0));
        }

        for (int i = 0; i < noiseCase.baseNoiseLayers.Count; i++)
        {
            NoiseProvider.BaseNoiseLayer layer = noiseCase.baseNoiseLayers[i];
            if (layer == null)
            {
                layer = NoiseProvider.CreateDefaultBaseLayer(i);
                noiseCase.baseNoiseLayers[i] = layer;
            }

            SanitizeBenchmarkLayer(layer);
        }
    }

    private static void SanitizeBenchmarkLayer(NoiseProvider.BaseNoiseLayer layer)
    {
        layer.frequency = Mathf.Max(0.000001f, layer.frequency);
        layer.octaves = Mathf.Clamp(layer.octaves, 1, 8);
        layer.lacunarity = Mathf.Max(1f, layer.lacunarity);
        layer.persistence = Mathf.Clamp01(layer.persistence);
        layer.strength = Mathf.Max(0f, layer.strength);
        layer.jitter = Mathf.Clamp01(layer.jitter);
    }

    private static void RemoveDuplicateNoiseCases(List<NoiseBenchmarkConfig> cases)
    {
        for (int i = cases.Count - 1; i >= 0; i--)
        {
            NoiseBenchmarkConfig current = cases[i];
            if (current == null)
            {
                continue;
            }

            for (int j = 0; j < i; j++)
            {
                NoiseBenchmarkConfig previous = cases[j];
                if (previous != null && AreEquivalentNoiseCases(previous, current))
                {
                    cases.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private static bool AreEquivalentNoiseCases(NoiseBenchmarkConfig a, NoiseBenchmarkConfig b)
    {
        if (a.enabled != b.enabled ||
            a.useLayerStack != b.useLayerStack ||
            a.hashMode != b.hashMode)
        {
            return false;
        }

        if (a.useLayerStack)
        {
            return AreEquivalentLayerStacks(a.baseNoiseLayers, b.baseNoiseLayers);
        }

        return
            a.type == b.type &&
            a.fractal == b.fractal &&
            Mathf.Approximately(a.strength, b.strength) &&
            Mathf.Approximately(a.frequency, b.frequency) &&
            a.octaves == b.octaves &&
            Mathf.Approximately(a.lacunarity, b.lacunarity) &&
            Mathf.Approximately(a.persistence, b.persistence) &&
            a.cellDistance == b.cellDistance &&
            a.cellReturn == b.cellReturn &&
            Mathf.Approximately(a.jitter, b.jitter);
    }

    private static bool AreEquivalentLayerStacks(
        List<NoiseProvider.BaseNoiseLayer> a,
        List<NoiseProvider.BaseNoiseLayer> b)
    {
        int aCount = a != null ? a.Count : 0;
        int bCount = b != null ? b.Count : 0;
        if (aCount != bCount)
        {
            return false;
        }

        for (int i = 0; i < aCount; i++)
        {
            if (!AreEquivalentLayers(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalentLayers(NoiseProvider.BaseNoiseLayer a, NoiseProvider.BaseNoiseLayer b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return a.enabled == b.enabled &&
            a.blendMode == b.blendMode &&
            Mathf.Approximately(a.strength, b.strength) &&
            a.type == b.type &&
            a.fractal == b.fractal &&
            Mathf.Approximately(a.frequency, b.frequency) &&
            a.octaves == b.octaves &&
            Mathf.Approximately(a.lacunarity, b.lacunarity) &&
            Mathf.Approximately(a.persistence, b.persistence) &&
            a.cellDistance == b.cellDistance &&
            a.cellReturn == b.cellReturn &&
            Mathf.Approximately(a.jitter, b.jitter);
    }

    private List<ChunkBenchmarkConfig> GetEnabledChunkValues()
    {
        EnsureBatchDefaults();
        SanitizeBatchValues();

        List<ChunkBenchmarkConfig> enabledValues = new List<ChunkBenchmarkConfig>();
        for (int i = 0; i < chunkValues.Count; i++)
        {
            ChunkBenchmarkConfig chunkConfig = chunkValues[i];
            if (chunkConfig != null && chunkConfig.enabled)
            {
                enabledValues.Add(chunkConfig);
            }
        }

        return enabledValues;
    }

    private List<NoiseBenchmarkConfig> GetEnabledNoiseCases()
    {
        EnsureBatchDefaults();
        SanitizeBatchValues();

        List<NoiseBenchmarkConfig> enabledCases = new List<NoiseBenchmarkConfig>();
        for (int i = 0; i < noiseCases.Count; i++)
        {
            NoiseBenchmarkConfig noiseCase = noiseCases[i];
            if (noiseCase != null && noiseCase.enabled)
            {
                enabledCases.Add(noiseCase);
            }
        }

        return enabledCases;
    }

    private Material ResolveChunkMaterial()
    {
        if (chunkMaterial != null)
        {
            return chunkMaterial;
        }

        if (_runtimeChunkMaterial != null)
        {
            return _runtimeChunkMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        _runtimeChunkMaterial = new Material(shader)
        {
            color = new Color(0.54f, 0.72f, 0.62f, 1f)
        };
        return _runtimeChunkMaterial;
    }

    private void SetIdleUi()
    {
        _isRunning = false;
        if (_startButton != null)
        {
            _startButton.SetEnabled(true);
        }

        SetStatus("Ready");
        SetProgress(0f);
        if (_spinnerLabel != null)
        {
            _spinnerLabel.text = "";
        }

        RefreshConfigLabel();
    }

    private void RefreshConfigLabel()
    {
        if (_configLabel == null)
        {
            return;
        }

        int chunkCount = GetEnabledChunkValues().Count;
        int noiseCount = GetEnabledNoiseCases().Count;
        int combinations = chunkCount * noiseCount;
        int dimensionalMicrobenchmarks = chunkCount * DimensionalNoiseCases.Length * repetitions;
        int measurements = combinations * repetitions * 4 + dimensionalMicrobenchmarks;
        _configLabel.text =
            $"Chunks: {chunkCount} | Noise cases: {noiseCount} | Repeats: {repetitions}\n" +
            $"Measurements: {measurements} | Eval samples: {noiseEvalSamples}\n" +
            $"Dimensional cases: {DimensionalNoiseCases.Length} | Backend: Burst";
    }

    private void StartBenchmark()
    {
        if (_benchmarkRoutine != null)
        {
            return;
        }

        _benchmarkRoutine = StartCoroutine(RunBenchmark());
    }

    private IEnumerator RunBenchmark()
    {
        _isRunning = true;
        _spinnerIndex = 0;
        _spinnerTimer = 0f;
        if (_spinnerLabel != null)
        {
            _spinnerLabel.text = SpinnerFrames[0];
        }

        if (_startButton != null)
        {
            _startButton.SetEnabled(false);
        }

        SetStatus("Collecting data: preparing benchmark");
        SetProgress(0f);
        RefreshConfigLabel();
        yield return null;

        NativeArray<float4> mathOutput = default;
        NativeArray<int> dimensionalPermutation = default;
        NoiseProviderSnapshot noiseSnapshot = default;
        bool hasNoiseSnapshot = false;

        try
        {
            ResolveNoiseProvider();
            noiseSnapshot = CaptureNoiseProviderSnapshot();
            hasNoiseSnapshot = true;
            noiseProvider.useBurst = true;
            RefreshConfigLabel();
            dimensionalPermutation = new NativeArray<int>(512, Allocator.Persistent);
            PermutationTable.BuildPermutation512(noiseProvider != null ? noiseProvider.seed : 1337, dimensionalPermutation);

            List<ChunkBenchmarkConfig> enabledChunks = GetEnabledChunkValues();
            List<NoiseBenchmarkConfig> enabledNoiseCases = GetEnabledNoiseCases();
            if (enabledChunks.Count == 0 || enabledNoiseCases.Count == 0)
            {
                SetStatus("No enabled chunk values or noise cases.");
                yield break;
            }

            int totalCombinations = enabledChunks.Count * enabledNoiseCases.Count;
            int dimensionalMicrobenchmarkSteps = enabledChunks.Count * DimensionalNoiseCases.Length * repetitions;
            int totalSteps = totalCombinations * repetitions * 4 + dimensionalMicrobenchmarkSteps;
            int step = 0;
            int combinationIndex = 0;
            List<double> allDimensionalNoiseSamples = new List<double>(dimensionalMicrobenchmarkSteps);
            List<double> allNoiseEvalSamples = new List<double>(totalCombinations * repetitions);
            List<double> allSingleThreadMathSamples = new List<double>(totalCombinations * repetitions);
            List<double> allMathSamples = new List<double>(totalCombinations * repetitions);
            List<double> allChunkSamples = new List<double>(totalCombinations * repetitions);
            List<MeasurementRecord> records = new List<MeasurementRecord>(totalCombinations * repetitions * 4 + dimensionalMicrobenchmarkSteps);

            for (int chunkIndex = 0; chunkIndex < enabledChunks.Count; chunkIndex++)
            {
                ChunkBenchmarkConfig chunkConfig = enabledChunks[chunkIndex];
                ApplyChunkConfig(chunkConfig);

                SetStatus($"Warmup dimensional noise microbenchmark: {chunkConfig.label}");
                yield return null;
                for (int noiseIndex = 0; noiseIndex < DimensionalNoiseCases.Length; noiseIndex++)
                {
                    MeasureDimensionalNoiseEvalOnce(DimensionalNoiseCases[noiseIndex], noiseEvalSamples, dimensionalPermutation);
                }

                for (int noiseIndex = 0; noiseIndex < DimensionalNoiseCases.Length; noiseIndex++)
                {
                    DimensionalNoiseBenchmarkCase microCase = DimensionalNoiseCases[noiseIndex];
                    List<double> dimensionalSamples = new List<double>(repetitions);
                    for (int i = 0; i < repetitions; i++)
                    {
                        SetStatus($"Collecting data: {microCase.label}, {chunkConfig.label}, dimensional noise eval {i + 1}/{repetitions}");
                        yield return null;
                        double elapsedMs = MeasureDimensionalNoiseEvalOnce(microCase, noiseEvalSamples, dimensionalPermutation);
                        dimensionalSamples.Add(elapsedMs);
                        allDimensionalNoiseSamples.Add(elapsedMs);
                        records.Add(new MeasurementRecord("noise_eval_dimensional", i + 1, elapsedMs, "MainThread", chunkConfig, microCase.label, noiseEvalSamples));
                        step++;
                        SetProgress(step / (float)totalSteps);
                        if (_mathResultLabel != null)
                        {
                            _mathResultLabel.text = FormatStats("Dimensional noise eval", dimensionalSamples);
                        }
                    }
                }
            }

            for (int noiseIndex = 0; noiseIndex < enabledNoiseCases.Count; noiseIndex++)
            {
                NoiseBenchmarkConfig noiseCase = enabledNoiseCases[noiseIndex];
                ApplyNoiseCase(noiseCase);
                noiseProvider.SetupNoise();
                noiseProvider.EnsureReadyForTerrainJobs();

                for (int chunkIndex = 0; chunkIndex < enabledChunks.Count; chunkIndex++)
                {
                    ChunkBenchmarkConfig chunkConfig = enabledChunks[chunkIndex];
                    combinationIndex++;
                    ApplyChunkConfig(chunkConfig);
                    EnsureChunkGenerator();

                    if (mathOutput.IsCreated)
                    {
                        mathOutput.Dispose();
                    }

                    int totalSamples = CalculateBorderedSampleCount(out int borderedVCount);
                    mathOutput = new NativeArray<float4>(totalSamples, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                    SetStatus($"Warmup {combinationIndex}/{totalCombinations}: {noiseCase.label}, {chunkConfig.label}");
                    yield return null;
                    MeasureNoiseEvalOnce(noiseEvalSamples, UsesLayerStack(noiseCase));
                    MeasureNoiseMathSingleThreadOnce(mathOutput, borderedVCount, totalSamples);
                    MeasureNoiseMathOnce(mathOutput, borderedVCount, totalSamples);
                    MeasureChunkOnce(-1);
                    yield return null;

                    List<double> noiseEvalDurations = new List<double>(repetitions);
                    List<double> singleThreadMathSamples = new List<double>(repetitions);
                    List<double> mathSamples = new List<double>(repetitions);
                    List<double> chunkSamples = new List<double>(repetitions);

                    for (int i = 0; i < repetitions; i++)
                    {
                        SetStatus($"Collecting data {combinationIndex}/{totalCombinations}: {noiseCase.label}, {chunkConfig.label}, noise eval {i + 1}/{repetitions}");
                        yield return null;
                        double elapsedMs = MeasureNoiseEvalOnce(noiseEvalSamples, UsesLayerStack(noiseCase));
                        noiseEvalDurations.Add(elapsedMs);
                        allNoiseEvalSamples.Add(elapsedMs);
                        records.Add(new MeasurementRecord("noise_eval", i + 1, elapsedMs, "MainThread", chunkConfig, noiseCase, noiseEvalSamples));
                        step++;
                        SetProgress(step / (float)totalSteps);
                        if (_mathResultLabel != null)
                        {
                            _mathResultLabel.text = FormatStats("Noise eval", noiseEvalDurations);
                        }
                    }

                    for (int i = 0; i < repetitions; i++)
                    {
                        SetStatus($"Collecting data {combinationIndex}/{totalCombinations}: {noiseCase.label}, {chunkConfig.label}, noise math single thread {i + 1}/{repetitions}");
                        yield return null;
                        double elapsedMs = MeasureNoiseMathSingleThreadOnce(mathOutput, borderedVCount, totalSamples);
                        singleThreadMathSamples.Add(elapsedMs);
                        allSingleThreadMathSamples.Add(elapsedMs);
                        records.Add(new MeasurementRecord("noise_math_single_thread", i + 1, elapsedMs, "SingleThread", chunkConfig, noiseCase, totalSamples));
                        step++;
                        SetProgress(step / (float)totalSteps);
                        if (_mathResultLabel != null)
                        {
                            _mathResultLabel.text = FormatStats("Noise math single thread", singleThreadMathSamples);
                        }
                    }

                    for (int i = 0; i < repetitions; i++)
                    {
                        SetStatus($"Collecting data {combinationIndex}/{totalCombinations}: {noiseCase.label}, {chunkConfig.label}, noise math {i + 1}/{repetitions}");
                        yield return null;
                        double elapsedMs = MeasureNoiseMathOnce(mathOutput, borderedVCount, totalSamples);
                        mathSamples.Add(elapsedMs);
                        allMathSamples.Add(elapsedMs);
                        records.Add(new MeasurementRecord("noise_math", i + 1, elapsedMs, GetNoiseMathBackend(), chunkConfig, noiseCase, totalSamples));
                        step++;
                        SetProgress(step / (float)totalSteps);
                        if (_mathResultLabel != null)
                        {
                            _mathResultLabel.text = FormatStats("Noise math", mathSamples);
                        }
                    }

                    for (int i = 0; i < repetitions; i++)
                    {
                        SetStatus($"Collecting data {combinationIndex}/{totalCombinations}: {noiseCase.label}, {chunkConfig.label}, chunk generation {i + 1}/{repetitions}");
                        yield return null;
                        double elapsedMs = MeasureChunkOnce(i);
                        chunkSamples.Add(elapsedMs);
                        allChunkSamples.Add(elapsedMs);
                        records.Add(new MeasurementRecord("chunk_generation", i + 1, elapsedMs, GetChunkGenerationBackend(), chunkConfig, noiseCase, totalSamples));
                        step++;
                        SetProgress(step / (float)totalSteps);
                        if (_chunkResultLabel != null)
                        {
                            _chunkResultLabel.text = FormatStats("Chunk generation", chunkSamples);
                        }
                    }
                }
            }

            SetProgress(1f);
            if (TryWriteCsv(records, out string csvPath, out string csvError))
            {
                SetStatus("Done. CSV saved.");
                if (_mathResultLabel != null)
                {
                    _mathResultLabel.text = $"{FormatStats("Dimensional noise eval total", allDimensionalNoiseSamples)}\n{FormatStats("Noise eval total", allNoiseEvalSamples)}\n{FormatStats("Noise math single thread total", allSingleThreadMathSamples)}\n{FormatStats("Noise math total", allMathSamples)}";
                }

                if (_chunkResultLabel != null)
                {
                    _chunkResultLabel.text = $"{FormatStats("Chunk generation total", allChunkSamples)}\nCSV: {csvPath}";
                }
            }
            else
            {
                SetStatus($"Done, but CSV write failed: {csvError}");
            }
        }
        finally
        {
            if (mathOutput.IsCreated)
            {
                mathOutput.Dispose();
            }

            if (dimensionalPermutation.IsCreated)
            {
                dimensionalPermutation.Dispose();
            }

            if (hasNoiseSnapshot)
            {
                RestoreNoiseProviderSnapshot(noiseSnapshot);
                noiseProvider.SetupNoise();
            }

            _benchmarkRoutine = null;
            _isRunning = false;
            if (_spinnerLabel != null)
            {
                _spinnerLabel.text = "";
            }

            if (_startButton != null)
            {
                _startButton.SetEnabled(true);
            }
        }
    }

    private int CalculateBorderedSampleCount(out int borderedVCount)
    {
        int segments = Mathf.Max(1, Mathf.FloorToInt(chunkSize / Mathf.Max(0.0001f, resolution)));
        int verticesPerLine = segments + 1;
        borderedVCount = verticesPerLine + 2;
        return borderedVCount * borderedVCount;
    }

    private void ApplyChunkConfig(ChunkBenchmarkConfig chunkConfig)
    {
        chunkSize = Mathf.Max(1f, chunkConfig.chunkSize);
        resolution = Mathf.Max(0.1f, chunkConfig.resolution);
        sampleOrigin = chunkConfig.sampleOrigin;
    }

    private void ApplyNoiseCase(NoiseBenchmarkConfig noiseCase)
    {
        if (UsesLayerStack(noiseCase))
        {
            ApplyLayerStackNoiseCase(noiseCase);
            return;
        }

        if (noiseProvider.baseNoiseLayers == null)
        {
            noiseProvider.baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
        }

        if (noiseProvider.baseNoiseLayers.Count == 0)
        {
            noiseProvider.baseNoiseLayers.Add(NoiseProvider.CreateDefaultBaseLayer(0));
        }

        NoiseProvider.BaseNoiseLayer layer = noiseProvider.baseNoiseLayers[0];
        if (layer == null)
        {
            layer = NoiseProvider.CreateDefaultBaseLayer(0);
            noiseProvider.baseNoiseLayers[0] = layer;
        }

        noiseProvider.baseEnabled = true;
        noiseProvider.hashMode = noiseCase.hashMode;
        layer.enabled = true;
        layer.blendMode = NoiseProvider.BaseNoiseBlendMode.Add;
        layer.strength = Mathf.Max(0f, noiseCase.strength);
        layer.type = noiseCase.type;
        layer.fractal = noiseCase.fractal;
        layer.frequency = Mathf.Max(0.000001f, noiseCase.frequency);
        layer.octaves = Mathf.Clamp(noiseCase.octaves, 1, 8);
        layer.lacunarity = Mathf.Max(1f, noiseCase.lacunarity);
        layer.persistence = Mathf.Clamp01(noiseCase.persistence);
        layer.cellDistance = noiseCase.cellDistance;
        layer.cellReturn = noiseCase.cellReturn;
        layer.jitter = Mathf.Clamp01(noiseCase.jitter);

        for (int i = 1; i < noiseProvider.baseNoiseLayers.Count; i++)
        {
            if (noiseProvider.baseNoiseLayers[i] != null)
            {
                noiseProvider.baseNoiseLayers[i].enabled = false;
            }
        }
    }

    private void ApplyLayerStackNoiseCase(NoiseBenchmarkConfig noiseCase)
    {
        SanitizeBenchmarkLayerStack(noiseCase);

        noiseProvider.baseEnabled = true;
        noiseProvider.hashMode = noiseCase.hashMode;
        if (noiseProvider.baseNoiseLayers == null)
        {
            noiseProvider.baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
        }

        noiseProvider.baseNoiseLayers.Clear();
        for (int i = 0; i < noiseCase.baseNoiseLayers.Count; i++)
        {
            noiseProvider.baseNoiseLayers.Add(CloneBaseLayer(noiseCase.baseNoiseLayers[i]));
        }
    }

    private NoiseProviderSnapshot CaptureNoiseProviderSnapshot()
    {
        NoiseProviderSnapshot snapshot = new NoiseProviderSnapshot
        {
            useBurst = noiseProvider.useBurst,
            baseEnabled = noiseProvider.baseEnabled,
            hashMode = noiseProvider.hashMode,
            baseLayers = new List<NoiseProvider.BaseNoiseLayer>()
        };

        if (noiseProvider.baseNoiseLayers != null)
        {
            for (int i = 0; i < noiseProvider.baseNoiseLayers.Count; i++)
            {
                snapshot.baseLayers.Add(CloneBaseLayer(noiseProvider.baseNoiseLayers[i]));
            }
        }

        return snapshot;
    }

    private void RestoreNoiseProviderSnapshot(NoiseProviderSnapshot snapshot)
    {
        if (noiseProvider == null)
        {
            return;
        }

        noiseProvider.baseEnabled = snapshot.baseEnabled;
        noiseProvider.useBurst = snapshot.useBurst;
        noiseProvider.hashMode = snapshot.hashMode;
        if (noiseProvider.baseNoiseLayers == null)
        {
            noiseProvider.baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
        }

        noiseProvider.baseNoiseLayers.Clear();
        if (snapshot.baseLayers == null)
        {
            return;
        }

        for (int i = 0; i < snapshot.baseLayers.Count; i++)
        {
            noiseProvider.baseNoiseLayers.Add(CloneBaseLayer(snapshot.baseLayers[i]));
        }
    }

    private static NoiseProvider.BaseNoiseLayer CloneBaseLayer(NoiseProvider.BaseNoiseLayer source)
    {
        if (source == null)
        {
            return null;
        }

        return new NoiseProvider.BaseNoiseLayer
        {
            enabled = source.enabled,
            blendMode = source.blendMode,
            strength = source.strength,
            type = source.type,
            fractal = source.fractal,
            frequency = source.frequency,
            octaves = source.octaves,
            lacunarity = source.lacunarity,
            persistence = source.persistence,
            cellDistance = source.cellDistance,
            cellReturn = source.cellReturn,
            jitter = source.jitter
        };
    }

    private double MeasureNoiseMathOnce(NativeArray<float4> output, int borderedVCount, int totalSamples)
    {
        ChunkGenerator.BiomeHeightParams biomeParams = ChunkGenerator.BuildBiomeParams(noiseProvider);
        long start = Stopwatch.GetTimestamp();

        if (noiseProvider.useBurst)
        {
            var job = new TerrainNoiseMathJobBurst
            {
                startX = sampleOrigin.x,
                startZ = sampleOrigin.y,
                resolution = resolution,
                borderedVCount = borderedVCount,
                baseNoises = noiseProvider.BaseNoiseLayersBurst,
                baseLayerSettings = noiseProvider.BaseNoiseLayerSettingsBurst,
                permutation = noiseProvider.Perm512,
                temperatureNoise = noiseProvider.TemperatureNoiseBurst,
                moistureNoise = noiseProvider.MoistureNoiseBurst,
                heightCurveLUT = noiseProvider.heightCurveLUT,
                fAmp = noiseProvider.baseAmplitude,
                biomeParams = biomeParams,
                output = output
            };

            job.Schedule(totalSamples, 64).Complete();
        }
        else
        {
            RunNoiseMathMainThread(
                output,
                totalSamples,
                borderedVCount,
                sampleOrigin.x,
                sampleOrigin.y,
                resolution,
                noiseProvider.BaseNoiseLayersBurst,
                noiseProvider.BaseNoiseLayerSettingsBurst,
                noiseProvider.Perm512,
                noiseProvider.TemperatureNoiseBurst,
                noiseProvider.MoistureNoiseBurst,
                noiseProvider.heightCurveLUT,
                noiseProvider.baseAmplitude,
                biomeParams);
        }

        long end = Stopwatch.GetTimestamp();
        return TicksToMilliseconds(start, end);
    }

    private double MeasureNoiseMathSingleThreadOnce(NativeArray<float4> output, int borderedVCount, int totalSamples)
    {
        ChunkGenerator.BiomeHeightParams biomeParams = ChunkGenerator.BuildBiomeParams(noiseProvider);
        long start = Stopwatch.GetTimestamp();

        RunNoiseMathMainThread(
            output,
            totalSamples,
            borderedVCount,
            sampleOrigin.x,
            sampleOrigin.y,
            resolution,
            noiseProvider.BaseNoiseLayersBurst,
            noiseProvider.BaseNoiseLayerSettingsBurst,
            noiseProvider.Perm512,
            noiseProvider.TemperatureNoiseBurst,
            noiseProvider.MoistureNoiseBurst,
            noiseProvider.heightCurveLUT,
            noiseProvider.baseAmplitude,
            biomeParams);

        long end = Stopwatch.GetTimestamp();
        return TicksToMilliseconds(start, end);
    }

    private double MeasureNoiseEvalOnce(int sampleCount, bool sampleLayerStack)
    {
        if (noiseProvider == null ||
            !noiseProvider.BaseNoiseLayersBurst.IsCreated ||
            noiseProvider.BaseNoiseLayersBurst.Length == 0 ||
            !noiseProvider.BaseNoiseLayerSettingsBurst.IsCreated ||
            noiseProvider.BaseNoiseLayerSettingsBurst.Length == 0)
        {
            return double.NaN;
        }

        sampleCount = Mathf.Max(1, sampleCount);
        RewriteFastNoiseLite noise = noiseProvider.BaseNoiseLayersBurst[0];
        NativeArray<RewriteFastNoiseLite> baseNoises = noiseProvider.BaseNoiseLayersBurst;
        NativeArray<NoiseProvider.BaseNoiseLayerRuntime> baseLayerSettings = noiseProvider.BaseNoiseLayerSettingsBurst;
        NativeArray<int> permutation = noiseProvider.Perm512;
        float originX = sampleOrigin.x;
        float originZ = sampleOrigin.y;
        float sampleStep = Mathf.Max(0.0001f, resolution);
        int rowWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(sampleCount)));

        float baselineSum = 0f;
        long baselineStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < sampleCount; i++)
        {
            int px = i % rowWidth;
            int pz = i / rowWidth;
            float x = originX + px * sampleStep;
            float z = originZ + pz * sampleStep;
            baselineSum += (x * 0.000001f) + (z * 0.0000001f);
        }
        long baselineEnd = Stopwatch.GetTimestamp();

        float noiseSum = 0f;
        long noiseStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < sampleCount; i++)
        {
            int px = i % rowWidth;
            int pz = i / rowWidth;
            float x = originX + px * sampleStep;
            float z = originZ + pz * sampleStep;
            noiseSum += sampleLayerStack
                ? NoiseProvider.SampleBaseNoiseStack(baseNoises, baseLayerSettings, permutation, x, z)
                : noise.GetNoise2D(x, z, permutation);
        }
        long noiseEnd = Stopwatch.GetTimestamp();

        s_NoiseEvalSink = noiseSum + baselineSum;
        double baselineMs = TicksToMilliseconds(baselineStart, baselineEnd);
        double noiseMs = TicksToMilliseconds(noiseStart, noiseEnd);
        return Math.Max(0.0, noiseMs - baselineMs);
    }

    private double MeasureDimensionalNoiseEvalOnce(
        DimensionalNoiseBenchmarkCase microCase,
        int sampleCount,
        NativeArray<int> dimensionalPermutation)
    {
        sampleCount = Mathf.Max(1, sampleCount);
        RewriteFastNoiseLite noise = CreateDimensionalNoiseEvaluator(microCase);
        NativeArray<int> permutation = microCase.hashMode == NoiseHashMode.PermutationTable512
            ? dimensionalPermutation
            : default;
        float originX = sampleOrigin.x;
        float originZ = sampleOrigin.y;
        float sampleStep = Mathf.Max(0.0001f, resolution);

        float baselineSum = 0f;
        long baselineStart = Stopwatch.GetTimestamp();
        if (microCase.dimensions == 2)
        {
            int rowWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(sampleCount)));
            for (int i = 0; i < sampleCount; i++)
            {
                int px = i % rowWidth;
                int pz = i / rowWidth;
                float x = originX + px * sampleStep;
                float z = originZ + pz * sampleStep;
                baselineSum += (x * 0.000001f) + (z * 0.0000001f);
            }
        }
        else
        {
            int cubeWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(sampleCount, 1f / 3f)));
            int sliceSize = cubeWidth * cubeWidth;
            for (int i = 0; i < sampleCount; i++)
            {
                int px = i % cubeWidth;
                int py = (i / cubeWidth) % cubeWidth;
                int pz = i / sliceSize;
                float x = originX + px * sampleStep;
                float y = py * sampleStep;
                float z = originZ + pz * sampleStep;
                baselineSum += (x * 0.000001f) + (y * 0.0000001f) + (z * 0.00000001f);
            }
        }
        long baselineEnd = Stopwatch.GetTimestamp();

        float noiseSum = 0f;
        long noiseStart = Stopwatch.GetTimestamp();
        if (microCase.dimensions == 2)
        {
            int rowWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(sampleCount)));
            for (int i = 0; i < sampleCount; i++)
            {
                int px = i % rowWidth;
                int pz = i / rowWidth;
                float x = originX + px * sampleStep;
                float z = originZ + pz * sampleStep;
                noiseSum += noise.GetNoise2D(x, z, permutation);
            }
        }
        else
        {
            int cubeWidth = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(sampleCount, 1f / 3f)));
            int sliceSize = cubeWidth * cubeWidth;
            for (int i = 0; i < sampleCount; i++)
            {
                int px = i % cubeWidth;
                int py = (i / cubeWidth) % cubeWidth;
                int pz = i / sliceSize;
                float x = originX + px * sampleStep;
                float y = py * sampleStep;
                float z = originZ + pz * sampleStep;
                noiseSum += noise.GetNoise(x, y, z, permutation);
            }
        }
        long noiseEnd = Stopwatch.GetTimestamp();

        s_DimensionalNoiseEvalSink = noiseSum + baselineSum;
        double baselineMs = TicksToMilliseconds(baselineStart, baselineEnd);
        double noiseMs = TicksToMilliseconds(noiseStart, noiseEnd);
        return Math.Max(0.0, noiseMs - baselineMs);
    }

    private RewriteFastNoiseLite CreateDimensionalNoiseEvaluator(DimensionalNoiseBenchmarkCase microCase)
    {
        int seed = noiseProvider != null ? noiseProvider.seed : 1337;
        RewriteFastNoiseLite noise = RewriteFastNoiseLite.Default(seed);
        noise.noiseType = microCase.noiseType;
        noise.fractalType = RewriteFastNoiseLite.FractalType.None;
        noise.hashMode = microCase.hashMode;
        noise.frequency = 0.002f;
        noise.octaves = 1;
        noise.lacunarity = 2f;
        noise.gain = 0.5f;
        return noise;
    }

    private double MeasureChunkOnce(int iteration)
    {
        chunkGenerator.ResetForReuse();

        long start = Stopwatch.GetTimestamp();
        bool scheduled = chunkGenerator.Generate(
            new Vector2Int(iteration, 0),
            sampleOrigin.x,
            sampleOrigin.y,
            chunkSize,
            resolution,
            default,
            0);

        if (!scheduled)
        {
            return double.NaN;
        }

        chunkGenerator.ApplyMesh();
        long end = Stopwatch.GetTimestamp();
        return TicksToMilliseconds(start, end);
    }

    private static void RunNoiseMathMainThread(
        NativeArray<float4> output,
        int totalSamples,
        int borderedVCount,
        float startX,
        float startZ,
        float resolution,
        NativeArray<RewriteFastNoiseLite> baseNoises,
        NativeArray<NoiseProvider.BaseNoiseLayerRuntime> baseLayerSettings,
        NativeArray<int> permutation,
        RewriteFastNoiseLite temperatureNoise,
        RewriteFastNoiseLite moistureNoise,
        NativeArray<float> heightCurveLUT,
        float fAmp,
        ChunkGenerator.BiomeHeightParams biomeParams)
    {
        for (int index = 0; index < totalSamples; index++)
        {
            int x = index % borderedVCount;
            int y = index / borderedVCount;

            float worldX = startX + (x - 1) * resolution;
            float worldZ = startZ + (y - 1) * resolution;

            float baseHeight = NoiseProvider.SampleBaseNoiseStack(baseNoises, baseLayerSettings, permutation, worldX, worldZ);
            float t = math.saturate((baseHeight + 1f) * 0.5f);
            float shaped = baseHeight;
            if (heightCurveLUT.IsCreated && heightCurveLUT.Length > 1)
            {
                float lutIndex = t * (heightCurveLUT.Length - 1);
                int i0 = (int)lutIndex;
                int i1 = math.min(i0 + 1, heightCurveLUT.Length - 1);
                float fract = lutIndex - i0;
                shaped = math.lerp(heightCurveLUT[i0], heightCurveLUT[i1], fract);
            }

            float finalHeight = shaped * fAmp;
            if (finalHeight <= 0f)
            {
                finalHeight = 0f;
            }

            float temperature = ChunkGenerator.ClimateDisabledValue;
            float moisture = ChunkGenerator.ClimateDisabledValue;
            if (biomeParams.temperatureEnabled != 0 || biomeParams.moistureEnabled != 0)
            {
                ChunkGenerator.ClimateSample climate = ChunkGenerator.SampleClimate(
                    temperatureNoise,
                    moistureNoise,
                    permutation,
                    worldX,
                    worldZ,
                    finalHeight,
                    biomeParams,
                    true);
                temperature = climate.temperature;
                moisture = climate.moisture;
            }

            output[index] = new float4(finalHeight, temperature, moisture, 0f);
        }
    }

    private static double TicksToMilliseconds(long start, long end)
    {
        return (end - start) * 1000.0 / Stopwatch.Frequency;
    }

    private void SetStatus(string text)
    {
        if (_statusLabel != null)
        {
            _statusLabel.text = text;
        }
    }

    private void SetProgress(float value)
    {
        value = Mathf.Clamp01(value);
        if (_progressFill != null)
        {
            _progressFill.style.width = Length.Percent(value * 100f);
        }

        if (_progressLabel != null)
        {
            _progressLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }

    private string GetNoiseMathBackend()
    {
        return GetBackendLabel(noiseProvider);
    }

    private string GetChunkGenerationBackend()
    {
        NoiseProvider provider = NoiseProvider.Instance != null ? NoiseProvider.Instance : noiseProvider;
        return GetBackendLabel(provider);
    }

    private static string GetBackendLabel(NoiseProvider provider)
    {
        return provider != null && provider.useBurst ? "Burst" : "MainThread";
    }

    private static string FormatStats(string title, List<double> samples)
    {
        TerrainMeasurementStats stats = TerrainMeasurementStats.From(samples);
        return $"{title}: avg {stats.mean:0.###} ms | median {stats.median:0.###} ms | min {stats.min:0.###} ms | max {stats.max:0.###} ms | n={stats.count}";
    }

    private bool TryWriteCsv(List<MeasurementRecord> records, out string path, out string error)
    {
        path = string.Empty;
        error = string.Empty;

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "TerrainPerformance");
            Directory.CreateDirectory(directory);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string fileName = $"{SanitizeFileName(csvFilePrefix)}_{timestamp}.csv";
            path = Path.Combine(directory, fileName);

            StringBuilder csv = new StringBuilder(4096);
            AppendCsvRow(csv,
                "session_utc",
                "noise_case",
                "phase",
                "iteration",
                "duration_ms",
                "ns_per_sample",
                "samples_per_second",
                "backend",
                "chunk_size",
                "resolution",
                "total_samples");

            string sessionUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            for (int i = 0; i < records.Count; i++)
            {
                MeasurementRecord record = records[i];
                AppendCsvRow(csv,
                    sessionUtc,
                    record.noiseCaseLabel,
                    record.phase,
                    record.iteration.ToString(CultureInfo.InvariantCulture),
                    record.durationMs.ToString("0.######", CultureInfo.InvariantCulture),
                    record.nsPerSample.ToString("0.######", CultureInfo.InvariantCulture),
                    record.samplesPerSecond.ToString("0.######", CultureInfo.InvariantCulture),
                    record.backend,
                    FormatFloat(record.chunkSize),
                    FormatFloat(record.resolution),
                    record.totalSamples.ToString(CultureInfo.InvariantCulture));
            }

            File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void AppendCsvRow(StringBuilder csv, params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                csv.Append(',');
            }

            csv.Append(EscapeCsv(values[i]));
        }

        csv.AppendLine();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static string FormatNoiseCaseLabel(NoiseBenchmarkConfig noiseCase)
    {
        if (noiseCase == null)
        {
            return string.Empty;
        }

        string label = string.IsNullOrWhiteSpace(noiseCase.label)
            ? $"{noiseCase.type} {noiseCase.fractal}"
            : noiseCase.label.Trim();

        return UsesLayerStack(noiseCase)
            ? $"{label} [Stack, {noiseCase.hashMode}]"
            : $"{label} [{noiseCase.hashMode}]";
    }

    private static string SanitizeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "terrain_performance" : value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            safe = safe.Replace(invalidChars[i], '_');
        }

        return safe;
    }

    [BurstCompile]
    private struct TerrainNoiseMathJobBurst : IJobParallelFor
    {
        public float startX;
        public float startZ;
        public float resolution;
        public int borderedVCount;

        [ReadOnly] public NativeArray<RewriteFastNoiseLite> baseNoises;
        [ReadOnly] public NativeArray<NoiseProvider.BaseNoiseLayerRuntime> baseLayerSettings;
        [ReadOnly] public NativeArray<int> permutation;
        public RewriteFastNoiseLite temperatureNoise;
        public RewriteFastNoiseLite moistureNoise;
        [ReadOnly] public NativeArray<float> heightCurveLUT;
        public float fAmp;
        public ChunkGenerator.BiomeHeightParams biomeParams;

        [WriteOnly] public NativeArray<float4> output;

        public void Execute(int index)
        {
            int x = index % borderedVCount;
            int y = index / borderedVCount;

            float worldX = startX + (x - 1) * resolution;
            float worldZ = startZ + (y - 1) * resolution;

            float baseHeight = NoiseProvider.SampleBaseNoiseStack(baseNoises, baseLayerSettings, permutation, worldX, worldZ);
            float t = math.saturate((baseHeight + 1f) * 0.5f);
            float shaped = baseHeight;
            if (heightCurveLUT.Length > 1)
            {
                float lutIndex = t * (heightCurveLUT.Length - 1);
                int i0 = (int)lutIndex;
                int i1 = math.min(i0 + 1, heightCurveLUT.Length - 1);
                float fract = lutIndex - i0;
                shaped = math.lerp(heightCurveLUT[i0], heightCurveLUT[i1], fract);
            }

            float finalHeight = shaped * fAmp;
            if (finalHeight <= 0f)
            {
                finalHeight = 0f;
            }

            float temperature = ChunkGenerator.ClimateDisabledValue;
            float moisture = ChunkGenerator.ClimateDisabledValue;
            if (biomeParams.temperatureEnabled != 0 || biomeParams.moistureEnabled != 0)
            {
                ChunkGenerator.ClimateSample climate = ChunkGenerator.SampleClimate(
                    temperatureNoise,
                    moistureNoise,
                    permutation,
                    worldX,
                    worldZ,
                    finalHeight,
                    biomeParams,
                    true);
                temperature = climate.temperature;
                moisture = climate.moisture;
            }

            output[index] = new float4(finalHeight, temperature, moisture, 0f);
        }
    }

    private struct MeasurementRecord
    {
        public readonly string phase;
        public readonly int iteration;
        public readonly double durationMs;
        public readonly double nsPerSample;
        public readonly double samplesPerSecond;
        public readonly string backend;
        public readonly float chunkSize;
        public readonly float resolution;
        public readonly int totalSamples;
        public readonly string noiseCaseLabel;

        public MeasurementRecord(
            string phase,
            int iteration,
            double durationMs,
            string backend,
            ChunkBenchmarkConfig chunkConfig,
            NoiseBenchmarkConfig noiseCase,
            int totalSamples)
            : this(phase, iteration, durationMs, backend, chunkConfig, FormatNoiseCaseLabel(noiseCase), totalSamples)
        {
        }

        public MeasurementRecord(
            string phase,
            int iteration,
            double durationMs,
            string backend,
            ChunkBenchmarkConfig chunkConfig,
            string noiseCaseLabel,
            int totalSamples)
        {
            int safeSampleCount = Math.Max(1, totalSamples);
            this.phase = phase;
            this.iteration = iteration;
            this.durationMs = durationMs;
            this.nsPerSample = durationMs * 1000000.0 / safeSampleCount;
            this.samplesPerSecond = durationMs > 0.0 ? safeSampleCount / (durationMs / 1000.0) : 0.0;
            this.backend = backend;
            this.chunkSize = chunkConfig.chunkSize;
            this.resolution = chunkConfig.resolution;
            this.totalSamples = safeSampleCount;
            this.noiseCaseLabel = noiseCaseLabel ?? string.Empty;
        }
    }

    private struct NoiseProviderSnapshot
    {
        public bool useBurst;
        public bool baseEnabled;
        public NoiseHashMode hashMode;
        public List<NoiseProvider.BaseNoiseLayer> baseLayers;
    }

}
