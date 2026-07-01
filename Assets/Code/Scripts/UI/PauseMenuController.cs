using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

[RequireComponent(typeof(UIDocument))]
public class PauseMenuController : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("If true, the controller toggles itself on ESC. Disable if you route ESC through PlayerInput's OnMenu action and call Toggle() yourself.")]
    [SerializeField] private bool autoHandleEscapeKey = true;
    [Tooltip("Show the menu immediately when this object is enabled.")]
    [SerializeField] private bool openOnEnable = false;

    [Header("Scenes")]
    [Tooltip("Scene to load on 'Restart'. Leave empty to reload the current scene.")]
    [FormerlySerializedAs("newGameSceneName")]
    [SerializeField] private string restartSceneName = "";
    [Tooltip("Scene to load on 'Return to Menu'.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    public bool IsOpen { get; private set; }
    public static PauseMenuController Instance { get; private set; }

    private UIDocument _doc;
    private VisualElement _root;
    private VisualElement[] _items;
    private Label _menuTagLabel;
    private Label _menuTitleLabel;
    private Label _menuSubtitleLabel;
    private int _selectedIndex;
    private OptionsPanelController _options;
    private int _lastMenuInputFrame = -1;
    private bool _isGameOverMode;

    private static readonly string[] Items =
    {
        "item-continue",
        "item-restart",
        "item-options",
        "item-return-to-menu",
        "item-exit",
    };

    private void Awake()
    {
        Instance = this;
        _doc = GetComponent<UIDocument>();
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        _root = _doc.rootVisualElement;
        if (_root == null) return;

        _menuTagLabel = _root.Q<Label>("menu-tag-label");
        _menuTitleLabel = _root.Q<Label>("menu-title-label");
        _menuSubtitleLabel = _root.Q<Label>("menu-subtitle-label");

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

        SetMenuPresentation(GameState.Pause);
        SetSelected(0);

        _options = new OptionsPanelController();
        _options.Bind(_root);

        SetVisible(openOnEnable);
        IsOpen = openOnEnable;
        if (IsOpen) ApplyPausedState();

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
            GameStateManager.Instance.OnStateChanged += HandleStateChanged;
            SyncWithState(GameStateManager.Instance.CurrentState);
        }
    }

    private void OnDisable()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (!IsOpen)
        {
            if (autoHandleEscapeKey && kb.escapeKey.wasPressedThisFrame && TryClaimMenuInputFrame() && CanOpen())
                Open();
            return;
        }

        if (_options != null && _options.IsOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame && TryClaimMenuInputFrame()) _options.Cancel();
            return;
        }

        if (!_isGameOverMode && kb.escapeKey.wasPressedThisFrame && TryClaimMenuInputFrame())
        {
            Close();
            return;
        }

        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
        {
            SetSelected(NextSelectableIndex(-1));
            return;
        }
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
        {
            SetSelected(NextSelectableIndex(1));
            return;
        }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            Activate(_selectedIndex);
            return;
        }
    }

    public void Toggle()
    {
        if (_isGameOverMode) return;

        if (IsOpen) Close();
        else if (CanOpen() || GameStateManager.Instance?.CurrentState == GameState.Pause) Open();
    }

    public bool HandleMenuInput()
    {
        if (!TryClaimMenuInputFrame()) return true;

        if (_options != null && _options.IsOpen)
        {
            _options.Cancel();
            return true;
        }

        if (_isGameOverMode) return true;

        Toggle();
        return true;
    }

    public void Open()
    {
        IsOpen = true;
        SetMenuPresentation(GameState.Pause);
        SetVisible(true);
        SetSelected(0);
        ApplyPausedState();
    }

    public void Close()
    {
        if (_isGameOverMode) return;

        IsOpen = false;
        SetVisible(false);
        ApplyResumedState();
    }

    private bool CanOpen()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return true;
        return gsm.CurrentState == GameState.InGame;
    }

    private void ApplyPausedState()
    {
        GameStateManager.Instance?.ChangeState(GameState.Pause);
    }

    private void ApplyResumedState()
    {
        GameStateManager.Instance?.ChangeState(GameState.InGame);
    }

    private void SetVisible(bool visible)
    {
        if (_root == null) return;
        _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private bool TryClaimMenuInputFrame()
    {
        if (_lastMenuInputFrame == Time.frameCount) return false;
        _lastMenuInputFrame = Time.frameCount;
        return true;
    }

    private void HandleStateChanged(GameState state)
    {
        SyncWithState(state);
    }

    private void SyncWithState(GameState state)
    {
        if (state == GameState.Pause)
        {
            SetOpenVisualState(true, state);
            return;
        }

        if (state == GameState.GameOver)
        {
            SetOpenVisualState(true, state);
            return;
        }

        if (IsOpen)
            SetOpenVisualState(false, state);
    }

    private void SetOpenVisualState(bool open, GameState state)
    {
        IsOpen = open;
        SetVisible(open);
        if (!open)
        {
            _isGameOverMode = false;
            return;
        }

        SetMenuPresentation(state);
        SetSelected(_isGameOverMode ? 1 : 0);
    }

    private void SetMenuPresentation(GameState state)
    {
        _isGameOverMode = state == GameState.GameOver;

        if (_menuTagLabel != null)
            _menuTagLabel.text = _isGameOverMode ? "RUN ENDED" : "MENU";
        if (_menuTitleLabel != null)
            _menuTitleLabel.text = _isGameOverMode ? "Game Over" : "Paused";
        if (_menuSubtitleLabel != null)
            _menuSubtitleLabel.text = _isGameOverMode ? "CHOOSE HOW TO CONTINUE" : "PICK AN OPTION";

        if (_items != null && _items.Length > 0 && _items[0] != null)
            _items[0].style.display = _isGameOverMode ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void SetSelected(int index)
    {
        if (_items == null || _items.Length == 0) return;
        _selectedIndex = Mathf.Clamp(index, 0, _items.Length - 1);
        if (!IsSelectable(_selectedIndex))
            _selectedIndex = NextSelectableIndex(1);

        for (int i = 0; i < _items.Length; i++)
        {
            _items[i]?.EnableInClassList("is-selected", i == _selectedIndex);
        }
    }

    private int NextSelectableIndex(int direction)
    {
        if (_items == null || _items.Length == 0) return 0;

        int step = direction < 0 ? -1 : 1;
        int index = Mathf.Clamp(_selectedIndex, 0, _items.Length - 1);

        for (int i = 0; i < _items.Length; i++)
        {
            index = (index + step + _items.Length) % _items.Length;
            if (IsSelectable(index)) return index;
        }

        return Mathf.Clamp(_selectedIndex, 0, _items.Length - 1);
    }

    private bool IsSelectable(int index)
    {
        return _items != null
            && index >= 0
            && index < _items.Length
            && _items[index] != null
            && _items[index].style.display != DisplayStyle.None;
    }

    private void Activate(int index)
    {
        if (!IsSelectable(index)) return;

        switch (index)
        {
            case 0:
                if (!_isGameOverMode) Close();
                break;
            case 1: RestartRun();    break;
            case 2: OpenOptions();   break;
            case 3: ReturnToMenu();  break;
            case 4: ExitGame();      break;
        }
    }

    private void RestartRun()
    {
        var scene = string.IsNullOrEmpty(restartSceneName)
            ? SceneManager.GetActiveScene().name
            : restartSceneName;
        SceneFlow.StartNewRunAndLoad(GameStateManager.ChosenPlaneID, scene);
    }

    private void ReturnToMenu()
    {
        SceneFlow.SaveActiveRunAndLoadMenu(mainMenuSceneName);
    }

    private void OpenOptions()
    {
        _options?.Open();
    }

    private void ExitGame()
    {
        SceneFlow.Quit();
    }
}
