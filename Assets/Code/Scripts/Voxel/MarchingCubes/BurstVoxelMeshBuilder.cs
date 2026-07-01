using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Voxel.MarchingCubes
{
    public class BurstVoxelMeshBuilder : IVoxelMeshBuilder, IDisposable
    {
        private NativeArray<int> _triTable;
        private NativeArray<int> _edgeTable;
        private NativeArray<int> _edgeCornerA;
        private NativeArray<int> _edgeCornerB;
        private NativeArray<float3> _cornerOffsets;

        public BurstVoxelMeshBuilder()
        {
            _edgeTable = new NativeArray<int>(MarchingCubesTables.EdgeTable, Allocator.Persistent);
            _edgeCornerA = new NativeArray<int>(MarchingCubesTables.EdgeCornerA, Allocator.Persistent);
            _edgeCornerB = new NativeArray<int>(MarchingCubesTables.EdgeCornerB, Allocator.Persistent);

            _triTable = new NativeArray<int>(256 * 16, Allocator.Persistent);
            for (int i = 0; i < 256; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    _triTable[i * 16 + j] = MarchingCubesTables.TriTable[i, j];
                }
            }

            _cornerOffsets = new NativeArray<float3>(8, Allocator.Persistent)
            {
                [0] = new float3(0, 0, 0), [1] = new float3(1, 0, 0), [2] = new float3(1, 1, 0), [3] = new float3(0, 1, 0),
                [4] = new float3(0, 0, 1), [5] = new float3(1, 0, 1), [6] = new float3(1, 1, 1), [7] = new float3(0, 1, 1)
            };
        }

        public void Build(NativeArray<float> density, int3 size, float voxelSize, float3 origin, float isoLevel,
                          NativeList<float3> outVertices, NativeList<int> outTriangles, NativeList<float3> outNormals)
        {
            var job = new MarchingCubesJob
            {
                density = density,
                size = size,
                voxelSize = voxelSize,
                origin = origin,
                isoLevel = isoLevel,
                vertices = outVertices,
                triangles = outTriangles,
                normals = outNormals,

                triTable = _triTable,
                edgeTable = _edgeTable,
                edgeCornerA = _edgeCornerA,
                edgeCornerB = _edgeCornerB,
                cornerOffsets = _cornerOffsets
            };

            job.Schedule().Complete();
        }

        public void Dispose()
        {
            if (_triTable.IsCreated) _triTable.Dispose();
            if (_edgeTable.IsCreated) _edgeTable.Dispose();
            if (_edgeCornerA.IsCreated) _edgeCornerA.Dispose();
            if (_edgeCornerB.IsCreated) _edgeCornerB.Dispose();
            if (_cornerOffsets.IsCreated) _cornerOffsets.Dispose();
        }
    }
}

[BurstCompile]
internal struct MarchingCubesJob : IJob
{
    [ReadOnly] public NativeArray<float> density;
    public int3 size;
    public float voxelSize;
    public float3 origin;
    public float isoLevel;

    public NativeList<float3> vertices;
    public NativeList<int> triangles;
    public NativeList<float3> normals;

    [ReadOnly] public NativeArray<int> triTable;
    [ReadOnly] public NativeArray<int> edgeTable;
    [ReadOnly] public NativeArray<int> edgeCornerA;
    [ReadOnly] public NativeArray<int> edgeCornerB;
    [ReadOnly] public NativeArray<float3> cornerOffsets;

