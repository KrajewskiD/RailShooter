using UnityEngine;
using System.Collections.Generic;

public class PlayerStatsManager : MonoBehaviour, IStatProvider
{
    [Tooltip("Inspectorowy fallback. W grze pierwszeństwo ma GameStateManager.Instance.ChosenPlaneData.")]
    [SerializeField] private PlaneData baseSettings;
    private Dictionary<HullStatType, Stat> _hullStats = new Dictionary<HullStatType, Stat>();
    private Dictionary<WeaponStatType, Stat> _fireStats = new Dictionary<WeaponStatType, Stat>();
    private Dictionary<WeaponStatType, Stat> _specialStats = new Dictionary<WeaponStatType, Stat>();
    private PlaneData _activePlaneData;
    public PlaneData ActivePlaneData => _activePlaneData;
    public WeaponData CurrentWeapon { get; private set; }
    public WeaponData CurrentSpecialWeapon { get; private set; }
    public System.Action<WeaponData> OnWeaponChanged;
    public System.Action<WeaponData> OnSpecialWeaponChanged;

    private void Awake() => InitializeAllStats();

    private void Start()
    {
        if (_hullStats.Count == 0 && ResolvePlaneData() != null)
        {
            InitializeAllStats();
        }
    }

    private PlaneData ResolvePlaneData()
    {
        var gsm = GameStateManager.Instance ?? GameStateManager.GetOrCreate();
        if (gsm.ChosenPlaneData != null) return gsm.ChosenPlaneData;
        return baseSettings;
    }

    private void InitializeAllStats()
    {
        _activePlaneData = ResolvePlaneData();
        if (_activePlaneData == null)
        {
            return;
        }

        InitHullStats(_activePlaneData);

        CurrentWeapon = _activePlaneData.defaultFire;
        CurrentSpecialWeapon = _activePlaneData.defaultSpecialFire;

        InitWeaponStats(_fireStats, CurrentWeapon);
        InitWeaponStats(_specialStats, CurrentSpecialWeapon);
    }

    public int GetMaxFirePointLevel(StatCategory category)
    {
        if (_activePlaneData == null)
        {
            return 1;
        }

        int cap = category == StatCategory.SpecialWeapon
            ? _activePlaneData.maxSpecialFirePointLevel
            : _activePlaneData.maxPrimaryFirePointLevel;
        return Mathf.Max(1, cap);
    }

    private void InitHullStats(PlaneData source)
    {
        _hullStats.Clear();
        _hullStats.Add(HullStatType.MaxHealth, new Stat(source.maxHealth));
        _hullStats.Add(HullStatType.MaxEnergy, new Stat(source.maxEnergy));
        _hullStats.Add(HullStatType.EnergyRegen, new Stat(source.energyRegen));
        _hullStats.Add(HullStatType.EnergyConsuption, new Stat(source.energyConsuption));
        _hullStats.Add(HullStatType.MaxShield, new Stat(source.maxShield));
    }

    private void InitWeaponStats(Dictionary<WeaponStatType, Stat> targetDict, WeaponData w)
    {
        targetDict.Clear();
        targetDict.Add(WeaponStatType.Damage, new Stat(w != null ? w.damage : 0f));
        targetDict.Add(WeaponStatType.FireRate, new Stat(w != null ? w.fireRate : 1f));
        targetDict.Add(WeaponStatType.MagazineSize, new Stat(w != null ? w.magazineSize : 1f));
        targetDict.Add(WeaponStatType.ReloadSpeed, new Stat(1f));
        targetDict.Add(WeaponStatType.MuzzleVelocity, new Stat(w != null ? w.muzzleVelocity : 10f));
        targetDict.Add(WeaponStatType.FirePointLevel, new Stat(1f));
    }

    public void SetHullModifier(HullStatType type, float val, StatModType mod) 
    {
        if(_hullStats.TryGetValue(type, out Stat s)) s.AddModifier(new StatModifier(val, mod));
    }

    public Stat GetHullStat(HullStatType type)
    {
        return _hullStats.TryGetValue(type, out Stat stat) ? stat : null;
    }


    public void SetWeaponModifier(StatCategory cat, WeaponStatType type, float val, StatModType mod)
    {
        var dict = (cat == StatCategory.SpecialWeapon) ? _specialStats : _fireStats;
        if(dict.TryGetValue(type, out Stat s)) s.AddModifier(new StatModifier(val, mod));
    }
    public Stat GetWeaponStat(WeaponStatType type, StatCategory category = StatCategory.PrimaryWeapon)
    {
        var targetDict = category == StatCategory.SpecialWeapon ? _specialStats : _fireStats;
        return targetDict.TryGetValue(type, out Stat stat) ? stat : null;
    }

    public void ChangeWeapon(WeaponData newData)
    {
        if (newData == null) return;
        CurrentWeapon = newData;
        UpdateWeaponDictionary(_fireStats, newData);
        OnWeaponChanged?.Invoke(newData);
    }

    public void ChangeSpecialWeapon(WeaponData newData)
    {
        if (newData == null) return;
        CurrentSpecialWeapon = newData;
        UpdateWeaponDictionary(_specialStats, newData);
        OnSpecialWeaponChanged?.Invoke(newData);
    }

    private void UpdateWeaponDictionary(Dictionary<WeaponStatType, Stat> dict, WeaponData newData)
    {
        dict[WeaponStatType.Damage].BaseValue = newData.damage;
        dict[WeaponStatType.FireRate].BaseValue = newData.fireRate;
        dict[WeaponStatType.MagazineSize].BaseValue = newData.magazineSize;
        dict[WeaponStatType.ReloadSpeed].BaseValue = 1f;
        dict[WeaponStatType.MuzzleVelocity].BaseValue = newData.muzzleVelocity;
    }
}