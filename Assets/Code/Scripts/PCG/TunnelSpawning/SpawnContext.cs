using UnityEngine;

public class SpawnContext
{
    public TunnelSegment Segment;
    public TunnelGenerator Generator;
    public int SegmentIndex;
    public int Seed;
    public System.Random Rng;
    public float SegmentLength;
    public float SegmentStartZ;
    public float SegmentEndZ;
    public int CurrentSpawnIndex;
    public int TotalSpawnCount;
    public float AvailableRadius;
}
