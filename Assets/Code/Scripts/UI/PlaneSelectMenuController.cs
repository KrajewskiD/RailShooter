using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

[RequireComponent(typeof(UIDocument))]
public partial class PlaneSelectMenuController : MonoBehaviour
{
    [System.Serializable]
    public class PlaneEntry
    {
        [Tooltip("ScriptableObject backing this slot. Drives display + gameplay.")]
        public PlaneData data;
        public int planeID;
    }

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "MainGame";

    [Header("Display")]
    [SerializeField] private List<PlaneEntry> planes = new();

    [Header("Stat scales (raw value that fills the bar to 100%)")]
    [SerializeField] private float healthScale      = 300f;
    [SerializeField] private float energyScale      = 300f;
    [SerializeField] private float shieldScale      = 200f;
    [SerializeField] private float consumptionScale = 100f;
    [SerializeField] private float regenScale       = 25f;

    [Header("3D Preview")]
    [SerializeField] private Vector2Int previewTextureSize = new(1024, 1024);
    [SerializeField] private float previewModelSize = 3.4f;
    [SerializeField] private float previewCameraDistance = 6f;
    [SerializeField] private float previewRotationSpeed = 0.35f;
    [SerializeField] private float previewModelYawOffset = 135f;
    [SerializeField] private float previewInitialYaw = -25f;

    [Header("Behaviour")]
    [Tooltip("If true the panel shows/hides itself based on GameStateManager.CurrentState == PlaneSelection.")]
    [SerializeField] private bool followGameState = true;

    public static PlaneSelectMenuController Instance { get; private set; }

    private UIDocument _doc;
    private VisualElement _root;


    private VisualElement _hangarList;
    private List<VisualElement> _cards = new();


    private Label _previewName, _previewRarity;
    private VisualElement _previewImageEl;
    private Image _previewRenderImage;
    private VisualElement _previewDots;


    private Label _description;
    private VisualElement _statHealthFill, _statShieldFill, _statEnergyFill, _statConsumptionFill, _statRegenFill;
    private Label _statHealthVal, _statShieldVal, _statEnergyVal, _statConsumptionVal, _statRegenVal;
    private Label _equipPrimary, _equipSpecial, _equipFirePoints;
    private VisualElement _unlockSection;
    private Label _unlockRequirement;
    private VisualElement _btnConfirm, _btnBack;
    private Label _btnConfirmLabel;
    private Label _hangarCount;

    private int _selectedIndex = 0;
    private int _previewIndex = 0;

    private void Awake()
    {
        Instance = this;
        _doc = GetComponent<UIDocument>();
    }

