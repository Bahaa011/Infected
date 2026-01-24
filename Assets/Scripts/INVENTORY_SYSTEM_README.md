# 🎮 Complete Inventory & Weapon System

## What's Been Created

### Core Scripts
1. **GunPickup.cs** - Allows picking up guns from the world
2. **GunItem.cs** - Gun item scriptable object for inventory storage
3. **EquipmentManager.cs** - Manages primary/secondary weapon slots
4. **InventoryItemUI.cs** - UI for displaying and equipping guns

### Files Modified
- **InputSystem_Actions.inputactions** - Added "Pickup" action with "E" key binding

## Complete Workflow

### 1. Player Approaches Gun
- Gun has **GunPickup** component with trigger collider
- Player gets notification when in range

### 2. Player Presses E
- GunPickup detects input
- Creates a **GunItem** and adds to inventory
- Gun disappears from world

### 3. Player Opens Inventory
- Sees gun with icon and weight
- Can click "Equip as Primary" or "Equip as Secondary"

### 4. Gun Is Equipped
- **EquipmentManager** instantiates gun in correct weapon slot
- Gun is ready to fire
- Player can press Q to switch between primary/secondary

### 5. Player Can Carry Multiple Guns
- Primary weapon (equipped, ready to fire)
- Secondary weapon (in slot, unequipped)
- Additional guns stored in inventory

## Quick Start Checklist

- [ ] Create GunItem assets for each gun type
- [ ] Add GunPickup component to gun prefabs in scenes
- [ ] Add EquipmentManager to player with weapon slots
- [ ] Create weapon slot hierarchy (PrimarySlot, SecondarySlot)
- [ ] Connect InputSystem Pickup action
- [ ] Test picking up a gun with E
- [ ] Test equipping guns to primary/secondary
- [ ] Create simple inventory UI to display guns

## Key Features

✅ **Pick up guns** with proximity detection
✅ **Inventory storage** with weight penalties  
✅ **Dual weapon slots** (primary + secondary)
✅ **Weapon switching** between loaded weapons
✅ **Inventory UI** for managing weapons
✅ **Event system** for animations/sounds
✅ **Full serialization** - can save/load weapon state

## Example Input Actions

- **E** - Pick up gun when in range
- **Q** - Switch between primary/secondary (add to input system)
- **Left Click** - Fire current weapon
- **Right Click** - Aim

## Tips

- Set **pickupRadius** in GunPickup to 2-3 meters for comfortable pickup
- Make gun **GunItems** non-stackable (maxStackSize = 1)
- Add **weight penalties** to encourage strategic loadouts
- Use **EquipmentManager events** to trigger reload animations
- Consider adding **ammo count** UI over equipped weapon

## Support Files

📖 **SETUP_GUIDE.md** - Detailed step-by-step setup instructions
