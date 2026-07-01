using UnityEngine;
using System.Collections.Generic;
using Unity.Jobs;

public class TerrainManager : MonoBehaviour
{
    public static TerrainManager Instance { get; private set; }
    public TerrainSettings settings;

    [Header("Grid Definition")]
    public ChunkGenerator chunkPrefab;
    public float chunkSize = 16f;      
    public float resolution = 2f;     

    [Header("References")]
    public Transform playerTransform;

    [Header("Pooling")]
    [Tooltip("Ile chunków maksymalnie zaaplikować w normalnej klatce rozgrywki.")]
    [SerializeField] private int maxAppliesPerFrame = 2;
    [Tooltip("Ile chunków maksymalnie zaaplikować na klatkę podczas pierwszych sekund ładowania świata.")]
    [SerializeField] private int maxInitialAppliesPerFrame = 8;
    [Tooltip("Miękki budżet czasu na upload mesh/dekoracji w normalnej klatce.")]
    [SerializeField] private float maxApplyMillisecondsPerFrame = 3.5f;
    [Tooltip("Miękki budżet czasu na upload mesh/dekoracji podczas pierwszych sekund świata.")]
    [SerializeField] private float maxInitialApplyMillisecondsPerFrame = 6f;
    [SerializeField] private int initialPoolSize = 64;
    [SerializeField] private bool allowPoolGrowth = true;

    [Header("Visibility")]
    [SerializeField] private float forwardLookahead = 40f;
    [Tooltip("Sekundy ruchu w przód doliczane do lookahead na podstawie prędkości gracza. 2-3s wystarcza dla 100 m/s.")]
    [SerializeField] private float velocityLookaheadSeconds = 2.5f;
    [SerializeField] private float maxVelocityLookahead = 300f;
    private Vector3 _lastPlayerPos;
    private float _smoothedSpeed;
    [Tooltip("Ile chunków maksymalnie schedule'ować jobów w jednej klatce. Same joby lecą na workerach, więc można więcej niż 1.")]
    [SerializeField] private int maxSpawnsPerFrame = 4;
    [SerializeField] private int maxLODUpdatesPerFrame = 2;
    [SerializeField] private float collisionRadius = 32f;
    [Tooltip("Deadband na granicy LOD jako frakcja threshold'u. 0.10 = 10% sticky band: " +
             "chunk LOD0 zostanie na 0 dopóki nie odsunie się o 10% poza próg, chunk LOD1 wróci do 0 dopiero " +
             "po wejściu o 10% pod próg. Zapobiega regeneracji bez końca przy ruchu wzdłuż granicy. " +
             "Bez tego widać 'pulsujące' coordy w logach (warning z ChunkGen po 5 regenach tego samego coorda).")]
    [Range(0f, 0.3f)]
    [SerializeField] private float lodHysteresisFactor = 0.1f;
    [Tooltip("Minimalny czas między pełnymi UpdateVisibleChunks (skan siatki ~37×37 chunków + frustum test + dist test). " +
             "Bez tego przy szybkim ruchu wołamy go co klatkę. Ruch i tak jest discrete kratami chunkSize/2, " +
             "więc 0.1s = 10 update'ów/s zostawia sporo zapasu, ale obcina narzut o ~80%.")]
    [SerializeField] private float minUpdateVisibleIntervalSeconds = 0.1f;
    [Tooltip("Jak długo chunk może wypaść z frustum/safe-zone zanim trafi do poola. Tłumi mruganie na granicy widoczności.")]
    [SerializeField] private float recycleGraceSeconds = 0.45f;
    [Tooltip("Dodatkowy bufor dystansu przed recyklingiem. Chunki w tym promieniu zostają aktywne nawet jeśli chwilowo wypadną z frustum.")]
    [SerializeField] private float recycleDistanceBufferChunks = 2f;
    private float _lastUpdateVisibleTime = -1f;

