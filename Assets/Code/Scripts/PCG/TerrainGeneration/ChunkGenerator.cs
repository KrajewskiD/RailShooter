using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Profiling;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine.Splines;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkGenerator : MonoBehaviour
{
    private Mesh _mesh;
    [SerializeField] private bool generateCollider = false;
    public int currentLodIndex { get; private set; } = -1;

    enum GenState { Idle, Meshing, ReadyToApply }
    GenState _state = GenState.Idle;
    public bool IsBusy => _state != GenState.Idle;
    public bool IsReady => _hasShownMesh && _state == GenState.Idle;
    public bool CanBeRecycled => _state == GenState.Idle;

    JobHandle _meshHandle;
    int _pendingVerticesPerLine;
    int _pendingTotalVertices;
    int _pendingTotalTriangles;
    float _pendingStartX, _pendingStartZ, _pendingSize;
    int _pendingLodIndex;
    int _pendingDecoSeed;

    private NativeArray<float4> _currentChunkData;
    private float[,] _cachedHeightsBuffer;

    private int _pendingBorderedVCount;
    private float _pendingResolution;

    private NativeArray<VertexData> _vertexBuffer;
    private NativeArray<ushort> _indices16;
    private NativeArray<int> _indices32;
    private bool _isFormat16;
    private bool _buffersAllocated;

    [StructLayout(LayoutKind.Sequential)]
    public struct VertexData
    {
        public Vector3 position;  
        public Vector3 normal; 
        public Color32   color;     
    }

    static readonly VertexAttributeDescriptor[] s_VertexLayout =
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.UNorm8, 4),
    };
    const MeshUpdateFlags k_UploadFlags =
        MeshUpdateFlags.DontRecalculateBounds |
        MeshUpdateFlags.DontValidateIndices |
        MeshUpdateFlags.DontNotifyMeshUsers |
        MeshUpdateFlags.DontResetBoneBounds;

    MeshRenderer _meshRenderer;
    MeshFilter _meshFilter;
    MasterDecorationChunk _decorator;
    TerrainCollider _terrainCollider;
    TerrainData _terrainData;
    bool _hasShownMesh;

    void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _meshFilter = GetComponent<MeshFilter>();
        TryGetComponent(out _decorator);
        TryGetComponent(out _terrainCollider);

        if (_terrainCollider != null)
        {
            _terrainCollider.enabled = generateCollider;
        }
        _meshRenderer.enabled = false;
        _state = GenState.Idle;


        enabled = false;
    }







    private static readonly ProfilerMarker s_MarkerSkipped    = new ProfilerMarker("ChunkGen.Generate.Skipped");
    private static readonly ProfilerMarker s_MarkerScheduled  = new ProfilerMarker("ChunkGen.Generate.Scheduled");
    private static readonly ProfilerMarker s_MarkerApplyMesh  = new ProfilerMarker("ChunkGen.Apply.Mesh");
    private static readonly ProfilerMarker s_MarkerCollider   = new ProfilerMarker("ChunkGen.Apply.Collider");
    private static readonly ProfilerMarker s_MarkerDecorator  = new ProfilerMarker("ChunkGen.Apply.Decorator");
    private static readonly ProfilerMarker s_MarkerCompleteStall = new ProfilerMarker("ChunkGen.Apply.CompleteStall");




#if TERRAIN_PERF_DEBUG



    internal static int s_GenerateCallsThisFrame;
    internal static int s_GenerateScheduledThisFrame;
    internal static int s_CompleteCallsThisFrame;
    internal static int s_ApplyMeshCallsThisFrame;
    internal static int s_ColliderUpdatesThisFrame;
    internal static int s_DecoratorDispatchesThisFrame;
    internal static int s_ApplyCompleteStallCount;
    internal static int s_LastResetFrame;

    internal static void ResetPerfDebugCounters()
    {
        s_GenerateCallsThisFrame = 0;
        s_GenerateScheduledThisFrame = 0;
        s_CompleteCallsThisFrame = 0;
        s_ApplyMeshCallsThisFrame = 0;
        s_ColliderUpdatesThisFrame = 0;
        s_DecoratorDispatchesThisFrame = 0;
        s_ApplyCompleteStallCount = 0;
        s_LastResetFrame = Time.frameCount;
    }
