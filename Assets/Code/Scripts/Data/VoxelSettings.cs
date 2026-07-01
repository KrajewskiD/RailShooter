using Unity.Collections;
using UnityEngine;
using NaughtyAttributes;
public abstract class VoxelSettings : SpawnSettings
{
    public enum BackendType { Cpu, Burst, Gpu }

    [BoxGroup("Noise Configuration")]
    public RewriteFastNoiseLite noise = RewriteFastNoiseLite.Default();

    [BoxGroup("Noise Configuration")]
    [Tooltip("Multiplier applied to raw noise before adding to density falloff.")]
    [AllowNesting]
    public float noiseStrength = 1f;

    [BoxGroup("Grid Settings")]
    [Min(0.1f)] 
    [AllowNesting]
    public float voxelSize = 1f;

    [BoxGroup("Grid Settings")]
    [Tooltip("Iso-surface threshold. Density < isoLevel = solid.")]
    [Range(-1f, 1f)] 
    [AllowNesting]
    public float isoLevel = 0f;

    [BoxGroup("System")]
    public BackendType backend = BackendType.Cpu;

    [BoxGroup("Destruction")]
    [InfoBox("Final radius = projectile damage * carve scale", NaughtyAttributes.EInfoBoxType.Normal)]
    [Tooltip("Carve sphere radius = projectile damage * carveScale.")]
    [Min(0f)] 
    [AllowNesting]
    public float carveScale = 0.05f;
    
    [BoxGroup("Physics")]
    public LayerMask hitMask;

    public abstract Vector3Int GetGridSize();
    public abstract Vector3 GetLocalOrigin();
    public abstract float SampleDensity(Vector3 localPos, float noiseValue);

    public virtual Mesh GetMasterMesh() => null;
    public virtual NativeArray<float> GetMasterDensity() => default;

    protected virtual void OnEnable() => Precompute();
    protected virtual void OnValidate() => Precompute();
    public virtual void Precompute() { }
}
