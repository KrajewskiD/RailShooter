using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-200)]
public class PlayerProgressManager : MonoBehaviour
{
    private const string SaveFileName = "player-progress.json";

    public static PlayerProgressManager Instance { get; private set; }

    [SerializeField] private PlayerSaveData data = new PlayerSaveData();

    public PlayerSaveData Data => data;
    public bool HasActiveRun => data != null && data.hasActiveRun && data.activeRun != null;
    public RunSaveData ActiveRun => HasActiveRun ? data.activeRun : null;

    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject(nameof(PlayerProgressManager));
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerProgressManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    private void OnApplicationQuit()
    {
        SaveActiveRun();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveActiveRun();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                data = JsonUtility.FromJson<PlayerSaveData>(json) ?? new PlayerSaveData();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to load player progress from {SavePath}: {ex.Message}");
            data = new PlayerSaveData();
        }

        EnsureData();
    }

    public void Save()
    {
        EnsureData();

        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to save player progress to {SavePath}: {ex.Message}");
        }
    }

    public void StartNewRun(int selectedPlaneId, string sceneName)
    {
        EnsureData();
        data.hasActiveRun = true;
        data.activeRun = new RunSaveData
        {
            selectedPlaneId = selectedPlaneId,
            sceneName = string.IsNullOrEmpty(sceneName) ? SceneManager.GetActiveScene().name : sceneName,
            runMaxLevel = 1
        };
        Save();
    }

    public void SaveActiveRun()
    {
        if (!HasActiveRun) return;
        Save();
    }

    public void ClearActiveRun(bool save = true)
    {
        EnsureData();
        data.hasActiveRun = false;
        data.activeRun = new RunSaveData();
        if (save) Save();
    }

    public void CommitActiveRunAndClear()
    {
        if (!HasActiveRun)
        {
            Save();
            return;
        }

        var run = data.activeRun;
        data.totalEnemiesKilled += Mathf.Max(0, run.runEnemiesKilled);
        data.totalDistanceMeters += Mathf.Max(0f, run.runDistanceMeters);
        data.maxLevelReached = Mathf.Max(data.maxLevelReached, run.runMaxLevel);
        data.completedRuns.Add(new RunRecord
        {
            enemiesKilled = Mathf.Max(0, run.runEnemiesKilled),
            distanceMeters = Mathf.Max(0f, run.runDistanceMeters),
            maxLevel = Mathf.Max(1, run.runMaxLevel)
        });

        ClearActiveRun(false);
        Save();
    }

    public void CompleteTutorial()
    {
        EnsureData();
        data.tutorialCompleted = true;
        Save();
    }

    public void RegisterEnemyKilled()
    {
        EnsureActiveRun();
        data.activeRun.runEnemiesKilled++;
    }

    public void RegisterLevelReached(int level)
    {
        EnsureActiveRun();
        data.activeRun.runMaxLevel = Mathf.Max(data.activeRun.runMaxLevel, level);
    }

    public void SetRunDistanceMeters(float meters)
    {
        EnsureActiveRun();
        data.activeRun.runDistanceMeters = Mathf.Max(data.activeRun.runDistanceMeters, meters);
    }

    public bool EvaluatePlaneUnlock(int planeId, PlaneData plane)
    {
        EnsureData();
        if (plane == null) return IsPlaneUnlocked(planeId);

        if (plane.unlockedByDefault || MeetsUnlockCondition(plane))
            UnlockPlane(planeId);

        return IsPlaneUnlocked(planeId);
    }

    public bool IsPlaneUnlocked(int planeId)
    {
        EnsureData();
        return data.unlockedPlaneIds.Contains(planeId);
    }

    public bool IsPlaneUnlocked(int planeId, PlaneData plane)
    {
        if (plane != null && plane.unlockedByDefault) return true;
        return IsPlaneUnlocked(planeId);
    }

    public int GetDisplayEnemiesKilled()
    {
        return data.totalEnemiesKilled + (HasActiveRun ? data.activeRun.runEnemiesKilled : 0);
    }

    public float GetDisplayDistanceMeters()
    {
        return data.totalDistanceMeters + (HasActiveRun ? data.activeRun.runDistanceMeters : 0f);
    }

    public int GetDisplayMaxLevelReached()
    {
        return Mathf.Max(data.maxLevelReached, HasActiveRun ? data.activeRun.runMaxLevel : 1);
    }

    private void UnlockPlane(int planeId)
    {
        if (planeId < 0 || data.unlockedPlaneIds.Contains(planeId)) return;
        data.unlockedPlaneIds.Add(planeId);
        Save();
    }

    private bool MeetsUnlockCondition(PlaneData plane)
    {
        switch (plane.unlockCondition)
        {
            case UnlockConditionType.CompleteTutorial:
                return data.tutorialCompleted;

            case UnlockConditionType.KillEnemiesInRun:
                return HasRunMeeting(plane.requiredKills, 0f);

            case UnlockConditionType.KillEnemiesAndFlyDistanceInRun:
                return HasRunMeeting(plane.requiredKills, plane.requiredDistanceMeters);

            case UnlockConditionType.ReachMaxLevel:
                return GetDisplayMaxLevelReached() >= plane.requiredLevel;

            default:
                return false;
        }
    }

    private bool HasRunMeeting(int kills, float distanceMeters)
    {
        int requiredKills = Mathf.Max(0, kills);
        float requiredDistance = Mathf.Max(0f, distanceMeters);

        if (HasActiveRun &&
            data.activeRun.runEnemiesKilled >= requiredKills &&
            data.activeRun.runDistanceMeters >= requiredDistance)
        {
            return true;
        }

        for (int i = 0; i < data.completedRuns.Count; i++)
        {
            var run = data.completedRuns[i];
            if (run.enemiesKilled >= requiredKills && run.distanceMeters >= requiredDistance)
                return true;
        }

        return false;
    }

    private void EnsureActiveRun()
    {
        EnsureData();
        if (HasActiveRun) return;

        int selectedPlaneId = GameStateManager.ChosenPlaneID >= 0 ? GameStateManager.ChosenPlaneID : 0;
        data.hasActiveRun = true;
        data.activeRun = new RunSaveData
        {
            selectedPlaneId = selectedPlaneId,
            sceneName = SceneManager.GetActiveScene().name,
            runMaxLevel = 1
        };
    }

    private void EnsureData()
    {
        if (data == null) data = new PlayerSaveData();
        if (data.unlockedPlaneIds == null) data.unlockedPlaneIds = new List<int>();
        if (data.completedRuns == null) data.completedRuns = new List<RunRecord>();
        if (data.activeRun == null) data.activeRun = new RunSaveData();
        if (data.maxLevelReached < 1) data.maxLevelReached = 1;
        if (data.activeRun.runMaxLevel < 1) data.activeRun.runMaxLevel = 1;
    }
}

[Serializable]
public class PlayerSaveData
{
    public int saveVersion = 1;
    public bool tutorialCompleted;
    public int totalEnemiesKilled;
    public float totalDistanceMeters;
    public int maxLevelReached = 1;
    public List<int> unlockedPlaneIds = new List<int>();
    public List<RunRecord> completedRuns = new List<RunRecord>();
    public bool hasActiveRun;
    public RunSaveData activeRun = new RunSaveData();
}

[Serializable]
public class RunSaveData
{
    public int selectedPlaneId = -1;
    public string sceneName = "MainGame";
    public int runEnemiesKilled;
    public float runDistanceMeters;
    public int runMaxLevel = 1;
}

[Serializable]
public class RunRecord
{
    public int enemiesKilled;
    public float distanceMeters;
    public int maxLevel;
}
