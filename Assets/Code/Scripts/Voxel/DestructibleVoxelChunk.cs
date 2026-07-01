using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxel.MarchingCubes;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DestructibleVoxelChunk : MonoBehaviour
{
    [SerializeField] private VoxelSettings config;
    public VoxelSettings Config => config;

    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private Rigidbody _rigidbody;
    [SerializeField] private MeshCollider physicsCollider;
    [SerializeField] private MeshCollider detailCollider;
    [SerializeField] private PhysicsMaterial asteroidPhysicsMaterial;
    private Mesh _mesh;

    private NativeArray<float> _density;

    private Vector3Int _size;
    private float _voxelSize;
    private float _isoLevel;
    private Vector3 _origin;
    private RewriteFastNoiseLite _noise;
    private float _noiseStrength;
    private IVoxelMeshBuilder _builder;

    private NativeList<float3> _vertices;
    private NativeList<int> _triangles;
    private NativeList<float3> _normals;

    private bool _isUnique = false;

    [SerializeField] private Material asteroidMaterial;

    [Header("Carve Throttling")]
    [Tooltip("Mesh + physics rebuild fires every N carves. Density is modified on every hit, but the visible mesh and colliders update in batches. Set to 1 to rebuild on every shot.")]
    [SerializeField, Min(1)] private int rebuildEveryNCarves = 1;

    private int _pendingCarves = 0;

    
    private void OnEnable()
    {
        AsteroidSettings.OnSettingsUpdated += RefreshFromSettings;
    }

    private void OnDisable()
    {
        AsteroidSettings.OnSettingsUpdated -= RefreshFromSettings;
    }

    private void Awake()
    {
        _filter = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        _rigidbody = GetComponent<Rigidbody>();

        if (physicsCollider == null) physicsCollider = CreateColliderChild("PhysicsHolder", "AsteroidPhysics", "AsteroidPhysics", false);
        else ApplyLayerAndTag(physicsCollider.gameObject, "AsteroidPhysics", "AsteroidPhysics");

        if (detailCollider == null) detailCollider = CreateColliderChild("DetailHolder", "AsteroidDetail", "AsteroidDetail", true);
        else ApplyLayerAndTag(detailCollider.gameObject, "AsteroidDetail", "AsteroidDetail");
    }

    private void RefreshFromSettings()
    {
        if (!_isUnique && config != null)
        {
            Initialize(config);
        }
    }

    public void Initialize(VoxelSettings cfg)
    {
        if (cfg == null)
        {
            return;
        }

        config = cfg;

        _size = cfg.GetGridSize();
        _voxelSize = cfg.voxelSize;
        _isoLevel = cfg.isoLevel;
        _origin = cfg.GetLocalOrigin();
        _noise = cfg.noise;
        _noiseStrength = cfg.noiseStrength;

        if (_builder is System.IDisposable oldDisposable) oldDisposable.Dispose();
        _builder = SelectBackend(cfg.backend);

        if (cfg is AsteroidSettings asteroidSettings)
        {
            asteroidSettings.BakeTemplate(_builder);
            _density = asteroidSettings.GetMasterDensity();
            var masterMesh = asteroidSettings.GetMasterMesh();
            _filter.sharedMesh = masterMesh;
            if (_renderer != null) _renderer.sharedMaterial = asteroidMaterial;
        }

        if (physicsCollider != null)
        {
            physicsCollider.sharedMesh = config.GetMasterMesh();
            physicsCollider.convex = true;
            physicsCollider.material = asteroidPhysicsMaterial;
            if (_rigidbody != null)
            {
                _rigidbody.centerOfMass = transform.InverseTransformPoint(physicsCollider.bounds.center);
                _rigidbody.ResetInertiaTensor();
            }
        }
        else
        {
            int total = _size.x * _size.y * _size.z;
            MakeUnique(total);
            FillDensity();
            Rebuild();
        }

        if (detailCollider != null)
        {
            var detailMesh = config.GetMasterMesh();
            if (detailMesh != null)
            {
                detailCollider.sharedMesh = null;
                detailCollider.convex = false;
                detailCollider.sharedMesh = detailMesh;
                detailCollider.material = asteroidPhysicsMaterial;
            }
        }

        _isUnique = false;
        _pendingCarves = 0;
        InitializePhysics();
    }

    private void FillDensity()
    {
        for (int z = 0; z < _size.z; z++)
        for (int y = 0; y < _size.y; y++)
        for (int x = 0; x < _size.x; x++)
        {
            Vector3 lp = _origin + new Vector3(x, y, z) * _voxelSize;
            float n = _noise.GetNoise(lp.x, lp.y, lp.z) * _noiseStrength;
            _density[Idx(x, y, z)] = config.SampleDensity(lp, n);
        }
    }

    public void Carve(Vector3 worldPos, float radius)
    {
        if (radius <= 0f || !_density.IsCreated) return;

        if (!_isUnique)
        {
            int total = _size.x * _size.y * _size.z;
            MakeUnique(total);
        }

        Vector3 localPos = transform.InverseTransformPoint(worldPos);

        var carveJob = new CarveJob
        {
            Density = _density,
            Size = new int3(_size.x, _size.y, _size.z),
            VoxelSize = _voxelSize,
            Origin = _origin,
            LocalPos = localPos,
            Radius = radius,
            RadiusSq = radius * radius
        };

        JobHandle handle = carveJob.Schedule(_density.Length, 64);
        handle.Complete();

        _pendingCarves++;
        if (_pendingCarves >= rebuildEveryNCarves)
        {
            _pendingCarves = 0;
            Rebuild();
        }
    }

    public void ForceRebuild()
    {
        if (_pendingCarves == 0) return;
        _pendingCarves = 0;
        Rebuild();
    }

    private void MakeUnique(int length)
    {
        var sourceData = _density;
        var newDensity = new NativeArray<float>(length, Allocator.Persistent);
        if (sourceData.IsCreated)
        {
            sourceData.CopyTo(newDensity);
        }
        if (_isUnique && sourceData.IsCreated) sourceData.Dispose();
        _density = newDensity;

        if (!_vertices.IsCreated) _vertices = new NativeList<float3>(Allocator.Persistent);
        if (!_triangles.IsCreated) _triangles = new NativeList<int>(Allocator.Persistent);
        if (!_normals.IsCreated) _normals = new NativeList<float3>(Allocator.Persistent);

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "UniqueVoxelChunkMesh", indexFormat = IndexFormat.UInt32 };
        }

        _isUnique = true;
    }

    private void Rebuild()
    {
        _vertices.Clear();
        _triangles.Clear();
        _normals.Clear();

        _builder.Build(_density, new int3(_size.x, _size.y, _size.z), _voxelSize, _origin, _isoLevel,
               _vertices, _triangles, _normals);

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "UniqueVoxelChunkMesh", indexFormat = IndexFormat.UInt32 };
        }

        _mesh.Clear();

        if (_vertices.Length > 0)
        {
            _mesh.SetVertices(_vertices.AsArray());
            _mesh.SetIndices(_triangles.AsArray(), MeshTopology.Triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_renderer != null) _renderer.sharedMaterial = asteroidMaterial;

            if (_filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;

            ExecuteBakingLogic();
        }
        else
        {
            if (physicsCollider != null) physicsCollider.sharedMesh = null;
            if (detailCollider != null) detailCollider.sharedMesh = null;
        }
    }

    private void ExecuteBakingLogic()
    {
        var fastCooking = MeshColliderCookingOptions.UseFastMidphase | MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.WeldColocatedVertices;
        if (physicsCollider != null) physicsCollider.cookingOptions = fastCooking;
        if (detailCollider != null) detailCollider.cookingOptions = fastCooking;

        int meshId = _mesh.GetInstanceID();
        var physicsBakeJob = new BakePhysicsJob { MeshInstanceID = meshId, Convex = true };
        Unity.Jobs.JobHandle finalHandle = physicsBakeJob.Schedule();

        if (detailCollider != null)
        {
            var detailBakeJob = new BakePhysicsJob { MeshInstanceID = meshId, Convex = false };
            finalHandle = Unity.Jobs.JobHandle.CombineDependencies(finalHandle, detailBakeJob.Schedule());
        }
        StartCoroutine(WaitAndAssignColliders(finalHandle, _mesh));
    }

    private System.Collections.IEnumerator WaitAndAssignColliders(Unity.Jobs.JobHandle bakeHandle, Mesh bakedMesh)
    {
        while (!bakeHandle.IsCompleted) yield return null; 

        bakeHandle.Complete();

        if (this == null || bakedMesh == null) yield break;

        if (physicsCollider != null)
        {
            physicsCollider.sharedMesh = null; 
            physicsCollider.convex = true; 
            physicsCollider.sharedMesh = bakedMesh;
            physicsCollider.material = asteroidPhysicsMaterial;
            physicsCollider.enabled = false;
            physicsCollider.enabled = true;
        }

        if (detailCollider != null)
        {
            Rigidbody detailRB = detailCollider.GetComponent<Rigidbody>();
            if (detailRB == null) detailRB = detailCollider.gameObject.AddComponent<Rigidbody>();
            detailRB.isKinematic = true; 
            detailRB.useGravity = false;

            detailCollider.sharedMesh = null;
            detailCollider.convex = true; 
            detailCollider.sharedMesh = bakedMesh;
            detailCollider.enabled = false;
            detailCollider.enabled = true;
        }

        if (_rigidbody != null)
        {
            _rigidbody.centerOfMass = transform.InverseTransformPoint(physicsCollider.bounds.center);
            _rigidbody.ResetInertiaTensor();
            _rigidbody.isKinematic = false;
        }
    }

    private MeshCollider CreateColliderChild(string objName, string layerName, string tagName, bool requireKinematicRigidbody)
    {
        Transform existing = transform.Find(objName);
        GameObject child = existing != null ? existing.gameObject : new GameObject(objName);
        if (existing == null)
        {
            child.transform.SetParent(this.transform);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
        }

        ApplyLayerAndTag(child, layerName, tagName);

        if (requireKinematicRigidbody)
        {
            Rigidbody rb = child.GetComponent<Rigidbody>();
            if (rb == null) rb = child.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        MeshCollider col = child.GetComponent<MeshCollider>();
        if (col == null) col = child.AddComponent<MeshCollider>();
        return col;
    }

    private static void ApplyLayerAndTag(GameObject go, string layerName, string tagName)
    {
        if (!string.IsNullOrEmpty(layerName))
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) go.layer = layer;
        }

        UnityObjectUtility.TrySetTag(go, tagName);
    }

    private void InitializePhysics()
    {
        if (_rigidbody == null) return;

        _rigidbody.constraints = RigidbodyConstraints.None;
        _rigidbody.useGravity = false;

        float speed = UnityEngine.Random.Range(15f, 35f);
        _rigidbody.linearVelocity = Vector3.back * speed;
        _rigidbody.angularVelocity = UnityEngine.Random.insideUnitSphere * UnityEngine.Random.Range(1f, 3f);
    }

    private void FixedUpdate()
    {
        if (_rigidbody == null) return;

        const float targetZSpeed = -20f;
        if (_rigidbody.linearVelocity.z > targetZSpeed)
        {
            _rigidbody.AddForce(Vector3.back * 2f, ForceMode.VelocityChange);
        }
    }

    private static IVoxelMeshBuilder SelectBackend(VoxelSettings.BackendType type)
    {
        switch (type)
        {
            case VoxelSettings.BackendType.Cpu: return new CpuVoxelMeshBuilder();
            case VoxelSettings.BackendType.Burst: return new BurstVoxelMeshBuilder();
            default: return new CpuVoxelMeshBuilder();
        }
    }

    private int Idx(int x, int y, int z) => x + y * _size.x + z * _size.x * _size.y;

    private void OnDestroy()
    {
        if (_builder is System.IDisposable disposable) disposable.Dispose();

        if (_isUnique && _density.IsCreated) _density.Dispose();

        if (_vertices.IsCreated) _vertices.Dispose();
        if (_triangles.IsCreated) _triangles.Dispose();
        if (_normals.IsCreated) _normals.Dispose();

        if (_mesh != null)
        {
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}
