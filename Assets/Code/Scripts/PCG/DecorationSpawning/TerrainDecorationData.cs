using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "NewDecoration", menuName = "RogueLike/Terrain/Decoration")]
public class TerrainDecorationData : ScriptableObject
{
    public enum FilterMode { Climate, Biome }
    public enum SpawnStrategy { Grid, RandomSpread, Clustered }

    [System.Serializable]
    public struct LODMeshSetup
    {
        public Mesh mesh;
        public Material[] materials;
        public float maxDistance;
    }

    [Header("Render Engine")]
    public DecorationRenderMode renderMode = DecorationRenderMode.GPUInstanced;
    public LODMeshSetup[] gpuLODs = new LODMeshSetup[5];

    [Header("Density (per 100m²)")]
    [Range(0f, 2000f)] public float densityPer100m2 = 0.3f;

    [Header("Placement Strategy")]
    public SpawnStrategy spawnStrategy = SpawnStrategy.RandomSpread;
    [ShowIf(nameof(IsClusteredMode))]
    [Tooltip("World-space size of cluster patches. Larger = wider, sparser clumps.")]
    public float clusterScale = 12f;
    [ShowIf(nameof(IsClusteredMode))]
    [Range(0f, 1f)]
    [Tooltip("Cluster-edge cutoff. Higher = smaller, denser pockets.")]
    public float clusterThreshold = 0.45f;

    [Header("Height & Slope Filters")]
    public float minHeight = 30f;
    public float maxHeight = 500f;
    [Range(0f, 1f)] public float minSlope = 0f;
    [Range(0f, 1f)] public float maxSlope = 0.4f;

    [HorizontalLine(color: EColor.Gray)]
    [Header("Filter Strategy")]
    [HideInInspector] public FilterMode filterMode = FilterMode.Climate;

    [Button("Climate")]
    private void UseClimateMode() => filterMode = FilterMode.Climate;
    [Button("Biome")]
    private void UseBiomeMode() => filterMode = FilterMode.Biome;

    [ShowIf(nameof(IsClimateMode))]
    [Label("Min Temperature")] [Range(0f, 1f)] public float minTemperature = 0f;
    [ShowIf(nameof(IsClimateMode))]
    [Label("Max Temperature")] [Range(0f, 1f)] public float maxTemperature = 1f;
    [ShowIf(nameof(IsClimateMode))]
    [Label("Min Moisture")] [Range(0f, 1f)] public float minMoisture = 0f;
    [ShowIf(nameof(IsClimateMode))]
    [Label("Max Moisture")] [Range(0f, 1f)] public float maxMoisture = 1f;

    [ShowIf(nameof(IsBiomeMode))]
    [Label("Biome Zone")] public BiomeZone biomeZone = BiomeZone.NormMid;

    private bool IsClimateMode => filterMode == FilterMode.Climate;
    private bool IsBiomeMode => filterMode == FilterMode.Biome;
    private bool IsClusteredMode => spawnStrategy == SpawnStrategy.Clustered;

    [Header("Variation")]
    public float scaleMin = 0.8f;
    public float scaleMax = 1.3f;
    public bool randomYRotation = true;
    public bool alignToTerrainNormal = false;

    [Header("LOD")]
    [Range(0, 4)] public int maxLODIndex = 1;

    [Header("Biome Tint")]
    public bool applyBiomeTint = false;
    public string[] tintShaderProperties = { "_BaseTint", "BiomeColor" };
    [ColorUsage(true, false)] public Color[] tintPalette = new Color[0];
    public float biomeMix = 0.4f;
    public float tintStrength = 1f;
}