using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Voxel.MarchingCubes
{
    public interface IVoxelMeshBuilder
    {
        void Build(
            NativeArray<float> density,
            int3 size,
            float voxelSize,
            float3 origin,
            float isoLevel,
            NativeList<float3> outVertices,
            NativeList<int> outTriangles,
            NativeList<float3> outNormals);
    }
}