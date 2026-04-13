using UnityEngine;

[CreateAssetMenu(fileName = "New Gun Item", menuName = "Inventory/Gun Item")]
public class GunItem : Item
{
    [SerializeField] private int ammoCapacity = 30;
    [SerializeField] private int currentAmmo = 30;
    [SerializeField] private Gun.GunType gunType = Gun.GunType.Pistol;

    public override int MaxStackSize => 1;

    public int AmmoCapacity => ammoCapacity;
    public int CurrentAmmo => currentAmmo;
    public Gun.GunType GunType => gunType;

    public void SetAmmo(int ammo) => currentAmmo = Mathf.Clamp(ammo, 0, ammoCapacity);

    public void AddAmmo(int amount) => currentAmmo = Mathf.Clamp(currentAmmo + amount, 0, ammoCapacity);

    public bool UseAmmo(int amount = 1)
    {
        if (currentAmmo >= amount)
        {
            currentAmmo -= amount;
            return true;
        }
        return false;
    }
}
