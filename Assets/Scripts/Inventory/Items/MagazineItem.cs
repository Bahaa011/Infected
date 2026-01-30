using UnityEngine;

[CreateAssetMenu(fileName = "New Magazine Item", menuName = "Inventory/Magazine Item")]
public class MagazineItem : Item
{
    [SerializeField] private Gun.GunType gunType = Gun.GunType.Pistol;
    [SerializeField] private int ammoCapacity = 30;

    public override int MaxStackSize => 20;

    public Gun.GunType GunType => gunType;
    public int AmmoCapacity => ammoCapacity;
}