    private Dictionary<Vector2Int, ChunkGenerator> _activeGrid = new Dictionary<Vector2Int, ChunkGenerator>();
    public IReadOnlyDictionary<Vector2Int, ChunkGenerator> ActiveGrid => _activeGrid;
    private Stack<ChunkGenerator> _pool = new Stack<ChunkGenerator>();
    private HashSet<Vector2Int> _currentIterationCoords = new HashSet<Vector2Int>();
    private List<Vector2Int> _toRemove = new List<Vector2Int>();
    private Dictionary<Vector2Int, float> _missingSince = new Dictionary<Vector2Int, float>();
    private List<ChunkSortData> _sortDataList = new List<ChunkSortData>();
    private Vector3 _lastUpdatePos;
    private Camera _camera;

    private HashSet<Vector2Int> _pendingSpawnSet = new HashSet<Vector2Int>();
    private Queue<Vector2Int> _pendingSpawnQueue = new Queue<Vector2Int>();
    private HashSet<ChunkGenerator> _applySet = new HashSet<ChunkGenerator>();
    private List<ChunkGenerator> _applyQueue = new List<ChunkGenerator>();
    private bool _applyQueueDirty = false;

    private List<ChunkGenerator> _meshingChunks = new List<ChunkGenerator>(128);
    [SerializeField] public int maxPoolSize = 128;
    internal Stack<GraphicsBuffer[]> SharedBufferPool { get; private set; } = new Stack<GraphicsBuffer[]>();
    private Plane[] _frustumPlanes = new Plane[6];
    private Vector3 _currentSortPos;
    private Vector3 _lastCamPos;
    private Quaternion _lastCamRot;
    bool _initialGenerationDone = false;
    private const int kMaxSafeInitialAppliesPerFrame = 16;
    
    private void Awake()
    {
        ClampRuntimeSettings();

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnValidate()
    {
        ClampRuntimeSettings();
    }

    private void ClampRuntimeSettings()
    {
        maxAppliesPerFrame = Mathf.Max(1, maxAppliesPerFrame);
        maxInitialAppliesPerFrame = Mathf.Clamp(maxInitialAppliesPerFrame, 1, kMaxSafeInitialAppliesPerFrame);
        maxApplyMillisecondsPerFrame = Mathf.Max(0.25f, maxApplyMillisecondsPerFrame);
        maxInitialApplyMillisecondsPerFrame = Mathf.Max(maxApplyMillisecondsPerFrame, maxInitialApplyMillisecondsPerFrame);
        initialPoolSize = Mathf.Max(0, initialPoolSize);
        maxSpawnsPerFrame = Mathf.Max(1, maxSpawnsPerFrame);
        maxLODUpdatesPerFrame = Mathf.Max(0, maxLODUpdatesPerFrame);
        collisionRadius = Mathf.Max(0f, collisionRadius);
        recycleGraceSeconds = Mathf.Max(0f, recycleGraceSeconds);
        recycleDistanceBufferChunks = Mathf.Max(0f, recycleDistanceBufferChunks);
    }

    void Start()
    {
        _camera = Camera.main;
        for (int i = 0; i < initialPoolSize; i++)
        {
            var chunk = Instantiate(chunkPrefab, transform);
            chunk.gameObject.SetActive(false);
            _pool.Push(chunk);
        }

        if (playerTransform != null)
        {
            _lastUpdatePos = playerTransform.position;
        }
    }

    void Update()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null)
        {
            Shader.SetGlobalVector("_GlobalCameraPosition", _camera.transform.position);
        }

        if (playerTransform == null || settings.detailLevels == null || settings.detailLevels.Length == 0) return;

        if (!_initialGenerationDone)
        {
            UpdateVisibleChunks();
            _lastUpdateVisibleTime = Time.unscaledTime;
            _initialGenerationDone = true;
        }





        float speedFactor = Mathf.Clamp01(_smoothedSpeed / 50f);
        float distThreshold = chunkSize * Mathf.Lerp(0.5f, 0.1f, speedFactor);





        float dtSinceLastUpdate = Time.unscaledTime - _lastUpdateVisibleTime;
        bool movedEnough = (playerTransform.position - _lastUpdatePos).sqrMagnitude > distThreshold * distThreshold;
        bool throttleElapsed = dtSinceLastUpdate >= minUpdateVisibleIntervalSeconds;
        if (movedEnough && throttleElapsed)
        {
            UpdateVisibleChunks();
            _lastUpdatePos = playerTransform.position;
            _lastUpdateVisibleTime = Time.unscaledTime;
        }

