using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-100)]
public class TerrainDecorationManager : MonoBehaviour
{
    public static TerrainDecorationManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private ComputeShader spawnerShader;

    [Header("Per-LOD Capacity (instancji per dekoracja na poziom LOD)")]
    [SerializeField] private int[] maxInstancesPerDecoPerLOD = new int[] { 8192, 16384, 32768, 65536, 131072 };

    [Header("Culling")]
    [SerializeField] private bool enableFrustumCullingGPU = true;
    [SerializeField] private float frustumExtraFOV = 10f;

    private static readonly int SrcOutputBufferID = Shader.PropertyToID("_OutputBuffer");
    private static readonly int SrcLocalDecoID = Shader.PropertyToID("_LocalDecoID");
    private static readonly int SrcMaxPerDeco = Shader.PropertyToID("_MaxPerDeco");



    private static readonly int IdMaxDecoTypes = Shader.PropertyToID("_MaxDecoTypes");
    private static readonly int IdInstancesPerSlot = Shader.PropertyToID("_InstancesPerSlot");
    private static readonly int IdLODCounters = Shader.PropertyToID("_LODCounters");
    private static readonly int IdMasterBufferRead = Shader.PropertyToID("_MasterBufferRead");
    private static readonly int IdActiveSlots = Shader.PropertyToID("_ActiveSlots");
    private static readonly int IdSlotMasterCountersRead = Shader.PropertyToID("_SlotMasterCountersRead");
    private static readonly int IdSlotMetaBuffer = Shader.PropertyToID("_SlotMetaBuffer");
    private static readonly int IdFilters = Shader.PropertyToID("_Filters");
    private static readonly int IdActiveSlotCount = Shader.PropertyToID("_ActiveSlotCount");
    private static readonly int IdEnableFrustumCulling = Shader.PropertyToID("_EnableFrustumCulling");
    private static readonly int IdFrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
    private static readonly int IdLODFadeRange = Shader.PropertyToID("_LODFadeRange");
    private static readonly int IdUpdateLODWorkOffset = Shader.PropertyToID("_UpdateLODWorkOffset");
    private static readonly int IdArgsBuffer = Shader.PropertyToID("_ArgsBuffer");
    private static readonly int IdCommandToDecoID = Shader.PropertyToID("_CommandToDecoID");
    private static readonly int IdCurrentLODIndex = Shader.PropertyToID("_CurrentLODIndex");
    private static readonly int IdCommandCount = Shader.PropertyToID("_CommandCount");
    private static readonly int IdGlobalCameraPosition = Shader.PropertyToID("_GlobalCameraPosition");

    private static readonly int[] IdLODStride =
    {
        Shader.PropertyToID("_LOD0_Stride"),
        Shader.PropertyToID("_LOD1_Stride"),
        Shader.PropertyToID("_LOD2_Stride"),
        Shader.PropertyToID("_LOD3_Stride"),
        Shader.PropertyToID("_LOD4_Stride"),
    };

    private static readonly int[] IdLODBufferRW =
    {
        Shader.PropertyToID("_LOD0_BufferRW"),
        Shader.PropertyToID("_LOD1_BufferRW"),
        Shader.PropertyToID("_LOD2_BufferRW"),
        Shader.PropertyToID("_LOD3_BufferRW"),
        Shader.PropertyToID("_LOD4_BufferRW"),
    };

    private int _kernelClearLOD;
    private int _kernelUpdateLOD;
    private int _kernelBuildArgs;
    private int _kernelMain;
    private int _kernelClearSlot;

    public int KernelMain => _kernelMain;
    public int KernelClearSlot => _kernelClearSlot;
    public ComputeShader SpawnerShader => spawnerShader;

    private GraphicsBuffer[] _lodBuffers = new GraphicsBuffer[5];
    private GraphicsBuffer[] _lodArgsBuffers = new GraphicsBuffer[5];
    private GraphicsBuffer[] _commandToDecoBuffers = new GraphicsBuffer[5];
    private GraphicsBuffer _lodCountersBuffer;
    private GraphicsBuffer _globalFiltersBuffer;


