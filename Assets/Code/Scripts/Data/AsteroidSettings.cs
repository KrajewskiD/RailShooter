using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Voxel.MarchingCubes;
using System;
using NaughtyAttributes; 

[CreateAssetMenu(menuName = "Obstacles/Voxel/Asteroid Settings", fileName = "AsteroidSettings")]
public class AsteroidSettings : VoxelSettings
{
    

    [BoxGroup("Asteroid Shape")]
    [Tooltip("Average radius of the asteroid blob.")]
    public float radius = 8f;

    [BoxGroup("Asteroid Shape")]
    [Tooltip("Surface roughness multiplier (0 = sphere, 1 = very irregular).")]
    public float roughness = 0.5f;

    private float _invRadius;

    private NativeArray<float> _masterDensity;
    private Mesh _masterMesh;
    private bool _isBaked = false;

    public static event System.Action OnSettingsUpdated;

    [Button("Force Re-Bake")]
    public void ForceReBake()
    {
        OnValidate();
    }
    public override void Precompute()
    {
        _invRadius = 1f / Mathf.Max(radius, 0.001f);
    }

    public override Vector3Int GetGridSize()
    {
        int s = Mathf.CeilToInt(radius * 2.5f / voxelSize) + 4;
        return new Vector3Int(s, s, s);
    }

    public override Vector3 GetLocalOrigin()
    {
        var s = GetGridSize();
        return new Vector3(-s.x * voxelSize * 0.5f, -s.y * voxelSize * 0.5f, -s.z * voxelSize * 0.5f);
    }

    public override float SampleDensity(Vector3 p, float noiseValue)
    {
        float dist = Mathf.Sqrt(p.x * p.x + p.y * p.y + p.z * p.z);
        
        float falloff = (dist - radius) / _invRadius;
        float density = falloff + noiseValue * roughness;
        return -density;
    }

    public override void ApplySetup(GameObject spawnedObject)
    {
        int layerIndex = 0;
        int maskValue = hitMask.value;

        if (maskValue > 0)
        {
            while ((maskValue & 1) == 0) { maskValue >>= 1; layerIndex++; }
            spawnedObject.layer = layerIndex;
        }

        if (spawnedObject.TryGetComponent<DestructibleVoxelChunk>(out var script))
        {
            script.Initialize(this);
        }
    }

    public void BakeTemplate(IVoxelMeshBuilder builder)
    {
        if (_isBaked && _masterMesh != null && _masterDensity.IsCreated)
        {
            return;
        }

        if (_isBaked)
        {
            if (_masterDensity.IsCreated) _masterDensity.Dispose();
            _masterMesh = null;
            _isBaked = false;
        }

        Precompute();
        var size = GetGridSize();
        int total = size.x * size.y * size.z;

        _masterDensity = new NativeArray<float>(total, Allocator.Persistent);

        var origin = GetLocalOrigin();
        float minD = float.MaxValue, maxD = float.MinValue;
        for (int z = 0; z < size.z; z++)
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
        {
            Vector3 lp = origin + new Vector3(x, y, z) * voxelSize;
            float n = noise.GetNoise(lp.x, lp.y, lp.z) * noiseStrength;
            float d = SampleDensity(lp, n);
            _masterDensity[x + y * size.x + z * size.x * size.y] = d;
            if (d < minD) minD = d;
            if (d > maxD) maxD = d;
        }

        _masterMesh = new Mesh { name = "Master_Asteroid_" + name };
        var v = new NativeList<float3>(Allocator.TempJob);
        var t = new NativeList<int>(Allocator.TempJob);
        var n_list = new NativeList<float3>(Allocator.TempJob);

        builder.Build(_masterDensity, new int3(size.x, size.y, size.z), voxelSize, origin, isoLevel, v, t, n_list);

        _masterMesh.SetVertices(v.AsArray());
        _masterMesh.SetIndices(t.AsArray(), MeshTopology.Triangles, 0);
        _masterMesh.RecalculateNormals();

        v.Dispose(); t.Dispose(); n_list.Dispose();
        _isBaked = true;
    }

    public override NativeArray<float> GetMasterDensity() => _masterDensity;
    public override Mesh GetMasterMesh() => _masterMesh;

    private void OnDisable()
    {
        if (_masterDensity.IsCreated) _masterDensity.Dispose();
        if (_masterMesh != null)
        {
            if (Application.isPlaying) Destroy(_masterMesh);
            else DestroyImmediate(_masterMesh);
        }
        _masterMesh = null;
        _isBaked = false;
    }
}