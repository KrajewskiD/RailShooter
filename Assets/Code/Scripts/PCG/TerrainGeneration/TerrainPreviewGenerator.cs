using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[AddComponentMenu("Terrain/Terrain Preview Generator")]
public class TerrainPreviewGenerator : MonoBehaviour
{
    public enum PreviewMode
    {
        TerrainColoredNoise = 0,
        Noise2D = 1,
        TerrainNoise = 2,
        TemperatureRaw = 3,
        HumidityRaw = 4,
        ColoredNoise2D = 5,
        ClimateNoise2D = 6,
        TemperatureEffective = 7,
        HumidityEffective = 8,
        Terrain3D = 9,
        ClimateEffective2D = 10
    }

    private struct TerrainSample
    {
        public float height;
        public float rawTemperature;
        public float rawMoisture;
        public float temperature;
        public float moisture;
        public float rawNoise01;
    }

    [Header("References")]
    [SerializeField] private NoiseProvider noiseProvider;
    [SerializeField] private Material coloredTerrainMaterial;
    [SerializeField] private Material texturePreviewMaterial;

    [Header("Terrain Area")]
    [SerializeField] private Vector2 sampleOrigin = Vector2.zero;
    [SerializeField] private Vector2 terrainSize = new Vector2(256f, 256f);
    [SerializeField, Min(0.1f)] private float meshResolution = 2f;
    [SerializeField] private bool centerMeshOnObject = true;

    [SerializeField, HideInInspector] private PreviewMode previewMode = PreviewMode.TerrainColoredNoise;
    [SerializeField, Min(16)] private int textureResolution = 512;
    [SerializeField] private string textureProperty = "_BaseMap";

    [Header("Noise")]
    [SerializeField] private bool overrideNoise = false;
    [SerializeField] private NoiseProvider.NoiseType noiseType = NoiseProvider.NoiseType.SimplexNoise;
    [SerializeField] private NoiseProvider.FractalType fractal = NoiseProvider.FractalType.FBm;
    [SerializeField] private NoiseHashMode hashMode = NoiseHashMode.Hash;
    [SerializeField, Range(0f, 2f)] private float strength = 1f;
    [SerializeField, Min(0.000001f)] private float frequency = 0.002f;
    [SerializeField, Range(1, 8)] private int octaves = 4;
    [SerializeField, Min(1f)] private float lacunarity = 2f;
    [SerializeField, Range(0f, 1f)] private float persistence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float jitter = 1f;
    [SerializeField] private CellularDistance cellDistance = CellularDistance.EuclideanSq;
    [SerializeField] private CellularReturn cellReturn = CellularReturn.Distance;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _generatedMesh;
    private Material _runtimeMaterial;
    private Texture2D _previewTexture;

    private static readonly ColorStop[] s_TemperatureStops =
    {
        new ColorStop(0.00f, new Color32(76, 0, 130, 255)),
        new ColorStop(0.16f, new Color32(30, 80, 220, 255)),
        new ColorStop(0.32f, new Color32(105, 205, 235, 255)),
        new ColorStop(0.50f, new Color32(70, 185, 95, 255)),
        new ColorStop(0.64f, new Color32(245, 225, 80, 255)),
        new ColorStop(0.78f, new Color32(245, 135, 45, 255)),
        new ColorStop(0.90f, new Color32(215, 40, 40, 255)),
        new ColorStop(1.00f, new Color32(105, 0, 35, 255))
    };

    private static readonly ColorStop[] s_MoistureStops =
    {
        new ColorStop(0.00f, new Color32(122, 72, 25, 255)),
        new ColorStop(0.15f, new Color32(196, 132, 55, 255)),
        new ColorStop(0.30f, new Color32(235, 210, 105, 255)),
        new ColorStop(0.45f, new Color32(170, 220, 105, 255)),
        new ColorStop(0.60f, new Color32(75, 190, 120, 255)),
        new ColorStop(0.75f, new Color32(70, 185, 220, 255)),
        new ColorStop(0.90f, new Color32(55, 100, 215, 255)),
        new ColorStop(1.00f, new Color32(80, 35, 150, 255))
    };

