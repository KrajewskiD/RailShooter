using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewPlane", menuName = "Plane/PlaneData")]
public class PlaneData : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Plane";
    public Rarity rarity      = Rarity.Common;

    [Header("Visuals")]
    public Sprite thumbnail;
    public GameObject visualPrefab;

    [Header("Description")]
    [TextArea(2, 6)] public string description;
    [TextArea(1, 4)] public string unlockRequirement = "Complete the required objective to unlock this aircraft.";

    [Header("Unlock")]
    public bool unlockedByDefault = true;
    public UnlockConditionType unlockCondition = UnlockConditionType.None;
    [Min(0)] public int requiredKills;
    [Min(0f)] public float requiredDistanceMeters;
    [Min(1)] public int requiredLevel = 1;

    [Header("Stats")]
    public float maxHealth = 100f;
    public float maxEnergy = 100f;
    public float maxShield = 100f;
    public float energyConsuption = 25f;
    public float energyRegen = 15f;

    [Header("Weapons")]
    [Tooltip("Maksymalny FirePointLevel dla PrimaryWeapon. Nie liczba Transformów — to górny cap presetu konfiguracji.")]
    [FormerlySerializedAs("maxPrimaryFirePoints")]
    [Min(1)] public int maxPrimaryFirePointLevel = 1;

    [Tooltip("Maksymalny FirePointLevel dla SpecialWeapon. Nie liczba Transformów — to górny cap presetu konfiguracji.")]
    [FormerlySerializedAs("maxSpecialFirePoints")]
    [Min(1)] public int maxSpecialFirePointLevel = 1;

    public WeaponData defaultFire;
    public WeaponData defaultSpecialFire;
}
