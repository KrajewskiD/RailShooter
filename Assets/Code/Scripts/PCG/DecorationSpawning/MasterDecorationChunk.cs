using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class MasterDecorationChunk : MonoBehaviour
{
    [SerializeField] private float slopeSampleRadius = 2f;

    private int _slotID = -1;

    private Texture2D _chunkDataTexture;
    private static Dictionary<int, Texture2D> s_TexturePool = new Dictionary<int, Texture2D>();
    private Bounds _chunkWorldBounds;
    private List<int> _activeGlobalDecoIDs = new List<int>(32);

    public int SlotID => _slotID;

    private void OnDisable()
    {
        ReleaseSlot();
    }

    private void OnDestroy()
    {
        ReleaseSlot();
    }

    public void ReleaseSlot()
    {
        if (_slotID >= 0 && MasterDecorationPool.Instance != null)
        {
            MasterDecorationPool.Instance.ReleaseSlot(_slotID);
        }
        _slotID = -1;
    }

    public void ExecuteGPUGeneration(
        float startX,
        float startZ,
        float chunkSize,
        int seed,
        NativeArray<float4> chunkData,
        int borderedVCount,
        float resolution)
    {
        var pool = MasterDecorationPool.Instance;
        var mgr = TerrainDecorationManager.Instance;
        if (pool == null) return;
        if (mgr == null) return;


        if (_slotID < 0) return;

        var np = NoiseProvider.Instance;
        float maxH = np.GetAbsoluteMaxHeight();
        float minH = -10f;

        _chunkWorldBounds = new Bounds(
            new Vector3(startX + chunkSize * 0.5f, (minH + maxH) * 0.5f, startZ + chunkSize * 0.5f),
            new Vector3(chunkSize, maxH - minH, chunkSize)
        );








        pool.UpdateSlotBounds(_slotID, _chunkWorldBounds.center, _chunkWorldBounds.extents);


        if (!s_TexturePool.TryGetValue(borderedVCount, out _chunkDataTexture) || _chunkDataTexture == null)
        {
            _chunkDataTexture = new Texture2D(borderedVCount, borderedVCount, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            s_TexturePool[borderedVCount] = _chunkDataTexture;
        }
        _chunkDataTexture.SetPixelData(chunkData, 0);
        _chunkDataTexture.Apply(false, false);


        float midX = startX + chunkSize * 0.5f;
        float midZ = startZ + chunkSize * 0.5f;
        ChunkGenerator.ClimateSample centerClimate = np.SampleEffectiveClimate(midX, midZ);
        float tempAtCenter = centerClimate.temperature;
        float moistAtCenter = centerClimate.moisture;

        var all = BiomeDatabase.Instance.allDecorations;
        _activeGlobalDecoIDs.Clear();

        for (int i = 0; i < all.Count; i++)
        {
            var deco = all[i];
            if (deco == null) continue;

            float minT, maxT, minM, maxM;
            if (deco.filterMode == TerrainDecorationData.FilterMode.Biome)
                BiomeDatabase.GetZoneBounds(deco.biomeZone, out minT, out maxT, out minM, out maxM);
            else
            {
                minT = deco.minTemperature; maxT = deco.maxTemperature;
                minM = deco.minMoisture;    maxM = deco.maxMoisture;
            }

            if (tempAtCenter >= 0f && (tempAtCenter < minT || tempAtCenter > maxT)) continue;
            if (moistAtCenter >= 0f && (moistAtCenter < minM || moistAtCenter > maxM)) continue;
            _activeGlobalDecoIDs.Add(i);
        }

        if (_activeGlobalDecoIDs.Count == 0)
        {


            ResetSlotCounter();
            return;
        }


        ResetSlotCounter();


        var shader = mgr.SpawnerShader;
        int kMain = mgr.KernelMain;

        shader.SetBuffer(kMain, "_Filters", mgr.GlobalFiltersBuffer);
        shader.SetTexture(kMain, "_ChunkData", _chunkDataTexture);
        shader.SetBuffer(kMain, "_MasterBuffer", pool.MasterBuffer);
        shader.SetBuffer(kMain, "_SlotMasterCounters", pool.SlotMasterCounters);

        shader.SetInt("_InstancesPerSlot", pool.InstancesPerSlot);
        shader.SetInt("_TargetSlotID", _slotID);
        shader.SetInt("_BorderedVCount", borderedVCount);
        shader.SetFloat("_Resolution", resolution);
        shader.SetFloat("_StartX", startX);
        shader.SetFloat("_StartZ", startZ);
        shader.SetFloat("_ChunkSize", chunkSize);
        shader.SetFloat("_SlopeSampleRadius", slopeSampleRadius);
        shader.SetInt("_ChunkSeed", seed);

        for (int idx = 0; idx < _activeGlobalDecoIDs.Count; idx++)
        {
            int globalDecoID = _activeGlobalDecoIDs[idx];
            var deco = all[globalDecoID];


            const int THREADS_PER_GROUP = 128;


            float cellSize = Mathf.Max(0.1f, deco.scaleMax * 2.0f);
            int cellsPerAxis = Mathf.Max(1, (int)(chunkSize / cellSize));
            int maxObjects = cellsPerAxis * cellsPerAxis;
            int groups = Mathf.CeilToInt(maxObjects / (float)THREADS_PER_GROUP);

            if (groups > 65535)
            {
                groups = 65535;
            }


            shader.SetInt("_SpawnStrategy", (int)deco.spawnStrategy);
            shader.SetFloat("_ClusterScale", deco.clusterScale);
            shader.SetFloat("_ClusterThreshold", deco.clusterThreshold);

            shader.SetInt("_CurrentGlobalDecoID", globalDecoID);
            shader.Dispatch(kMain, groups, 1, 1);
        }

    }

    public void SetSlot(int id) 
{ 
    _slotID = id; 
}

    private void ResetSlotCounter()
    {
        var pool = MasterDecorationPool.Instance;
        var mgr = TerrainDecorationManager.Instance;
        if (pool == null || mgr == null || _slotID < 0) return;

        pool.ClearSlotCounterGPU(mgr.SpawnerShader, mgr.KernelClearSlot, _slotID);
    }
}