        int spawnsThisFrame = 0;
        float halfChunk = chunkSize * 0.5f;
        float maxViewDstSqStale = settings.MaxViewDst * settings.MaxViewDst;
        while (_pendingSpawnQueue.Count > 0 && spawnsThisFrame < maxSpawnsPerFrame)
        {
            Vector2Int coord = _pendingSpawnQueue.Dequeue();
            _pendingSpawnSet.Remove(coord);

            float dx = (coord.x * chunkSize + halfChunk) - playerTransform.position.x;
            float dz = (coord.y * chunkSize + halfChunk) - playerTransform.position.z;
            float sqrDist = (dx * dx) + (dz * dz);





            if (sqrDist > maxViewDstSqStale) continue;

            bool needsPhysics = sqrDist <= (collisionRadius * collisionRadius);

            SpawnChunk(coord, CalculateLODFromSqrDistance(sqrDist), needsPhysics);
            spawnsThisFrame++;
        }

        PollMeshingChunks();

        if (_applyQueueDirty)
        {
            SortApplyQueue();
            _applyQueueDirty = false;
        }

        int appliesThisFrame = 0;

        int currentApplyLimit = (Time.timeSinceLevelLoad < 3f) ? maxInitialAppliesPerFrame : maxAppliesPerFrame;
        float currentApplyBudgetMs = (Time.timeSinceLevelLoad < 3f)
            ? maxInitialApplyMillisecondsPerFrame
            : maxApplyMillisecondsPerFrame;
        double applyDeadline = Time.realtimeSinceStartupAsDouble + currentApplyBudgetMs * 0.001;

