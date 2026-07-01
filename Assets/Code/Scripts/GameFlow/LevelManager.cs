using UnityEngine;
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Level Settings")]
    public string levelSeed;
    public float baseSpeed = 10f;
    public float boostMultiplier = 1.5f;
    public float speedIncreasePerSecond = 0.5f;
    public float maxCapSpeed = 50f;
    
    private float _elapsedLevelTime;

    public float CurrentTargetSpeed { get; private set; } 

    private void Awake()
    {
        Instance = this;
        CurrentTargetSpeed = baseSpeed;
        Random.InitState((levelSeed ?? string.Empty).GetHashCode());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        _elapsedLevelTime += Time.deltaTime;

        float calculatedSpeed = baseSpeed + (_elapsedLevelTime * speedIncreasePerSecond);
        CurrentTargetSpeed = Mathf.Min(calculatedSpeed, maxCapSpeed);
    }
}
