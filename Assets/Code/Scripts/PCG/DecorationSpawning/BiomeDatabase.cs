using System.Collections.Generic;
using UnityEngine;

public class BiomeDatabase : MonoBehaviour
{
    public static BiomeDatabase Instance { get; private set; }
    public List<TerrainDecorationData> allDecorations = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    public static BiomeZone GetZone(float temperature, float moisture)
    {
        int tempIdx  = temperature < 0.25f ? 0 : temperature < 0.75f ? 1 : 2;
        int moistIdx = moisture    < 0.25f ? 0 : moisture    < 0.75f ? 1 : 2;
        return (BiomeZone)(tempIdx * 3 + moistIdx);
    }

    public static void GetZoneBounds(BiomeZone zone,
        out float minTemperature, out float maxTemperature,
        out float minMoisture, out float maxMoisture)
    {
        int idx = (int)zone;
        int tempIdx  = idx / 3;
        int moistIdx = idx % 3;

        minTemperature = tempIdx == 0 ? 0f    : tempIdx == 1 ? 0.25f : 0.75f;
        maxTemperature = tempIdx == 0 ? 0.25f : tempIdx == 1 ? 0.75f : 1f;
        minMoisture    = moistIdx == 0 ? 0f    : moistIdx == 1 ? 0.25f : 0.75f;
        maxMoisture    = moistIdx == 0 ? 0.25f : moistIdx == 1 ? 0.75f : 1f;
    }
}