        while (_applyQueue.Count > 0 && appliesThisFrame < currentApplyLimit)
        {
            int lastIndex = _applyQueue.Count - 1;
            var chunk = _applyQueue[lastIndex];

            _applyQueue.RemoveAt(lastIndex);
            _applySet.Remove(chunk);

            if (chunk != null)
            {
                chunk.ApplyMesh();
                appliesThisFrame++;

                if (appliesThisFrame >= 1 && Time.realtimeSinceStartupAsDouble >= applyDeadline)
                {
                    break;
                }
            }
        }

#if TERRAIN_PERF_DEBUG
        ChunkGenerator.ResetPerfDebugCounters();
#endif
    }

    private int CalculateLODFromSqrDistance(float sqrDistance)
    {

        return CalculateLODFromSqrDistanceWithHysteresis(sqrDistance, -1);
    }




    private int CalculateLODFromSqrDistanceWithHysteresis(float sqrDistance, int currentLod)
    {
        int lodIndex = 0;
        for (int i = 0; i < settings.detailLevels.Length - 1; i++)
        {
            float threshold = settings.detailLevels[i].visibleDstThreshold;






            float effectiveThreshold = threshold;
            if (currentLod >= 0 && lodHysteresisFactor > 0f)
            {
                if (currentLod > i)
                {

                    effectiveThreshold = threshold * (1f - lodHysteresisFactor);
                }
                else
                {

                    effectiveThreshold = threshold * (1f + lodHysteresisFactor);
                }
            }

            if (sqrDistance > effectiveThreshold * effectiveThreshold)
                lodIndex = i + 1;
            else
                break;
        }
        return lodIndex;
    }

    public void RegisterReadyChunk(ChunkGenerator chunk)
    {
        if (chunk == null) return;

        if (_applySet.Add(chunk))
        {
            _applyQueue.Add(chunk);
            _applyQueueDirty = true;
        }
    }

    internal void RegisterMeshingChunk(ChunkGenerator chunk)
    {
        if (chunk != null) _meshingChunks.Add(chunk);
    }


    private void PollMeshingChunks()
    {
        int i = 0;
        while (i < _meshingChunks.Count)
        {
            var chunk = _meshingChunks[i];
            if (chunk == null || chunk.PollMeshingCompletion())
            {
                int last = _meshingChunks.Count - 1;
                _meshingChunks[i] = _meshingChunks[last];
                _meshingChunks.RemoveAt(last);
            }
            else
            {
                i++;
            }
        }
    }







    public void CompleteAllChunkJobs()
    {
        foreach (var chunk in _activeGrid.Values)
        {
            if (chunk != null) chunk.CompleteMeshJobIfRunning();
        }

        foreach (var chunk in _pool)
        {
            if (chunk != null) chunk.CompleteMeshJobIfRunning();
        }

        for (int i = 0; i < _applyQueue.Count; i++)
        {
            if (_applyQueue[i] != null) _applyQueue[i].CompleteMeshJobIfRunning();
        }
    }

    private void SortApplyQueue()
    {
        if (_applyQueue.Count <= 1) return;
        _currentSortPos = playerTransform.position;
        _applyQueue.Sort(CompareChunksByDistance);
    }

    private int CompareChunksByDistance(ChunkGenerator a, ChunkGenerator b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        float dA = (a.transform.position - _currentSortPos).sqrMagnitude;
        float dB = (b.transform.position - _currentSortPos).sqrMagnitude;
        return dB.CompareTo(dA);
    }
    void UpdateVisibleChunks()
    {
        _currentIterationCoords.Clear();
        _toRemove.Clear();

        float maxViewDst = settings.MaxViewDst;
        Vector3 playerPos = playerTransform.position;

        Vector3 fwdXZ = playerTransform.forward;
        fwdXZ.y = 0f;
        if (fwdXZ.sqrMagnitude > 0.0001f) fwdXZ.Normalize();
        else fwdXZ = Vector3.forward;





        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 frameDelta = playerPos - _lastPlayerPos;
        frameDelta.y = 0f;
        float instantSpeed = frameDelta.magnitude / dt;
        _lastPlayerPos = playerPos;
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instantSpeed, 0.15f);
        float dynamicLookahead = forwardLookahead + Mathf.Min(_smoothedSpeed * velocityLookaheadSeconds, maxVelocityLookahead);

        Vector3 lookaheadPos = playerPos + fwdXZ * dynamicLookahead;

        Transform camT = _camera.transform;
        if (camT.position != _lastCamPos || camT.rotation != _lastCamRot)
        {
            Matrix4x4 viewMatrix = _camera.worldToCameraMatrix;
            float extraFOV = 25f;
            Matrix4x4 projectionMatrix = Matrix4x4.Perspective(
                _camera.fieldOfView + extraFOV,
                _camera.aspect,
                _camera.nearClipPlane,
                _camera.farClipPlane
            );
            Matrix4x4 worldToProjectionMatrix = projectionMatrix * viewMatrix;
            
            GeometryUtility.CalculateFrustumPlanes(worldToProjectionMatrix, _frustumPlanes);

            _lastCamPos = camT.position;
            _lastCamRot = camT.rotation;
        }

        int currentChunkCoordX = Mathf.RoundToInt(lookaheadPos.x / chunkSize);
        int currentChunkCoordZ = Mathf.RoundToInt(lookaheadPos.z / chunkSize);
        int chunksInView = Mathf.CeilToInt(maxViewDst / chunkSize);
        _sortDataList.Clear();

        Vector2 lookaheadXZ = new Vector2(lookaheadPos.x, lookaheadPos.z);
        for (int zOffset = -chunksInView; zOffset <= chunksInView; zOffset++)
        {
            for (int xOffset = -chunksInView; xOffset <= chunksInView; xOffset++)
            {
                Vector2Int coord = new Vector2Int(currentChunkCoordX + xOffset, currentChunkCoordZ + zOffset);

                float centerX = coord.x * chunkSize + chunkSize * 0.5f;
                float centerZ = coord.y * chunkSize + chunkSize * 0.5f;

                float dxL = centerX - lookaheadXZ.x;
                float dzL = centerZ - lookaheadXZ.y;
                float dxP = centerX - playerPos.x;
                float dzP = centerZ - playerPos.z;

                _sortDataList.Add(new ChunkSortData {
                    coord = coord,
                    sqrDistToLookahead = (dxL * dxL) + (dzL * dzL),
                    sqrDistToPlayer = (dxP * dxP) + (dzP * dzP)
                });
            }
        }

        _sortDataList.Sort();
    float collisionRadiusSq = collisionRadius * collisionRadius;
    float safeZoneRadiusSq = (chunkSize * 2.0f) * (chunkSize * 2.0f);
    float maxViewDstSq = maxViewDst * maxViewDst;
    float recycleDistance = maxViewDst + chunkSize * recycleDistanceBufferChunks;
    float recycleDistanceSq = recycleDistance * recycleDistance;

    int lodUpdatesThisFrame = 0;


    for (int i = 0; i < _sortDataList.Count; i++)
    {
        var data = _sortDataList[i];
        Vector2Int viewedChunkCoord = data.coord;
        float sqrDistFromPlayer = data.sqrDistToPlayer;

        if (sqrDistFromPlayer > maxViewDstSq) continue;

        float worldX = viewedChunkCoord.x * chunkSize;
        float worldZ = viewedChunkCoord.y * chunkSize;

        Vector3 center = new Vector3(worldX + chunkSize / 2, 0, worldZ + chunkSize / 2);
        Bounds chunkBounds = new Bounds(center, new Vector3(chunkSize, 500f, chunkSize));
        bool inFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, chunkBounds);
        bool inSafeZone = sqrDistFromPlayer <= safeZoneRadiusSq;

        if (inSafeZone || inFrustum)
        {

            _currentIterationCoords.Add(viewedChunkCoord);
            _missingSince.Remove(viewedChunkCoord);
            
            bool needsPhysics = sqrDistFromPlayer <= collisionRadiusSq;

            if (_activeGrid.TryGetValue(viewedChunkCoord, out ChunkGenerator chunk))
            {


                int lodIndex = CalculateLODFromSqrDistanceWithHysteresis(sqrDistFromPlayer, chunk.currentLodIndex);
                needsPhysics = needsPhysics && (lodIndex == 0);

                bool lodChanged = chunk.currentLodIndex != lodIndex;
                bool physicsNeededButMissing = needsPhysics && !chunk.HasColliderActive();

                if ((lodChanged || physicsNeededButMissing) && !chunk.IsBusy)
                {
                    if (lodUpdatesThisFrame < maxLODUpdatesPerFrame)
                    {
                        lodUpdatesThisFrame++;
                        chunk.SetPhysicsEnabled(needsPhysics);
                        float lodMultiplier = settings.detailLevels[lodIndex].lod;
                        if (lodMultiplier == 0) lodMultiplier = 1;

                        chunk.Generate(viewedChunkCoord, worldX, worldZ, chunkSize,
                                    resolution * lodMultiplier, settings, lodIndex);
                    }
                }
            }
            else
            {
                if (!_pendingSpawnSet.Contains(viewedChunkCoord))
                {
                    _pendingSpawnSet.Add(viewedChunkCoord);
                    _pendingSpawnQueue.Enqueue(viewedChunkCoord);
                }
            }
        }
    }



    foreach (var pair in _activeGrid)
    {
        if (!_currentIterationCoords.Contains(pair.Key))
        {
            float centerX = pair.Key.x * chunkSize + chunkSize * 0.5f;
            float centerZ = pair.Key.y * chunkSize + chunkSize * 0.5f;
            float dx = centerX - playerPos.x;
            float dz = centerZ - playerPos.z;

            if ((dx * dx) + (dz * dz) <= recycleDistanceSq)
            {
                _missingSince.Remove(pair.Key);
                continue;
            }

            if (!_missingSince.TryGetValue(pair.Key, out float missingSince))
            {
                _missingSince[pair.Key] = Time.unscaledTime;
                continue;
            }

            if (Time.unscaledTime - missingSince < recycleGraceSeconds)
            {
                continue;
            }

            _toRemove.Add(pair.Key);
        }
    }


    foreach (var coord in _toRemove)
    {
        var chunk = _activeGrid[coord];
        if (chunk == null)
        {
            _missingSince.Remove(coord);
            _activeGrid.Remove(coord);
            continue;
        }

        if (!chunk.CanBeRecycled)
        {




            if (!chunk.TryDiscardPending())
            {
                continue;
            }

            if (_applySet.Remove(chunk))
            {
                _applyQueue.Remove(chunk);
                _applyQueueDirty = true;
            }
        }

        chunk.gameObject.SetActive(false);
        _pool.Push(chunk);
        _missingSince.Remove(coord);
        _activeGrid.Remove(coord);
    }
    }

    void SpawnChunk(Vector2Int coord, int lodIndex, bool needsPhysics)
    {
        float worldX = coord.x * chunkSize;
        float worldZ = coord.y * chunkSize;

        float lodMultiplier = settings.detailLevels[lodIndex].lod;
        if (lodMultiplier == 0) lodMultiplier = 1;
        float currentResolution = resolution * lodMultiplier;

        ChunkGenerator chunk;
        if (_pool.Count > 0)
        {
            chunk = _pool.Pop();
            chunk.transform.position = new Vector3(worldX, 0, worldZ);
            chunk.ResetForReuse();
            chunk.gameObject.SetActive(true);
        }
        else if (allowPoolGrowth)
        {
            chunk = Instantiate(chunkPrefab, new Vector3(worldX, 0, worldZ), Quaternion.identity, transform);
        }
        else
        {
            return;
        }

        chunk.SetPhysicsEnabled(needsPhysics);
        chunk.Generate(coord, worldX, worldZ, chunkSize, currentResolution, settings, lodIndex);
        _missingSince.Remove(coord);
        _activeGrid.Add(coord, chunk);
    }

    private void OnDestroy()
    {
        if (SharedBufferPool == null) return; 
        
        while (SharedBufferPool.Count > 0)
        {
            var buffers = SharedBufferPool.Pop();
            for (int i = 0; i < 5; i++) buffers[i]?.Release();
        }
        TerrainBufferBridge.CleanUpBridge();
        NativeArrayPool.DisposeAll();
    }

    private struct ChunkSortData : System.IComparable<ChunkSortData>
    {
        public Vector2Int coord;
        public float sqrDistToLookahead;
        public float sqrDistToPlayer;

        public int CompareTo(ChunkSortData other)
        {
            return sqrDistToLookahead.CompareTo(other.sqrDistToLookahead);
        }
    }
}

