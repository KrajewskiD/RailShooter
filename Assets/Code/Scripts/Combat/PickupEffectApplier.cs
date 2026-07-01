using UnityEngine;

public static class PickupEffectApplier
{
    public static void Apply(
        PickupEffect effect,
        float value,
        float duration,
        WeaponData weaponTarget,
        Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        switch (effect)
        {
            case PickupEffect.Fuel:
                ApplyEnergy(playerTransform, value);
                break;

            case PickupEffect.HP:
                PlayerReferenceResolver.FindOnPlayer<EntityHealth>(playerTransform)?.Heal(value);
                break;

            case PickupEffect.Shield:
                PlayerReferenceResolver.FindOnPlayer<EntityHealth>(playerTransform)?.AddShield(value);
                break;

            case PickupEffect.SpeedBoost:
                ApplySpeedBoost(playerTransform, value, duration);
                break;

            case PickupEffect.Exp:
                ProgressionManager.Instance?.AddXP(value);
                break;

            case PickupEffect.WeaponChange:
                ApplyWeaponChange(playerTransform, weaponTarget);
                break;
        }
    }

    private static void ApplyEnergy(Transform playerTransform, float value)
    {
        SplinePlayerController splineController =
            PlayerReferenceResolver.FindOnPlayer<SplinePlayerController>(playerTransform);
        if (splineController != null)
        {
            splineController.RestoreEnergy(value);
            return;
        }

        PlayerReferenceResolver.FindOnPlayer<PlayerController>(playerTransform)?.RestoreEnergy(value);
    }

    private static void ApplySpeedBoost(Transform playerTransform, float value, float duration)
    {
        SplinePlayerController splineController =
            PlayerReferenceResolver.FindOnPlayer<SplinePlayerController>(playerTransform);
        if (splineController != null)
        {
            splineController.ApplyTempSpeedBoost(value, duration);
            return;
        }

        PlayerReferenceResolver.FindOnPlayer<PlayerController>(playerTransform)?.ApplyTempSpeedBoost(value, duration);
    }

    private static void ApplyWeaponChange(Transform playerTransform, WeaponData weaponTarget)
    {
        if (weaponTarget == null)
        {
            return;
        }

        PlayerStatsManager stats = PlayerReferenceResolver.FindOnPlayer<PlayerStatsManager>(playerTransform);
        if (stats == null)
        {
            return;
        }

        if (weaponTarget.isSpecialWeapon)
        {
            stats.ChangeSpecialWeapon(weaponTarget);
        }
        else
        {
            stats.ChangeWeapon(weaponTarget);
        }
    }
}
