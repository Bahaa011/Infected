using UnityEngine;

[CreateAssetMenu(fileName = "New Melee Weapon", menuName = "Inventory/Melee Weapon Item")]
public class MeleeWeaponItem : Item
{
    [SerializeField] private GameObject weaponPrefab;
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private float attackSpeed = 1f; // Attacks per second
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float knockbackForce = 10f;
    [SerializeField] private MeleeWeapon.WeaponType weaponType = MeleeWeapon.WeaponType.Sword;

    public override int MaxStackSize => 1;

    public GameObject WeaponPrefab => weaponPrefab;
    public float BaseDamage => baseDamage;
    public float AttackSpeed => attackSpeed;
    public float AttackRange => attackRange;
    public float KnockbackForce => knockbackForce;
    public MeleeWeapon.WeaponType WeaponType => weaponType;
}
