using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SelectionScreenController : MonoBehaviour
{
    private const string SelectionAssetPath = "UI/SelectionScreens";
    private const string PanelSettingsPath = "UI/InGameHUDPanelSettings";
    private static SelectionScreenController _instance;

    [SerializeField] private VisualTreeAsset visualTreeAsset;
    [SerializeField] private PanelSettings panelSettings;
    [SerializeField] private int sortingOrder = 30;

    public static SelectionScreenController Instance => _instance;
    public static bool IsAvailable => _instance != null && _instance.EnsureReady();

    private enum SelectionMode
    {
        None,
        Upgrade,
        Weapon
    }

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _cardsContainer;
    private Label _screenTag;
    private Label _screenTitle;
    private Label _screenSubtitle;
    private readonly List<VisualElement> _cards = new List<VisualElement>();
    private readonly List<Action> _primaryActions = new List<Action>();
    private SelectionMode _mode = SelectionMode.None;
    private int _selectedIndex;
    private int _activeUpgradeLevel = 1;
    private bool _stateSubscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        var go = new GameObject(nameof(SelectionScreenController));
        DontDestroyOnLoad(go);
        go.AddComponent<UIDocument>();
        go.AddComponent<SelectionScreenController>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _document = GetComponent<UIDocument>();
        EnsureDocumentAssets();
    }

    private void OnEnable()
    {
        EnsureReady();
        SetVisible(false);
        TrySubscribeState();
    }

    private void OnDisable()
    {
        if (_stateSubscribed && GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
        _stateSubscribed = false;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        TrySubscribeState();

        if (!IsVisible()) return;

        var kb = Keyboard.current;
        if (kb == null || _cards.Count == 0) return;

        if (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
        {
            SetSelected(_selectedIndex - 1);
            return;
        }

        if (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
        {
            SetSelected(_selectedIndex + 1);
            return;
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
        {
            ActivateSelected();
        }
    }

    public void ShowUpgradeSelection(UpgradeManager manager, int level, IReadOnlyList<UpgradeChoice> choices)
    {
        if (manager == null || !EnsureReady()) return;

        _mode = SelectionMode.Upgrade;
        _activeUpgradeLevel = Mathf.Max(1, level);
        PrepareScreen("UPGRADE", "UPGRADE SELECT", $"LEVEL {_activeUpgradeLevel}  ·  REROLLS {manager.rerollsLeft}", "is-upgrade");

        if (choices == null || choices.Count == 0)
        {
            AddEmptyState("NO UPGRADE OPTIONS AVAILABLE");
        }
        else
        {
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                _cardsContainer.Add(BuildUpgradeCard(manager, choices[index], index));
            }
        }

        FinishBuild();
    }

    public void HideUpgradeSelection()
    {
        if (_mode == SelectionMode.Upgrade)
            HideAll();
    }

    public void ShowWeaponSelection(WeaponManager manager, IReadOnlyList<WeaponData> weapons)
    {
        if (manager == null || !EnsureReady()) return;

        _mode = SelectionMode.Weapon;
        PrepareScreen("ARMORY", "WEAPON SELECT", "CHOOSE STARTING WEAPON", "is-weapon");

        if (weapons == null || weapons.Count == 0)
        {
            AddEmptyState("NO WEAPONS AVAILABLE");
        }
        else
        {
            for (int i = 0; i < weapons.Count; i++)
                _cardsContainer.Add(BuildWeaponCard(manager, weapons[i], i));
        }

        FinishBuild();
    }

    public void HideWeaponSelection()
    {
        if (_mode == SelectionMode.Weapon)
            HideAll();
    }

    public void HideAll()
    {
        _mode = SelectionMode.None;
        ClearCards();
        SetVisible(false);
    }

    private bool EnsureReady()
    {
        if (_document == null)
            _document = GetComponent<UIDocument>();

        if (_document == null) return false;

        EnsureDocumentAssets();
        CacheElements();
        return _root != null && _cardsContainer != null;
    }

    private void EnsureDocumentAssets()
    {
        if (visualTreeAsset == null)
            visualTreeAsset = Resources.Load<VisualTreeAsset>(SelectionAssetPath);

        if (_document != null && _document.visualTreeAsset == null && visualTreeAsset != null)
            _document.visualTreeAsset = visualTreeAsset;

        if (_document == null || _document.panelSettings != null) return;

        PanelSettings source = panelSettings != null
            ? panelSettings
            : Resources.Load<PanelSettings>(PanelSettingsPath);

        panelSettings = source != null
            ? Instantiate(source)
            : ScriptableObject.CreateInstance<PanelSettings>();

        panelSettings.sortingOrder = sortingOrder;
        _document.panelSettings = panelSettings;
    }

    private void CacheElements()
    {
        if (_document?.rootVisualElement == null) return;

        _root = _document.rootVisualElement.Q<VisualElement>("selection-root");
        if (_root == null) return;

        _cardsContainer = _root.Q<VisualElement>("selection-cards");
        _screenTag = _root.Q<Label>("selection-tag");
        _screenTitle = _root.Q<Label>("selection-title");
        _screenSubtitle = _root.Q<Label>("selection-subtitle");
    }

    private void PrepareScreen(string tag, string title, string subtitle, string modeClass)
    {
        ClearCards();

        if (_screenTag != null) _screenTag.text = tag;
        if (_screenTitle != null) _screenTitle.text = title;
        if (_screenSubtitle != null) _screenSubtitle.text = subtitle;

        _cardsContainer.EnableInClassList("is-upgrade", modeClass == "is-upgrade");
        _cardsContainer.EnableInClassList("is-weapon", modeClass == "is-weapon");
    }

    private VisualElement BuildUpgradeCard(UpgradeManager manager, UpgradeChoice choice, int index)
    {
        var card = CreateCard(choice?.Rarity ?? Rarity.Common);
        AddSelectable(card, () => manager.ApplyChoice(index));

        var top = new VisualElement();
        top.AddToClassList("sel-card__top");
        top.Add(BuildIcon(choice?.Template != null ? choice.Template.icon : null, "+"));

        var heading = new VisualElement();
        heading.AddToClassList("sel-card__heading");

        var meta = new VisualElement();
        meta.AddToClassList("sel-card__meta");
        meta.Add(BuildRarityChip(choice?.Rarity ?? Rarity.Common));
        meta.Add(MakeLabel(manager.BuildUpgradeTargetLabel(choice), "sel-card__target"));

        heading.Add(meta);
        heading.Add(MakeLabel(choice?.Template != null ? choice.Template.upgradeName : "Unknown Upgrade", "sel-card__title"));
        top.Add(heading);
        card.Add(top);

        var description = MakeLabel(manager.BuildUpgradeDescription(choice), "sel-card__description");
        description.enableRichText = true;
        card.Add(description);

        var actions = new VisualElement();
        actions.AddToClassList("sel-card__actions");

        var selectButton = new Button(() => manager.ApplyChoice(index)) { text = "SELECT" };
        selectButton.AddToClassList("sel-button");
        selectButton.AddToClassList("sel-button--primary");
        actions.Add(selectButton);

        var rerollButton = new Button(() =>
        {
            if (manager.TryRerollSingle(index, out _))
            {
                ShowUpgradeSelection(manager, _activeUpgradeLevel, manager.CurrentChoices);
                SetSelected(Mathf.Min(index, _cards.Count - 1));
            }
        })
        {
            text = $"REROLL ({manager.rerollsLeft})"
        };
        rerollButton.AddToClassList("sel-button");
        rerollButton.AddToClassList("sel-button--secondary");
        rerollButton.SetEnabled(manager.rerollsLeft > 0);
        actions.Add(rerollButton);

        card.Add(actions);
        return card;
    }

    private VisualElement BuildWeaponCard(WeaponManager manager, WeaponData weapon, int index)
    {
        Rarity rarity = weapon != null ? weapon.rarity : Rarity.Common;
        var card = CreateCard(rarity);
        AddSelectable(card, () => manager.ApplySelectedWeapon(weapon));

        var top = new VisualElement();
        top.AddToClassList("sel-card__top");
        top.Add(BuildIcon(weapon != null ? weapon.weaponIcon : null, "W"));

        var heading = new VisualElement();
        heading.AddToClassList("sel-card__heading");

        var meta = new VisualElement();
        meta.AddToClassList("sel-card__meta");
        meta.Add(BuildRarityChip(rarity));
        meta.Add(MakeLabel(weapon != null && weapon.isSpecialWeapon ? "SPECIAL" : "PRIMARY", "sel-card__target"));

        heading.Add(meta);
        heading.Add(MakeLabel(weapon != null ? weapon.name : "Unknown Weapon", "sel-card__title"));
        top.Add(heading);
        card.Add(top);

        var stats = new VisualElement();
        stats.AddToClassList("sel-stats");

        if (weapon != null)
        {
            StatCategory category = weapon.isSpecialWeapon ? StatCategory.SpecialWeapon : StatCategory.PrimaryWeapon;
            PlayerStatsManager playerStats = manager.playerStats;
            stats.Add(BuildStatRow("DAMAGE", PreviewWeaponStat(playerStats, WeaponStatType.Damage, category, weapon.damage)));
            stats.Add(BuildStatRow("FIRE RATE", PreviewWeaponStat(playerStats, WeaponStatType.FireRate, category, weapon.fireRate)));
            stats.Add(BuildStatRow("MAGAZINE", PreviewWeaponStat(playerStats, WeaponStatType.MagazineSize, category, weapon.magazineSize)));
            stats.Add(BuildStatRow("VELOCITY", PreviewWeaponStat(playerStats, WeaponStatType.MuzzleVelocity, category, weapon.muzzleVelocity)));
            stats.Add(BuildStatRow("RELOAD", weapon.reloadTime));
        }

        card.Add(stats);

        var note = MakeLabel(weapon != null && weapon.isLimitedAmmo ? "LIMITED AMMO" : "STANDARD AMMO", "sel-card__note");
        card.Add(note);

        var selectButton = new Button(() => manager.ApplySelectedWeapon(weapon)) { text = "EQUIP" };
        selectButton.AddToClassList("sel-button");
        selectButton.AddToClassList("sel-button--primary");
        card.Add(selectButton);

        return card;
    }

    private VisualElement CreateCard(Rarity rarity)
    {
        var card = new VisualElement();
        card.AddToClassList("sel-card");
        card.AddToClassList(RarityClass("sel-card", rarity));
        return card;
    }

    private VisualElement BuildIcon(Sprite sprite, string fallbackText)
    {
        var icon = new VisualElement();
        icon.AddToClassList("sel-card__icon");
        if (sprite != null)
        {
            icon.style.backgroundImage = new StyleBackground(sprite);
        }
        else
        {
            icon.Add(MakeLabel(fallbackText, "sel-card__icon-text"));
        }

        return icon;
    }

    private VisualElement BuildRarityChip(Rarity rarity)
    {
        var chip = MakeLabel(rarity.ToString().ToUpperInvariant(), "sel-chip");
        chip.AddToClassList(RarityClass("sel-chip", rarity));
        return chip;
    }

    private VisualElement BuildStatRow(string label, float value)
    {
        var row = new VisualElement();
        row.AddToClassList("sel-stat");
        row.Add(MakeLabel(label, "sel-stat__label"));
        row.Add(MakeLabel(value.ToString("0.##"), "sel-stat__value"));
        return row;
    }

    private void AddEmptyState(string text)
    {
        var empty = new VisualElement();
        empty.AddToClassList("sel-empty");
        empty.Add(MakeLabel(text, "sel-empty__text"));
        _cardsContainer.Add(empty);
    }

    private void AddSelectable(VisualElement card, Action primaryAction)
    {
        int index = _cards.Count;
        _cards.Add(card);
        _primaryActions.Add(primaryAction);
        card.RegisterCallback<PointerEnterEvent>(_ => SetSelected(index));
    }

    private void FinishBuild()
    {
        SetSelected(0);
        SetVisible(true);
    }

    private void ClearCards()
    {
        _cardsContainer?.Clear();
        _cards.Clear();
        _primaryActions.Clear();
        _selectedIndex = 0;
    }

    private void SetSelected(int index)
    {
        if (_cards.Count == 0) return;

        _selectedIndex = ((index % _cards.Count) + _cards.Count) % _cards.Count;
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].EnableInClassList("is-selected", i == _selectedIndex);
    }

    private void ActivateSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _primaryActions.Count) return;
        _primaryActions[_selectedIndex]?.Invoke();
    }

    private void SetVisible(bool visible)
    {
        if (_root == null) return;
        _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private bool IsVisible()
    {
        return _root != null && _root.style.display == DisplayStyle.Flex;
    }

    private void HandleStateChanged(GameState state)
    {
        if (state != GameState.UpgradeSelection && state != GameState.ProjectileSelection)
            HideAll();
    }

    private void TrySubscribeState()
    {
        if (_stateSubscribed) return;

        GameStateManager.GetOrCreate().OnStateChanged += HandleStateChanged;
        _stateSubscribed = true;
    }

    private static Label MakeLabel(string text, params string[] classes)
    {
        var label = new Label(text);
        for (int i = 0; i < classes.Length; i++)
            label.AddToClassList(classes[i]);
        return label;
    }

    private static float PreviewWeaponStat(PlayerStatsManager stats, WeaponStatType type, StatCategory category, float weaponBase)
    {
        if (stats == null) return weaponBase;
        Stat stat = stats.GetWeaponStat(type, category);
        return stat != null ? stat.PreviewValueWith(weaponBase) : weaponBase;
    }

    private static string RarityClass(string prefix, Rarity rarity)
    {
        return $"{prefix}--{rarity.ToString().ToLowerInvariant()}";
    }

}
