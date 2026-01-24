# Inventory & Weapon System Setup Guide

## Overview
This system allows players to pick up guns, store them in inventory, and equip them as primary/secondary weapons.

## Components Created

1. **GunPickup.cs** - Placed on gun prefabs in the world
2. **GunItem.cs** - ScriptableObject representing a gun in inventory
3. **EquipmentManager.cs** - Manages primary/secondary weapon slots
4. **Updated Inventory.cs** - Already exists, supports any Item

## Setup Instructions

### Step 1: Input System Setup
Add a new Input Action called "Pickup" to your InputSystem_Actions:
1. Open `InputSystem_Actions.inputactions`
2. Create a new Action Map called "Gameplay" (if not exists)
3. Add Action: "Pickup" with binding "E" (or your preferred key)
4. Save

### Step 2: Create Gun Items
For each gun you want to pick up:
1. Right-click in Project > Create > Inventory > Gun Item
2. Name it (e.g., "Rifle_Item")
3. Set properties:
   - gunName: "Rifle" (display name)
   - weight: 2.5 (or appropriate weight)
   - maxStackSize: 1 (guns don't stack)
   - icon: Drag a sprite for inventory display
4. Drag the gun PREFAB into gunPrefab field

### Step 3: Set Up Gun Prefabs in World
For each gun placed in the scene:
1. Select the gun GameObject
2. Add component: GunPickup
3. Drag the GunItem asset into "Gun" field (the GunPickup script will auto-find it)
4. Set Pickup Radius (e.g., 2 meters)
5. Assign Pickup Action reference from InputSystem_Actions

### Step 4: Add EquipmentManager to Player
1. Select Player GameObject
2. Add component: EquipmentManager
3. Create two empty child objects: "PrimarySlot" and "SecondarySlot"
4. Drag these into the respective slot fields
5. Connect events (optional, for UI feedback)

### Step 5: Update Player Script
Open Player.cs and ensure it has EquipmentManager reference:

```csharp
private EquipmentManager equipmentManager;

private void Awake()
{
    // ... existing code ...
    equipmentManager = GetComponent<EquipmentManager>();
}

public EquipmentManager GetEquipmentManager() => equipmentManager;
```

### Step 6: Create Inventory UI (Optional but Recommended)
Display gun items in UI:
1. Create a Canvas with a grid layout
2. Update InventoryUI.cs to show GunItem icons
3. Add buttons to equip guns as primary/secondary:

```csharp
public void EquipGunAsPrimary(GunItem gunItem)
{
    equipmentManager.EquipAsPrimary(gunItem);
}

public void EquipGunAsSecondary(GunItem gunItem)
{
    equipmentManager.EquipAsSecondary(gunItem);
}
```

## How It Works

### Pickup Flow:
1. Player approaches gun (within GunPickup radius)
2. Player presses "E" (Pickup action)
3. GunPickup creates a GunItem and adds it to inventory
4. Gun disappears from world (disabled)

### Equipping Flow:
1. Player clicks gun in inventory UI
2. Calls EquipmentManager.EquipAsPrimary() or EquipAsSecondary()
3. Gun prefab instantiated in weapon slot
4. Gun.Equip() called, making it ready to fire

### Switching Flow:
1. Press weapon switch key (bind to EquipmentManager.SwitchWeapon())
2. Current weapon unequipped, other weapon equipped
3. Player can now fire with the new weapon

## Key Features

✅ **Weight System** - Heavier guns slow you down
✅ **Inventory Limit** - Can't carry more than weight limit
✅ **Primary/Secondary Slots** - Carry two weapons
✅ **Weapon Switching** - Quick swap between loaded weapons
✅ **Pickup Radius** - Configurable detection range
✅ **Event System** - Hook into weapon changes for animations/UI

## Troubleshooting

**"Player out of range" keeps appearing?**
- Make sure Player has Collider component (not marked as trigger)
- Make sure GunPickup collider is marked as Trigger

**Can't pick up gun?**
- Check InputSystem is properly set up with "Pickup" action
- Check Player has Inventory component
- Look at Console for debug messages

**Gun appears but can't fire?**
- Make sure Gun script has bulletPrefab assigned
- Check mainCamera exists in scene
- Verify gun firePoint transform is set

**Gun won't disappear after pickup?**
- Check GunPickup component exists on gun
- Verify isPickedUp flag is being set
