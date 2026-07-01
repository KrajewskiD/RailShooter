using UnityEngine;
using NaughtyAttributes;

public enum ConditionType { None, Chance, EveryNthSegment, MinSegmentIndex, SpecificSegment, ProgressiveChance }
public enum PlacementType { None, CenterAxis, RandomInsideTunnel, PerimeterRing, AvoidCenter, GridLanes, Cluster }

[CreateAssetMenu(fileName = "NewTunnelSpawnEntry", menuName = "RogueLike/Tunnel/Spawn Entry")]
public class TunnelSpawnData : ScriptableObject
{
    [Header("Editor Notes")]
    [TextArea(3, 5)]
    public string description;
    [Space(10)]

    [Header("What")]
    public SpawnablePrefab[] prefabPool;
    [Min(1)] public int countPerSegment = 1;

    [Header("When")]
    [Tooltip("Choose the condition type from the list.")]
    [OnValueChanged("GenerateCondition")]
    public ConditionType conditionType = ConditionType.None;

    [SerializeReference]
    public TunnelSpawnCondition condition;

    [Header("Where")]
    [Tooltip("Choose the placement type from the list.")]
    [OnValueChanged("GeneratePlacement")]
    public PlacementType placementType = PlacementType.None;

    [SerializeReference]
    public TunnelSpawnPlacement placement;

    [Header("Hierarchy")]
    public string containerName = "Spawned";

    private void GenerateCondition()
    {
        switch (conditionType)
        {
            case ConditionType.None: condition = new NoneCondition(); break;
            case ConditionType.Chance: condition = new ChanceCondition(); break;
            case ConditionType.EveryNthSegment: condition = new EveryNthSegmentCondition(); break;
            case ConditionType.MinSegmentIndex: condition = new MinSegmentIndexCondition(); break;
            case ConditionType.SpecificSegment: condition = new SpecificSegmentIndexCondition(); break;
            case ConditionType.ProgressiveChance: condition = new ProgressiveChanceCondition(); break;
        }
    }

    private void GeneratePlacement()
    {
        switch (placementType)
        {
            case PlacementType.None: placement = null; break;
            case PlacementType.CenterAxis: placement = new CenterAxisPlacement(); break;
            case PlacementType.RandomInsideTunnel: placement = new RandomInsideTunnelPlacement(); break;
            case PlacementType.PerimeterRing: placement = new PerimeterRingPlacement(); break;
            case PlacementType.AvoidCenter: placement = new AvoidCenterPlacement(); break;
            case PlacementType.GridLanes: placement = new GridLanesPlacement(); break;
            case PlacementType.Cluster: placement = new ClusterPlacement(); break;
        }
    }

    private void OnEnable()
    {
        if (condition == null || condition is NoneCondition) conditionType = ConditionType.None;
        else if (condition is ChanceCondition) conditionType = ConditionType.Chance;
        else if (condition is EveryNthSegmentCondition) conditionType = ConditionType.EveryNthSegment;
        else if (condition is MinSegmentIndexCondition) conditionType = ConditionType.MinSegmentIndex;
        else if (condition is SpecificSegmentIndexCondition) conditionType = ConditionType.SpecificSegment;
        else if (condition is ProgressiveChanceCondition) conditionType = ConditionType.ProgressiveChance;

        if (placement == null) placementType = PlacementType.None;
        else if (placement is CenterAxisPlacement) placementType = PlacementType.CenterAxis;
        else if (placement is RandomInsideTunnelPlacement) placementType = PlacementType.RandomInsideTunnel;
        else if (placement is AvoidCenterPlacement) placementType = PlacementType.AvoidCenter;
        else if (placement is GridLanesPlacement) placementType = PlacementType.GridLanes;
        else if (placement is ClusterPlacement) placementType = PlacementType.Cluster;
    }
}
