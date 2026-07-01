using UnityEngine;
using NaughtyAttributes;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Combat/WeaponData")]
public class WeaponData : ScriptableObject
{
    [BoxGroup("General Settings")]
    public bool allowButtonHold = true;
    public bool isSpecialWeapon;
    public bool isLimitedAmmo;

    [BoxGroup("General Settings")]
    public FirePattern firePattern = FirePattern.Alternating;

    [BoxGroup("Firing & Ammunition")]
    [Min(1)] public int bulletsPerTap = 1;

    [BoxGroup("Firing & Ammunition")]
    [Min(0.01f)] public float fireRate = 12f;

    [BoxGroup("Firing & Ammunition")]
    [Min(1)] public int magazineSize = 60;

    [BoxGroup("Firing & Ammunition")]
    [Min(0f)] public float reloadTime = 1.5f;

    [BoxGroup("Firing & Ammunition")]
    [Min(1)] public int ammoCostPerShot = 1;

    [BoxGroup("Base Stats")]
    public float damage = 10f;

    [BoxGroup("Base Stats")]
    public float muzzleVelocity = 250f;

    [BoxGroup("Visuals & Effects")]
    public Sprite weaponIcon;
    public GameObject visualModel;

    [BoxGroup("Visuals & Effects")]
    public GameObject hitEffect;
    public Rarity rarity;

    public float maxDistance = 200f;
}
