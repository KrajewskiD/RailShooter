using UnityEngine;

public readonly struct ProjectileInitData
{
    public readonly WeaponData Weapon;
    public readonly Vector3 Direction;
    public readonly float TotalVelocity;
    public readonly float Damage;
    public readonly GameObject Owner;

    public ProjectileInitData(WeaponData weapon, Vector3 direction, float totalVelocity, float damage, GameObject owner)
    {
        Weapon = weapon;
        Direction = direction;
        TotalVelocity = totalVelocity;
        Damage = damage;
        Owner = owner;
    }
}
