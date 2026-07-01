using UnityEngine;
using System;

public class ProgressionManager : MonoBehaviour
{
    public static ProgressionManager Instance;

    [Header("Settings")]
    [SerializeField] private float baseXPToLevelUp = 100f;
    [SerializeField] private float xpGrowthMultiplier = 1.28f;
    [SerializeField] private float flatXPIncreasePerLevel = 20f;
    [SerializeField] private float maxXPToLevelUp = 0f;

    [Header("Runtime")]
    public float currentXP = 0f;
    public float xpToLevelUp = 100f;
    public int currentLevel = 1;

    public static event Action<float, float> OnXPChanged;
    public static event Action<int> OnLevelUp;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (PlayerProgressManager.Instance != null && PlayerProgressManager.Instance.HasActiveRun)
        {
            currentLevel = Mathf.Max(currentLevel, PlayerProgressManager.Instance.ActiveRun.runMaxLevel);
        }

        xpToLevelUp = CalculateXPToLevelUp(currentLevel);
        PlayerProgressManager.Instance?.RegisterLevelReached(currentLevel);
        OnXPChanged?.Invoke(currentXP, xpToLevelUp);
    }

    public void AddXP(float amount)
    {
        if (amount <= 0f) return;
        currentXP += amount;

        while (currentXP >= xpToLevelUp)
        {
            currentXP -= xpToLevelUp;
            LevelUp();
        }

        OnXPChanged?.Invoke(currentXP, xpToLevelUp);
    }

    private void LevelUp()
    {
        currentLevel++;
        xpToLevelUp = CalculateXPToLevelUp(currentLevel);

        PlayerProgressManager.Instance?.RegisterLevelReached(currentLevel);
        OnLevelUp?.Invoke(currentLevel);
    }

    private float CalculateXPToLevelUp(int level)
    {
        int levelIndex = Mathf.Max(0, level - 1);
        float scaledXP = Mathf.Max(1f, baseXPToLevelUp) * Mathf.Pow(Mathf.Max(1f, xpGrowthMultiplier), levelIndex);
        float flatXP = Mathf.Max(0f, flatXPIncreasePerLevel) * levelIndex;
        float result = scaledXP + flatXP;

        if (maxXPToLevelUp > 0f)
            result = Mathf.Min(result, maxXPToLevelUp);

        return Mathf.Max(1f, Mathf.Round(result));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        baseXPToLevelUp = Mathf.Max(1f, baseXPToLevelUp);
        xpGrowthMultiplier = Mathf.Max(1f, xpGrowthMultiplier);
        flatXPIncreasePerLevel = Mathf.Max(0f, flatXPIncreasePerLevel);
        maxXPToLevelUp = Mathf.Max(0f, maxXPToLevelUp);
        currentLevel = Mathf.Max(1, currentLevel);
        currentXP = Mathf.Max(0f, currentXP);
        xpToLevelUp = CalculateXPToLevelUp(currentLevel);
    }
#endif
}
