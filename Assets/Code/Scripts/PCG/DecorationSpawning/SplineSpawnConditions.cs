using System;
using UnityEngine;



[Serializable]
public abstract class SplineSpawnCondition
{
    public abstract bool Check(SplineSpawnContext ctx);
}

[Serializable]
public class SplineNoneCondition : SplineSpawnCondition
{
    public override bool Check(SplineSpawnContext ctx) => true;
}

[Serializable]
public class SplineChanceCondition : SplineSpawnCondition
{
    [Range(0f, 1f)] public float probability = 0.5f;
    public override bool Check(SplineSpawnContext ctx) => ctx.Rng.NextDouble() < probability;
}

[Serializable]
public class SplineEveryNthSegmentCondition : SplineSpawnCondition
{
    [Min(1)] public int interval = 3;
    public int offset = 0;
    public override bool Check(SplineSpawnContext ctx) => ((ctx.SegmentIndex - offset) % interval) == 0;
}

[Serializable]
public class SplineMinSegmentIndexCondition : SplineSpawnCondition
{
    [Tooltip("Spawning zaczyna się od tego indeksu segmentu (inclusive). Przydatne na warm-up.")]
    public int minIndex = 2;
    public override bool Check(SplineSpawnContext ctx) => ctx.SegmentIndex >= minIndex;
}

[Serializable]
public class SplineSpecificSegmentIndexCondition : SplineSpawnCondition
{
    public int targetIndex = 5;
    public override bool Check(SplineSpawnContext ctx) => ctx.SegmentIndex == targetIndex;
}

[Serializable]
public class SplineProgressiveChanceCondition : SplineSpawnCondition
{
    public float baseChance = 0.05f;
    public float increasePerSegment = 0.01f;
    public int startAtSegment = 5;

    public override bool Check(SplineSpawnContext ctx)
    {
        if (ctx.SegmentIndex < startAtSegment) return false;
        int segmentsPassed = ctx.SegmentIndex - startAtSegment;
        float currentChance = Mathf.Clamp01(baseChance + (increasePerSegment * segmentsPassed));
        return ctx.Rng.NextDouble() <= currentChance;
    }
}



[Serializable]
public abstract class SplineSpawnPlacement
{
    [Tooltip("Auto-clamp pozycji końcowej do zasięgu joystick'a gracza (FrameHalfWidth/Height z Controllera). " +
             "Wyłącz tylko dla przeszkód, które GRACZ MA OMIJAĆ.")]
    public bool respectPlayerReach = true;

    public abstract bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot);


    protected static void GetFrame(SplineGenerator gen, float arc, out Vector3 pos, out Vector3 tangent, out Vector3 right, out Vector3 up)
    {
        pos = gen.GetPositionAtArc(arc);
        tangent = gen.GetTangentAtArc(arc);
        up = Vector3.up;
        right = Vector3.Cross(up, tangent).normalized;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
    }



    protected static Vector3 ClampToReach(Vector3 splineCenter, Vector3 worldPos, Vector3 right, Vector3 up, SplineGenerator gen)
    {
        Vector3 delta = worldPos - splineCenter;
        float lat = Vector3.Dot(delta, right);
        float ver = Vector3.Dot(delta, up);
        float maxLat = gen.ReachHalfWidth;
        float maxVer = gen.ReachHalfHeight;
        lat = Mathf.Clamp(lat, -maxLat, maxLat);
        ver = Mathf.Clamp(ver, -maxVer, maxVer);
        return splineCenter + right * lat + up * ver;
    }
}


[Serializable]
public class SplineCenterAxisPlacement : SplineSpawnPlacement
{
    [Range(0f, 1f)] public float arcNormalized = 0.5f;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null) { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }
        float arc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, arcNormalized);
        ctx.CurrentPlacementArc = arc;
        GetFrame(ctx.Generator, arc, out var pos, out var tangent, out _, out _);
        ctx.AvailableSpawnRadius = ctx.Generator.CorridorRadius;
        worldPos = pos;
        worldRot = Quaternion.LookRotation(tangent, Vector3.up);
        return true;
    }
}


