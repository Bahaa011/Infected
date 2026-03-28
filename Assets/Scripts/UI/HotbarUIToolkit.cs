using UnityEngine;
using UnityEngine.UIElements;

public class HotbarUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private EquipmentManager equipmentManager;

    private VisualElement primarySlot;
    private VisualElement secondarySlot;
    private VisualElement meleeSlot;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        
        var root = uiDocument.rootVisualElement;
        primarySlot = root.Q<VisualElement>(className: "primary-slot");
        secondarySlot = root.Q<VisualElement>(className: "secondary-slot");
        meleeSlot = root.Q<VisualElement>(className: "melee-slot");
    }

    private void OnEnable()
    {
        if (equipmentManager == null)
            equipmentManager = FindFirstObjectByType<EquipmentManager>();
        
        if (equipmentManager != null)
        {
            equipmentManager.onPrimaryEquipped.AddListener(OnPrimaryEquipped);
            equipmentManager.onSecondaryEquipped.AddListener(OnSecondaryEquipped);
            equipmentManager.onMeleeEquipped.AddListener(OnMeleeEquipped);
            equipmentManager.onMeleeUnequipped.AddListener(OnMeleeUnequipped);

            // Initialize current state
            OnPrimaryEquipped(equipmentManager.GetPrimaryWeapon());
            OnSecondaryEquipped(equipmentManager.GetSecondaryWeapon());
            OnMeleeEquipped(equipmentManager.GetMeleeWeapon());
        }
    }

    private void OnDisable()
    {
        if (equipmentManager != null)
        {
            equipmentManager.onPrimaryEquipped.RemoveListener(OnPrimaryEquipped);
            equipmentManager.onSecondaryEquipped.RemoveListener(OnSecondaryEquipped);
            equipmentManager.onMeleeEquipped.RemoveListener(OnMeleeEquipped);
            equipmentManager.onMeleeUnequipped.RemoveListener(OnMeleeUnequipped);
        }
    }

    private void OnPrimaryEquipped(Gun gun)
    {
        if (gun != null)
        {
            GunItem gunItem = gun.GetGunItem();
            if (gunItem != null && gunItem.Icon != null)
            {
                Image icon = primarySlot.Q<Image>(className: "primary-icon");
                icon.sprite = gunItem.Icon;
                icon.style.display = DisplayStyle.Flex;
            }
            else
            {
                Image icon = primarySlot.Q<Image>(className: "primary-icon");
                icon.style.display = DisplayStyle.None;
            }
        }
        else
        {
            Image icon = primarySlot.Q<Image>(className: "primary-icon");
            icon.style.display = DisplayStyle.None;
        }
    }

    private void OnSecondaryEquipped(Gun gun)
    {
        if (gun != null)
        {
            GunItem gunItem = gun.GetGunItem();
            if (gunItem != null && gunItem.Icon != null)
            {
                Image icon = secondarySlot.Q<Image>(className: "secondary-icon");
                icon.sprite = gunItem.Icon;
                icon.style.display = DisplayStyle.Flex;
            }
            else
            {
                Image icon = secondarySlot.Q<Image>(className: "secondary-icon");
                icon.style.display = DisplayStyle.None;
            }
        }
        else
        {
            Image icon = secondarySlot.Q<Image>(className: "secondary-icon");
            icon.style.display = DisplayStyle.None;
        }
    }

    private void OnMeleeEquipped(MeleeWeapon melee)
    {
        if (meleeSlot == null)
            return;

        Image icon = meleeSlot.Q<Image>(className: "melee-icon");
        if (icon == null)
            return;

        if (melee != null)
        {
            MeleeWeaponItem meleeItem = melee.GetWeaponItem();
            if (meleeItem != null && meleeItem.Icon != null)
            {
                icon.sprite = meleeItem.Icon;
                icon.style.display = DisplayStyle.Flex;
                return;
            }
        }

        icon.style.display = DisplayStyle.None;
    }

    private void OnMeleeUnequipped()
    {
        if (meleeSlot == null)
            return;

        Image icon = meleeSlot.Q<Image>(className: "melee-icon");
        if (icon != null)
            icon.style.display = DisplayStyle.None;
    }
}
