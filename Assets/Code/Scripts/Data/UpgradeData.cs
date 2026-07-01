using UnityEngine;
using System.Collections.Generic;
using NaughtyAttributes;

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Upgrade")]
public class UpgradeData : ScriptableObject
{
    [Header("Info")]
    public string upgradeName;
    [TextArea(3, 5)] public string description;
    public Sprite icon;

    [Header("Targeting")]
    public StatCategory category;

    [ShowIf("IsBaseCategory")] public HullStatType hullStatType;
    [HideIf("IsBaseCategory")] public WeaponStatType weaponStatType;

    [Header("Modification")]
    public StatModType modType;
    public float baseValue;

    [Header("Rarity")]
    public List<Rarity> allowedRarities = new List<Rarity> { Rarity.Common, Rarity.Rare };

    private bool IsBaseCategory() => category == StatCategory.Base;

    public bool IsFirePointLevelUpgrade =>
        category != StatCategory.Base && weaponStatType == WeaponStatType.FirePointLevel;

    public bool IsSupportedUpgradeStat()
    {
        if (category == StatCategory.Base)
        {
            switch (hullStatType)
            {
                case HullStatType.MaxHealth:
                case HullStatType.MaxShield:
                case HullStatType.MaxEnergy:
                case HullStatType.EnergyRegen:
                case HullStatType.EnergyConsuption:
                    return true;
                default:
                    return false;
            }
        }

        if (category == StatCategory.PrimaryWeapon || category == StatCategory.SpecialWeapon)
        {
            switch (weaponStatType)
            {
                case WeaponStatType.Damage:
                case WeaponStatType.FireRate:
                case WeaponStatType.MagazineSize:
                case WeaponStatType.ReloadSpeed:
                case WeaponStatType.MuzzleVelocity:
                case WeaponStatType.FirePointLevel:
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (weaponStatType == WeaponStatType.FirePointLevel)
        {
            if (category == StatCategory.Base)
            {
                category = StatCategory.PrimaryWeapon;
            }
            modType = StatModType.Flat;
            baseValue = Mathf.Max(1, Mathf.Round(baseValue));
        }
    }
#endif
}

[System.Serializable]
public struct RaritySlot
{
    public Rarity rarity;
    public int count;
}

[System.Serializable]
public struct RarityWeight
{
    public Rarity rarity;
    public float weight;
}

[System.Serializable]
public struct LevelRequirement
{
    public int interval;
    public int upgradeOptionsCount;
    public List<RarityWeight> rarityWeights;
}
