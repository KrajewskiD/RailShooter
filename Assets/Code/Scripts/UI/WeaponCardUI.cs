using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WeaponCardUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI weaponNameText;
    public Image weaponIconDisplay;

    [Header("Stats Values")]
    public TextMeshProUGUI dmgValue;
    public TextMeshProUGUI fireRateValue;
    public TextMeshProUGUI velocityValue;

    private WeaponData _data;
    private WeaponManager _lotteryManager;

    public void Setup(WeaponData newData, WeaponManager mngr)
    {
        _data = newData;
        _lotteryManager = mngr;

        weaponNameText.text = _data.name;

        StatCategory cat = _data.isSpecialWeapon ? StatCategory.SpecialWeapon : StatCategory.PrimaryWeapon;
        PlayerStatsManager stats = mngr != null ? mngr.playerStats : null;

        dmgValue.text = PreviewStat(stats, WeaponStatType.Damage, cat, _data.damage).ToString("0.##");
        fireRateValue.text = PreviewStat(stats, WeaponStatType.FireRate, cat, _data.fireRate).ToString("0.##");
        velocityValue.text = PreviewStat(stats, WeaponStatType.MuzzleVelocity, cat, _data.muzzleVelocity).ToString("0.##");

        if (weaponIconDisplay != null && _data.weaponIcon != null)
        {
            weaponIconDisplay.sprite = _data.weaponIcon;
        }
    }

    private static float PreviewStat(PlayerStatsManager stats, WeaponStatType type, StatCategory cat, float weaponBase)
    {
        if (stats == null) return weaponBase;
        Stat s = stats.GetWeaponStat(type, cat);
        return s != null ? s.PreviewValueWith(weaponBase) : weaponBase;
    }

    public void OnClickSelect()
    {
        if (_lotteryManager != null && _data != null)
        {
            _lotteryManager.ApplySelectedWeapon(_data);
        }

        if (GameStateManager.Instance != null)
        {
            GameStateManager.GetOrCreate().ChangeState(GameState.InGame);
        }
    }
}
