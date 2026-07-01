using UnityEngine;
using NaughtyAttributes;

[CreateAssetMenu(fileName = "LaserGrid", menuName = "Obstacles/Laser/Laser Grid")]
public class LaserGridSettings : SpawnSettings
{
    [Header("Editor Notes")]
    [TextArea(3, 5)]
    public string description;

    [Header("--- Layout Configuration ---")]
    public int laserCount = 6;
    public float sideMargin = 0f;
    public float railsOffsetY = 20f;
    public bool isHorizontal = false;
    public LaserDirection directionMode = LaserDirection.BottomToTop;

    [Header("--- Visual Behavior (Blinking) ---")]
    public bool alwaysLight = false;

    [HideIf("alwaysLight")]
    [AllowNesting]
    public bool isBlinking = true;

    [ShowIf("ShouldShowBlinkingDetails")]
    [AllowNesting]
    public bool invertWave = false;

    [ShowIf("ShouldShowBlinkingDetails")]
    [AllowNesting]
    public float cycleSpeed = 2.0f;

    [ShowIf("ShouldShowBlinkingDetails")]
    [AllowNesting]
    public float pauseDuration = 0.5f;

    [Header("--- Sequence & Waves ---")]
    [HideIf("alwaysLight")]
    [AllowNesting]
    public WaveStyle waveStyle = WaveStyle.OutsideIn;

    [ShowIf("ShouldShowLasersPerWave")]
    [AllowNesting]
    public int lasersPerWave = 2;

    [HideIf("alwaysLight")]
    [AllowNesting]
    public float waveDelay = 1.0f;

    [ShowIf("IsAlternateDirection")]
    [AllowNesting]
    public int sequenceLength = 2;

    [Header("--- Animation & Motion ---")]
    public bool isMovement = true;

    [ShowIf("isMovement")]
    [AllowNesting]
    public float moveSpeed = 1.0f;

    [ShowIf("isMovement")]
    [AllowNesting]
    [Range(0f, 1f)] public float movementFreedom = 0.8f;

    [Space(5)]
    public bool isRoll = false;

    [ShowIf("isRoll")]
    [AllowNesting]
    public float rollSpeed = 90f;

    private bool ShouldShowBlinkingDetails()
    {
        return !alwaysLight && isBlinking;
    }

    private bool ShouldShowLasersPerWave()
    {
        return !alwaysLight && waveStyle == WaveStyle.Linear;
    }

    private bool IsAlternateDirection()
    {
        return directionMode == LaserDirection.Alternate;
    }

    public override void ApplySetup(GameObject spawnedObject)
    {
        LaserSpawner spawner = spawnedObject.GetComponentInChildren<LaserSpawner>();
        if (spawner != null)
        {
            spawner.settings = this;
            spawner.Initialize();
        }
    }
}
