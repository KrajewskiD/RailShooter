using System;
using UnityEngine;

[Serializable]
public abstract class TunnelSpawnCondition
{
    public abstract bool Check(SpawnContext ctx);
}

[Serializable]
public class NoneCondition : TunnelSpawnCondition
{
    public override bool Check(SpawnContext ctx) => true;
}

[Serializable]
public class ChanceCondition : TunnelSpawnCondition
{
    [Range(0f, 1f)] public float probability = 0.5f;

    public override bool Check(SpawnContext ctx) => ctx.Rng.NextDouble() < probability;
}

[Serializable]
public class EveryNthSegmentCondition : TunnelSpawnCondition
{
    [Min(1)] public int interval = 5;
    public int offset = 0;

    public override bool Check(SpawnContext ctx) => ((ctx.SegmentIndex - offset) % interval) == 0;
}

[Serializable]
public class MinSegmentIndexCondition : TunnelSpawnCondition
{
    [Tooltip("Spawning starts at this segment index (inclusive). Useful as a warm-up zone.")]
    public int minIndex = 5;

    public override bool Check(SpawnContext ctx) => ctx.SegmentIndex >= minIndex;
}

[Serializable]
public class SpecificSegmentIndexCondition : TunnelSpawnCondition
{
    public int targetIndex = 10;

    public override bool Check(SpawnContext ctx) => ctx.SegmentIndex == targetIndex;
}

[Serializable]
public class ProgressiveChanceCondition : TunnelSpawnCondition
{
    [Tooltip("Base chance (e.g. 0.05 = 5%).")]
    public float baseChance = 0.05f;
    [Tooltip("Amount the chance increases for each consecutive segment.")]
    public float increasePerSegment = 0.01f;
    [Tooltip("Segment index from which the rolling starts.")]
    public int startAtSegment = 10;

    public override bool Check(SpawnContext ctx)
    {
        if (ctx.SegmentIndex < startAtSegment) return false;
        int segmentsPassed = ctx.SegmentIndex - startAtSegment;
        float currentChance = Mathf.Clamp01(baseChance + (increasePerSegment * segmentsPassed));
        return ctx.Rng.NextDouble() <= currentChance;
    }
}

[Serializable]
public abstract class TunnelSpawnPlacement
{
    public abstract bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot);
}

[Serializable]
public class CenterAxisPlacement : TunnelSpawnPlacement
{
    [Tooltip("Position along segment in [0..1] (0 = start, 1 = end).")]
    [Range(0f, 1f)] public float zNormalized = 0.5f;

    public override bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            return false;
        }

        float z = Mathf.Lerp(ctx.SegmentStartZ, ctx.SegmentEndZ, zNormalized);

        ctx.AvailableRadius = ctx.Generator.GetRadiusAt(z);
        worldPos = ctx.Generator.GetCenterAt(z);
        worldRot = ctx.Generator.transform.rotation;
        return true;
    }
}

[Serializable]
public class RandomInsideTunnelPlacement : TunnelSpawnPlacement
{
    [Tooltip("Inner radius factor (0 = always at center, 1 = full tunnel radius).")]
    [Range(0f, 1f)] public float maxRadialFactor = 0.7f;

    [Tooltip("Wall safety margin - fraction of tunnel radius left empty near the walls.")]
    [Range(0f, 0.95f)] public float wallSafetyMargin = 0.2f;

    public override bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            return false;
        }

        float zT = (float)ctx.Rng.NextDouble();
        float z = Mathf.Lerp(ctx.SegmentStartZ, ctx.SegmentEndZ, zT);

        Vector3 center = ctx.Generator.GetCenterAt(z);
        float radius = ctx.Generator.GetRadiusAt(z) * (1f - wallSafetyMargin);

        float angle = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
        float r = (float)ctx.Rng.NextDouble() * maxRadialFactor * radius;

        Vector3 localOffset = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
        worldPos = center + ctx.Generator.transform.rotation * localOffset;
        worldRot = Quaternion.LookRotation(ctx.Generator.transform.forward, Vector3.up);
        return true;
    }
}

[Serializable]
public class PerimeterRingPlacement : TunnelSpawnPlacement
{
    [Tooltip("Position along segment in [0..1] (0 = start, 1 = end).")]
    [Range(0f, 1f)] public float zNormalized = 0.5f;

    [Tooltip("Distance from the tunnel wall edge.")]
    public float wallOffset = 0.5f;

    public override bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null || ctx.TotalSpawnCount <= 0)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            return false;
        }

        float z = Mathf.Lerp(ctx.SegmentStartZ, ctx.SegmentEndZ, zNormalized);
        Vector3 center = ctx.Generator.GetCenterAt(z);

        float radius = ctx.Generator.GetRadiusAt(z) - wallOffset;

        float angleStep = 360f / ctx.TotalSpawnCount;
        float angleInDegrees = ctx.CurrentSpawnIndex * angleStep;
        float angleInRadians = angleInDegrees * Mathf.Deg2Rad;

        float x = Mathf.Cos(angleInRadians) * radius;
        float y = Mathf.Sin(angleInRadians) * radius;

        Vector3 localOffset = new Vector3(x, y, 0f);

        worldPos = center + ctx.Generator.transform.rotation * localOffset;

        Vector3 directionToCenter = center - worldPos;
        worldRot = Quaternion.LookRotation(ctx.Generator.transform.forward, directionToCenter);

        return true;
    }
}

