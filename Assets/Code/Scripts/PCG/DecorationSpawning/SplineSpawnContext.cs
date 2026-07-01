using UnityEngine;

public class SplineSpawnContext
{
    public SplineSegment Segment;
    public SplineGenerator Generator;
    public int SegmentIndex;
    public int Seed;
    public System.Random Rng;
    public float SegmentLength;
    public float SegmentStartArc;
    public float SegmentEndArc;
    public int CurrentSpawnIndex;
    public int TotalSpawnCount;
    public float CurrentPlacementArc;
    public float AvailableSpawnRadius;
}