    private static readonly Color32 s_DisabledClimateColor = new Color32(24, 24, 24, 255);

    public void SetPreviewMode(PreviewMode mode)
    {
        previewMode = mode;
    }

    [ContextMenu("Generate")]
    public void GenerateTerrain()
    {
        EnsureComponents();

        if (!TryPrepareNoise(out NoiseProvider np))
        {
            Debug.LogWarning("No active NoiseProvider or noise data could not be prepared.", this);
            return;
        }

        ApplyNoiseSettingsToProvider(np);

        terrainSize.x = Mathf.Max(0.1f, terrainSize.x);
        terrainSize.y = Mathf.Max(0.1f, terrainSize.y);
        meshResolution = Mathf.Max(0.1f, meshResolution);

        int segmentsX = Mathf.Max(1, Mathf.RoundToInt(terrainSize.x / meshResolution));
        int segmentsZ = Mathf.Max(1, Mathf.RoundToInt(terrainSize.y / meshResolution));
        int verticesX = segmentsX + 1;
        int verticesZ = segmentsZ + 1;
        int vertexCount = verticesX * verticesZ;

        if (vertexCount > 1_000_000)
        {
            Debug.LogWarning($"Preview mesh is too large ({vertexCount} vertices). Increase Mesh Resolution.", this);
            return;
        }

        float halfX = centerMeshOnObject ? terrainSize.x * 0.5f : 0f;
        float halfZ = centerMeshOnObject ? terrainSize.y * 0.5f : 0f;

        var biomeParams = ChunkGenerator.BuildBiomeParams(np);
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var colors = new Color32[vertexCount];
        var triangles = new int[segmentsX * segmentsZ * 6];

        for (int z = 0; z < verticesZ; z++)
        {
            float v = z / (float)segmentsZ;
            float worldZ = sampleOrigin.y + v * terrainSize.y;

            for (int x = 0; x < verticesX; x++)
            {
                float u = x / (float)segmentsX;
                float worldX = sampleOrigin.x + u * terrainSize.x;
                TerrainSample sample = SampleTerrain(np, biomeParams, previewMode, worldX, worldZ);
                int index = z * verticesX + x;

                float previewHeight = UsesTexturePreview(previewMode) ? 0f : sample.height;
                vertices[index] = new Vector3((u * terrainSize.x) - halfX, previewHeight, (v * terrainSize.y) - halfZ);
                uvs[index] = new Vector2(u, v);
                colors[index] = PreviewVertexColor(previewMode, sample, biomeParams, np);
            }
        }

        int triangleIndex = 0;
        for (int z = 0; z < segmentsZ; z++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                int i = z * verticesX + x;
                triangles[triangleIndex++] = i;
                triangles[triangleIndex++] = i + verticesX;
                triangles[triangleIndex++] = i + 1;
                triangles[triangleIndex++] = i + 1;
                triangles[triangleIndex++] = i + verticesX;
                triangles[triangleIndex++] = i + verticesX + 1;
            }
        }

        Mesh mesh = GetOrCreateMesh(vertexCount > 65535);
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _meshFilter.sharedMesh = mesh;