    private void OnDestroy()
    {
        UnregisterPreviewInput();
        ReleasePreviewResources();

        if (Instance == this) Instance = null;
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void OnDisable()
    {
        UnregisterPreviewInput();
        SetPreviewRigActive(false);

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void OnEnable()
    {
        _root = _doc.rootVisualElement?.Q<VisualElement>("plane-select-root") ?? _doc.rootVisualElement;
        if (_root == null) return;

        CacheElements();
        EnsurePreviewRig();
        RegisterPreviewInput();
        BuildHangar();
        BuildDots();
        SetSelected(FirstUnlockedIndex());


        _root.style.display = DisplayStyle.None;
        SetPreviewRigActive(false);

        GameStateManager gameStateManager = GameStateManager.GetOrCreate();
        gameStateManager.OnStateChanged -= HandleStateChanged;
        gameStateManager.OnStateChanged += HandleStateChanged;
        ApplyVisibility(gameStateManager.CurrentState);
    }

    private void Update()
    {
        if (!IsOpen()) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)        { Back(); return; }
        if (_cards.Count == 0) return;

        if (kb.enterKey.wasPressedThisFrame ||
            kb.numpadEnterKey.wasPressedThisFrame)   { Confirm(); return; }

        if (kb.leftArrowKey.wasPressedThisFrame ||
            kb.aKey.wasPressedThisFrame)             { Step(-1); return; }
        if (kb.rightArrowKey.wasPressedThisFrame ||
            kb.dKey.wasPressedThisFrame ||
            kb.upArrowKey.wasPressedThisFrame ||
            kb.downArrowKey.wasPressedThisFrame ||
            kb.wKey.wasPressedThisFrame ||
            kb.sKey.wasPressedThisFrame)
        {
            int dir = (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) ? -1 : 1;
            Step(dir);
        }
    }




    private void CacheElements()
    {
        _hangarList    = _root.Q<VisualElement>("hangar-list");
        _hangarCount   = _root.Q<Label>("hangar-count");

        _previewName   = _root.Q<Label>("preview-name");
        _previewRarity = _root.Q<Label>("preview-rarity");
        _previewImageEl = _root.Q<VisualElement>("preview-image");
        _previewRenderImage = _root.Q<Image>("preview-render");
        _previewDots   = _root.Q<VisualElement>("preview-dots");

        _description   = _root.Q<Label>("specs-description");

        _statHealthFill      = _root.Q<VisualElement>("stat-health-fill");
        _statShieldFill      = _root.Q<VisualElement>("stat-shield-fill");
        _statEnergyFill      = _root.Q<VisualElement>("stat-energy-fill");
        _statConsumptionFill = _root.Q<VisualElement>("stat-consumption-fill");
        _statRegenFill       = _root.Q<VisualElement>("stat-regen-fill");

        _statHealthVal      = _root.Q<Label>("stat-health-val");
        _statShieldVal      = _root.Q<Label>("stat-shield-val");
        _statEnergyVal      = _root.Q<Label>("stat-energy-val");
        _statConsumptionVal = _root.Q<Label>("stat-consumption-val");
        _statRegenVal       = _root.Q<Label>("stat-regen-val");

        _equipPrimary    = _root.Q<Label>("equip-primary");
        _equipSpecial    = _root.Q<Label>("equip-special");
        _equipFirePoints = _root.Q<Label>("equip-firepoints");

        _unlockSection     = _root.Q<VisualElement>("unlock-section");
        _unlockRequirement = _root.Q<Label>("unlock-requirement");

        _btnConfirm      = _root.Q<VisualElement>("btn-confirm");
        _btnConfirmLabel = _root.Q<Label>("btn-confirm-label");
        _btnBack         = _root.Q<VisualElement>("btn-back");

        _btnConfirm?.RegisterCallback<ClickEvent>(_ => Confirm());
        _btnBack?.RegisterCallback<ClickEvent>(_ => Back());
    }




    private void BuildHangar()
    {
        if (_hangarList == null) return;
        _hangarList.Clear();
        _cards.Clear();

        int unlocked = 0;
        for (int i = 0; i < planes.Count; i++)
        {
            var entry = planes[i];
            GameStateManager.GetOrCreate().RegisterPlane(entry.planeID, entry.data);
            PlayerProgressManager.Instance?.EvaluatePlaneUnlock(entry.planeID, entry.data);
            if (!IsLocked(entry)) unlocked++;
            var card = CreateCard(entry, i);
            _hangarList.Add(card);
            _cards.Add(card);
        }

        if (_hangarCount != null)
            _hangarCount.text = $"{unlocked}/{planes.Count} UNLOCKED";
    }

    private VisualElement CreateCard(PlaneEntry entry, int index)
    {
        var d = entry.data;
        string displayName = (d != null && !string.IsNullOrEmpty(d.displayName)) ? d.displayName : "Plane";
        Rarity rarity      = d != null ? d.rarity : Rarity.Common;
        Sprite thumbnail   = d != null ? d.thumbnail : null;
        bool locked        = IsLocked(entry);

        var card = new VisualElement();
        card.AddToClassList("ps-card");
        if (locked) card.AddToClassList("is-locked");
        card.focusable = true;

        var accent = new VisualElement();
        accent.AddToClassList("ps-card__accent");
        card.Add(accent);

        var thumb = new VisualElement();
        thumb.AddToClassList("ps-card__thumb");
        var thumbImage = new VisualElement();
        thumbImage.AddToClassList("ps-card__thumb-image");
        if (thumbnail != null)
            thumbImage.style.backgroundImage = new StyleBackground(thumbnail);
        thumb.Add(thumbImage);
        var lockGlyph = new Label("🔒");
        lockGlyph.AddToClassList("ps-card__lock");
        thumb.Add(lockGlyph);
        card.Add(thumb);

        var body = new VisualElement();
        body.AddToClassList("ps-card__body");

        var name = new Label(displayName);
        name.AddToClassList("ps-card__name");
        body.Add(name);

        var meta = new VisualElement();
        meta.AddToClassList("ps-card__meta");

        var rarityChip = new Label(rarity.ToString().ToUpperInvariant());
        rarityChip.AddToClassList("ps-rarity");
        rarityChip.AddToClassList(RarityClass(rarity));
        meta.Add(rarityChip);

        body.Add(meta);
        card.Add(body);

        var arrow = new Label("›");
        arrow.AddToClassList("ps-card__arrow");
        card.Add(arrow);

        int captured = index;
        card.RegisterCallback<PointerEnterEvent>(_ => SetPreview(captured));
        card.RegisterCallback<PointerLeaveEvent>(_ => SetPreview(_selectedIndex));
        card.RegisterCallback<ClickEvent>(_ => SetSelected(captured));
        return card;
    }

    private void BuildDots()
    {
        if (_previewDots == null) return;
        _previewDots.Clear();
        for (int i = 0; i < planes.Count; i++)
        {
            var dot = new VisualElement();
            dot.AddToClassList("ps-dot");
            _previewDots.Add(dot);
        }
    }




    private void SetSelected(int index)
    {
        if (planes.Count == 0) return;
        _selectedIndex = Mathf.Clamp(index, 0, planes.Count - 1);
        SetPreview(_selectedIndex);

        RefreshCardStates();
        RefreshConfirmButton();
    }

    private void SetPreview(int index)
    {
        if (planes.Count == 0) return;
        _previewIndex = Mathf.Clamp(index, 0, planes.Count - 1);
        RefreshCardStates();

        var dots = _previewDots?.Children();
        if (dots != null)
        {
            int i = 0;
            foreach (var d in dots) { d.EnableInClassList("is-active", i == _previewIndex); i++; }
        }

        RenderEntry(planes[_previewIndex]);
    }

    private void Step(int dir)
    {
        if (planes.Count == 0) return;
        int n = planes.Count;
        int next = ((_previewIndex + dir) % n + n) % n;
        SetPreview(next);
    }

    private void RefreshCardStates()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].EnableInClassList("is-selected", i == _selectedIndex);
            _cards[i].EnableInClassList("is-previewed", i == _previewIndex && i != _selectedIndex);
        }
    }

    private void RenderEntry(PlaneEntry e)
    {
        var d = e.data;
        bool locked = IsLocked(e);

        if (_previewName != null) _previewName.text = (d != null && !string.IsNullOrEmpty(d.displayName)) ? d.displayName : "—";

        if (_previewRarity != null)
        {
            var r = d != null ? d.rarity : Rarity.Common;
            _previewRarity.text = r.ToString().ToUpperInvariant();
            ApplyRarityClass(_previewRarity, r);
        }

        RenderPreviewModel(d);

        if (_description != null)
            _description.text = (d != null && !string.IsNullOrEmpty(d.description)) ? d.description : "—";

        SetStat(_statHealthFill,      _statHealthVal,      d != null ? d.maxHealth        : 0f, healthScale);
        SetStat(_statShieldFill,      _statShieldVal,      d != null ? d.maxShield        : 0f, shieldScale);
        SetStat(_statEnergyFill,      _statEnergyVal,      d != null ? d.maxEnergy        : 0f, energyScale);
        SetStat(_statConsumptionFill, _statConsumptionVal, d != null ? d.energyConsuption : 0f, consumptionScale);
        SetStat(_statRegenFill,       _statRegenVal,       d != null ? d.energyRegen      : 0f, regenScale);

        if (_equipPrimary    != null) _equipPrimary.text    = (d != null && d.defaultFire        != null) ? d.defaultFire.name        : "—";
        if (_equipSpecial    != null) _equipSpecial.text    = (d != null && d.defaultSpecialFire != null) ? d.defaultSpecialFire.name : "—";
        if (_equipFirePoints != null) _equipFirePoints.text = d != null ? $"{d.maxPrimaryFirePointLevel} (P) / {d.maxSpecialFirePointLevel} (S)" : "—";

        if (_unlockSection != null)
            _unlockSection.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;

        if (_unlockRequirement != null)
        {
            string requirement = d != null ? d.unlockRequirement : string.Empty;
            _unlockRequirement.text = string.IsNullOrWhiteSpace(requirement)
                ? "Unlock requirement unavailable."
                : requirement;
        }

        RefreshConfirmButton();
    }

    private static void SetStat(VisualElement fill, Label value, float raw, float scale)
    {
        float pct = scale > 0f ? Mathf.Clamp01(raw / scale) * 100f : 0f;
        if (fill  != null) fill.style.width = Length.Percent(pct);
        if (value != null) value.text       = Mathf.RoundToInt(raw).ToString();
    }

    private static string RarityClass(Rarity r) => r switch
    {
        Rarity.Common    => "ps-rarity--standard",
        Rarity.Uncommon  => "ps-rarity--rare",
        Rarity.Rare      => "ps-rarity--rare",
        Rarity.Epic      => "ps-rarity--epic",
        Rarity.Legendary => "ps-rarity--legendary",
        _                => "ps-rarity--standard",
    };

    private static void ApplyRarityClass(VisualElement el, Rarity r)
    {
        el.RemoveFromClassList("ps-rarity--standard");
        el.RemoveFromClassList("ps-rarity--rare");
        el.RemoveFromClassList("ps-rarity--heavy");
        el.RemoveFromClassList("ps-rarity--epic");
        el.RemoveFromClassList("ps-rarity--legendary");
        el.AddToClassList(RarityClass(r));
    }
    private int FirstUnlockedIndex()
    {
        for (int i = 0; i < planes.Count; i++) if (!IsLocked(planes[i])) return i;
        return 0;
    }

    private static bool IsLocked(PlaneEntry entry)
    {
        if (entry == null) return true;
        if (entry.data == null) return true;
        var progress = PlayerProgressManager.Instance;
        if (progress == null) return !entry.data.unlockedByDefault;
        return !progress.IsPlaneUnlocked(entry.planeID, entry.data);
    }

    private bool IsOpen() => _root != null && _root.style.display == DisplayStyle.Flex;

    private void HandleStateChanged(GameState s) => ApplyVisibility(s);

    private void ApplyVisibility(GameState s)
    {
        if (!followGameState || _root == null) return;
        bool show = s == GameState.PlaneSelection;
        _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        SetPreviewRigActive(show);
    }

    public void Confirm()
    {
        if (planes.Count == 0) return;

        if (_previewIndex != _selectedIndex)
        {
            SetSelected(_previewIndex);
            return;
        }

        var entry = planes[_selectedIndex];
        if (IsLocked(entry)) return;

        GameStateManager gameStateManager = GameStateManager.GetOrCreate();
        if (entry.data != null)
            gameStateManager.SelectPlane(entry.data, entry.planeID);
        gameStateManager.ChangeState(GameState.InGame);

        PlayerProgressManager.Instance?.StartNewRun(entry.planeID, gameSceneName);

        SceneFlow.LoadScene(gameSceneName);
    }

    public void Back()
    {
        GameStateManager.GetOrCreate().ChangeState(GameState.MainMenu);
    }

    private void RefreshConfirmButton()
    {
        if (_btnConfirm == null || _btnConfirmLabel == null || planes.Count == 0) return;

        bool previewIsSelected = _previewIndex == _selectedIndex;
        bool selectedLocked = IsLocked(planes[_selectedIndex]);
        bool launchBlocked = previewIsSelected && selectedLocked;

        _btnConfirm.EnableInClassList("is-disabled", launchBlocked);
        _btnConfirm.SetEnabled(!launchBlocked);

        if (!previewIsSelected)
            _btnConfirmLabel.text = "SELECT AIRCRAFT";
        else
            _btnConfirmLabel.text = selectedLocked ? "LOCKED" : "SELECT & LAUNCH";
    }
}