[System.Serializable]
    public struct LODInfo 
    {
        public int lod;                     
        public float visibleDstThreshold;
    }

[System.Serializable]
public struct TerrainSettings 
{
    public LODInfo[] detailLevels; 
    public float MaxViewDst {
        get {
            if (detailLevels == null || detailLevels.Length == 0) return 0;
            return detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        }
    }
}

public static class TerrainBufferBridge
{
    private static readonly Stack<GraphicsBuffer> _singleBufferPool = new Stack<GraphicsBuffer>();
    private static readonly object _lockObj = new object();
    private const int BufferStride = 48;
    private const int BufferElements = 65536;
    public static GraphicsBuffer[] AcquireBuffers()
    {
        GraphicsBuffer[] lodBuffers = new GraphicsBuffer[5];

        lock (_lockObj)
        {
            for (int j = 0; j < 5; j++)
            {
                if (_singleBufferPool.Count > 0)
                {
                    lodBuffers[j] = _singleBufferPool.Pop();
                    
                    lodBuffers[j].SetCounterValue(0);
                }
                else
                {
                    lodBuffers[j] = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 
                        BufferElements, 
                        BufferStride
                    );
                    lodBuffers[j].SetCounterValue(0);
                }
            }
        }
            
        return lodBuffers;
    }

    public static void ReleaseBuffers(GraphicsBuffer[] buffers)
    {
        if (buffers == null) return;
        
        var manager = TerrainManager.Instance;
        int maxAllowedBuffers = (manager != null) ? manager.maxPoolSize * 5 : 500;

        lock (_lockObj)
        {
            for (int i = 0; i < buffers.Length; i++)
            {
                if (buffers[i] == null) continue;

                if (_singleBufferPool.Count < maxAllowedBuffers)
                {
                    _singleBufferPool.Push(buffers[i]);
                }
                else
                {
                    buffers[i].Release();
                }
            }
        }

        System.Array.Clear(buffers, 0, buffers.Length);
    }

    public static void CleanUpBridge()
    {
        lock (_lockObj)
        {
            while (_singleBufferPool.Count > 0)
            {
                var buf = _singleBufferPool.Pop();
                if (buf != null && buf.IsValid())
                {
                    buf.Release();
                }
            }
        }
    }
}