        string materialStatus = ApplyMaterial(np, biomeParams);
        if (!string.IsNullOrEmpty(materialStatus))
        {
            Debug.LogWarning(materialStatus, this);
        }
    }

    private void ApplyNoiseSettingsToProvider(NoiseProvider np)
    {
        if (!overrideNoise || np == null)
        {
            return;
        }

        np.hashMode = hashMode;

        if (np.baseNoiseLayers == null)
        {
            np.baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
        }

        if (np.baseNoiseLayers.Count == 0)
        {
            np.baseNoiseLayers.Add(NoiseProvider.CreateDefaultBaseLayer(0));
        }

        NoiseProvider.BaseNoiseLayer layer = np.baseNoiseLayers[0];
        if (layer == null)
        {
            layer = NoiseProvider.CreateDefaultBaseLayer(0);
            np.baseNoiseLayers[0] = layer;
        }

        np.baseEnabled = true;
        layer.enabled = true;
        layer.blendMode = NoiseProvider.BaseNoiseBlendMode.Add;
        layer.type = noiseType;
        layer.fractal = fractal;
        layer.strength = Mathf.Max(0f, strength);
        layer.frequency = Mathf.Max(0.000001f, frequency);

        if (noiseType == NoiseProvider.NoiseType.Worley)
        {
            layer.cellDistance = cellDistance;
            layer.cellReturn = NoiseProvider.NormalizeWorleyReturn(cellReturn);
            layer.jitter = Mathf.Clamp01(jitter);
        }

        if (fractal != NoiseProvider.FractalType.None)
        {
            layer.octaves = Mathf.Clamp(octaves, 1, 8);
            layer.lacunarity = Mathf.Max(1f, lacunarity);
            layer.persistence = Mathf.Clamp01(persistence);
        }

        np.SetupNoise();
    }

    [ContextMenu("Clear Preview")]
    public void ClearGenerated()
    {
        EnsureComponents();

        if (_meshFilter != null)
        {
            _meshFilter.sharedMesh = null;
        }

        DestroyUnityObject(_generatedMesh);
        DestroyUnityObject(_previewTexture);
        DestroyUnityObject(_runtimeMaterial);

        _generatedMesh = null;
        _previewTexture = null;
        _runtimeMaterial = null;
    }

    private string ApplyMaterial(NoiseProvider np, ChunkGenerator.BiomeHeightParams biomeParams)
    {
        if (_meshRenderer == null)
        {
            return null;
        }

        bool usesTexture = UsesTexturePreview(previewMode);
        bool isColoredNoise = previewMode == PreviewMode.ColoredNoise2D;
        Material material;
        string status = null;

        if (isColoredNoise)
        {
            material = IsColoredNoiseMaterial(coloredTerrainMaterial)
                ? coloredTerrainMaterial
                : IsColoredNoiseMaterial(texturePreviewMaterial)
                    ? texturePreviewMaterial
                    : GetRuntimeMaterial(FindColoredNoiseShader(), "Terrain Preview 2D Colored Noise Material");

            if (material != null)
            {
                ApplyColoredNoiseShaderParams(material, np);
            }
        }
        else if (usesTexture)
        {
            material = IsUnlitTextureMaterial(texturePreviewMaterial)
                ? texturePreviewMaterial
                : GetRuntimeMaterial(FindTextureShader(), "Terrain Preview Noise Material");

            if (material != null && texturePreviewMaterial != null && !IsUnlitTextureMaterial(texturePreviewMaterial))
            {
                string shaderName = texturePreviewMaterial.shader != null ? texturePreviewMaterial.shader.name : "null";
                status = $"Texture Preview Material '{texturePreviewMaterial.name}' uses shader '{shaderName}', which lights the texture. Noise preview uses '{material.shader.name}'.";
            }
        }
        else
        {
            material = IsVertexColorMaterial(coloredTerrainMaterial)
                ? coloredTerrainMaterial
                : GetRuntimeMaterial(FindVertexColorShader(), "Terrain Preview Vertex Color Material");

            if (material != null && coloredTerrainMaterial != null && !IsVertexColorMaterial(coloredTerrainMaterial))
            {
                string shaderName = coloredTerrainMaterial.shader != null ? coloredTerrainMaterial.shader.name : "null";
                status = $"Colored Terrain Material '{coloredTerrainMaterial.name}' uses shader '{shaderName}', which does not read vertex colors. Preview uses '{material.shader.name}'.";
            }
        }

        if (material == null)
        {
            return "No preview material or matching preview shader was found.";
        }

        _meshRenderer.sharedMaterial = material;

        if (usesTexture)
        {
            GeneratePreviewTexture(np, biomeParams);
            AssignPreviewTexture(material);
        }

        return status;
    }

    private void GeneratePreviewTexture(NoiseProvider np, ChunkGenerator.BiomeHeightParams biomeParams)
    {
        int size = Mathf.Clamp(textureResolution, 16, 4096);
        var pixels = new Color[size * size];
        float noiseMin = 0f;
        float noiseMax = 1f;

        if (previewMode == PreviewMode.Noise2D)
        {
            noiseMin = 1f;
            noiseMax = 0f;

            for (int y = 0; y < size; y++)
            {
                float v = size == 1 ? 0f : y / (float)(size - 1);
                float worldZ = sampleOrigin.y + v * terrainSize.y;

                for (int x = 0; x < size; x++)
                {
                    float u = size == 1 ? 0f : x / (float)(size - 1);
                    float worldX = sampleOrigin.x + u * terrainSize.x;
                    TerrainSample sample = SampleTerrain(np, biomeParams, previewMode, worldX, worldZ);
                    noiseMin = math.min(noiseMin, sample.rawNoise01);
                    noiseMax = math.max(noiseMax, sample.rawNoise01);
                }
            }

            if (noiseMax - noiseMin < 0.0001f)
            {
                noiseMin = 0f;
                noiseMax = 1f;
            }
        }

        for (int y = 0; y < size; y++)
        {
            float v = size == 1 ? 0f : y / (float)(size - 1);
            float worldZ = sampleOrigin.y + v * terrainSize.y;

            for (int x = 0; x < size; x++)
            {
                float u = size == 1 ? 0f : x / (float)(size - 1);
                float worldX = sampleOrigin.x + u * terrainSize.x;
                TerrainSample sample = SampleTerrain(np, biomeParams, previewMode, worldX, worldZ);
                pixels[y * size + x] = PreviewTexturePixel(previewMode, sample, biomeParams, np, noiseMin, noiseMax);
            }
        }

        DestroyUnityObject(_previewTexture);
        TextureFormat textureFormat = previewMode == PreviewMode.ColoredNoise2D
            ? TextureFormat.RGBAFloat
            : TextureFormat.RGBA32;
        _previewTexture = new Texture2D(size, size, textureFormat, false, true)
        {
            name = $"TerrainPreview_{previewMode}_{size}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        _previewTexture.SetPixels(pixels);
        _previewTexture.Apply(false, false);
    }

    private void AssignPreviewTexture(Material material)
    {
        if (material == null || _previewTexture == null)
        {
            return;
        }

        int terrainDataId = Shader.PropertyToID("_TerrainDataTex");
        int requestedId = Shader.PropertyToID(textureProperty);
        int baseMapId = Shader.PropertyToID("_BaseMap");
        int mainTexId = Shader.PropertyToID("_MainTex");
        bool assigned = false;

        if (material.HasProperty(terrainDataId))
        {
            material.SetTexture(terrainDataId, _previewTexture);
            assigned = true;
        }

        if (material.HasProperty(requestedId))
        {
            material.SetTexture(requestedId, _previewTexture);
            assigned = true;
        }

        if (material.HasProperty(baseMapId))
        {
            material.SetTexture(baseMapId, _previewTexture);
            assigned = true;
        }

        if (material.HasProperty(mainTexId))
        {
            material.SetTexture(mainTexId, _previewTexture);
            assigned = true;
        }

        if (!assigned)
        {
            material.mainTexture = _previewTexture;
        }
    }

    private TerrainSample SampleTerrain(
        NoiseProvider np,
        ChunkGenerator.BiomeHeightParams biomeParams,
        PreviewMode mode,
        float worldX,
        float worldZ)
    {
        float rawNoise01 = 0f;
        float terrainHeight = 0f;
        bool usesTerrainData = UsesTerrainData(mode);

        if (usesTerrainData)
        {
            float baseHeight = NoiseProvider.SampleBaseNoiseStack(np.BaseNoiseLayersBurst, np.BaseNoiseLayerSettingsBurst, np.Perm512, worldX, worldZ);
            rawNoise01 = math.saturate((baseHeight + 1f) * 0.5f);
            float shaped = SampleHeightCurveLut(np.heightCurveLUT, rawNoise01);
            terrainHeight = shaped * np.baseAmplitude;

            if (terrainHeight <= 0f)
            {
                terrainHeight = 0f;
            }
        }

        ChunkGenerator.ClimateSample climate = ChunkGenerator.SampleClimate(
            np.TemperatureNoiseBurst,
            np.MoistureNoiseBurst,
            np.Perm512,
            worldX,
            worldZ,
            terrainHeight,
            biomeParams,
            UsesEffectiveClimate(mode));

        return new TerrainSample
        {
            height = terrainHeight,
            rawTemperature = climate.rawTemperature,
            rawMoisture = climate.rawMoisture,
            temperature = climate.temperature,
            moisture = climate.moisture,
            rawNoise01 = rawNoise01
        };
    }

    private static Color32 PreviewVertexColor(
        PreviewMode mode,
        TerrainSample sample,
        ChunkGenerator.BiomeHeightParams biomeParams,
        NoiseProvider np)
    {
        switch (mode)
        {
            case PreviewMode.TerrainColoredNoise:
            case PreviewMode.ColoredNoise2D:
                return ChunkGenerator.BiomeShading.BiomeColor(sample.height, sample.moisture, sample.temperature, biomeParams);

            case PreviewMode.TerrainNoise:
            {
                float maxHeight = np != null ? math.max(0.001f, np.GetAbsoluteMaxHeight()) : 1f;
                return Grayscale(math.saturate(sample.height / maxHeight));
            }

            case PreviewMode.TemperatureRaw:
                return TemperatureMapColor(sample.rawTemperature);

            case PreviewMode.HumidityRaw:
                return MoistureMapColor(sample.rawMoisture);

            case PreviewMode.ClimateNoise2D:
                return ClimateMapColor(sample.rawTemperature, sample.rawMoisture);

            case PreviewMode.ClimateEffective2D:
                return ClimateMapColor(sample.temperature, sample.moisture);

            case PreviewMode.TemperatureEffective:
                return TemperatureMapColor(sample.temperature);

            case PreviewMode.HumidityEffective:
                return MoistureMapColor(sample.moisture);

            case PreviewMode.Terrain3D:
                return ClimateMapColor(sample.temperature, sample.moisture);

            default:
                return Grayscale(sample.rawNoise01);
        }
    }

    private static Color PreviewTexturePixel(
        PreviewMode mode,
        TerrainSample sample,
        ChunkGenerator.BiomeHeightParams biomeParams,
        NoiseProvider np,
        float noiseMin,
        float noiseMax)
    {
        if (mode == PreviewMode.ColoredNoise2D)
        {
            float amplitude = np != null ? math.max(0.001f, np.baseAmplitude) : 1f;
            return new Color(
                math.saturate(sample.height / amplitude),
                sample.moisture,
                sample.temperature,
                math.saturate(sample.rawNoise01));
        }

        if (mode == PreviewMode.Noise2D)
        {
            float value = math.saturate((sample.rawNoise01 - noiseMin) / math.max(0.0001f, noiseMax - noiseMin));
            return new Color(value, value, value, 1f);
        }

        return PreviewVertexColor(mode, sample, biomeParams, np);
    }

    private static float SampleHeightCurveLut(NativeArray<float> heightCurveLUT, float t)
    {
        t = math.saturate(t);
        if (!heightCurveLUT.IsCreated || heightCurveLUT.Length < 2)
        {
            return t * 2f - 1f;
        }

        float idx = t * (heightCurveLUT.Length - 1);
        int i0 = (int)idx;
        int i1 = math.min(i0 + 1, heightCurveLUT.Length - 1);
        return math.lerp(heightCurveLUT[i0], heightCurveLUT[i1], idx - i0);
    }

    private bool TryPrepareNoise(out NoiseProvider np)
    {
        if (noiseProvider == null)
        {
            noiseProvider = NoiseProvider.Instance;
        }

        if (noiseProvider == null)
        {
#if UNITY_2023_1_OR_NEWER
            noiseProvider = FindFirstObjectByType<NoiseProvider>();
#else
            noiseProvider = FindObjectOfType<NoiseProvider>();
#endif
        }

        np = noiseProvider;
        if (np == null)
        {
            return false;
        }

        np.EnsureReadyForTerrainJobs();
        return np.IsReadyForTerrainJobs;
    }

    private Mesh GetOrCreateMesh(bool useUInt32)
    {
        if (_generatedMesh == null)
        {
            _generatedMesh = new Mesh
            {
                name = "Terrain Preview Mesh",
                hideFlags = HideFlags.DontSave,
                indexFormat = useUInt32 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
        }
        else
        {
            _generatedMesh.indexFormat = useUInt32 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        }

        return _generatedMesh;
    }

    private Material GetRuntimeMaterial(Shader shader, string materialName)
    {
        if (shader == null)
        {
            return null;
        }

        if (_runtimeMaterial == null || _runtimeMaterial.shader != shader)
        {
            DestroyUnityObject(_runtimeMaterial);
            _runtimeMaterial = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };
        }

        return _runtimeMaterial;
    }

    private static Shader FindTextureShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Standard");
        return shader;
    }

    private static Shader FindColoredNoiseShader()
    {
        return Shader.Find("Custom/Terrain2DColoredNoise");
    }

    private static Shader FindVertexColorShader()
    {
        Shader shader = Shader.Find("Custom/TerrainLowPoly");
        if (shader == null) shader = Shader.Find("Custom/VertexColorLit");
        return shader;
    }

    private static bool UsesTexturePreview(PreviewMode mode)
    {
        return mode != PreviewMode.TerrainColoredNoise &&
               mode != PreviewMode.TerrainNoise &&
               mode != PreviewMode.Terrain3D;
    }

    private static bool UsesTerrainData(PreviewMode mode)
    {
        return mode == PreviewMode.TerrainColoredNoise ||
               mode == PreviewMode.ColoredNoise2D ||
               mode == PreviewMode.Noise2D ||
               mode == PreviewMode.TerrainNoise ||
               mode == PreviewMode.Terrain3D ||
               mode == PreviewMode.ClimateEffective2D ||
               mode == PreviewMode.TemperatureEffective ||
               mode == PreviewMode.HumidityEffective;
    }

    private static bool UsesEffectiveClimate(PreviewMode mode)
    {
        return mode == PreviewMode.TerrainColoredNoise ||
               mode == PreviewMode.ColoredNoise2D ||
               mode == PreviewMode.Terrain3D ||
               mode == PreviewMode.ClimateEffective2D ||
               mode == PreviewMode.TemperatureEffective ||
               mode == PreviewMode.HumidityEffective;
    }

    private static void ApplyColoredNoiseShaderParams(Material material, NoiseProvider np)
    {
        if (material == null || np == null)
        {
            return;
        }

        SetFloatIfPresent(material, "_BeachTop", np.beachTopFraction);
        SetFloatIfPresent(material, "_BiomeTop", np.biomeTopFraction);
        SetFloatIfPresent(material, "_RockStart", np.rockStartFraction);
        SetFloatIfPresent(material, "_RockEnd", np.rockEndFraction);
        SetFloatIfPresent(material, "_SnowLineCold", np.snowLineColdFraction);
        SetFloatIfPresent(material, "_SnowLineHot", np.snowLineHotFraction);
        SetFloatIfPresent(material, "_SnowBandWidth", np.snowBandWidthFraction);
        SetFloatIfPresent(material, "_TemperatureEnabled", np.temperatureEnabled ? 1f : 0f);
        SetFloatIfPresent(material, "_MoistureEnabled", np.moistureEnabled ? 1f : 0f);
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        int id = Shader.PropertyToID(propertyName);
        if (material.HasProperty(id))
        {
            material.SetFloat(id, value);
        }
    }

    private static bool IsColoredNoiseMaterial(Material material)
    {
        return material != null &&
               material.shader != null &&
               material.shader.name == "Custom/Terrain2DColoredNoise";
    }

    private static bool IsUnlitTextureMaterial(Material material)
    {
        return material != null &&
               material.shader != null &&
               (material.shader.name == "Universal Render Pipeline/Unlit" ||
                material.shader.name == "Unlit/Texture");
    }

    private static bool IsVertexColorMaterial(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        string shaderName = material.shader.name;
        return shaderName == "Custom/TerrainLowPoly" ||
               shaderName == "Custom/VertexColorLit";
    }

    private void EnsureComponents()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
    }

    private void Awake()
    {
        EnsureComponents();
    }

    private void OnEnable()
    {
        EnsureComponents();
    }

    private void OnValidate()
    {
        terrainSize.x = Mathf.Max(0.1f, terrainSize.x);
        terrainSize.y = Mathf.Max(0.1f, terrainSize.y);
        meshResolution = Mathf.Max(0.1f, meshResolution);
        textureResolution = Mathf.Clamp(textureResolution, 16, 4096);
    }

    private void OnDestroy()
    {
        DestroyUnityObject(_generatedMesh);
        DestroyUnityObject(_previewTexture);
        DestroyUnityObject(_runtimeMaterial);
    }

    private static Color32 Grayscale(float value)
    {
        byte gray = (byte)math.round(math.saturate(value) * 255f);
        return new Color32(gray, gray, gray, 255);
    }

    private static Color32 TemperatureColor(float value)
    {
        value = math.saturate(value);
        return GradientColor(value, s_TemperatureStops);
    }

    private static Color32 MoistureColor(float value)
    {
        value = math.saturate(value);
        return GradientColor(value, s_MoistureStops);
    }

    private static Color32 ClimateColor(float temperature, float moisture)
    {
        Color32 temp = TemperatureColor(temperature);
        Color32 moist = MoistureColor(moisture);
        Color32 mixed = Color32Lerp(temp, moist, 0.45f);

        float dryFactor = 1f - math.saturate(moisture);
        float wetFactor = math.saturate(moisture);
        mixed = Color32Lerp(mixed, new Color32(190, 150, 85, 255), dryFactor * 0.25f);
        mixed = Color32Lerp(mixed, new Color32(60, 110, 220, 255), wetFactor * 0.18f);
        return mixed;
    }

    private static Color32 TemperatureMapColor(float value)
    {
        return value >= 0f ? TemperatureColor(value) : s_DisabledClimateColor;
    }

    private static Color32 MoistureMapColor(float value)
    {
        return value >= 0f ? MoistureColor(value) : s_DisabledClimateColor;
    }

    private static Color32 ClimateMapColor(float temperature, float moisture)
    {
        bool hasTemperature = temperature >= 0f;
        bool hasMoisture = moisture >= 0f;

        if (hasTemperature && hasMoisture)
        {
            return ClimateColor(temperature, moisture);
        }

        if (hasTemperature)
        {
            return TemperatureColor(temperature);
        }

        if (hasMoisture)
        {
            return MoistureColor(moisture);
        }

        return s_DisabledClimateColor;
    }

    private readonly struct ColorStop
    {
        public readonly float t;
        public readonly Color32 color;

        public ColorStop(float t, Color32 color)
        {
            this.t = t;
            this.color = color;
        }
    }

    private static Color32 GradientColor(float value, ColorStop[] stops)
    {
        value = math.saturate(value);
        if (stops == null || stops.Length == 0)
        {
            return Grayscale(value);
        }

        if (value <= stops[0].t)
        {
            return stops[0].color;
        }

        for (int i = 1; i < stops.Length; i++)
        {
            if (value <= stops[i].t)
            {
                float range = math.max(0.0001f, stops[i].t - stops[i - 1].t);
                return Color32Lerp(stops[i - 1].color, stops[i].color, (value - stops[i - 1].t) / range);
            }
        }

        return stops[stops.Length - 1].color;
    }

    private static Color32 Color32Lerp(Color32 a, Color32 b, float t)
    {
        t = math.saturate(t);
        return new Color32(
            (byte)math.round(math.lerp(a.r, b.r, t)),
            (byte)math.round(math.lerp(a.g, b.g, t)),
            (byte)math.round(math.lerp(a.b, b.b, t)),
            255);
    }

    private static void DestroyUnityObject(UnityEngine.Object obj)
    {
        UnityObjectUtility.Destroy(obj);
    }
}
