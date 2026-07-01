using UnityEngine;

[CreateAssetMenu(fileName = "Rotator", menuName = "Obstacles/Laser/Rotator")]
public class RotatorSettings : SpawnSettings
{
    [Header("Editor Notes")]
    [TextArea(3, 5)] 
    public string description;
    [Space(10)]

    public RotationAxis axis = RotationAxis.LocalZ; 
    public float speed = 90f;

    public override void ApplySetup(GameObject spawnedObject)
    {
        LaserRotator obstacleScript = spawnedObject.GetComponentInChildren<LaserRotator>();
    
    if (obstacleScript != null)
    {
        obstacleScript.Initialize(this.axis, this.speed);
    }
    }
}