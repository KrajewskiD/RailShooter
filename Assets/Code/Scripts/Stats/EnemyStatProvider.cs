using System.Collections.Generic;
using UnityEngine;

public class EnemyStatProvider : MonoBehaviour, IStatProvider
{
    [SerializeField] private EnemyData data;
    private Dictionary<HullStatType, Stat> _hullStats = new Dictionary<HullStatType, Stat>();
    private Dictionary<WeaponStatType, Stat> _weaponStats = new Dictionary<WeaponStatType, Stat>();
    public EnemyData GetData() => data;

    private void Awake()
    {
        if (data == null)
        {
            return;
        }

        _hullStats.Add(HullStatType.MaxHealth, new Stat(data.maxHealth));

        WeaponData w = data.defaultWeapon;
        _weaponStats.Add(WeaponStatType.Damage, new Stat(w != null ? w.damage : 0f));
        _weaponStats.Add(WeaponStatType.FireRate, new Stat(w != null ? w.fireRate : 1f));
        _weaponStats.Add(WeaponStatType.MagazineSize, new Stat(w != null ? w.magazineSize : 1f));
        _weaponStats.Add(WeaponStatType.ReloadSpeed, new Stat(1f));
        _weaponStats.Add(WeaponStatType.MuzzleVelocity, new Stat(w != null ? w.muzzleVelocity : 10f));
        _weaponStats.Add(WeaponStatType.FirePointLevel, new Stat(1f));
    }

    public Stat GetHullStat(HullStatType type)
    {
        return _hullStats.TryGetValue(type, out Stat stat) ? stat : null;
    }

    public Stat GetWeaponStat(WeaponStatType type, StatCategory category = StatCategory.PrimaryWeapon)
    {
        return _weaponStats.TryGetValue(type, out Stat stat) ? stat : null;
    }

    public float GetXPReward() => data != null ? data.xpReward : 0f;
}