public interface IStatProvider
{
    Stat GetHullStat(HullStatType type);
    Stat GetWeaponStat(WeaponStatType type, StatCategory category = StatCategory.PrimaryWeapon);
}