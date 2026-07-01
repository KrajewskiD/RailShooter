using UnityEngine;

[CreateAssetMenu(fileName = "NewPickupSettings", menuName = "RogueLike/Pickup Settings")]
public class PickupSettings : SpawnSettings
{
    [Header("Effect")]
    public PickupEffect effect = PickupEffect.Exp;
    [Tooltip("Ilość paliwa / HP / exp, albo mnożnik prędkości.")]
    public float value = 10f;
    [Tooltip("Czas trwania dla efektów chwilowych (Shield, SpeedBoost).")]
    public float duration = 3f;
    [Tooltip("Tylko dla WeaponChange.")]
    public WeaponData weaponTarget;

    [Header("Visual")]
    [Tooltip("Jeśli zostawione domyślne (białe), użyje Pickup.DefaultColorFor(effect). HDR — intensity > 1 = bloom.")]
    [ColorUsage(true, true)]
    public Color visualColor = Color.white;
    public float visualScale = 5f;

    [Header("Collection Feel")]
    [Tooltip("Bazowy zasięg aktywacji magnesu. Rzeczywisty zasięg rośnie z prędkością lotu.")]
    public float magnetDistance = 12f;
    [Tooltip("Bazowa prędkość przyciągania przed dodaniem bonusu z prędkości gracza.")]
    public float magnetSpeed = 40f;
    [Tooltip("Dodatkowe przyspieszenie zależne od dystansu pickupa do gracza.")]
    public float homingSpeedPerMeter = 6f;
    [Tooltip("Ile aktualnej prędkości lotu dodać do prędkości homingu.")]
    public float playerSpeedInfluence = 1.5f;
    [Tooltip("Wyprzedzenie aktywacji w sekundach lotu. 0.18 przy 50 u/s daje +9m zasięgu.")]
    public float speedLeadTime = 0.18f;
    [Tooltip("Limit bonusowego zasięgu z prędkości lotu.")]
    public float maxSpeedDistanceBonus = 14f;
    [Tooltip("Jak szybko pickup dochodzi do docelowej prędkości homingu.")]
    public float magnetAcceleration = 10f;
    [Tooltip("Prędkość shrink animacji po zebraniu.")]
    public float collectScaleSpeed = 12f;
    [Range(0.01f, 0.1f)]
    public float collectTolerancePercent = 0.05f;
    public float minimumCollectDistance = 1.5f;

    public override void ApplySetup(GameObject spawnedObject)
    {
        if (spawnedObject == null) return;
        var pickup = spawnedObject.GetComponent<Pickup>();
        if (pickup == null)
        {
            pickup = spawnedObject.AddComponent<Pickup>();
        }

        pickup.effect = effect;
        pickup.value = value;
        pickup.duration = duration;
        pickup.weaponTarget = weaponTarget;
        pickup.visualScale = visualScale;
        pickup.visualColor = (visualColor == Color.white) ? Pickup.DefaultColorFor(effect) : visualColor;
        pickup.ConfigureCollectionFeel(
            magnetDistance,
            magnetSpeed,
            homingSpeedPerMeter,
            playerSpeedInfluence,
            speedLeadTime,
            maxSpeedDistanceBonus,
            magnetAcceleration,
            collectScaleSpeed,
            collectTolerancePercent,
            minimumCollectDistance);

        pickup.RefreshVisual();
    }
}