[Serializable]
public class SplineLateralOffsetPlacement : SplineSpawnPlacement
{
    [Range(0f, 1f)] public float arcNormalized = 0.5f;
    [Tooltip("Offset boczny w jednostkach świata. Ujemne = lewo, dodatnie = prawo.")]
    public float lateralOffset = 0f;
    public float verticalOffset = 0f;
    [Tooltip("Jeśli true, losuje lateralOffset w zakresie ±randomLateralRange (ignoruje stały lateralOffset).")]
    public bool randomize = false;
    public float randomLateralRange = 8f;
    public float randomVerticalRange = 3f;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null) { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }
        float arc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, arcNormalized);
        ctx.CurrentPlacementArc = arc;
        GetFrame(ctx.Generator, arc, out var pos, out var tangent, out var right, out var up);
        float dx = randomize ? (float)(ctx.Rng.NextDouble() * 2.0 - 1.0) * randomLateralRange : lateralOffset;
        float dy = randomize ? (float)(ctx.Rng.NextDouble() * 2.0 - 1.0) * randomVerticalRange : verticalOffset;
        worldPos = pos + right * dx + up * dy;
        if (respectPlayerReach) worldPos = ClampToReach(pos, worldPos, right, up, ctx.Generator);
        worldRot = Quaternion.LookRotation(tangent, Vector3.up);
        ctx.AvailableSpawnRadius = ctx.Generator.CorridorRadius;
        return true;
    }
}


[Serializable]
public class SplineCorridorRingPlacement : SplineSpawnPlacement
{
    [Range(0f, 1f)] public float arcNormalized = 0.5f;
    [Tooltip("Promień pierścienia.")]
    public float ringRadius = 6f;
    [Tooltip("Offset kątowy startu w stopniach.")]
    public float angleOffsetDeg = 0f;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null || ctx.TotalSpawnCount <= 0)
        { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }

        float arc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, arcNormalized);
        ctx.CurrentPlacementArc = arc;
        GetFrame(ctx.Generator, arc, out var pos, out var tangent, out var right, out var up);

        float step = 360f / ctx.TotalSpawnCount;
        float angleRad = (ctx.CurrentSpawnIndex * step + angleOffsetDeg) * Mathf.Deg2Rad;
        Vector3 offset = (right * Mathf.Cos(angleRad) + up * Mathf.Sin(angleRad)) * ringRadius;

        worldPos = pos + offset;
        if (respectPlayerReach) worldPos = ClampToReach(pos, worldPos, right, up, ctx.Generator);
        worldRot = Quaternion.LookRotation(tangent, up);
        ctx.AvailableSpawnRadius = ringRadius;
        return true;
    }
}


[Serializable]
public class SplineGridLanesPlacement : SplineSpawnPlacement
{
    [Range(0f, 1f)] public float arcNormalized = 0.5f;
    [Min(1)] public int gridSize = 3;
    public float corridorWidth = 16f;
    public float corridorHeight = 10f;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null) { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }

        float arc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, arcNormalized);
        ctx.CurrentPlacementArc = arc;
        GetFrame(ctx.Generator, arc, out var pos, out var tangent, out var right, out var up);

        int xi = ctx.Rng.Next(gridSize);
        int yi = ctx.Rng.Next(gridSize);
        float cellW = corridorWidth / gridSize;
        float cellH = corridorHeight / gridSize;
        float dx = (xi * cellW) + (cellW * 0.5f) - corridorWidth * 0.5f;
        float dy = (yi * cellH) + (cellH * 0.5f) - corridorHeight * 0.5f;

        worldPos = pos + right * dx + up * dy;
        if (respectPlayerReach) worldPos = ClampToReach(pos, worldPos, right, up, ctx.Generator);
        worldRot = Quaternion.LookRotation(tangent, up);
        ctx.AvailableSpawnRadius = Mathf.Min(cellW, cellH) * 0.5f;
        return true;
    }
}


