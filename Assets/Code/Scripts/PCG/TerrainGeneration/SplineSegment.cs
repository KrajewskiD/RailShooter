using UnityEngine;
public class SplineSegment : MonoBehaviour
{
    public int SegmentIndex { get; set; }
    public float StartArc { get; set; }
    public float EndArc { get; set; }
    public float SegmentArcLength => EndArc - StartArc;
}
