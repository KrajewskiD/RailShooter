using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(-100)]
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }
    
    public static int ChosenPlaneID = -1;
    public PlaneData ChosenPlaneData { get; private set; }
    public static event Action<int> OnPlaneSelected;
    private readonly Dictionary<int, PlaneData> _planeCatalog = new Dictionary<int, PlaneData>();

    public GameState CurrentState { get; private set; } = GameState.InGame;
    public event Action<GameState> OnStateChanged;

    private float _cachedTimeScale = -1f;

    public static GameStateManager GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

        var go = new GameObject(nameof(GameStateManager));
        return go.AddComponent<GameStateManager>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        GetOrCreate();
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (Instance != this) return;
        ApplyStateEffects(CurrentState);
    }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState)
        {
            return;
        }

        CurrentState = newState;
        ApplyStateEffects(newState);
        OnStateChanged?.Invoke(newState);
    }

    private void ApplyStateEffects(GameState state)
    {
        switch (state)
        {
            case GameState.MainMenu:
            case GameState.PlaneSelection:
                SetTimeScale(1f);
                UpdateCursor(true, CursorLockMode.None);
                break;

            case GameState.InGame:
                SetTimeScale(1f);
                UpdateCursor(false, CursorLockMode.Locked);
                break;

            case GameState.ProjectileSelection:
            case GameState.UpgradeSelection:
            case GameState.Pause:
            case GameState.GameOver:
                SetTimeScale(0f);
                UpdateCursor(true, CursorLockMode.None);
                break;
        }
    }

    private void SetTimeScale(float scale)
    {
        if (Mathf.Approximately(_cachedTimeScale, scale)) return;
        Time.timeScale = scale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        _cachedTimeScale = scale;
    }

    private void UpdateCursor(bool visible, CursorLockMode lockState)
    {
        if (Cursor.visible != visible)
        {
            Cursor.visible = visible;
        }

        if (Cursor.lockState != lockState)
        {
            Cursor.lockState = lockState;
        }
    }

    public void SelectPlane(PlaneData planeData, int id)
    {
        RegisterPlane(id, planeData);
        ChosenPlaneData = planeData;
        ChosenPlaneID = id;
        OnPlaneSelected?.Invoke(id);
    }

    public void RegisterPlane(int id, PlaneData planeData)
    {
        if (id < 0 || planeData == null) return;
        _planeCatalog[id] = planeData;
    }

    public bool TrySelectPlane(int id)
    {
        if (_planeCatalog.TryGetValue(id, out PlaneData planeData) && planeData != null)
        {
            SelectPlane(planeData, id);
            return true;
        }

        ChosenPlaneID = id;
        OnPlaneSelected?.Invoke(id);
        return false;
    }
}