    private RenderParams[][][] _cachedRenderParams;
    private int[] _commandCountsPerLOD = new int[5];
    private Bounds _globalRenderBounds;

    private List<TerrainDecorationData> _registeredDecorations;
    private List<DecorationFilterData> _globalFilters = new();

    private Vector4[] _frustumPlanesArray = new Vector4[6];
    private Plane[] _frustumPlanesScratch = new Plane[6];
    private Camera _camera;

    private bool _initialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (BiomeDatabase.Instance == null)
        {
            enabled = false;
            return;
        }
        if (MasterDecorationPool.Instance == null)
        {
            enabled = false;
            return;
        }
        if (spawnerShader == null)
        {
            enabled = false;
            return;
        }

        _kernelMain = spawnerShader.FindKernel("CSMain");
        _kernelUpdateLOD = spawnerShader.FindKernel("CSUpdateLOD");
        _kernelBuildArgs = spawnerShader.FindKernel("CSBuildArguments");
        _kernelClearLOD = spawnerShader.FindKernel("CSClearLODCounters");
        _kernelClearSlot = spawnerShader.FindKernel("CSClearSlot");

        _camera = Camera.main;
        _registeredDecorations = BiomeDatabase.Instance.allDecorations;

        BuildGlobalFiltersBuffer();
        AllocateLODBuffers();
        BuildArgsAndCommandBuffers();
        BuildRenderParamsCache();

