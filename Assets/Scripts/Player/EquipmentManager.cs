using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class EquipmentManager : MonoBehaviour
{
    [Header("Weapon Layer Switching")]
    [SerializeField] private string weaponLayerName = "Weapon";
    private int weaponLayer = -1;
    [SerializeField] private Transform handSlot;
    [SerializeField] private Transform primaryRestingSlot;
    [SerializeField] private Transform secondaryRestingSlot;
    [SerializeField] private InputActionReference switchWeaponAction;
    [Header("Weapon Hand Placement")]
    [Tooltip("Local rotation (in degrees) to apply to weapon when equipped in hand.")]
    [SerializeField] private Vector3 weaponHandRotation = Vector3.zero;
    
    private Gun equippedPrimaryWeapon;
    private Gun equippedSecondaryWeapon;
    private Gun currentWeaponInHand;
    private Inventory inventory;

    public UnityEvent<Gun> onPrimaryEquipped;
    public UnityEvent<Gun> onSecondaryEquipped;
    public UnityEvent<Gun> onWeaponSwitched;

    private void Awake()
    {
        inventory = GetComponent<Inventory>();
        InitializeEquipment();
        if (!string.IsNullOrEmpty(weaponLayerName))
            weaponLayer = LayerMask.NameToLayer(weaponLayerName);
    }

    private void InitializeEquipment()
    {
        if (handSlot == null) return;

        Gun gunInHand = handSlot.GetComponentInChildren<Gun>();
        if (gunInHand != null)
        {
            currentWeaponInHand = gunInHand;
            equippedPrimaryWeapon = gunInHand;
            gunInHand.Unequip();
            gunInHand.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (switchWeaponAction != null)
            switchWeaponAction.action.Enable();
    }

    private void OnDisable()
    {
        if (switchWeaponAction != null)
            switchWeaponAction.action.Disable();
    }

    private void Update()
    {
        // Check for weapon switch input
        if (switchWeaponAction != null && switchWeaponAction.action.WasPerformedThisFrame())
        {
            SwitchWeapon();
        }
    }

    public bool EquipAsPrimary(GunItem gunItem)
    {
        if (gunItem == null || gunItem.GunPrefab == null)
            return false;

        if (equippedPrimaryWeapon != null)
            equippedPrimaryWeapon.gameObject.SetActive(false);

        GameObject weaponInstance = Instantiate(gunItem.GunPrefab, handSlot);
        weaponInstance.name = gunItem.ItemName;
        weaponInstance.transform.localPosition = Vector3.zero;
        weaponInstance.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(weaponInstance, weaponLayer);

        Gun newWeapon = weaponInstance.GetComponent<Gun>();
        if (newWeapon != null)
        {
            newWeapon.SetGunItem(gunItem);
            equippedPrimaryWeapon = newWeapon;
            currentWeaponInHand = equippedPrimaryWeapon;
            equippedPrimaryWeapon.Equip();

            if (equippedSecondaryWeapon != null)
            {
                equippedSecondaryWeapon.transform.SetParent(secondaryRestingSlot);
                equippedSecondaryWeapon.Unequip();
                // Optionally revert layer for secondary weapon
                if (equippedSecondaryWeapon.gameObject != null)
                    SetLayerRecursively(equippedSecondaryWeapon.gameObject, 0); // Default layer
            }

            onPrimaryEquipped?.Invoke(equippedPrimaryWeapon);
            Debug.Log($"Equipped primary weapon in hand: {gunItem.ItemName}");
            return true;
        }

        return false;
    }

    public bool EquipAsSecondary(GunItem gunItem)
    {
        if (gunItem == null || gunItem.GunPrefab == null)
            return false;

        if (equippedSecondaryWeapon != null)
            equippedSecondaryWeapon.gameObject.SetActive(false);

        GameObject weaponInstance = Instantiate(gunItem.GunPrefab, handSlot);
        weaponInstance.name = gunItem.ItemName;
        weaponInstance.transform.localPosition = Vector3.zero;
        weaponInstance.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(weaponInstance, weaponLayer);

        Gun newWeapon = weaponInstance.GetComponent<Gun>();
        if (newWeapon != null)
        {
            newWeapon.SetGunItem(gunItem);
            equippedSecondaryWeapon = newWeapon;
            currentWeaponInHand = equippedSecondaryWeapon;
            equippedSecondaryWeapon.Equip();

            if (equippedPrimaryWeapon != null)
            {
                equippedPrimaryWeapon.transform.SetParent(primaryRestingSlot);
                equippedPrimaryWeapon.Unequip();
                // Optionally revert layer for primary weapon
                if (equippedPrimaryWeapon.gameObject != null)
                    SetLayerRecursively(equippedPrimaryWeapon.gameObject, 0); // Default layer
            }

            onSecondaryEquipped?.Invoke(equippedSecondaryWeapon);
            Debug.Log($"Equipped secondary weapon in hand: {gunItem.ItemName}");
            return true;
        }

        return false;
    }

    public void SwitchWeapon()
    {
        if (equippedPrimaryWeapon == null || equippedSecondaryWeapon == null)
        {
            Debug.Log("Cannot switch: both weapons must be equipped");
            return;
        }

        Gun nextWeapon = currentWeaponInHand == equippedPrimaryWeapon ? equippedSecondaryWeapon : equippedPrimaryWeapon;
        Gun restingWeapon = currentWeaponInHand == equippedPrimaryWeapon ? equippedPrimaryWeapon : equippedSecondaryWeapon;
        Transform nextRestingSlot = nextWeapon == equippedPrimaryWeapon ? primaryRestingSlot : secondaryRestingSlot;

        restingWeapon.transform.SetParent(nextRestingSlot);
        restingWeapon.Unequip();

        nextWeapon.transform.SetParent(handSlot);
        // Force correct local position and rotation in hand
        nextWeapon.transform.localPosition = Vector3.zero;
        nextWeapon.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(nextWeapon.gameObject, weaponLayer);
        nextWeapon.Equip();
        currentWeaponInHand = nextWeapon;
        // Revert resting weapon to default layer

        if (restingWeapon != null && restingWeapon.gameObject != null)
            SetLayerRecursively(restingWeapon.gameObject, 0);

        onWeaponSwitched?.Invoke(currentWeaponInHand);
        Debug.Log($"Switched to {(nextWeapon == equippedPrimaryWeapon ? "primary" : "secondary")} weapon");
    }

    // Recursively set layer for weapon and all children
    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    public Gun GetCurrentWeapon() => currentWeaponInHand;
    public Gun GetPrimaryWeapon() => equippedPrimaryWeapon;
    public Gun GetSecondaryWeapon() => equippedSecondaryWeapon;

    public bool HasPrimaryWeapon() => equippedPrimaryWeapon != null;
    public bool HasSecondaryWeapon() => equippedSecondaryWeapon != null;

    public bool IsGunInHand(Gun gun) => gun == currentWeaponInHand && gun.gameObject.activeInHierarchy;

    public bool IsGunEquipped(Gun gun) => gun == equippedPrimaryWeapon || gun == equippedSecondaryWeapon;

    public bool IsGunPrimaryEquipped(Gun gun) => gun == equippedPrimaryWeapon;

    public bool IsGunSecondaryEquipped(Gun gun) => gun == equippedSecondaryWeapon;

    public Gun.GunType GetCurrentGunType() => currentWeaponInHand != null ? currentWeaponInHand.GetGunType() : Gun.GunType.Pistol;

    public bool IsCurrentWeaponPistol() => GetCurrentGunType() == Gun.GunType.Pistol;

    public bool IsCurrentWeaponAssaultRifle() => GetCurrentGunType() == Gun.GunType.AssaultRifle;
}
