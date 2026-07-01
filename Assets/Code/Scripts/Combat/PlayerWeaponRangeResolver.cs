using UnityEngine;

public static class PlayerWeaponRangeResolver
{
    public const float DefaultRange = 200f;

    public static float Resolve(Transform player, bool includeSpecialWeapons = false, float fallbackRange = DefaultRange)
    {
        return TryResolve(player, includeSpecialWeapons, out float range)
            ? range
            : Mathf.Max(0f, fallbackRange);
    }

    public static bool TryResolve(Transform player, bool includeSpecialWeapons, out float range)
    {
        range = 0f;
        if (player == null) return false;

        Transform root = player.root != null ? player.root : player;
        bool found = false;

        PlayerStatsManager stats = FindInParents<PlayerStatsManager>(player);
        if (stats == null) stats = root.GetComponentInChildren<PlayerStatsManager>(true);
        if (stats != null)
        {
            found |= Accumulate(stats.CurrentWeapon, ref range);
            if (includeSpecialWeapons)
                found |= Accumulate(stats.CurrentSpecialWeapon, ref range);
        }

        GunEngine[] guns = root.GetComponentsInChildren<GunEngine>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            GunEngine gun = guns[i];
            if (gun == null || !gun.HasWeapon) continue;
            if (!includeSpecialWeapons && gun.weaponCategory != StatCategory.PrimaryWeapon) continue;

            found |= Accumulate(gun.CurrentWeapon, ref range);
        }

        return found && range > 0f;
    }

    private static bool Accumulate(WeaponData weapon, ref float range)
    {
        if (weapon == null || weapon.maxDistance <= 0f) return false;
        range = Mathf.Max(range, weapon.maxDistance);
        return true;
    }

    private static T FindInParents<T>(Transform source) where T : Component
    {
        T[] components = source.GetComponentsInParent<T>(true);
        return components != null && components.Length > 0 ? components[0] : null;
    }
}
