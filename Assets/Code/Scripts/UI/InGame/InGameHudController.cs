using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class InGameHudController : MonoBehaviour
{
    private const string HudAssetPath = "UI/InGameHUD";
    private const string PanelSettingsPath = "UI/InGameHUDPanelSettings";
    private static InGameHudController _instance;

    [SerializeField] private VisualTreeAsset visualTreeAsset;
    [SerializeField] private PanelSettings panelSettings;
    [SerializeField] private float valueAnimationSpeed = 12f;
    [SerializeField] private int maxPrimarySegments = 32;
    [SerializeField] private int maxSpecialSegments = 8;

    private UIDocument _document;
    private VisualElement _root;
    private VisualElement _healthFill, _shieldFill, _energyFill, _xpFill;
    private VisualElement _ammoSegments, _specialSegments;
    private Label _healthValue, _shieldValue, _energyValue, _ammoValue, _specialValue;
    private Label _xpCurrentLevel, _xpNextLevel;

    private EntityHealth _health;
    private PlayerStatsManager _playerStats;
    private SplinePlayerController _splineController;
    private PlayerController _railController;
    private GunEngine _primaryGun;
    private GunEngine _specialGun;

    private float _targetHealth, _targetMaxHealth;
    private float _targetShield, _targetMaxShield;
    private float _displayHealth, _displayShield, _displayEnergy, _displayXp;
    private float _targetXp, _targetXpMax = 1f;
    private int _currentLevel = 1;
    private int _primarySegmentCount = -1;
    private int _specialSegmentCount = -1;
    private int _lastHealthCurrent = int.MinValue;
    private int _lastHealthMax = int.MinValue;
    private int _lastShieldCurrent = int.MinValue;
    private int _lastShieldMax = int.MinValue;
    private int _lastEnergyCurrent = int.MinValue;
    private int _lastEnergyMax = int.MinValue;
    private int _lastPrimaryAmmoCurrent = int.MinValue;
    private int _lastPrimaryAmmoMax = int.MinValue;
    private int _lastSpecialAmmoCurrent = int.MinValue;
    private int _lastSpecialAmmoMax = int.MinValue;
    private float _bindRetryTimer;

    private readonly List<VisualElement> _primarySegmentElements = new List<VisualElement>();
    private readonly List<VisualElement> _specialSegmentElements = new List<VisualElement>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;

        var go = new GameObject(nameof(InGameHudController));
        DontDestroyOnLoad(go);
        go.AddComponent<UIDocument>();
        go.AddComponent<InGameHudController>();
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

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameStateManager.OnPlaneSelected -= OnPlaneSelected;
        GameStateManager.OnPlaneSelected += OnPlaneSelected;
    }

    private void OnEnable()
    {
        ProgressionManager.OnXPChanged += HandleXPChanged;
        ProgressionManager.OnLevelUp += HandleLevelUp;

        CacheElements();
        BindPlayer();
        RefreshXPFromManager();
        UpdateVisibility();
        GameStateManager gameStateManager = GameStateManager.GetOrCreate();
        gameStateManager.OnStateChanged -= HandleStateChanged;
        gameStateManager.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        ProgressionManager.OnXPChanged -= HandleXPChanged;
        ProgressionManager.OnLevelUp -= HandleLevelUp;
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
        UnbindHealth();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameStateManager.OnPlaneSelected -= OnPlaneSelected;
        if (_instance == this) _instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResetPlayerBinding();
        CacheElements();
        BindPlayer();
        SyncHealthFromSource();
        RefreshXPFromManager();
        UpdateVisibility();
    }

    private void OnPlaneSelected(int planeId)
    {
        ResetPlayerBinding();
        BindPlayer();
        SyncHealthFromSource();
    }

    private void ResetPlayerBinding()
    {
        UnbindHealth();
        _playerStats = null;
        _splineController = null;
        _railController = null;
        _primaryGun = null;
        _specialGun = null;
        _primarySegmentCount = -1;
        _specialSegmentCount = -1;
        _bindRetryTimer = 0f;
    }

    private void Update()
    {
        if (_root == null)
        {
            EnsureDocumentAssets();
            CacheElements();
            RefreshXPFromManager();
            UpdateVisibility();
            if (_root == null) return;
        }

        if (NeedsPlayerRebind())
        {
            _bindRetryTimer -= Time.deltaTime;
            if (_bindRetryTimer <= 0f)
            {
                _bindRetryTimer = 0.5f;
                BindPlayer();
            }
        }

        UpdateVisibility();
        PollLiveSources();
        AnimateAndRender(Time.deltaTime);
    }

    private void EnsureDocumentAssets()
    {
        if (visualTreeAsset == null)
            visualTreeAsset = Resources.Load<VisualTreeAsset>(HudAssetPath);

        if (_document.visualTreeAsset == null && visualTreeAsset != null)
            _document.visualTreeAsset = visualTreeAsset;

        if (_document.panelSettings == null)
        {
            if (panelSettings == null)
                panelSettings = Resources.Load<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _document.panelSettings = panelSettings;
        }
    }

    private void CacheElements()
    {
        _root = _document.rootVisualElement?.Q<VisualElement>("in-game-hud-root");
        if (_root == null) return;

        _healthFill = _root.Q<VisualElement>("health-fill");
        _shieldFill = _root.Q<VisualElement>("shield-fill");
        _energyFill = _root.Q<VisualElement>("energy-fill");
        _xpFill = _root.Q<VisualElement>("xp-fill");

        _ammoSegments = _root.Q<VisualElement>("ammo-segments");
        _specialSegments = _root.Q<VisualElement>("special-segments");

        _healthValue = _root.Q<Label>("health-value");
        _shieldValue = _root.Q<Label>("shield-value");
        _energyValue = _root.Q<Label>("energy-value");
        _ammoValue = _root.Q<Label>("ammo-value");
        _specialValue = _root.Q<Label>("special-value");

        _xpCurrentLevel = _root.Q<Label>("xp-level-current");
        _xpNextLevel = _root.Q<Label>("xp-level-next");
    }

    private void BindPlayer()
    {
        Transform player = ResolvePlayerTransform();
        if (player == null) return;

        var nextHealth = PlayerReferenceResolver.FindOnPlayer<EntityHealth>(player);
        if (nextHealth != _health)
        {
            UnbindHealth();
            _health = nextHealth;
            if (_health != null)
            {
                _health.OnHealthChanged += HandleHealthChanged;
                _health.OnShieldChanged += HandleShieldChanged;
                SyncHealthFromSource();
            }
        }
        else if (_health != null)
        {
            SyncHealthFromSource();
        }

        _splineController = PlayerReferenceResolver.FindOnPlayer<SplinePlayerController>(player);
        _railController = PlayerReferenceResolver.FindOnPlayer<PlayerController>(player);
        _playerStats = PlayerReferenceResolver.FindOnPlayer<PlayerStatsManager>(player);
        RefreshGuns();
    }

    private void SyncHealthFromSource()
    {
        if (_health != null && !IsUnityNull(_health) && _health.MaxHealth > 0f)
        {
            HandleHealthChanged(_health.CurrentHealth, _health.MaxHealth);
            HandleShieldChanged(_health.CurrentShield, _health.MaxShield);
            _displayHealth = _targetHealth;
            _displayShield = _targetShield;
            return;
        }

        if (HasLiveVehicle()) return;

        PlaneData plane = ResolveChosenPlaneData();
        if (plane == null) return;

        HandleHealthChanged(plane.maxHealth, plane.maxHealth);
        HandleShieldChanged(0f, plane.maxShield);
        _displayHealth = _targetHealth;
        _displayShield = _targetShield;
    }

    private static PlaneData ResolveChosenPlaneData()
    {
        var gsm = GameStateManager.Instance;
        if (gsm != null && gsm.ChosenPlaneData != null) return gsm.ChosenPlaneData;

        return null;
    }

    private PlaneData ResolveActivePlaneData()
    {
        if (_playerStats != null && _playerStats.ActivePlaneData != null)
        {
            return _playerStats.ActivePlaneData;
        }

        return ResolveChosenPlaneData();
    }

    private float ResolveMaxEnergy()
    {
        if (_splineController != null) return _splineController.MaxEnergy;
        if (_railController != null) return _railController.MaxEnergy;

        Stat energyStat = _playerStats?.GetHullStat(HullStatType.MaxEnergy);
        if (energyStat != null) return energyStat.Value;

        PlaneData plane = ResolveActivePlaneData();
        return plane != null ? plane.maxEnergy : 0f;
    }

    private float ResolveCurrentEnergy(float maxEnergy)
    {
        if (_splineController != null) return _splineController.CurrentEnergy;
        if (_railController != null) return _railController.CurrentEnergy;
        return HasLiveVehicle() ? 0f : maxEnergy;
    }

    private int ResolveMagazineMax(StatCategory category, GunEngine gun)
    {
        if (gun != null)
        {
            int liveMax = gun.MaxMagazine;
            if (liveMax > 0) return liveMax;
        }

        Stat magazineStat = _playerStats?.GetWeaponStat(WeaponStatType.MagazineSize, category);
        if (magazineStat != null) return Mathf.Max(0, Mathf.RoundToInt(magazineStat.Value));

        PlaneData plane = ResolveActivePlaneData();
        if (plane == null) return 0;

        WeaponData weapon = category == StatCategory.SpecialWeapon
            ? plane.defaultSpecialFire
            : plane.defaultFire;
        return weapon != null ? Mathf.Max(0, weapon.magazineSize) : 0;
    }

    private int ResolveMagazineCurrent(GunEngine gun, int max)
    {
        if (gun != null && max > 0)
        {
            return Mathf.Clamp(gun.BulletsLeft, 0, max);
        }

        return HasLiveVehicle() ? 0 : max;
    }

    private bool HasLiveVehicle()
    {
        return !IsUnityNull(_splineController) || !IsUnityNull(_railController);
    }

    private bool NeedsPlayerRebind()
    {
        if (GameStateManager.Instance == null || GameStateManager.Instance.CurrentState != GameState.InGame)
        {
            return false;
        }

        if (!HasLiveVehicle())
        {
            return true;
        }

        if (_health == null || IsUnityNull(_health))
        {
            return true;
        }

        return _health.MaxHealth <= 0f;
    }

    private static bool IsUnityNull(Object obj)
    {
        return obj == null;
    }

    private static Transform ResolvePlayerTransform()
    {
        return PlayerReferenceResolver.ResolvePlayerTransform();
    }

    private void UnbindHealth()
    {
        if (_health == null) return;
        _health.OnHealthChanged -= HandleHealthChanged;
        _health.OnShieldChanged -= HandleShieldChanged;
        _health = null;
    }

    private void RefreshGuns()
    {
        _primaryGun = null;
        _specialGun = null;

        if (_splineController != null)
        {
            _primaryGun = _splineController.PrimaryGun;
            _specialGun = _splineController.SpecialGun;
        }
        else if (_railController != null)
        {
            _primaryGun = _railController.PrimaryGun;
            _specialGun = _railController.SpecialGun;
        }

        if (_primaryGun != null && _specialGun != null) return;

        Transform player = ResolvePlayerTransform();
        if (player == null) return;

        foreach (GunEngine gun in player.GetComponentsInChildren<GunEngine>(true))
        {
            if (gun.weaponCategory == StatCategory.PrimaryWeapon)
            {
                _primaryGun = gun;
            }
            else if (_specialGun == null)
            {
                _specialGun = gun;
            }
        }
    }

    private void PollLiveSources()
    {
        RefreshGuns();
        UpdateEnergy();
        UpdateAmmo(
            _primaryGun,
            StatCategory.PrimaryWeapon,
            _ammoSegments,
            _primarySegmentElements,
            ref _primarySegmentCount,
            maxPrimarySegments,
            _ammoValue,
            false,
            ref _lastPrimaryAmmoCurrent,
            ref _lastPrimaryAmmoMax);
        UpdateAmmo(
            _specialGun,
            StatCategory.SpecialWeapon,
            _specialSegments,
            _specialSegmentElements,
            ref _specialSegmentCount,
            maxSpecialSegments,
            _specialValue,
            true,
            ref _lastSpecialAmmoCurrent,
            ref _lastSpecialAmmoMax);
    }

    private void UpdateEnergy()
    {
        float max = ResolveMaxEnergy();
        float current = ResolveCurrentEnergy(max);

        _displayEnergy = MoveDisplay(_displayEnergy, current, max, Time.deltaTime);
        SetFill(_energyFill, _displayEnergy, max);
        SetValue(_energyValue, _displayEnergy, max, ref _lastEnergyCurrent, ref _lastEnergyMax);
    }

    private void UpdateAmmo(
        GunEngine gun,
        StatCategory category,
        VisualElement container,
        List<VisualElement> segments,
        ref int cachedCount,
        int segmentLimit,
        Label valueLabel,
        bool special,
        ref int lastValueCurrent,
        ref int lastValueMax)
    {
        int max = ResolveMagazineMax(category, gun);
        int current = ResolveMagazineCurrent(gun, max);
        int responsiveLimit = GetResponsiveSegmentLimit(container, segmentLimit, special);
        int segmentCount = Mathf.Clamp(max, 1, responsiveLimit);

        if (cachedCount != segmentCount)
        {
            BuildSegments(container, segments, segmentCount, special);
            cachedCount = segmentCount;
            lastValueCurrent = int.MinValue;
            lastValueMax = int.MinValue;
        }

        bool reloading = gun != null && gun.IsReloading;
        float reloadProgress = gun != null ? gun.ReloadProgress : 1f;
        float ammoRatio = max > 0 ? current / (float)max : 0f;
        float reloadRatio = reloading ? reloadProgress : 0f;

        for (int i = 0; i < segments.Count; i++)
        {
            float threshold = (i + 1f) / segments.Count;
            bool filled = ammoRatio >= threshold;
            bool reload = reloading && reloadRatio >= threshold;
            segments[i].EnableInClassList("is-filled", filled);
            segments[i].EnableInClassList("is-reload", reload && !filled);
        }

        SetIntPair(valueLabel, current, max, ref lastValueCurrent, ref lastValueMax);
    }

    private static void BuildSegments(VisualElement container, List<VisualElement> cache, int count, bool special)
    {
        if (container == null) return;

        container.Clear();
        cache.Clear();

        for (int i = 0; i < count; i++)
        {
            var segment = new VisualElement();
            segment.AddToClassList(special ? "hud-special-segment" : "hud-segment");
            container.Add(segment);
            cache.Add(segment);
        }
    }

    private static int GetResponsiveSegmentLimit(VisualElement container, int requestedLimit, bool special)
    {
        int maxLimit = Mathf.Max(1, requestedLimit);
        if (container == null) return maxLimit;

        float width = container.resolvedStyle.width;
        if (float.IsNaN(width) || width <= 1f) return maxLimit;

        float segmentWidth = special ? 42f : 12f;
        int minimum = special ? 1 : 8;
        int fittingCount = Mathf.FloorToInt(width / segmentWidth);
        return Mathf.Clamp(fittingCount, minimum, maxLimit);
    }

    private void AnimateAndRender(float dt)
    {
        _displayHealth = MoveDisplay(_displayHealth, _targetHealth, _targetMaxHealth, dt);
        _displayShield = MoveDisplay(_displayShield, _targetShield, _targetMaxShield, dt);
        _displayXp = MoveDisplay(_displayXp, _targetXp, _targetXpMax, dt);

        SetFill(_healthFill, _displayHealth, _targetMaxHealth);
        SetFill(_shieldFill, _displayShield, _targetMaxShield);
        SetFill(_xpFill, _displayXp, _targetXpMax);

        SetValue(_healthValue, _displayHealth, _targetMaxHealth, ref _lastHealthCurrent, ref _lastHealthMax);
        SetValue(_shieldValue, _displayShield, _targetMaxShield, ref _lastShieldCurrent, ref _lastShieldMax);
    }

    private float MoveDisplay(float current, float target, float max, float dt)
    {
        if (Mathf.Approximately(current, target)) return target;
        float speed = Mathf.Max(12f, Mathf.Max(1f, max) * valueAnimationSpeed);
        return Mathf.MoveTowards(current, target, speed * dt);
    }

    private static void SetFill(VisualElement fill, float current, float max)
    {
        if (fill == null) return;
        float pct = max > 0f ? Mathf.Clamp01(current / max) * 100f : 0f;
        fill.style.width = Length.Percent(pct);
    }

    private static void SetValue(Label label, float current, float max, ref int lastCurrent, ref int lastMax)
    {
        if (label == null) return;
        SetIntPair(label, Mathf.RoundToInt(current), Mathf.RoundToInt(max), ref lastCurrent, ref lastMax);
    }

    private static void SetIntPair(Label label, int current, int max, ref int lastCurrent, ref int lastMax)
    {
        if (label == null) return;

        int displayCurrent = max > 0 ? current : 0;
        int displayMax = max > 0 ? max : 0;
        if (displayCurrent == lastCurrent && displayMax == lastMax) return;

        lastCurrent = displayCurrent;
        lastMax = displayMax;
        label.text = $"{displayCurrent}/{displayMax}";
    }

    private void HandleHealthChanged(float current, float max)
    {
        _targetHealth = current;
        _targetMaxHealth = max;
    }

    private void HandleShieldChanged(float current, float max)
    {
        _targetShield = current;
        _targetMaxShield = max;
    }

    private void HandleXPChanged(float current, float max)
    {
        _targetXp = Mathf.Max(0f, current);
        _targetXpMax = Mathf.Max(1f, max);
    }

    private void HandleLevelUp(int level)
    {
        _currentLevel = Mathf.Max(1, level);
        RefreshLevelLabels();
    }

    private void RefreshXPFromManager()
    {
        if (ProgressionManager.Instance != null)
        {
            _currentLevel = Mathf.Max(1, ProgressionManager.Instance.currentLevel);
            HandleXPChanged(ProgressionManager.Instance.currentXP, ProgressionManager.Instance.xpToLevelUp);
        }
        RefreshLevelLabels();
    }

    private void RefreshLevelLabels()
    {
        if (_xpCurrentLevel != null) _xpCurrentLevel.text = $"LVL {_currentLevel}";
        if (_xpNextLevel != null) _xpNextLevel.text = $"LVL {_currentLevel + 1}";
    }

    private void HandleStateChanged(GameState state)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_root == null) return;
        bool show = GameStateManager.Instance != null && GameStateManager.Instance.CurrentState == GameState.InGame;
        _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
