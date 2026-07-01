using UnityEngine;

public abstract class SpawnSettings : ScriptableObject
{
    public abstract void ApplySetup(GameObject spawnedObject);
}