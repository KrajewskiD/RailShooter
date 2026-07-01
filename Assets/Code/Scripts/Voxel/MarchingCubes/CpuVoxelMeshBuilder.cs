using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Voxel.MarchingCubes
{
    public class CpuVoxelMeshBuilder : IVoxelMeshBuilder, IDisposable
    {
        private static readonly float3[] CornerOffsets =
        {
            new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 1, 0), new float3(0, 1, 0),
            new float3(0, 0, 1), new float3(1, 0, 1), new float3(1, 1, 1), new float3(0, 1, 1)
        };

        private NativeArray<float3> _gradients;
        private readonly float3[] _vertList = new float3[12];
        private readonly float3[] _normList = new float3[12];
        private readonly float[] _cornerD = new float[8];
        private readonly int[] _cornerIdx = new int[8];

        public void Build(
            NativeArray<float> density,
            int3 size,
            float voxelSize,
            float3 origin,
            float isoLevel,
            NativeList<float3> outVertices,
            NativeList<int> outTriangles,
            NativeList<float3> outNormals)
        {
            int sx = size.x;
            int sy = size.y;
            int sz = size.z;
            int total = sx * sy * sz;

            if (!_gradients.IsCreated || _gradients.Length < total)
            {
                if (_gradients.IsCreated) _gradients.Dispose();
                _gradients = new NativeArray<float3>(total, Allocator.Persistent);
            }

            int strideY = sx, strideZ = sx * sy;

            for (int z = 0; z < sz; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                    {
                        int xm = math.max(x - 1, 0);
                        int xp = math.min(x + 1, sx - 1);
                        int ym = math.max(y - 1, 0);
                        int yp = math.min(y + 1, sy - 1);
                        int zm = math.max(z - 1, 0);
                        int zp = math.min(z + 1, sz - 1);

                        float gx = density[xp + y * strideY + z * strideZ] - density[xm + y * strideY + z * strideZ];
                        float gy = density[x + yp * strideY + z * strideZ] - density[x + ym * strideY + z * strideZ];
                        float gz = density[x + y * strideY + zp * strideZ] - density[x + y * strideY + zm * strideZ];

                        float3 g = new float3(gx, gy, gz);
                        float m2 = math.lengthsq(g);
                        _gradients[x + y * strideY + z * strideZ] = m2 > 1e-8f ? g * math.rsqrt(m2) : new float3(0, 1, 0);
                    }

            for (int z = 0; z < sz - 1; z++)
                for (int y = 0; y < sy - 1; y++)
                    for (int x = 0; x < sx - 1; x++)
                    {
                        int b = x + y * strideY + z * strideZ;
                        _cornerIdx[0] = b;
                        _cornerIdx[1] = b + 1;
                        _cornerIdx[2] = b + 1 + strideY;
                        _cornerIdx[3] = b + strideY;
                        _cornerIdx[4] = b + strideZ;
                        _cornerIdx[5] = b + 1 + strideZ;
                        _cornerIdx[6] = b + 1 + strideY + strideZ;
                        _cornerIdx[7] = b + strideY + strideZ;

                        int cubeIndex = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            float d = density[_cornerIdx[c]];
                            _cornerD[c] = d;
                            if (d < isoLevel) cubeIndex |= 1 << c;
                        }

                        int edges = MarchingCubesTables.EdgeTable[cubeIndex];
                        if (edges == 0) continue;

                        float3 cubeBase = origin + new float3(x, y, z) * voxelSize;

                        for (int e = 0; e < 12; e++)
                        {
                            if ((edges & (1 << e)) == 0) continue;
                            int a = MarchingCubesTables.EdgeCornerA[e];
                            int b2 = MarchingCubesTables.EdgeCornerB[e];
                            float da = _cornerD[a];
                            float db = _cornerD[b2];
                            float denom = db - da;
                            float t = math.abs(denom) > 1e-6f ? (isoLevel - da) / denom : 0.5f;

                            _vertList[e] = cubeBase + math.lerp(CornerOffsets[a], CornerOffsets[b2], t) * voxelSize;
                            float3 nLerp = math.lerp(_gradients[_cornerIdx[a]], _gradients[_cornerIdx[b2]], t);
                            float nm2 = math.lengthsq(nLerp);
                            _normList[e] = nm2 > 1e-8f ? nLerp * math.rsqrt(nm2) : new float3(0f, 1f, 0f);
                        }

                        for (int i = 0; i < 16; i += 3)
                        {
                            int e0 = MarchingCubesTables.TriTable[cubeIndex, i];
                            if (e0 == -1) break;
                            int e1 = MarchingCubesTables.TriTable[cubeIndex, i + 1];
                            int e2 = MarchingCubesTables.TriTable[cubeIndex, i + 2];

                            int baseVtx = outVertices.Length;
                            outVertices.Add(_vertList[e0]);
                            outVertices.Add(_vertList[e1]);
                            outVertices.Add(_vertList[e2]);

                            outNormals.Add(_normList[e0]);
                            outNormals.Add(_normList[e1]);
                            outNormals.Add(_normList[e2]);

                            outTriangles.Add(baseVtx);
                            outTriangles.Add(baseVtx + 1);
                            outTriangles.Add(baseVtx + 2);
                        }
                    }
        }

        public void Dispose()
        {
            if (_gradients.IsCreated) _gradients.Dispose();
        }
    }
}