#endif

    public bool Generate(Vector2Int id, float startX, float startZ, float size, float resolution, TerrainSettings settings, int lodIndex)
    {
#if TERRAIN_PERF_DEBUG
        s_GenerateCallsThisFrame++;
#endif
        if (_state != GenState.Idle)
        {
            s_MarkerSkipped.Begin();
            s_MarkerSkipped.End();
            return false;
        }

        if (currentLodIndex == lodIndex && _hasShownMesh)
        {
            s_MarkerSkipped.Begin();
            s_MarkerSkipped.End();
            return false;
        }

        var np = NoiseProvider.Instance;
        if (np == null)
        {
            return false;
        }

        np.EnsureReadyForTerrainJobs();
        if (!np.IsReadyForTerrainJobs)
        {
            return false;
        }

        s_MarkerScheduled.Begin();
#if TERRAIN_PERF_DEBUG
        s_GenerateScheduledThisFrame++;
#endif

        gameObject.name = $"Chunk_{id.x}_{id.y}";

        int segments = Mathf.Max(1, Mathf.FloorToInt(size / resolution));
        int verticesPerLine = segments + 1;
        int borderedVerticesPerLine = verticesPerLine + 2;
        int totalBorderedVertices = borderedVerticesPerLine * borderedVerticesPerLine;
        int totalVertices = verticesPerLine * verticesPerLine;
        int totalTriangles = segments * segments * 6;

        IndexFormat format = (totalVertices > 65535) ? IndexFormat.UInt32 : IndexFormat.UInt16;

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "GridChunk", indexFormat = format };
            _mesh.MarkDynamic();
            _meshFilter.mesh = _mesh;
        }

        else
        {
            _mesh.indexFormat = format;
        }

        _currentChunkData = NativeArrayPool.Get<float4>(totalBorderedVertices);

        _pendingBorderedVCount = borderedVerticesPerLine;
        _pendingResolution = resolution;

        _isFormat16 = (format == IndexFormat.UInt16);

        _vertexBuffer = NativeArrayPool.Get<VertexData>(totalVertices);
        if (_isFormat16) _indices16 = NativeArrayPool.Get<ushort>(totalTriangles);
        else _indices32 = NativeArrayPool.Get<int>(totalTriangles);

        _buffersAllocated = true;

        var vertexBuffer = _vertexBuffer;
        var indices16 = _indices16;
        var indices32 = _indices32;


        var biomeParams = BuildBiomeParams(np);

        float radiusToClear = 25f;
        bool isSplineAvailable = false;
        Unity.Collections.FixedList512Bytes<float3> corridorSamples = default;




        bool needsSplineCheck = lodIndex < 2 && PathMath.Instance != null && PathMath.Instance.pathSpline != null;

        if (needsSplineCheck)
        {



            radiusToClear = PathMath.Instance.corridorWidth + 4f;
            float chunkCenterX = startX + size * 0.5f;
            float chunkCenterZ = startZ + size * 0.5f;
            float chunkHalfDiag = size * 0.7072f;
            float maxTreeHeight = 25f;
            float splineRejectRadius = radiusToClear + maxTreeHeight + chunkHalfDiag + 8f;
            float minSqrToSpline = PathMath.Instance.GetMinDistanceXZSqrToSpline(chunkCenterX, chunkCenterZ);

            if (minSqrToSpline <= splineRejectRadius * splineRejectRadius)
            {




                int samples = PathMath.Instance.AppendCorridorSamples(chunkCenterX, chunkCenterZ, splineRejectRadius, ref corridorSamples);
                isSplineAvailable = samples > 0;
            }
        }

        JobHandle handle1;
        if (np.useBurst)
        {
            var terrainJob = new GenerateTerrainJobBurst
            {
                startX = startX, startZ = startZ, resolution = resolution,
                borderedVCount = borderedVerticesPerLine, vCount = verticesPerLine,
                baseNoises = np.BaseNoiseLayersBurst,
                baseLayerSettings = np.BaseNoiseLayerSettingsBurst,
                permutation = np.Perm512,
                temperatureNoise = np.TemperatureNoiseBurst,
                moistureNoise = np.MoistureNoiseBurst,
                fAmp = np.baseAmplitude,
                heightCurveLUT = np.heightCurveLUT,
                biomeParams = biomeParams,
                vertexData = vertexBuffer,
                chunkData = _currentChunkData,
                corridorSamples = corridorSamples,
                clearRadius = radiusToClear,
                hasSpline = isSplineAvailable
            };
            handle1 = terrainJob.Schedule(totalBorderedVertices, 64);
        }
        else
        {
            var terrainJob = new GenerateTerrainJobMono
            {
                startX = startX, startZ = startZ, resolution = resolution,
                borderedVCount = borderedVerticesPerLine, vCount = verticesPerLine,
                baseNoises = np.BaseNoiseLayersBurst,
                baseLayerSettings = np.BaseNoiseLayerSettingsBurst,
                permutation = np.Perm512,
                temperatureNoise = np.TemperatureNoiseBurst,
                moistureNoise = np.MoistureNoiseBurst,
                fAmp = np.baseAmplitude,
                heightCurveLUT = np.heightCurveLUT,
                biomeParams = biomeParams,
                vertexData = vertexBuffer,
                chunkData = _currentChunkData
            };
            handle1 = terrainJob.Schedule(totalBorderedVertices, 64);
        }

        np.RegisterHeightLutReader(handle1);

        var normalsJob = new CalculateNormalsJob
        {
            chunkData = _currentChunkData, vertexData = vertexBuffer,
            borderedVCount = borderedVerticesPerLine, vCount = verticesPerLine, resolution = resolution
        };
        JobHandle handle2 = normalsJob.Schedule(totalVertices, 64, handle1);

        JobHandle handle3;
        if (format == IndexFormat.UInt16)
        {
            var trianglesJob = new BuildTrianglesJob16
            {
                triangles = indices16,
                segments = segments,
                vCount = verticesPerLine
            };
            handle3 = trianglesJob.Schedule(segments * segments, 64, handle2);
        }
        else
        {
            var trianglesJob = new BuildTrianglesJob32
            {
                triangles = indices32,
                segments = segments,
                vCount = verticesPerLine
            };
            handle3 = trianglesJob.Schedule(segments * segments, 64, handle2);
        }
        _meshHandle = handle3;






        _state = GenState.Meshing;

        _pendingVerticesPerLine = verticesPerLine;
        _pendingTotalVertices   = totalVertices;
        _pendingTotalTriangles  = totalTriangles;
        _pendingStartX = startX;
        _pendingStartZ = startZ;
        _pendingSize   = size;
        _pendingLodIndex = lodIndex;
        _pendingDecoSeed = unchecked(id.x * 73856093 ^ id.y * 19349663);

        currentLodIndex = lodIndex;


        var mgr = TerrainManager.Instance;
        if (mgr != null)
        {
            mgr.RegisterMeshingChunk(this);

        }
        else
        {
            enabled = true;
        }
        s_MarkerScheduled.End();
        return true;
    }



    internal bool PollMeshingCompletion()
    {
        if (_state != GenState.Meshing) return true;
        if (!_meshHandle.IsCompleted) return false;
        _meshHandle.Complete();
#if TERRAIN_PERF_DEBUG
        s_CompleteCallsThisFrame++;
#endif
        _state = GenState.ReadyToApply;
        TerrainManager manager = TerrainManager.Instance;
        if (manager != null)
            manager.RegisterReadyChunk(this);
        else
            ApplyMesh();
        return true;
    }

    void LateUpdate()
    {

        if (_state != GenState.Meshing) { enabled = false; return; }
        if (!_meshHandle.IsCompleted) return;

        _meshHandle.Complete();
        _state = GenState.ReadyToApply;
        if (TerrainManager.Instance != null)
            TerrainManager.Instance.RegisterReadyChunk(this);
        else
            ApplyMesh();
        enabled = false;
    }

    public void ApplyMesh()
    {




        if (_state == GenState.Meshing)
        {
            if (!_meshHandle.IsCompleted)
            {
                s_MarkerCompleteStall.Begin();
#if TERRAIN_PERF_DEBUG
                s_ApplyCompleteStallCount++;
#endif
                _meshHandle.Complete();
                s_MarkerCompleteStall.End();
            }
            else
            {
                _meshHandle.Complete();
            }
#if TERRAIN_PERF_DEBUG
            s_CompleteCallsThisFrame++;
#endif
            _state = GenState.ReadyToApply;
        }

        if (_state != GenState.ReadyToApply)
        {
            return;
        }


        using var applyScope = s_MarkerApplyMesh.Auto();
#if TERRAIN_PERF_DEBUG
        s_ApplyMeshCallsThisFrame++;
#endif

        int vertexCount = _pendingTotalVertices;
        int indexCount = _pendingTotalTriangles;

        bool hasVertexData = _buffersAllocated && _vertexBuffer.IsCreated && _vertexBuffer.Length >= vertexCount;
        bool hasIndexData = _isFormat16
            ? _indices16.IsCreated && _indices16.Length >= indexCount
            : _indices32.IsCreated && _indices32.Length >= indexCount;

        if (!hasVertexData || !hasIndexData)
        {
            Cleanup();
            _state = GenState.Idle;
            return;
        }

        var np = NoiseProvider.Instance;
        float yMax = np != null ? np.GetAbsoluteMaxHeight() : 200f;
        float yMin = -10f;
        var meshBounds = new Bounds(
            new Vector3(_pendingSize * 0.5f, (yMin + yMax) * 0.5f, _pendingSize * 0.5f),
            new Vector3(_pendingSize + 1f, yMax - yMin, _pendingSize + 1f));

        _mesh.SetVertexBufferParams(vertexCount, s_VertexLayout);
        _mesh.SetVertexBufferData(_vertexBuffer, 0, 0, vertexCount, 0, k_UploadFlags);

        if (_isFormat16)
        {
            _mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
            _mesh.SetIndexBufferData(_indices16, 0, 0, indexCount, k_UploadFlags);
        }
        else
        {
            _mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(_indices32, 0, 0, indexCount, k_UploadFlags);
        }

        _mesh.subMeshCount = 1;
        _mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
        {
            firstVertex = 0,
            vertexCount = vertexCount,
            bounds = meshBounds
        }, k_UploadFlags);

        _mesh.bounds = meshBounds;

        _meshRenderer.enabled = true;
        _hasShownMesh = true;

        if (_decorator != null)
        {
            var pool = MasterDecorationPool.Instance;
            if (pool != null && _decorator.SlotID < 0)
            {





                const float DECORATION_HEIGHT_BUFFER = 20f;
                var worldBoundsCenter = new Vector3(
                    _pendingStartX + _pendingSize * 0.5f,
                    (yMin + yMax) * 0.5f + DECORATION_HEIGHT_BUFFER * 0.5f,
                    _pendingStartZ + _pendingSize * 0.5f);
                var worldBoundsExtents = new Vector3(
                    _pendingSize * 0.5f,
                    (yMax - yMin) * 0.5f + DECORATION_HEIGHT_BUFFER * 0.5f,
                    _pendingSize * 0.5f);
                int newSlot = pool.AcquireSlot(worldBoundsCenter, worldBoundsExtents);
                _decorator.SetSlot(newSlot);
            }

            if (pool != null)
            {
                s_MarkerDecorator.Begin();
#if TERRAIN_PERF_DEBUG
                s_DecoratorDispatchesThisFrame++;
#endif
                _decorator.ExecuteGPUGeneration(
                    _pendingStartX,
                    _pendingStartZ,
                    _pendingSize,
                    _pendingDecoSeed,
                    _currentChunkData,
                    _pendingBorderedVCount,
                    _pendingResolution
                );
                s_MarkerDecorator.End();
            }
        }

        if (generateCollider && _terrainCollider != null && _pendingLodIndex == 0)
        {
            s_MarkerCollider.Begin();
#if TERRAIN_PERF_DEBUG
            s_ColliderUpdatesThisFrame++;
#endif
            UpdateTerrainCollider();
            _terrainCollider.enabled = true;
            s_MarkerCollider.End();
        }
        else if (_terrainCollider != null)
        {
            _terrainCollider.enabled = false;
        }




        NativeArrayPool.Return(ref _currentChunkData);
        if (_buffersAllocated)
        {
            NativeArrayPool.Return(ref _vertexBuffer);
            if (_isFormat16) NativeArrayPool.Return(ref _indices16);
            else NativeArrayPool.Return(ref _indices32);
            _buffersAllocated = false;
        }
        _state = GenState.Idle;
    }

    void UpdateTerrainCollider()
    {
        int verts = _pendingVerticesPerLine;
        int bordered = _pendingBorderedVCount;
        
        var np = NoiseProvider.Instance;
        float maxHeight = np != null ? np.GetAbsoluteMaxHeight() : 200f;
        
        if (_terrainData == null)
        {
            _terrainData = new TerrainData();
            _terrainData.heightmapResolution = verts;
            _terrainData.size = new Vector3(_pendingSize, maxHeight, _pendingSize);
            _terrainCollider.terrainData = _terrainData;
        }
        else
        {
            if (_terrainData.heightmapResolution != verts)
            {
                _terrainData.heightmapResolution = verts;
            }
            if (_terrainData.size.x != _pendingSize || _terrainData.size.y != maxHeight)
            {
                _terrainData.size = new Vector3(_pendingSize, maxHeight, _pendingSize);
            }
        }
        
        if (_cachedHeightsBuffer == null || _cachedHeightsBuffer.GetLength(0) != verts)
        {
            _cachedHeightsBuffer = new float[verts, verts];
        }
        float invMax = 1f / maxHeight;
        
        for (int z = 0; z < verts; z++)
        {
            for (int x = 0; x < verts; x++)
            {
                int srcIdx = (z + 1) * bordered + (x + 1); 
                _cachedHeightsBuffer[z, x] = math.saturate(_currentChunkData[srcIdx].x * invMax);
            }
        }
        
        _terrainData.SetHeightsDelayLOD(0, 0, _cachedHeightsBuffer);
        _terrainData.SyncHeightmap();
    }


    public void SetPhysicsEnabled(bool value) 
    {
        generateCollider = value;
        if (_terrainCollider != null) 
        {
            _terrainCollider.enabled = value;
        }
    }

    public bool HasColliderActive() 
    {
        return _terrainCollider != null && _terrainCollider.enabled && _terrainData != null;
    }

    void OnDisable() => Cleanup();

    void OnDestroy()
    {
        Cleanup();
        if (_mesh != null)
        {
            DestroyImmediate(_mesh);
            _mesh = null;
        }
        
        if (_terrainData != null)
        {
            DestroyImmediate(_terrainData);
            _terrainData = null;
        }        
    } 

    private void Cleanup()
    {



        if (_state == GenState.Meshing) _meshHandle.Complete();

        NativeArrayPool.Return(ref _currentChunkData);
        if (_buffersAllocated)
        {
            NativeArrayPool.Return(ref _vertexBuffer);
            if (_isFormat16) NativeArrayPool.Return(ref _indices16);
            else NativeArrayPool.Return(ref _indices32);
            _buffersAllocated = false;
        }
    }



    public void CompleteMeshJobIfRunning()
    {
        if (_state == GenState.Meshing) _meshHandle.Complete();
    }








    public bool TryDiscardPending()
    {
        if (_state == GenState.Idle) return true;
        if (_state == GenState.Meshing) return false;



        Cleanup();
        if (_decorator != null) _decorator.ReleaseSlot();
        _state = GenState.Idle;
        currentLodIndex = -1;
        _hasShownMesh = false;
        _meshRenderer.enabled = false;
        return true;
    }

    public void ResetForReuse()
    {
        _hasShownMesh = false;
        _state = GenState.Idle; 
        currentLodIndex = -1;
        if (_meshRenderer != null) _meshRenderer.enabled = false;
        if (_terrainCollider != null) _terrainCollider.enabled = false;
    }

    

    public struct BiomeHeightParams
    {
        public float seaLevel;
        public float beachTop;
        public float biomeTop;
        public float rockStart;
        public float rockEnd;
        public float snowLineCold;
        public float snowLineHot;
        public float snowBandWidth;
        public float valleyTop;
        public float peakHeight;
        public float temperatureAltitudeCooling;
        public float valleyMoistureBoost;
        public float peakMoistureDryness;
        public int temperatureEnabled;
        public int moistureEnabled;
    }

    public struct ClimateSample
    {
        public float rawTemperature;
        public float rawMoisture;
        public float temperature;
        public float moisture;
    }

    public const float ClimateDisabledValue = -1f;

    public static ClimateSample SampleClimate(
        RewriteFastNoiseLite temperatureNoise,
        RewriteFastNoiseLite moistureNoise,
        float worldX,
        float worldZ,
        float terrainHeight,
        BiomeHeightParams biomeParams,
        bool applyTerrainEffects)
    {
        return SampleClimate(
            temperatureNoise,
            moistureNoise,
            default,
            worldX,
            worldZ,
            terrainHeight,
            biomeParams,
            applyTerrainEffects);
    }

    public static ClimateSample SampleClimate(
        RewriteFastNoiseLite temperatureNoise,
        RewriteFastNoiseLite moistureNoise,
        NativeArray<int> permutation,
        float worldX,
        float worldZ,
        float terrainHeight,
        BiomeHeightParams biomeParams,
        bool applyTerrainEffects)
    {
        float rawTemperature = biomeParams.temperatureEnabled != 0
            ? NormalizeTemperatureNoise(temperatureNoise.GetNoise2D(worldX, worldZ, permutation))
            : ClimateDisabledValue;
        float rawMoisture = biomeParams.moistureEnabled != 0
            ? NormalizeMoistureNoise(moistureNoise.GetNoise2D(worldX, worldZ, permutation))
            : ClimateDisabledValue;
        float temperature = rawTemperature;
        float moisture = rawMoisture;

        if (applyTerrainEffects)
        {
            ApplyTerrainClimateEffects(ref temperature, ref moisture, terrainHeight, biomeParams);
        }

        return new ClimateSample
        {
            rawTemperature = rawTemperature,
            rawMoisture = rawMoisture,
            temperature = temperature,
            moisture = moisture
        };
    }

    public static float NormalizeTemperatureNoise(float rawNoise)
    {
        rawNoise = math.sign(rawNoise) * math.sqrt(math.abs(rawNoise));
        return math.saturate((rawNoise + 1f) * 0.5f);
    }

    public static float NormalizeMoistureNoise(float rawNoise)
    {
        return math.saturate((rawNoise + 1f) * 0.5f);
    }

    public static void ApplyTerrainClimateEffects(
        ref float temperature,
        ref float moisture,
        float terrainHeight,
        BiomeHeightParams biomeParams)
    {
        float peakHeight = math.max(0.001f, biomeParams.peakHeight);
        float height01 = math.saturate(terrainHeight / peakHeight);

        if (biomeParams.temperatureEnabled != 0)
        {
            temperature = math.saturate(temperature - height01 * biomeParams.temperatureAltitudeCooling);
        }

        if (biomeParams.moistureEnabled != 0)
        {
            float valleyTop = math.max(0.001f, biomeParams.valleyTop);
            float valleyWetness = (1f - math.saturate(terrainHeight / valleyTop)) * biomeParams.valleyMoistureBoost;
            float peakDryness = height01 * biomeParams.peakMoistureDryness;
            moisture = math.saturate(moisture + valleyWetness);
            moisture *= math.saturate(1f - peakDryness);
        }
    }

    [BurstCompile]
    public struct GenerateTerrainJobBurst : IJobParallelFor
    {
        public float startX, startZ, resolution;
        public int borderedVCount, vCount;
        [ReadOnly] [NoAlias] public NativeArray<RewriteFastNoiseLite> baseNoises;
        [ReadOnly] [NoAlias] public NativeArray<NoiseProvider.BaseNoiseLayerRuntime> baseLayerSettings;
        [ReadOnly] [NoAlias] public NativeArray<int> permutation;
        public RewriteFastNoiseLite temperatureNoise;
        public RewriteFastNoiseLite moistureNoise;
        public float fAmp;
        public BiomeHeightParams biomeParams;
        [ReadOnly] [NoAlias] public NativeArray<float> heightCurveLUT;
        [NativeDisableParallelForRestriction] [WriteOnly] [NoAlias] public NativeArray<VertexData> vertexData;
        [WriteOnly] [NoAlias] public NativeArray<float4> chunkData;





        public Unity.Collections.FixedList512Bytes<float3> corridorSamples;
        public float clearRadius;
        public bool hasSpline;

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
                float idx = t * (heightCurveLUT.Length - 1);
                int i0 = (int)idx;
                int i1 = math.min(i0 + 1, heightCurveLUT.Length - 1);
                float fract = idx - i0;
                shaped = math.lerp(heightCurveLUT[i0], heightCurveLUT[i1], fract);
            }

            float baseHeightShaped = shaped * fAmp;

            float terrainHeight = baseHeightShaped;

            const float seaLevel = 0f;
            if (terrainHeight <= seaLevel) {
                terrainHeight = seaLevel;
            }

            float finalHeight = terrainHeight;

            float temperature = ClimateDisabledValue;
            float moisture = ClimateDisabledValue;
            if (biomeParams.temperatureEnabled != 0 || biomeParams.moistureEnabled != 0)
            {
                ClimateSample climate = SampleClimate(
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

            

            float decoMask = 0f;

            if (hasSpline && corridorSamples.Length > 0)
            {


                float minDistSq = float.MaxValue;
                float nearestY = 0f;
                int sampleCount = corridorSamples.Length;
                for (int s = 0; s < sampleCount; s++)
                {
                    float3 sample = corridorSamples[s];
                    float dxs = sample.x - worldX;
                    float dzs = sample.z - worldZ;
                    float sqr = (dxs * dxs) + (dzs * dzs);
                    if (sqr < minDistSq)
                    {
                        minDistSq = sqr;
                        nearestY = sample.y;
                    }
                }

                float dist2D = math.sqrt(minDistSq);
                float maxTreeHeight = 25f;
                bool heightConflict = (nearestY - finalHeight) < (clearRadius + maxTreeHeight);

                if (dist2D < (clearRadius + 6f) && heightConflict)
                {
                    decoMask = 1f;
                }
            }

            chunkData[index] = new float4(finalHeight, temperature, moisture, decoMask);

            if (x >= 1 && x <= vCount && y >= 1 && y <= vCount)
            {
                int meshIdx = (y - 1) * vCount + (x - 1);
                vertexData[meshIdx] = new VertexData
                {
                    position = new float3((x - 1) * resolution, finalHeight, (y - 1) * resolution),
                    normal = default,  
                    color = BiomeShading.BiomeColor(finalHeight, moisture, temperature, biomeParams)
                };
            }
        }
    }

    public struct GenerateTerrainJobMono : IJobParallelFor
    {
        public float startX, startZ, resolution;
        public int borderedVCount, vCount;
        [ReadOnly] [NoAlias] public NativeArray<RewriteFastNoiseLite> baseNoises;
        [ReadOnly] [NoAlias] public NativeArray<NoiseProvider.BaseNoiseLayerRuntime> baseLayerSettings;
        [ReadOnly] [NoAlias] public NativeArray<int> permutation;
        public RewriteFastNoiseLite temperatureNoise;
        public RewriteFastNoiseLite moistureNoise;
        public float fAmp;
        public BiomeHeightParams biomeParams;

        [ReadOnly] [NoAlias] 
        public NativeArray<float> heightCurveLUT;
        [WriteOnly] [NoAlias] 
        public NativeArray<float4> chunkData;
        [NativeDisableParallelForRestriction] [WriteOnly] [NoAlias] 
        public NativeArray<VertexData> vertexData;

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
                float idx = t * (heightCurveLUT.Length - 1);
                int i0 = (int)idx;
                int i1 = math.min(i0 + 1, heightCurveLUT.Length - 1);
                float fract = idx - i0;
                shaped = math.lerp(heightCurveLUT[i0], heightCurveLUT[i1], fract);
            }

            float baseHeightShaped = shaped * fAmp;

            float terrainHeight = baseHeightShaped;

            const float seaLevel = 0f;
            if (terrainHeight <= seaLevel) {
                terrainHeight = seaLevel;
            }

            float finalHeight = terrainHeight;

            float temperature = ClimateDisabledValue;
            float moisture = ClimateDisabledValue;
            if (biomeParams.temperatureEnabled != 0 || biomeParams.moistureEnabled != 0)
            {
                ClimateSample climate = SampleClimate(
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

            chunkData[index] = new float4(finalHeight, temperature, moisture, 0f);

            if (x >= 1 && x <= vCount && y >= 1 && y <= vCount)
            {
                int meshIdx = (y - 1) * vCount + (x - 1);
                vertexData[meshIdx] = new VertexData
                {
                    position = new float3((x - 1) * resolution, finalHeight, (y - 1) * resolution),
                    normal = default,
                    color = BiomeShading.BiomeColor(finalHeight, moisture, temperature, biomeParams)
                };
            }
        }
    }

    [BurstCompile]
    public struct CalculateNormalsJob : IJobParallelFor
    {
        [ReadOnly] [NoAlias]
        public NativeArray<float4> chunkData;

        [NoAlias]
        public NativeArray<VertexData> vertexData;
        public int borderedVCount, vCount;
        public float resolution;

        public void Execute(int index)
        {
            int mx = index % vCount;
            int my = index / vCount;

            int hx = mx + 1;
            int hy = my + 1;

            float hL = chunkData[hy * borderedVCount + (hx - 1)].x;
            float hR = chunkData[hy * borderedVCount + (hx + 1)].x;
            float hD = chunkData[(hy - 1) * borderedVCount + hx].x;
            float hU = chunkData[(hy + 1) * borderedVCount + hx].x;

            var v = vertexData[index];
            v.normal = math.normalize(new float3(hL - hR, 2f * resolution, hD - hU));
            vertexData[index] = v;
        }
    }

    [BurstCompile]
    public struct BuildTrianglesJob16: IJobParallelFor
    {
        [WriteOnly] [NativeDisableParallelForRestriction] [NoAlias]
        public NativeArray<ushort> triangles;
        public int segments, vCount;

        public void Execute(int index)
        {
            int x = index % segments;
            int z = index / segments;

            int i = z * vCount + x;
            int trisIdx = index * 6;

            triangles[trisIdx + 0] = (ushort)i;
            triangles[trisIdx + 1] = (ushort)(i + vCount);
            triangles[trisIdx + 2] = (ushort)(i + 1);
            triangles[trisIdx + 3] = (ushort)(i + 1);
            triangles[trisIdx + 4] = (ushort)(i + vCount);
            triangles[trisIdx + 5] = (ushort)(i + vCount + 1);
        }
    }

    [BurstCompile]
    public struct BuildTrianglesJob32: IJobParallelFor
    {
        [WriteOnly] [NativeDisableParallelForRestriction] [NoAlias]
        public NativeArray<int> triangles;
        public int segments, vCount;

        public void Execute(int index)
        {
            int x = index % segments;
            int z = index / segments;

            int i = z * vCount + x;
            int trisIdx = index * 6;

            triangles[trisIdx + 0] = (int)i;
            triangles[trisIdx + 1] = (int)(i + vCount);
            triangles[trisIdx + 2] = (int)(i + 1);
            triangles[trisIdx + 3] = (int)(i + 1);
            triangles[trisIdx + 4] = (int)(i + vCount);
            triangles[trisIdx + 5] = (int)(i + vCount + 1);
        }
    }

    public static class BiomeShading
    {




        private static readonly Color32 Sea     = new Color32( 60, 165, 220, 255);
        private static readonly Color32 Beach   = new Color32(245, 225, 165, 255);
        private static readonly Color32 Rock    = new Color32(165, 150, 130, 255);
        private static readonly Color32 Snow    = new Color32(248, 252, 255, 255);
        private static readonly Color32 ColdDry = new Color32(215, 225, 240, 255);
        private static readonly Color32 ColdMid = new Color32(165, 215, 175, 255);
        private static readonly Color32 ColdWet = new Color32( 45, 135,  80, 255);
        private static readonly Color32 NormDry = new Color32(245, 215, 110, 255);
        private static readonly Color32 NormMid = new Color32(170, 230,  90, 255);
        private static readonly Color32 NormWet = new Color32( 55, 165,  80, 255);
        private static readonly Color32 HotDry  = new Color32(255, 175,  90, 255);
        private static readonly Color32 HotMid  = new Color32(220, 200,  70, 255);
        private static readonly Color32 HotWet  = new Color32( 35, 150,  50, 255);

        public static Color32 BiomeColor(float height, float moisture, float temperature, BiomeHeightParams p)
        {
            if (height < p.seaLevel) return Sea;

            if (height < p.beachTop)
                return Color32Lerp(Sea, Beach, math.smoothstep(p.seaLevel, p.beachTop, height));

            float colorTemperature = p.temperatureEnabled != 0 && temperature >= 0f ? temperature : 0.5f;
            float colorMoisture = p.moistureEnabled != 0 && moisture >= 0f ? moisture : 0.5f;

            Color32 biome = GetZoneColor(colorTemperature, colorMoisture,
                        ColdDry, ColdMid, ColdWet,
                        NormDry, NormMid, NormWet,
                        HotDry, HotMid, HotWet);

            if (height < p.biomeTop)
                return Color32Lerp(Beach, biome, math.smoothstep(p.beachTop, p.biomeTop, height));

            float rockBlend = math.smoothstep(p.rockStart, p.rockEnd, height) * 0.5f;
            biome = Color32Lerp(biome, Rock, rockBlend);

            float snowLine = math.lerp(p.snowLineCold, p.snowLineHot, math.saturate(colorTemperature));
            float snowBlend = math.smoothstep(snowLine, snowLine + p.snowBandWidth, height);
            
            return Color32Lerp(biome, Snow, snowBlend);
        }

        public static Color32 Color32Lerp(Color32 a, Color32 b, float t)
        {
            t = math.saturate(t);
            return new Color32(
                (byte)math.lerp(a.r, b.r, t),
                (byte)math.lerp(a.g, b.g, t),
                (byte)math.lerp(a.b, b.b, t),
                255
            );
        }

        public static float GetBiomeMultiplier(float temp, float moisture)
        {
            float coldDry = 0.7f;
            float coldMid = 1.0f;
            float coldWet = 1.5f;

            float normDry = 0.8f;
            float normMid = 1.0f;
            float normWet = 1.3f;

            float hotDry = 0.6f;
            float hotMid = 0.9f;
            float hotWet = 1.4f;

            float dryMultiplier = FloatBand(temp, coldDry, normDry, hotDry);
            float midMultiplier = FloatBand(temp, coldMid, normMid, hotMid);
            float wetMultiplier = FloatBand(temp, coldWet, normWet, hotWet);

            return FloatBand(moisture, dryMultiplier, midMultiplier, wetMultiplier);
        }

        public static float FloatBand(float value, float cold, float norm, float hot)
        {
            if (value < 0.5f)
                return math.lerp(cold, norm, value * 2f);
            return math.lerp(norm, hot, (value - 0.5f) * 2f);
        }

        public static Color32 GetZoneColor(float temperature, float moisture,
                    Color32 cD, Color32 cM, Color32 cW,
                    Color32 nD, Color32 nM, Color32 nW,
                    Color32 hD, Color32 hM, Color32 hW)
        {
            Color32 cold = MoistureBand(moisture, cD, cM, cW);
            Color32 norm = MoistureBand(moisture, nD, nM, nW);
            Color32 hot  = MoistureBand(moisture, hD, hM, hW);

            return TempBand(temperature, cold, norm, hot);
        }

        public static Color32 MoistureBand(float moisture, Color32 dry, Color32 mid, Color32 wet)
        {
            if (moisture < 0.5f)
                return Color32Lerp(dry, mid, math.smoothstep(0f, 1f, moisture * 2f));
            return Color32Lerp(mid, wet, math.smoothstep(0f, 1f, (moisture - 0.5f) * 2f));
        }

        public static Color32 TempBand(float temp, Color32 cold, Color32 norm, Color32 hot)
        {
            if (temp < 0.5f)
                return Color32Lerp(cold, norm, math.smoothstep(0f, 1f, temp * 2f));
            return Color32Lerp(norm, hot, math.smoothstep(0f, 1f, (temp - 0.5f) * 2f));
        }
    }

    public static BiomeHeightParams BuildBiomeParams(NoiseProvider np)
    {
        return new BiomeHeightParams
        {
            seaLevel       = 0f,
            beachTop       = np.baseAmplitude * np.beachTopFraction,
            biomeTop       = np.baseAmplitude * np.biomeTopFraction,
            rockStart      = np.baseAmplitude * np.rockStartFraction,
            rockEnd        = np.baseAmplitude * np.rockEndFraction,
            snowLineCold   = np.baseAmplitude * np.snowLineColdFraction,
            snowLineHot    = np.baseAmplitude * np.snowLineHotFraction,
            snowBandWidth  = np.baseAmplitude * np.snowBandWidthFraction,
            valleyTop      = np.baseAmplitude * np.valleyTopFraction,
            peakHeight     = np.baseAmplitude * np.peakHeightFraction,
            temperatureAltitudeCooling = math.saturate(np.altitudeTemperatureCooling),
            valleyMoistureBoost        = math.saturate(np.valleyMoistureBoost),
            peakMoistureDryness        = math.saturate(np.peakMoistureDryness),
            temperatureEnabled          = np.temperatureEnabled ? 1 : 0,
            moistureEnabled             = np.moistureEnabled ? 1 : 0,
        };
    }
}

