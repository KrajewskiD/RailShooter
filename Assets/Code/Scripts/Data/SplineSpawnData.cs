using UnityEngine;
using NaughtyAttributes;

public enum SplineConditionType { None, Chance, EveryNthSegment, MinSegmentIndex, SpecificSegment, ProgressiveChance }
public enum SplinePlacementType { None, CenterAxis, LateralOffset, CorridorRing, GridLanes, AvoidCenter, Cluster, ArcPattern }

[CreateAssetMenu(fileName = "NewSplineSpawnEntry", menuName = "RogueLike/Spline/Spawn Entry")]
public class SplineSpawnData : ScriptableObject
{
    [Header("Editor Notes")]
    [TextArea(3, 5)] public string description;
    [Space(10)]

    [Header("What")]
    public SpawnablePrefab[] prefabPool;
    [Min(1)] public int countPerSegment = 1;

    [Header("When")]
    [OnValueChanged("GenerateCondition")]
    public SplineConditionType conditionType = SplineConditionType.None;

    [SerializeReference]
    public SplineSpawnCondition condition;

    [Header("Where")]
    [OnValueChanged("GeneratePlacement")]
    public SplinePlacementType placementType = SplinePlacementType.None;

    [SerializeReference]
    public SplineSpawnPlacement placement;

    [Header("Hierarchy")]
    public string containerName = "Spawned";

    private void GenerateCondition()
    {
        switch (conditionType)
        {
            case SplineConditionType.None:              condition = new SplineNoneCondition(); break;
            case SplineConditionType.Chance:            condition = new SplineChanceCondition(); break;
            case SplineConditionType.EveryNthSegment:   condition = new SplineEveryNthSegmentCondition(); break;
            case SplineConditionType.MinSegmentIndex:   condition = new SplineMinSegmentIndexCondition(); break;
            case SplineConditionType.SpecificSegment:   condition = new SplineSpecificSegmentIndexCondition(); break;
            case SplineConditionType.ProgressiveChance: condition = new SplineProgressiveChanceCondition(); break;
        }
    }

    private void GeneratePlacement()
    {
        switch (placementType)
        {
            case SplinePlacementType.None:           placement = null; break;
            case SplinePlacementType.CenterAxis:     placement = new SplineCenterAxisPlacement(); break;
            case SplinePlacementType.LateralOffset:  placement = new SplineLateralOffsetPlacement(); break;
            case SplinePlacementType.CorridorRing:   placement = new SplineCorridorRingPlacement(); break;
            case SplinePlacementType.GridLanes:      placement = new SplineGridLanesPlacement(); break;
            case SplinePlacementType.AvoidCenter:    placement = new SplineAvoidCenterPlacement(); break;
            case SplinePlacementType.Cluster:        placement = new SplineClusterPlacement(); break;
            case SplinePlacementType.ArcPattern:     placement = new SplineArcPatternPlacement(); break;
        }
    }

    private void OnEnable()
    {
        if (condition == null || condition is SplineNoneCondition) conditionType = SplineConditionType.None;
        else if (condition is SplineChanceCondition)               conditionType = SplineConditionType.Chance;
        else if (condition is SplineEveryNthSegmentCondition)      conditionType = SplineConditionType.EveryNthSegment;
        else if (condition is SplineMinSegmentIndexCondition)      conditionType = SplineConditionType.MinSegmentIndex;
        else if (condition is SplineSpecificSegmentIndexCondition) conditionType = SplineConditionType.SpecificSegment;
        else if (condition is SplineProgressiveChanceCondition)    conditionType = SplineConditionType.ProgressiveChance;

        if (placement == null)                                     placementType = SplinePlacementType.None;
        else if (placement is SplineCenterAxisPlacement)           placementType = SplinePlacementType.CenterAxis;
        else if (placement is SplineLateralOffsetPlacement)        placementType = SplinePlacementType.LateralOffset;
        else if (placement is SplineCorridorRingPlacement)         placementType = SplinePlacementType.CorridorRing;
        else if (placement is SplineGridLanesPlacement)            placementType = SplinePlacementType.GridLanes;
        else if (placement is SplineAvoidCenterPlacement)          placementType = SplinePlacementType.AvoidCenter;
        else if (placement is SplineClusterPlacement)              placementType = SplinePlacementType.Cluster;
        else if (placement is SplineArcPatternPlacement)           placementType = SplinePlacementType.ArcPattern;
    }
}