        _globalRenderBounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e4f, 1e6f));

        _initialized = true;
    }

    private int GetGlobalDecoCount() => _registeredDecorations?.Count ?? 0;

    private void BuildGlobalFiltersBuffer()
    {
        _globalFilters.Clear();
        for (int i = 0; i < _registeredDecorations.Count; i++)
        {
            var deco = _registeredDecorations[i];
            if (deco == null)
            {
                _globalFilters.Add(default);
                continue;
            }

            float minT, maxT, minM, maxM;
            if (deco.filterMode == TerrainDecorationData.FilterMode.Biome)
            {
                BiomeDatabase.GetZoneBounds(deco.biomeZone, out minT, out maxT, out minM, out maxM);
            }
            else
            {
                minT = deco.minTemperature; maxT = deco.maxTemperature;
                minM = deco.minMoisture;    maxM = deco.maxMoisture;
            }





            float l0 = GetLODMaxDist(deco, 0, 0f);
            float l1 = GetLODMaxDist(deco, 1, l0);
            float l2 = GetLODMaxDist(deco, 2, l1);
            float l3 = GetLODMaxDist(deco, 3, l2);
            float l4 = GetLODMaxDist(deco, 4, l3);

            _globalFilters.Add(new DecorationFilterData
            {
                indexID = i,
                renderMode = (int)deco.renderMode,
                densityPer100m2 = deco.densityPer100m2,
                minHeight = deco.minHeight, maxHeight = deco.maxHeight,
                minSlope = deco.minSlope, maxSlope = deco.maxSlope,
                minTemperature = minT, maxTemperature = maxT,
                minMoisture = minM, maxMoisture = maxM,
                scaleMin = deco.scaleMin, scaleMax = deco.scaleMax,
                randomYRotation = deco.randomYRotation ? 1 : 0,
                alignToTerrainNormal = deco.alignToTerrainNormal ? 1 : 0,
                lod0MaxDist = l0,
                lod1MaxDist = l1,
                lod2MaxDist = l2,
                lod3MaxDist = l3,
                lod4MaxDist = l4
            });
        }

        if (_globalFilters.Count == 0) return;

        _globalFiltersBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _globalFilters.Count, DecorationFilterData.Size);
        _globalFiltersBuffer.SetData(_globalFilters);
    }

    private static float GetLODMaxDist(TerrainDecorationData deco, int lod, float prevMax)
    {
        if (deco.gpuLODs == null || lod >= deco.gpuLODs.Length) return prevMax;
        var setup = deco.gpuLODs[lod];
        if (setup.mesh == null || setup.materials == null || setup.materials.Length == 0)
            return prevMax;


        return Mathf.Max(setup.maxDistance, prevMax);
    }

    public GraphicsBuffer GlobalFiltersBuffer => _globalFiltersBuffer;

    private void AllocateLODBuffers()
    {
        int maxDeco = MasterDecorationPool.Instance.MaxDecorationTypes;

        for (int lod = 0; lod < 5; lod++)
        {
            int perDeco = maxInstancesPerDecoPerLOD[Mathf.Min(lod, maxInstancesPerDecoPerLOD.Length - 1)];
            _lodBuffers[lod] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxDeco * perDeco, MasterDecorationSpawnResult.Size);
        }

        _lodCountersBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5 * maxDeco, sizeof(uint));
        _lodCountersBuffer.SetData(new uint[5 * maxDeco]);
    }

    private void BuildArgsAndCommandBuffers()
    {
        int decoCount = _registeredDecorations.Count;

        for (int lod = 0; lod < 5; lod++)
        {
            var argsList = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
            var decoIDList = new List<uint>();

            for (int d = 0; d < decoCount; d++)
            {
                var deco = _registeredDecorations[d];
                if (deco == null) continue;
                if (lod >= deco.gpuLODs.Length) continue;

                var lodSetup = deco.gpuLODs[lod];
                if (lodSetup.mesh == null || lodSetup.materials == null || lodSetup.materials.Length == 0) continue;

                for (int s = 0; s < lodSetup.mesh.subMeshCount; s++)
                {
                    var sub = lodSetup.mesh.GetSubMesh(s);
                    argsList.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = (uint)sub.indexCount,
                        instanceCount = 0,
                        startIndex = (uint)sub.indexStart,
                        baseVertexIndex = (uint)sub.baseVertex,
                        startInstance = 0
                    });
                    decoIDList.Add((uint)d);
                }
            }

            int commandCount = argsList.Count;
            _commandCountsPerLOD[lod] = commandCount;

            if (commandCount > 0)
            {
                _lodArgsBuffers[lod] = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                    commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                _lodArgsBuffers[lod].SetData(argsList);

                _commandToDecoBuffers[lod] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, commandCount, sizeof(uint));
                _commandToDecoBuffers[lod].SetData(decoIDList);
            }
        }
    }

    private void BuildRenderParamsCache()
    {
        int decoCount = _registeredDecorations.Count;
        int maxDeco = MasterDecorationPool.Instance.MaxDecorationTypes;

        Bounds staticGlobalBounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e4f, 1e6f));

        _cachedRenderParams = new RenderParams[5][][];
        for (int lod = 0; lod < 5; lod++)
        {
            int perDeco = maxInstancesPerDecoPerLOD[Mathf.Min(lod, maxInstancesPerDecoPerLOD.Length - 1)];
            _cachedRenderParams[lod] = new RenderParams[decoCount][];

            for (int d = 0; d < decoCount; d++)
            {
                var deco = _registeredDecorations[d];
                if (deco == null) continue;
                if (lod >= deco.gpuLODs.Length) continue;

                var lodSetup = deco.gpuLODs[lod];
                if (lodSetup.mesh == null || lodSetup.materials == null || lodSetup.materials.Length == 0) continue;

                int submeshCount = lodSetup.mesh.subMeshCount;
                _cachedRenderParams[lod][d] = new RenderParams[submeshCount];

                for (int s = 0; s < submeshCount; s++)
                {
                    if (s >= lodSetup.materials.Length || lodSetup.materials[s] == null) continue;

                    var mpb = new MaterialPropertyBlock();
                    mpb.SetBuffer(SrcOutputBufferID, _lodBuffers[lod]);
                    mpb.SetInt(SrcLocalDecoID, d);
                    mpb.SetInt(SrcMaxPerDeco, perDeco);
                    mpb.SetMatrix("unity_ObjectToWorld", Matrix4x4.identity);
                    mpb.SetMatrix("unity_WorldToObject", Matrix4x4.identity);

                    _cachedRenderParams[lod][d][s] = new RenderParams(lodSetup.materials[s])
                    {

                        shadowCastingMode = lod <= 3 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                        receiveShadows = false,
                        layer = gameObject.layer,
                        matProps = mpb,
                        worldBounds = staticGlobalBounds 
                    };
                }
            }
        }
    }

    private void Update()
    {
        if (!_initialized) return;

        var pool = MasterDecorationPool.Instance;

        if (pool == null || pool.ActiveSlotCount == 0) return;

        pool.RefreshActiveSlotsIfDirty();
        UpdateCameraAndFrustum();

        int activeSlots = pool.ActiveSlotCount;
        int decoCount = _registeredDecorations.Count;
        int maxDeco = pool.MaxDecorationTypes;

        spawnerShader.SetInt(IdMaxDecoTypes, maxDeco);
        spawnerShader.SetInt(IdInstancesPerSlot, pool.InstancesPerSlot);


        spawnerShader.SetBuffer(_kernelClearLOD, IdLODCounters, _lodCountersBuffer);
        int clearGroups = Mathf.CeilToInt((5 * maxDeco) / 64f);
        spawnerShader.Dispatch(_kernelClearLOD, clearGroups, 1, 1);


        spawnerShader.SetBuffer(_kernelUpdateLOD, IdMasterBufferRead, pool.MasterBuffer);
        spawnerShader.SetBuffer(_kernelUpdateLOD, IdActiveSlots, pool.ActiveSlotsBuffer);
        spawnerShader.SetBuffer(_kernelUpdateLOD, IdSlotMasterCountersRead, pool.SlotMasterCounters);
        spawnerShader.SetBuffer(_kernelUpdateLOD, IdSlotMetaBuffer, pool.SlotMetaBuffer);

        spawnerShader.SetBuffer(_kernelUpdateLOD, IdFilters, _globalFiltersBuffer);
        spawnerShader.SetInt(IdActiveSlotCount, activeSlots);
        spawnerShader.SetInt(IdEnableFrustumCulling, enableFrustumCullingGPU ? 1 : 0);

        for (int lod = 0; lod < 5; lod++)
        {
            spawnerShader.SetBuffer(_kernelUpdateLOD, IdLODBufferRW[lod], _lodBuffers[lod]);
        }
        spawnerShader.SetBuffer(_kernelUpdateLOD, IdLODCounters, _lodCountersBuffer);

        SetCameraPositionUniform();
        spawnerShader.SetVectorArray(IdFrustumPlanes, _frustumPlanesArray);




        spawnerShader.SetFloat(IdLODFadeRange, 0.15f);




        for (int lod = 0; lod < 5; lod++)
        {
            int stride = maxInstancesPerDecoPerLOD[Mathf.Min(lod, maxInstancesPerDecoPerLOD.Length - 1)];
            spawnerShader.SetInt(IdLODStride[lod], stride);
        }




        const int UPDATE_LOD_THREADS_PER_GROUP = 256;
        const int UPDATE_LOD_MAX_GROUPS = 65535;
        int updateLodBatchWork = UPDATE_LOD_THREADS_PER_GROUP * UPDATE_LOD_MAX_GROUPS;
        int totalWork = activeSlots * pool.InstancesPerSlot;

        for (int workOffset = 0; workOffset < totalWork; workOffset += updateLodBatchWork)
        {
            int workThisBatch = Mathf.Min(updateLodBatchWork, totalWork - workOffset);
            int groupsThisBatch = Mathf.CeilToInt(workThisBatch / (float)UPDATE_LOD_THREADS_PER_GROUP);
            if (groupsThisBatch <= 0) break;
            spawnerShader.SetInt(IdUpdateLODWorkOffset, workOffset);
            spawnerShader.Dispatch(_kernelUpdateLOD, groupsThisBatch, 1, 1);
        }


        for (int lod = 0; lod < 5; lod++)
        {
            int cmd = _commandCountsPerLOD[lod];
            if (cmd == 0 || _lodArgsBuffers[lod] == null) continue;

            spawnerShader.SetBuffer(_kernelBuildArgs, IdArgsBuffer, _lodArgsBuffers[lod]);
            spawnerShader.SetBuffer(_kernelBuildArgs, IdCommandToDecoID, _commandToDecoBuffers[lod]);
            spawnerShader.SetBuffer(_kernelBuildArgs, IdLODCounters, _lodCountersBuffer);
            spawnerShader.SetInt(IdCurrentLODIndex, lod);
            spawnerShader.SetInt(IdCommandCount, cmd);
            spawnerShader.Dispatch(_kernelBuildArgs, Mathf.CeilToInt(cmd / 64f), 1, 1);
        }



        for (int lod = 0; lod < 5; lod++)
        {
            if (_lodArgsBuffers[lod] == null) continue;

            int globalCommandIdx = 0;
            for (int d = 0; d < decoCount; d++)
            {
                var deco = _registeredDecorations[d];
                if (deco == null) continue;
                if (lod >= deco.gpuLODs.Length) continue;

                var lodSetup = deco.gpuLODs[lod];
                if (lodSetup.mesh == null || lodSetup.materials == null || lodSetup.materials.Length == 0) continue;
                if (_cachedRenderParams[lod][d] == null) { globalCommandIdx += lodSetup.mesh.subMeshCount; continue; }






                int perDeco = maxInstancesPerDecoPerLOD[Mathf.Min(lod, maxInstancesPerDecoPerLOD.Length - 1)];
                Shader.SetGlobalBuffer(SrcOutputBufferID, _lodBuffers[lod]);
                Shader.SetGlobalInt(SrcLocalDecoID, d);
                Shader.SetGlobalInt(SrcMaxPerDeco, perDeco);

                int submeshCount = lodSetup.mesh.subMeshCount;
                for (int s = 0; s < submeshCount; s++)
                {
                    var rp = _cachedRenderParams[lod][d][s];
                    if (rp.matProps == null) { globalCommandIdx++; continue; }

                    Graphics.RenderMeshIndirect(rp, lodSetup.mesh, _lodArgsBuffers[lod], 1, globalCommandIdx);
                    globalCommandIdx++;
                }
            }
        }
    }

    private void SetCameraPositionUniform()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null)
        {
            var p = _camera.transform.position;
            spawnerShader.SetVector(IdGlobalCameraPosition, new Vector4(p.x, p.y, p.z, 0f));
        }
    }

    private void UpdateCameraAndFrustum()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;




        if (frustumExtraFOV > 0.01f)
        {
            float origFov = _camera.fieldOfView;
            _camera.fieldOfView = origFov + frustumExtraFOV;
            GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanesScratch);
            _camera.fieldOfView = origFov;
        }
        else
        {
            GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanesScratch);
        }

        for (int i = 0; i < 6; i++)
        {
            var p = _frustumPlanesScratch[i];
            _frustumPlanesArray[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        for (int i = 0; i < 5; i++)
        {
            _lodBuffers[i]?.Release(); _lodBuffers[i] = null;
            _lodArgsBuffers[i]?.Release(); _lodArgsBuffers[i] = null;
            _commandToDecoBuffers[i]?.Release(); _commandToDecoBuffers[i] = null;
        }
        _lodCountersBuffer?.Release(); _lodCountersBuffer = null;
        _globalFiltersBuffer?.Release(); _globalFiltersBuffer = null;
    }
}