    public void Execute()
    {
        int sx = size.x;
        int sy = size.y;
        int sz = size.z;
        int strideY = sx;
        int strideZ = sx * sy;
        int total = sx * sy * sz;

        var gradients = new NativeArray<float3>(total, Allocator.Temp);
        for (int z = 0; z < sz; z++)
            for (int y = 0; y < sy; y++)
                for (int x = 0; x < sx; x++)
                {
                    int xm = x > 0 ? x - 1 : x;
                    int xp = x < sx - 1 ? x + 1 : x;
                    int ym = y > 0 ? y - 1 : y;
                    int yp = y < sy - 1 ? y + 1 : y;
                    int zm = z > 0 ? z - 1 : z;
                    int zp = z < sz - 1 ? z + 1 : z;

                    float gx = density[xp + y * strideY + z * strideZ] - density[xm + y * strideY + z * strideZ];
                    float gy = density[x + yp * strideY + z * strideZ] - density[x + ym * strideY + z * strideZ];
                    float gz = density[x + y * strideY + zp * strideZ] - density[x + y * strideY + zm * strideZ];

                    float3 g = new float3(gx, gy, gz);
                    float m2 = math.lengthsq(g);
                    gradients[x + y * strideY + z * strideZ] = m2 > 1e-8f ? g * math.rsqrt(m2) : new float3(0f, 1f, 0f);
                }

        var vertList = new NativeArray<float3>(12, Allocator.Temp);
        var normList = new NativeArray<float3>(12, Allocator.Temp);
        var cornerD = new NativeArray<float>(8, Allocator.Temp);
        var cornerIdx = new NativeArray<int>(8, Allocator.Temp);

        for (int z = 0; z < sz - 1; z++)
            for (int y = 0; y < sy - 1; y++)
                for (int x = 0; x < sx - 1; x++)
                {
                    int b = x + y * strideY + z * strideZ;

                    cornerIdx[0] = b;
                    cornerIdx[1] = b + 1;
                    cornerIdx[2] = b + 1 + strideY;
                    cornerIdx[3] = b + strideY;
                    cornerIdx[4] = b + strideZ;
                    cornerIdx[5] = b + 1 + strideZ;
                    cornerIdx[6] = b + 1 + strideY + strideZ;
                    cornerIdx[7] = b + strideY + strideZ;

                    int cubeIndex = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        float d = density[cornerIdx[c]];
                        cornerD[c] = d;
                        if (d < isoLevel) cubeIndex |= 1 << c;
                    }

                    int edges = edgeTable[cubeIndex];
                    if (edges == 0) continue;

                    float3 cubeBase = origin + new float3(x, y, z) * voxelSize;

                    for (int e = 0; e < 12; e++)
                    {
                        if ((edges & (1 << e)) == 0) continue;

                        int a = edgeCornerA[e];
                        int b2 = edgeCornerB[e];

                        float da = cornerD[a];
                        float db = cornerD[b2];
                        float t = math.abs(db - da) > 1e-6f ? (isoLevel - da) / (db - da) : 0.5f;

                        vertList[e] = cubeBase + math.lerp(cornerOffsets[a], cornerOffsets[b2], t) * voxelSize;

                        float3 nLerp = math.lerp(gradients[cornerIdx[a]], gradients[cornerIdx[b2]], t);
                        float nm2 = math.lengthsq(nLerp);
                        normList[e] = nm2 > 1e-8f ? nLerp * math.rsqrt(nm2) : new float3(0f, 1f, 0f);
                    }

                    int triRowBase = cubeIndex * 16;
                    for (int i = 0; i < 16; i += 3)
                    {
                        int e0 = triTable[triRowBase + i];
                        if (e0 == -1) break;

                        int e1 = triTable[triRowBase + i + 1];
                        int e2 = triTable[triRowBase + i + 2];

                        int baseVtx = vertices.Length;

                        vertices.Add(vertList[e0]);
                        vertices.Add(vertList[e1]);
                        vertices.Add(vertList[e2]);

                        normals.Add(normList[e0]);
                        normals.Add(normList[e1]);
                        normals.Add(normList[e2]);

                        triangles.Add(baseVtx);
                        triangles.Add(baseVtx + 1);
                        triangles.Add(baseVtx + 2);
                    }
                }

        gradients.Dispose();
        vertList.Dispose();
        normList.Dispose();
        cornerD.Dispose();
        cornerIdx.Dispose();
    }
}
