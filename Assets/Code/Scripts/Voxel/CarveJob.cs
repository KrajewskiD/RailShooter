using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct CarveJob : IJobParallelFor
{
    public NativeArray<float> Density;
    [ReadOnly] public int3 Size;
    [ReadOnly] public float VoxelSize;
    [ReadOnly] public float3 Origin;
    [ReadOnly] public float3 LocalPos;
    [ReadOnly] public float Radius;
    [ReadOnly] public float RadiusSq;

    public void Execute(int index)
    {
        int sliceSize = Size.x * Size.y;

        int z = index / sliceSize;
        int rem = index % sliceSize;
        int y = rem / Size.x;
        int x = rem % Size.x;

        float3 vp = Origin + new float3(x, y, z) * VoxelSize;
        float3 d = vp - LocalPos;

        float dist2 = math.lengthsq(d);
        if (dist2 > RadiusSq) return;

        float dist = math.sqrt(dist2);
        float strength = math.saturate(1f - dist / Radius);

        float newVal = Density[index] - (strength * 5.0f);
        Density[index] = math.max(newVal, -2.0f);
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct FindIslandsJob : IJob
{
    [ReadOnly] public NativeArray<float> Density;
    public NativeArray<bool> Visited;

    [ReadOnly] public int3 Size;
    [ReadOnly] public float IsoLevel;

    public NativeList<int> IslandOffsets;
    public NativeList<int> IslandIndices;

    public void Execute()
    {
        NativeQueue<int> queue = new NativeQueue<int>(Allocator.Temp);
        int slice = Size.x * Size.y;

        int offsetRight = 1;
        int offsetLeft = -1;
        int offsetUp = Size.x;
        int offsetDown = -Size.x;
        int offsetForward = slice;
        int offsetBackward = -slice;

        for (int i = 0; i < Density.Length; i++)
        {
            if (Density[i] < IsoLevel || Visited[i]) continue;

            queue.Enqueue(i);
            Visited[i] = true;
            int islandVoxelCount = 0;

            while (queue.TryDequeue(out int curr))
            {
                IslandIndices.Add(curr);
                islandVoxelCount++;

                int z = curr / slice;
                int rem = curr % slice;
                int y = rem / Size.x;
                int x = rem % Size.x;

                if (x < Size.x - 1) CheckAndEnqueue(curr + offsetRight, ref queue);
                if (x > 0) CheckAndEnqueue(curr + offsetLeft, ref queue);
                if (y < Size.y - 1) CheckAndEnqueue(curr + offsetUp, ref queue);
                if (y > 0) CheckAndEnqueue(curr + offsetDown, ref queue);
                if (z < Size.z - 1) CheckAndEnqueue(curr + offsetForward, ref queue);
                if (z > 0) CheckAndEnqueue(curr + offsetBackward, ref queue);
            }
            IslandOffsets.Add(islandVoxelCount);
        }
        queue.Dispose();
    }

    private void CheckAndEnqueue(int ni, ref NativeQueue<int> queue)
    {
        if (!Visited[ni] && Density[ni] >= IsoLevel)
        {
            Visited[ni] = true;
            queue.Enqueue(ni);
        }
    }
}
