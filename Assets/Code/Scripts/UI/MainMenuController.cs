using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    private const string MenuSkyDomeResourcePath = "SkyDome";

    [Header("Scenes")]
    [Tooltip("Scene loaded by 'Play' / 'Continue'.")]
    [SerializeField] private string gameSceneName = "MainGame";

    [Header("Behaviour")]
    [Tooltip("If true, ESC quits the game from the main menu (matches the footer hint).")]
    [SerializeField] private bool escapeQuits = true;
    [SerializeField] private string menuPlaneObjectName = "Dragonfly";

    public static MainMenuController Instance { get; private set; }

    private UIDocument _doc;
    private VisualElement _root;
    private VisualElement[] _items;
    private int _selectedIndex;
    private OptionsPanelController _options;
    private VisualElement _statisticsOverlay;
    private Label _statsEnemiesKilled, _statsDistance, _statsMaxLevel;
    private GameObject _menuSkyDome;
    private GameObject _menuPlane;

    private static readonly string[] Items =
    {
        "item-continue",
        "item-play",
        "item-statistics",
        "item-options",
        "item-quit",
    };

    private void Awake()
    {
        Instance = this;
        _doc = GetComponent<UIDocument>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void OnEnable()
    {
        _root = _doc.rootVisualElement;
        if (_root == null) return;

        EnsureMenuSkyDome();
        EnsureMenuPlaneIdleMotion();
        _items = new VisualElement[Items.Length];
        for (int i = 0; i < Items.Length; i++)
        {
            var element = _root.Q(Items[i]);
            _items[i] = element;
            if (element == null) continue;

            int captured = i;
            element.RegisterCallback<PointerEnterEvent>(_ => SetSelected(captured));
            element.RegisterCallback<ClickEvent>(_ => Activate(captured));
        }

        _options = new OptionsPanelController();
        _options.Bind(_root);
        CacheStatisticsPanel();
        RefreshMenuAvailability();
        SetSelected(FirstAvailableIndex());


        _root.style.display = DisplayStyle.Flex;

        GameStateManager gameStateManager = GameStateManager.GetOrCreate();
        if (gameStateManager != null)
        {
            gameStateManager.OnStateChanged -= HandleStateChanged;
            gameStateManager.OnStateChanged += HandleStateChanged;

            if (gameStateManager.CurrentState != GameState.PlaneSelection)
                gameStateManager.ChangeState(GameState.MainMenu);
            ApplyVisibility(gameStateManager.CurrentState);
        }
    }

    private void HandleStateChanged(GameState s) => ApplyVisibility(s);

    private void ApplyVisibility(GameState s)
    {
        if (_root == null) return;
        _root.style.display = s == GameState.MainMenu ? DisplayStyle.Flex : DisplayStyle.None;
        SetMenuSkyDomeVisible(s == GameState.MainMenu || s == GameState.PlaneSelection);
        SetMenuPlaneVisible(s == GameState.MainMenu);
    }

    private bool IsVisible() => _root != null && _root.style.display == DisplayStyle.Flex;

    private void EnsureMenuSkyDome()
    {
        if (_menuSkyDome != null) return;

        _menuSkyDome = GameObject.Find("SkyDome");
        if (_menuSkyDome != null)
        {
            ConfigureSkyDomeTransform(_menuSkyDome);
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(MenuSkyDomeResourcePath);
        if (prefab == null)
        {
            return;
        }

        _menuSkyDome = Instantiate(prefab);
        _menuSkyDome.name = "SkyDome";
        ConfigureSkyDomeTransform(_menuSkyDome);
    }

    private void SetMenuSkyDomeVisible(bool visible)
    {
        EnsureMenuSkyDome();
        if (_menuSkyDome != null)
            _menuSkyDome.SetActive(visible);
    }

    private static void ConfigureSkyDomeTransform(GameObject dome)
    {
        if (dome == null) return;

        foreach (var renderer in dome.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private void EnsureMenuPlaneIdleMotion()
    {
        EnsureMenuPlane();
        if (_menuPlane == null) return;

        if (_menuPlane.GetComponent<MenuPlaneIdleMotion>() == null)
            _menuPlane.AddComponent<MenuPlaneIdleMotion>();
    }

    private void EnsureMenuPlane()
    {
        if (_menuPlane != null || string.IsNullOrWhiteSpace(menuPlaneObjectName)) return;
        _menuPlane = GameObject.Find(menuPlaneObjectName);
    }

    private void SetMenuPlaneVisible(bool visible)
    {
        EnsureMenuPlane();
        if (_menuPlane != null)
            _menuPlane.SetActive(visible);
    }

    private void Update()
    {
        if (!IsVisible()) return;
        var kb = Keyboard.current;
        if (kb == null || _items == null) return;

        if (_options != null && _options.IsOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame) _options.Cancel();
            return;
        }

        if (IsStatisticsOpen())
        {
            if (kb.escapeKey.wasPressedThisFrame ||
                kb.enterKey.wasPressedThisFrame ||
                kb.numpadEnterKey.wasPressedThisFrame)
            {
                CloseStatistics();
            }
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame && escapeQuits)
        {
            Quit();
            return;
        }

        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
        {
            StepSelection(-1);
            return;
        }
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
        {
            StepSelection(1);
            return;
        }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            Activate(_selectedIndex);
            return;
        }
    }

    private void SetSelected(int index)
    {
        if (_items == null || _items.Length == 0) return;
        _selectedIndex = Mathf.Clamp(index, 0, _items.Length - 1);
        for (int i = 0; i < _items.Length; i++)
        {
            _items[i]?.EnableInClassList("is-selected", i == _selectedIndex);
        }
    }

    private void StepSelection(int dir)
    {
        if (_items == null || _items.Length == 0) return;

        int next = _selectedIndex;
        for (int i = 0; i < _items.Length; i++)
        {
            next = ((next + dir) % _items.Length + _items.Length) % _items.Length;
            if (IsItemAvailable(next))
            {
                SetSelected(next);
                return;
            }
        }
    }

    private void Activate(int index)
    {
        if (!IsItemAvailable(index)) return;

        switch (index)
        {
            case 0: Continue();    break;
            case 1: Play();        break;
            case 2: OpenStatistics(); break;
            case 3: OpenOptions(); break;
            case 4: Quit();        break;
        }
    }

    private void Play()
    {
        PlayerProgressManager.Instance?.ClearActiveRun();
        SetMenuPlaneVisible(false);

        GameStateManager.GetOrCreate().ChangeState(GameState.PlaneSelection);
    }

    private void Continue()
    {
        var progress = PlayerProgressManager.Instance;
        if (progress == null || !progress.HasActiveRun) return;

        var activeRun = progress.ActiveRun;
        GameStateManager gameStateManager = GameStateManager.GetOrCreate();
        gameStateManager.TrySelectPlane(activeRun.selectedPlaneId);
        gameStateManager.ChangeState(GameState.InGame);
        LoadGameScene(activeRun.sceneName);
    }

    private void OpenOptions()
    {
        _options?.Open();
    }

    private void CacheStatisticsPanel()
    {
        _statisticsOverlay = _root.Q<VisualElement>("statistics-overlay");
        _statsEnemiesKilled = _root.Q<Label>("stats-enemies-killed");
        _statsDistance = _root.Q<Label>("stats-distance");
        _statsMaxLevel = _root.Q<Label>("stats-max-level");

        _root.Q<Button>("statistics-close")?.RegisterCallback<ClickEvent>(_ => CloseStatistics());
        _root.Q<Button>("statistics-back")?.RegisterCallback<ClickEvent>(_ => CloseStatistics());
    }

    private void OpenStatistics()
    {
        RefreshStatistics();
        if (_statisticsOverlay != null)
            _statisticsOverlay.style.display = DisplayStyle.Flex;
    }

    private void CloseStatistics()
    {
        if (_statisticsOverlay != null)
            _statisticsOverlay.style.display = DisplayStyle.None;
    }

    private bool IsStatisticsOpen()
    {
        return _statisticsOverlay != null && _statisticsOverlay.style.display == DisplayStyle.Flex;
    }

    private void RefreshStatistics()
    {
        var progress = PlayerProgressManager.Instance;
        if (progress == null) return;

        if (_statsEnemiesKilled != null)
            _statsEnemiesKilled.text = progress.GetDisplayEnemiesKilled().ToString();
        if (_statsDistance != null)
            _statsDistance.text = FormatDistance(progress.GetDisplayDistanceMeters());
        if (_statsMaxLevel != null)
            _statsMaxLevel.text = progress.GetDisplayMaxLevelReached().ToString();
    }

    private void RefreshMenuAvailability()
    {
        bool canContinue = PlayerProgressManager.Instance != null && PlayerProgressManager.Instance.HasActiveRun;
        if (_items != null && _items.Length > 0 && _items[0] != null)
            _items[0].style.display = canContinue ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private bool IsItemAvailable(int index)
    {
        if (_items == null || index < 0 || index >= _items.Length) return false;
        var item = _items[index];
        return item != null && item.style.display != DisplayStyle.None;
    }

    private int FirstAvailableIndex()
    {
        if (_items == null) return 0;
        for (int i = 0; i < _items.Length; i++)
            if (IsItemAvailable(i)) return i;
        return 0;
    }

    private static string FormatDistance(float meters)
    {
        return meters < 1000f
            ? $"{Mathf.RoundToInt(meters)} m"
            : $"{meters / 1000f:0.0} km";
    }

    private void Quit()
    {
        SceneFlow.Quit();
    }

    private void LoadGameScene(string sceneOverride = null)
    {
        string sceneToLoad = string.IsNullOrEmpty(sceneOverride) ? gameSceneName : sceneOverride;
        SceneFlow.LoadScene(sceneToLoad);
    }
}