public static class NativeArrayPool
{
    private static readonly List<System.Action> _cleanupActions = new List<System.Action>();
    private static readonly object _globalLock = new object();

    private static class TypedPool<T> where T : struct
    {
        public static readonly Dictionary<int, Stack<NativeArray<T>>> Pool = new Dictionary<int, Stack<NativeArray<T>>>();
        public static readonly object LockObj = new object();
        public const int MAX_POOL_SIZE_PER_KEY = 8;

        static TypedPool()
        {
            lock (_globalLock)
            {
                _cleanupActions.Add(DisposeTypePool);
            }
        }

        public static void DisposeTypePool()
        {
            lock (LockObj)
            {
                foreach (var stack in Pool.Values)
                {
                    while (stack.Count > 0)
                    {
                        var arr = stack.Pop();
                        if (arr.IsCreated) arr.Dispose();
                    }
                }
                Pool.Clear();
            }
        }
    }

    public static NativeArray<T> Get<T>(int size) where T : struct
    {
        lock (TypedPool<T>.LockObj)
        {
            var pool = TypedPool<T>.Pool;
            if (pool.TryGetValue(size, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }
        }
        return new NativeArray<T>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
    }

    public static void Return<T>(ref NativeArray<T> array) where T : struct
    {
        if (!array.IsCreated) return;
        int size = array.Length;
        
        lock (TypedPool<T>.LockObj)
        {
            var pool = TypedPool<T>.Pool;
            if (!pool.TryGetValue(size, out var stack))
            {
                stack = new Stack<NativeArray<T>>();
                pool[size] = stack;
            }
            
            if (stack.Count < TypedPool<T>.MAX_POOL_SIZE_PER_KEY && !stack.Contains(array))
            {
                stack.Push(array);
                array = default;
            }
            else
            {
                array.Dispose();
                array = default;
            }
        }
    }

    public static void DisposeAll()
    {
        lock (_globalLock)
        {
            foreach (var cleanup in _cleanupActions)
            {
                cleanup?.Invoke();
            }
        }
    }
}
