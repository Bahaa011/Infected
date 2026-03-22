using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class EquipmentManager : MonoBehaviour
{
    [Header("Weapon Layer Switching")]
    [SerializeField] private string weaponLayerName = "Weapon";
    private int weaponLayer = -1;
    [SerializeField] private Transform assaultHandSlot;
    [SerializeField] private Transform pistolHandSlot;
    [SerializeField] private Transform primaryRestingSlot;
    [SerializeField] private Transform secondaryRestingSlot;
    [SerializeField] private InputActionReference switchWeaponAction;
    [Header("Weapon Hand Placement")]
    [Tooltip("Local rotation (in degrees) to apply to weapon when equipped in hand.")]
    [SerializeField] private Vector3 weaponHandRotation = Vector3.zero;
    
    // Gun equipment
    private Gun equippedPrimaryWeapon;
    private Gun equippedSecondaryWeapon;
    private Gun currentWeaponInHand;
    
    // Melee equipment
    private MeleeWeapon equippedMeleeWeapon;
    private bool isMeleeEquipped = false;
    
    private Inventory inventory;

    public UnityEvent<Gun> onPrimaryEquipped;
    public UnityEvent<Gun> onSecondaryEquipped;
    public UnityEvent<Gun> onWeaponSwitched;
    public UnityEvent<MeleeWeapon> onMeleeEquipped;
    public UnityEvent onMeleeUnequipped;

    private void Awake()
    {
        inventory = GetComponent<Inventory>();
        InitializeEquipment();
        if (!string.IsNullOrEmpty(weaponLayerName))
            weaponLayer = LayerMask.NameToLayer(weaponLayerName);
    }

    private void InitializeEquipment()
    {
        if (assaultHandSlot == null && pistolHandSlot == null) return;

        // Check for gun in either hand slot
        Gun gunInHand = null;
        if (pistolHandSlot != null)
            gunInHand = pistolHandSlot.GetComponentInChildren<Gun>();
        if (gunInHand == null && assaultHandSlot != null)
            gunInHand = assaultHandSlot.GetComponentInChildren<Gun>();

        if (gunInHand != null)
        {
            currentWeaponInHand = gunInHand;
            equippedPrimaryWeapon = gunInHand;
            gunInHand.Unequip();
            gunInHand.gameObject.SetActive(false);
        }

        // Check for melee weapon in either hand slot
        MeleeWeapon meleeInHand = null;
        if (pistolHandSlot != null)
            meleeInHand = pistolHandSlot.GetComponentInChildren<MeleeWeapon>();
        if (meleeInHand == null && assaultHandSlot != null)
            meleeInHand = assaultHandSlot.GetComponentInChildren<MeleeWeapon>();

        if (meleeInHand != null)
        {
            equippedMeleeWeapon = meleeInHand;
            meleeInHand.Unequip();
            meleeInHand.gameObject.SetActive(false);
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

        // Check for direct weapon selection inputs
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            EquipPrimary();
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            EquipSecondary();
        }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            HolsterAll();
        }
    }

    public bool EquipAsPrimary(GunItem gunItem)
    {
        Debug.Log($"[EquipmentManager] EquipAsPrimary called with: {(gunItem != null ? gunItem.ItemName : "null")}");
        
        // Handle unequip
        if (gunItem == null)
        {
            if (equippedPrimaryWeapon != null)
            {
                equippedPrimaryWeapon.Unequip();
                equippedPrimaryWeapon.gameObject.SetActive(false);
                equippedPrimaryWeapon = null;
                if (currentWeaponInHand == null && equippedSecondaryWeapon != null)
                {
                    Transform secondaryHandSlot = GetHandSlotForGun(equippedSecondaryWeapon);
                    equippedSecondaryWeapon.transform.SetParent(secondaryHandSlot);
                    equippedSecondaryWeapon.transform.localPosition = Vector3.zero;
                    equippedSecondaryWeapon.transform.localRotation = Quaternion.Euler(weaponHandRotation);
                    equippedSecondaryWeapon.Equip();
                    currentWeaponInHand = equippedSecondaryWeapon;
                }
                else
                {
                    currentWeaponInHand = null;
                }
                onPrimaryEquipped?.Invoke(null);
                return true;
            }
            return false;
        }

        if (gunItem.GunPrefab == null)
        {
            Debug.LogError($"[EquipmentManager] Cannot equip {gunItem.ItemName}: GunPrefab is null! Make sure the GunItem asset has a prefab assigned.");
            return false;
        }

        Transform targetHandSlot = GetHandSlotForGunItem(gunItem);
        
        if (targetHandSlot == null)
        {
            Debug.LogError("[EquipmentManager] Cannot equip: no hand slot is assigned!");
            return false;
        }
        
        if (primaryRestingSlot == null)
        {
            Debug.LogError("[EquipmentManager] Cannot equip: primaryRestingSlot is not assigned!");
            return false;
        }

        // Move current weapon to resting slot if needed
        if (currentWeaponInHand != null)
        {
            if (currentWeaponInHand == equippedSecondaryWeapon)
            {
                currentWeaponInHand.transform.SetParent(secondaryRestingSlot);
                currentWeaponInHand.transform.localPosition = Vector3.zero;
                currentWeaponInHand.transform.localRotation = Quaternion.identity;
                currentWeaponInHand.gameObject.SetActive(true);
                currentWeaponInHand.Unequip();
                if (weaponLayer >= 0) SetLayerRecursively(currentWeaponInHand.gameObject, 0);
            }
        }

        // Instantiate new primary weapon in hand slot (not resting slot)
        GameObject weaponInstance = Instantiate(gunItem.GunPrefab, targetHandSlot);
        weaponInstance.name = gunItem.ItemName;
        weaponInstance.SetActive(true);
        weaponInstance.transform.localPosition = Vector3.zero;
        weaponInstance.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(weaponInstance, weaponLayer);

        Gun newWeapon = weaponInstance.GetComponent<Gun>();
        if (newWeapon != null)
        {
            newWeapon.SetGunItem(gunItem);
            
            // Destroy old primary weapon if exists
            if (equippedPrimaryWeapon != null)
                Destroy(equippedPrimaryWeapon.gameObject);
            
            equippedPrimaryWeapon = newWeapon;
            currentWeaponInHand = newWeapon;
            
            // Actually equip the weapon so it can fire and animations work
            newWeapon.Equip();
            
            Debug.Log($"[EquipmentManager] Primary weapon {gunItem.ItemName} equipped and active!");

            onPrimaryEquipped?.Invoke(equippedPrimaryWeapon);
            return true;
        }

        return false;
    }

    public bool EquipAsSecondary(GunItem gunItem)
    {
        Debug.Log($"[EquipmentManager] EquipAsSecondary called with: {(gunItem != null ? gunItem.ItemName : "null")}");
        
        // Handle unequip
        if (gunItem == null)
        {
            if (equippedSecondaryWeapon != null)
            {
                equippedSecondaryWeapon.Unequip();
                equippedSecondaryWeapon.gameObject.SetActive(false);
                equippedSecondaryWeapon = null;
                if (currentWeaponInHand == null && equippedPrimaryWeapon != null)
                {
                    Transform primaryHandSlot = GetHandSlotForGun(equippedPrimaryWeapon);
                    equippedPrimaryWeapon.transform.SetParent(primaryHandSlot);
                    equippedPrimaryWeapon.transform.localPosition = Vector3.zero;
                    equippedPrimaryWeapon.transform.localRotation = Quaternion.Euler(weaponHandRotation);
                    equippedPrimaryWeapon.Equip();
                    currentWeaponInHand = equippedPrimaryWeapon;
                }
                else
                {
                    currentWeaponInHand = null;
                }
                onSecondaryEquipped?.Invoke(null);
                return true;
            }
            return false;
        }

        if (gunItem.GunPrefab == null)
        {
            Debug.LogError($"[EquipmentManager] Cannot equip {gunItem.ItemName}: GunPrefab is null! Make sure the GunItem asset has a prefab assigned.");
            return false;
        }

        Transform targetHandSlot = GetHandSlotForGunItem(gunItem);
        
        if (targetHandSlot == null)
        {
            Debug.LogError("[EquipmentManager] Cannot equip: no hand slot is assigned!");
            return false;
        }
        
        if (secondaryRestingSlot == null)
        {
            Debug.LogError("[EquipmentManager] Cannot equip: secondaryRestingSlot is not assigned!");
            return false;
        }

        // Move current weapon to resting slot if needed
        if (currentWeaponInHand != null)
        {
            if (currentWeaponInHand == equippedPrimaryWeapon)
            {
                currentWeaponInHand.transform.SetParent(primaryRestingSlot);
                currentWeaponInHand.transform.localPosition = Vector3.zero;
                currentWeaponInHand.transform.localRotation = Quaternion.identity;
                currentWeaponInHand.gameObject.SetActive(true);
                currentWeaponInHand.Unequip();
                if (weaponLayer >= 0) SetLayerRecursively(currentWeaponInHand.gameObject, 0);
            }
        }

        // Instantiate new secondary weapon in hand slot (not resting slot)
        GameObject weaponInstance = Instantiate(gunItem.GunPrefab, targetHandSlot);
        weaponInstance.name = gunItem.ItemName;
        weaponInstance.SetActive(true);
        weaponInstance.transform.localPosition = Vector3.zero;
        weaponInstance.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(weaponInstance, weaponLayer);

        Gun newWeapon = weaponInstance.GetComponent<Gun>();
        if (newWeapon != null)
        {
            newWeapon.SetGunItem(gunItem);
            
            // Destroy old secondary weapon if exists
            if (equippedSecondaryWeapon != null)
                Destroy(equippedSecondaryWeapon.gameObject);
            
            equippedSecondaryWeapon = newWeapon;
            currentWeaponInHand = newWeapon;
            
            // Actually equip the weapon so it can fire and animations work
            newWeapon.Equip();
            
            Debug.Log($"[EquipmentManager] Secondary weapon {gunItem.ItemName} equipped and active!");

            onSecondaryEquipped?.Invoke(equippedSecondaryWeapon);
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
        restingWeapon.transform.localPosition = Vector3.zero;
        restingWeapon.transform.localRotation = Quaternion.identity;
        restingWeapon.gameObject.SetActive(true);
        restingWeapon.Unequip();

        nextWeapon.transform.SetParent(GetHandSlotForGun(nextWeapon));
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
    }

    public void EquipPrimary()
    {
        if (equippedPrimaryWeapon == null)
            return;

        if (currentWeaponInHand == equippedPrimaryWeapon)
            return; // Already equipped

        // Holster current weapon
        if (currentWeaponInHand != null)
        {
            Transform restingSlot = currentWeaponInHand == equippedSecondaryWeapon ? secondaryRestingSlot : primaryRestingSlot;
            currentWeaponInHand.transform.SetParent(restingSlot);
            currentWeaponInHand.transform.localPosition = Vector3.zero;
            currentWeaponInHand.transform.localRotation = Quaternion.identity;
            currentWeaponInHand.gameObject.SetActive(true);
            currentWeaponInHand.Unequip();
            if (weaponLayer >= 0) SetLayerRecursively(currentWeaponInHand.gameObject, 0);
        }

        // Equip primary
        equippedPrimaryWeapon.transform.SetParent(GetHandSlotForGun(equippedPrimaryWeapon));
        equippedPrimaryWeapon.transform.localPosition = Vector3.zero;
        equippedPrimaryWeapon.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(equippedPrimaryWeapon.gameObject, weaponLayer);
        equippedPrimaryWeapon.Equip();
        currentWeaponInHand = equippedPrimaryWeapon;

        onWeaponSwitched?.Invoke(currentWeaponInHand);
    }

    public void EquipSecondary()
    {
        if (equippedSecondaryWeapon == null)
            return;

        if (currentWeaponInHand == equippedSecondaryWeapon)
            return; // Already equipped

        // Holster current weapon
        if (currentWeaponInHand != null)
        {
            Transform restingSlot = currentWeaponInHand == equippedPrimaryWeapon ? primaryRestingSlot : secondaryRestingSlot;
            currentWeaponInHand.transform.SetParent(restingSlot);
            currentWeaponInHand.transform.localPosition = Vector3.zero;
            currentWeaponInHand.transform.localRotation = Quaternion.identity;
            currentWeaponInHand.gameObject.SetActive(true);
            currentWeaponInHand.Unequip();
            if (weaponLayer >= 0) SetLayerRecursively(currentWeaponInHand.gameObject, 0);
        }

        // Equip secondary
        equippedSecondaryWeapon.transform.SetParent(GetHandSlotForGun(equippedSecondaryWeapon));
        equippedSecondaryWeapon.transform.localPosition = Vector3.zero;
        equippedSecondaryWeapon.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(equippedSecondaryWeapon.gameObject, weaponLayer);
        equippedSecondaryWeapon.Equip();
        currentWeaponInHand = equippedSecondaryWeapon;

        onWeaponSwitched?.Invoke(currentWeaponInHand);
    }

    public void HolsterAll()
    {
        if (currentWeaponInHand != null)
        {
            Transform restingSlot = currentWeaponInHand == equippedPrimaryWeapon ? primaryRestingSlot : secondaryRestingSlot;
            currentWeaponInHand.transform.SetParent(restingSlot);
            currentWeaponInHand.transform.localPosition = Vector3.zero;
            currentWeaponInHand.transform.localRotation = Quaternion.identity;
            currentWeaponInHand.gameObject.SetActive(true);
            currentWeaponInHand.Unequip();
            if (weaponLayer >= 0) SetLayerRecursively(currentWeaponInHand.gameObject, 0);
            currentWeaponInHand = null;

            onWeaponSwitched?.Invoke(null);
        }
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

    private Transform GetDefaultHandSlot()
    {
        if (pistolHandSlot != null) return pistolHandSlot;
        if (assaultHandSlot != null) return assaultHandSlot;
        return null;
    }

    private Transform GetHandSlotForGunType(Gun.GunType gunType)
    {
        if (gunType == Gun.GunType.AssaultRifle)
            return assaultHandSlot != null ? assaultHandSlot : pistolHandSlot;

        return pistolHandSlot != null ? pistolHandSlot : assaultHandSlot;
    }

    private Transform GetHandSlotForGun(Gun gun)
    {
        if (gun == null)
            return GetDefaultHandSlot();

        return GetHandSlotForGunType(gun.GetGunType());
    }

    private Transform GetHandSlotForGunItem(GunItem gunItem)
    {
        if (gunItem == null || gunItem.GunPrefab == null)
            return GetDefaultHandSlot();

        Gun gunPrefabComponent = gunItem.GunPrefab.GetComponent<Gun>();
        if (gunPrefabComponent == null)
            return GetDefaultHandSlot();

        return GetHandSlotForGunType(gunPrefabComponent.GetGunType());
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

    public bool EquipMeleeWeapon(MeleeWeaponItem meleeItem)
    {
        Debug.Log($"[EquipmentManager] EquipMeleeWeapon called with: {(meleeItem != null ? meleeItem.ItemName : "null")}");

        // Handle unequip
        if (meleeItem == null)
        {
            if (equippedMeleeWeapon != null)
            {
                equippedMeleeWeapon.Unequip();
                equippedMeleeWeapon.gameObject.SetActive(false);
                equippedMeleeWeapon = null;
                isMeleeEquipped = false;
                onMeleeUnequipped?.Invoke();
                return true;
            }
            return false;
        }

        if (meleeItem.WeaponPrefab == null)
        {
            Debug.LogError($"[EquipmentManager] Cannot equip {meleeItem.ItemName}: WeaponPrefab is null!");
            return false;
        }

        Transform meleeHandSlot = GetDefaultHandSlot();
        if (meleeHandSlot == null)
        {
            Debug.LogError("[EquipmentManager] Cannot equip: no hand slot is assigned!");
            return false;
        }

        // Unequip current weapon if needed
        if (currentWeaponInHand != null && currentWeaponInHand is Gun)
        {
            currentWeaponInHand.Unequip();
            currentWeaponInHand.gameObject.SetActive(false);
        }

        // Destroy old melee weapon if exists
        if (equippedMeleeWeapon != null)
            Destroy(equippedMeleeWeapon.gameObject);

        // Instantiate new melee weapon
        GameObject weaponInstance = Instantiate(meleeItem.WeaponPrefab, meleeHandSlot);
        weaponInstance.name = meleeItem.ItemName;
        weaponInstance.SetActive(true);
        weaponInstance.transform.localPosition = Vector3.zero;
        weaponInstance.transform.localRotation = Quaternion.Euler(weaponHandRotation);
        if (weaponLayer >= 0) SetLayerRecursively(weaponInstance, weaponLayer);

        MeleeWeapon newWeapon = weaponInstance.GetComponent<MeleeWeapon>();
        if (newWeapon != null)
        {
            newWeapon.SetWeaponItem(meleeItem);
            equippedMeleeWeapon = newWeapon;
            currentWeaponInHand = null; // Clear gun reference
            isMeleeEquipped = true;

            // Equip the weapon
            newWeapon.Equip();

            Debug.Log($"[EquipmentManager] Melee weapon {meleeItem.ItemName} equipped and active!");

            onMeleeEquipped?.Invoke(equippedMeleeWeapon);
            return true;
        }

        return false;
    }

    public bool IsMeleeEquipped() => isMeleeEquipped && equippedMeleeWeapon != null;

    public MeleeWeapon GetMeleeWeapon() => equippedMeleeWeapon;

    public object GetCurrentWeaponInHand()
    {
        if (isMeleeEquipped)
            return equippedMeleeWeapon;
        return currentWeaponInHand;
    }
}