[Serializable]
public class AvoidCenterPlacement : TunnelSpawnPlacement
{
    [Tooltip("Inner safe-zone radius (as a fraction of full radius, 0-1).")]
    [Range(0f, 0.9f)] public float safeInnerFactor = 0.3f;

    [Tooltip("Wall safety margin.")]
    [Range(0f, 0.9f)] public float wallSafetyMargin = 0.1f;

    public override bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            return false;
        }

        float zT = (float)ctx.Rng.NextDouble();
        float z = Mathf.Lerp(ctx.SegmentStartZ, ctx.SegmentEndZ, zT);

        Vector3 center = ctx.Generator.GetCenterAt(z);
        float radius = ctx.Generator.GetRadiusAt(z);

        float minR = safeInnerFactor * radius;
        float maxR = radius * (1f - wallSafetyMargin);

        if (minR >= maxR) minR = 0;

        float angle = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
        float r = Mathf.Lerp(minR, maxR, (float)ctx.Rng.NextDouble());

        Vector3 localOffset = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);

        worldPos = center + ctx.Generator.transform.rotation * localOffset;
        worldRot = Quaternion.LookRotation(ctx.Generator.transform.forward, Vector3.up);

        ctx.AvailableRadius = maxR - r;

        return true;
    }
}

[Serializable]
public class GridLanesPlacement : TunnelSpawnPlacement
{
    [Tooltip("Number of lanes per axis (e.g. 3 = 3x3 grid).")]
    [Min(1)] public int gridSize = 3;

    [Tooltip("Wall safety margin from the tunnel wall.")]
    [Range(0f, 0.9f)] public float wallSafetyMargin = 0.1f;

    public override bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            return false;
        }

        float z = Mathf.Lerp(ctx.SegmentStartZ, ctx.SegmentEndZ, 0.5f);
        Vector3 center = ctx.Generator.GetCenterAt(z);

        float totalGridSize = (ctx.Generator.GetRadiusAt(z) * 2f) * (1f - wallSafetyMargin);

        float cellSize = totalGridSize / gridSize;
        float halfGrid = totalGridSize / 2f;

        int xIndex = ctx.Rng.Next(gridSize);
        int yIndex = ctx.Rng.Next(gridSize);

        float x = (xIndex * cellSize) + (cellSize / 2f) - halfGrid;
        float y = (yIndex * cellSize) + (cellSize / 2f) - halfGrid;

        Vector3 localOffset = new Vector3(x, y, 0f);

        worldPos = center + ctx.Generator.transform.rotation * localOffset;
        worldRot = Quaternion.LookRotation(ctx.Generator.transform.forward, Vector3.up);

        ctx.AvailableRadius = cellSize * 0.5f;

        return true;
    }
}

[Serializable]
public class ClusterPlacement : TunnelSpawnPlacement
{
    [Tooltip("Maximum scatter of objects relative to the generated cluster center.")]
    [Range(0f, 1f)] public float scatterFactor = 0.3f;

    private Vector3 _currentClusterCenter;
    private float _currentZ;

    public override bool TryGetPlacement(SpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            return false;
        }

        if (ctx.CurrentSpawnIndex == 0)
        {
            float zT = (float)ctx.Rng.NextDouble();
            _currentZ = Mathf.Lerp(ctx.SegmentStartZ, ctx.SegmentEndZ, zT);

            Vector3 center = ctx.Generator.GetCenterAt(_currentZ);
            float radius = ctx.Generator.GetRadiusAt(_currentZ) * 0.7f;

            float angleCenter = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
            float rCenter = (float)ctx.Rng.NextDouble() * radius;

            Vector3 localOffsetCenter = new Vector3(Mathf.Cos(angleCenter) * rCenter, Mathf.Sin(angleCenter) * rCenter, 0f);
            _currentClusterCenter = center + ctx.Generator.transform.rotation * localOffsetCenter;
        }

        float currentRadius = ctx.Generator.GetRadiusAt(_currentZ);
        float scatterRadius = currentRadius * scatterFactor;

        float angleScatter = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
        float rScatter = (float)ctx.Rng.NextDouble() * scatterRadius;

        Vector3 localScatter = new Vector3(Mathf.Cos(angleScatter) * rScatter, Mathf.Sin(angleScatter) * rScatter, 0f);

        worldPos = _currentClusterCenter + ctx.Generator.transform.rotation * localScatter;
        worldRot = Quaternion.LookRotation(ctx.Generator.transform.forward, Vector3.up);

        return true;
    }
}
