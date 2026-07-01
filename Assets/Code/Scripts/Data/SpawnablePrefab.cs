using UnityEngine;

[System.Serializable]
public class SpawnablePrefab
{
    public GameObject prefab;
    public Vector3 scale = Vector3.one;
    public SpawnSettings settings;
}
