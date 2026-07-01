using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyData", menuName = "RogueLike/EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("Editor Notes")]
    [TextArea(3, 5)]
    public string description;
    [Space(10)]
    public float maxHealth = 50f;
    public float xpReward = 25f;
    public string enemyName;
    public GameObject visualModel;
    public WeaponData defaultWeapon;
}