[Serializable]
public class SplineAvoidCenterPlacement : SplineSpawnPlacement
{
    [Range(0f, 0.95f)] public float safeInnerRadius = 4f;
    public float maxRadius = 14f;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null) { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }

        float zT = (float)ctx.Rng.NextDouble();
        float arc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, zT);
        ctx.CurrentPlacementArc = arc;
        GetFrame(ctx.Generator, arc, out var pos, out var tangent, out var right, out var up);

        float angle = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
        float r = Mathf.Lerp(safeInnerRadius, maxRadius, (float)ctx.Rng.NextDouble());
        Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r;

        worldPos = pos + offset;
        if (respectPlayerReach) worldPos = ClampToReach(pos, worldPos, right, up, ctx.Generator);
        worldRot = Quaternion.LookRotation(tangent, up);
        ctx.AvailableSpawnRadius = maxRadius - r;
        return true;
    }
}


[Serializable]
public class SplineClusterPlacement : SplineSpawnPlacement
{
    public float clusterRadius = 4f;
    public float lateralOffsetRange = 6f;
    public float verticalOffsetRange = 3f;

    private Vector3 _centerWorld;
    private float _centerArc;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null) { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }

        if (ctx.CurrentSpawnIndex == 0)
        {
            float zT = (float)ctx.Rng.NextDouble();
            _centerArc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, zT);
            GetFrame(ctx.Generator, _centerArc, out var basePos, out _, out var rightV, out var upV);
            float lx = ((float)ctx.Rng.NextDouble() * 2f - 1f) * lateralOffsetRange;
            float ly = ((float)ctx.Rng.NextDouble() * 2f - 1f) * verticalOffsetRange;
            _centerWorld = basePos + rightV * lx + upV * ly;
        }

        GetFrame(ctx.Generator, _centerArc, out var splineCenter, out var tangent, out var right, out var up);
        ctx.CurrentPlacementArc = _centerArc;
        float angle = (float)(ctx.Rng.NextDouble() * Mathf.PI * 2.0);
        float r = (float)ctx.Rng.NextDouble() * clusterRadius;
        Vector3 scatter = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r;

        worldPos = _centerWorld + scatter;
        if (respectPlayerReach) worldPos = ClampToReach(splineCenter, worldPos, right, up, ctx.Generator);
        worldRot = Quaternion.LookRotation(tangent, up);
        ctx.AvailableSpawnRadius = clusterRadius * 0.5f;
        return true;
    }
}


[Serializable]
public class SplineArcPatternPlacement : SplineSpawnPlacement
{
    [Tooltip("Wysokość łuku (peak w środku) w jednostkach świata. Ujemne = łuk nurkuje w dół.")]
    public float arcHeight = 3f;
    [Tooltip("Stały offset pionowy całego łuku. Ujemne = zsuwa łuk niżej. " +
             "Aby wycentrować łuk wokół splajnu (połowa nad, połowa pod), ustaw na -arcHeight/2.")]
    public float verticalOffset = 0f;
    [Tooltip("Offset boczny startu i końca łuku.")]
    public float lateralOffset = 0f;

    public override bool TryGetPlacement(SplineSpawnContext ctx, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (ctx.Generator == null || ctx.TotalSpawnCount <= 0)
        { worldPos = Vector3.zero; worldRot = Quaternion.identity; return false; }

        float u = ctx.TotalSpawnCount == 1 ? 0.5f : (float)ctx.CurrentSpawnIndex / (ctx.TotalSpawnCount - 1);
        float arc = Mathf.Lerp(ctx.SegmentStartArc, ctx.SegmentEndArc, u);
        ctx.CurrentPlacementArc = arc;
        GetFrame(ctx.Generator, arc, out var pos, out var tangent, out var right, out var up);


        float h = 4f * u * (1f - u) * arcHeight + verticalOffset;
        worldPos = pos + right * lateralOffset + up * h;
        if (respectPlayerReach) worldPos = ClampToReach(pos, worldPos, right, up, ctx.Generator);
        worldRot = Quaternion.LookRotation(tangent, up);
        ctx.AvailableSpawnRadius = ctx.Generator.CorridorRadius;
        return true;
    }
}
