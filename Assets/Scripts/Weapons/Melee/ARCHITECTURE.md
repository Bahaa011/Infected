# Melee Weapon System Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                      PLAYER CONTROLLER                       │
│  (Input, Movement, Health, Stamina, Skills)                 │
└──────────────┬──────────────────────────────────────────────┘
               │
        ┌──────▼───────────────────────────┐
        │   EQUIPMENT MANAGER              │
        │  (Weapon equip/unequip)          │
        │  Manages: Gun + Melee            │
        └──────┬──────────────┬────────────┘
               │              │
      ┌────────▼──────┐  ┌────▼──────────┐
      │  GUN SYSTEM   │  │ MELEE WEAPON  │
      │  (Guns)       │  │  (New)        │
      │  - Fire       │  │  - Attack     │
      │  - Reload     │  │  - Combo      │
      │  - Ammo       │  │  - Hit detect │
      └───────┬───────┘  └────┬──────────┘
              │               │
              │        ┌──────▼─────────────┐
              │        │ MELEE WEAPON ITEM  │
              │        │ (Scriptable Object)│
              │        │ - Damage: 25       │
              │        │ - Speed: 1.5/sec   │
              │        │ - Range: 2.5m      │
              │        │ - Type: Sword/Axe  │
              │        └────────────────────┘
              │
      ┌───────▼────────────────────────┐
      │   INVENTORY SYSTEM             │
      │  (Store weapons, track weight) │
      └────────────────────────────────┘
                    │
      ┌─────────────▼──────────────┐
      │  ANIMATOR                  │
      │  (Animation playback)      │
      │  - Weapon types            │
      │  - Combo index             │
      │  - Attack trigger          │
      └────────────────────────────┘
                    │
      ┌─────────────▼──────────────┐
      │  SKILL SYSTEM              │
      │  - Strength XP on hit      │
      │  - +50% damage bonus       │
      └────────────────────────────┘
                    │
      ┌─────────────▼──────────────┐
      │  ENEMY SYSTEM              │
      │  (Implements IDamageable)  │
      │  - Take damage             │
      │  - Play hit animation      │
      │  - Die when health = 0     │
      └────────────────────────────┘
```

## Attack Flow Diagram

```
Player Input (Attack)
        │
        ▼
MeleeWeapon.Update()
        │
        ├─ Check: Can attack? (stamina, cooldown)
        │   └─ No → Return
        │
        ├─ Yes ↓
        ▼
TryAttack()
        │
        ├─ Check: Attack cooldown passed?
        │   └─ No → Return
        │
        ├─ Yes ↓
        ▼
PerformAttack()
        │
        ├─ Drain stamina (15)
        ├─ Play attack animation
        ├─ Update combo count
        ├─ Fire onAttackStarted event
        └─ Start DamageDetectionRoutine()
                │
                ▼
        While (attack duration < 0.5s)
                │
                ├─ DetectHitsInRange()
                │   │
                │   ├─ Physics.OverlapSphere()
                │   ├─ For each collider:
                │   │   ├─ Is IDamageable?
                │   │   └─ Not already hit?
                │   │       ▼
                │   │       DealDamageToTarget()
                │   │       │
                │   │       ├─ Calculate damage (base + skill bonus)
                │   │       ├─ Apply knockback
                │   │       ├─ Call TakeDamage()
                │   │       ├─ Fire onAttackDealt event
                │   │       └─ Register Strength XP
                │   │
                │   └─ Mark as hit (prevent double-hit)
                │
                └─ Yield until next frame
                        │
                        ▼
                Fire onAttackEnded event
```

## Data Flow: Damage Calculation

```
MeleeWeaponItem (Scriptable Object)
    │
    └─ baseDamage: 25
            │
            ▼
    MeleeWeapon.GetBaseDamage()
            │
            ▼
    PlayerSkills.GetStrengthDamageBonus()
    │
    ├─ Get Strength skill level (1-10)
    ├─ Calculate bonus: (level-1) / 10 * 0.5
    │   Example: level 6 → (5/10)*0.5 = 0.25 = 25% bonus
    │
    └─ Return bonus multiplier
            │
            ▼
    finalDamage = 25 * (1 + bonus)
    Example: 25 * (1 + 0.25) = 31.25
            │
            ▼
    Enemy.TakeDamage(31.25)
```

## Combo System State Machine

```
┌──────────────────────────────────────────┐
│        COMBO SYSTEM STATE MACHINE         │
└──────────────────────────────────────────┘

Start: ComboCount = 0, LastComboTime = Now
│
▼
Update()
│
├─ Check: Time since combo > 2 seconds?
│   ├─ Yes: Reset ComboCount to 0
│   └─ No: Keep current combo
│
├─ Player presses Attack
│   │
│   ▼
│ ComboCount++
│ ComboCount = ComboCount % 3    (0, 1, 2, then wrap to 0)
│ LastComboTime = Now
│   │
│   ▼
│ Set animator ComboIndex = ComboCount
│ Play attack animation based on ComboIndex
│
└─ Attack completes
   │
   ├─ Combo 0: First attack
   ├─ Combo 1: Second attack (if within 2 seconds)
   ├─ Combo 2: Third attack (if within 2 seconds)
   │   └─ Resets to 0 for next cycle
   │
   └─ Or: Timeout (2+ seconds idle) → Reset to 0
```

## Class Relationships

```
┌──────────────────────────────────────────────────────┐
│ Player (MonoBehaviour)                               │
│  - Controls input, health, stamina                   │
│  - References: EquipmentManager, Inventory, Skills  │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│ EquipmentManager (MonoBehaviour)                     │
│  - Manages Gun + MeleeWeapon equipment               │
│  - Can have max 2 guns OR 1 melee weapon            │
│  - Methods: EquipMeleeWeapon(), IsMeleeEquipped()   │
│  - Events: onMeleeEquipped, onMeleeUnequipped       │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│ MeleeWeapon (MonoBehaviour)                          │
│  - Attached to weapon prefab                         │
│  - Handles attacks and damage detection              │
│  - Methods: Equip(), Unequip(), TryAttack()         │
│  - Events: onAttackDealt, onHitTarget               │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│ MeleeWeaponItem (ScriptableObject)                   │
│  - Asset definition for melee weapons                │
│  - Inherits from Item (stackSize=1)                  │
│  - Properties: damage, speed, range, etc.            │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│ IDamageable (Interface)                              │
│  - Implemented by: Zombie, CustomEnemy              │
│  - Method: TakeDamage(float damage)                 │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│ PlayerSkills (MonoBehaviour)                         │
│  - Tracks skill levels and XP                        │
│  - New: Strength skill for melee bonuses            │
│  - Methods: RegisterMeleeAttack(), GetStrengthBonus()│
└──────────────────────────────────────────────────────┘
```

## Equipment Switching Flow

```
Initial State: Gun equipped

Player Input (Switch to Melee)
        │
        ▼
EquipmentManager.EquipMeleeWeapon(swordItem)
        │
        ├─ Unequip current gun
        │   ├─ Gun.Unequip()
        │   └─ animator.SetLayerWeight(1, 0)
        │
        ├─ Instantiate sword prefab
        │   └─ Attach to handSlot
        │
        ├─ Get MeleeWeapon component
        │
        ├─ MeleeWeapon.SetWeaponItem(swordItem)
        │   └─ Copy damage, speed, range from item
        │
        ├─ MeleeWeapon.Equip()
        │   ├─ animator.SetLayerWeight(1, 1)
        │   ├─ animator.SetBool("isMelee", true)
        │   ├─ animator.SetBool("isSword", true)
        │   └─ Set weapon layer
        │
        ├─ Fire onMeleeEquipped event
        │
        └─ Ready to attack!

Later: Switch back to Gun

Player Input (Switch to Gun via EquipmentManager)
        │
        ▼
EquipmentManager.EquipAsPrimary(gunItem)
        │
        ├─ Unequip melee
        │   ├─ MeleeWeapon.Unequip()
        │   └─ animator.SetBool("isMelee", false)
        │
        ├─ Equip gun (existing code)
        │   └─ Gun.Equip()
        │
        └─ Ready to shoot!
```

## Animator State Transitions

```
┌─────────────────────────────────────────────────────┐
│              ANIMATOR LAYER 1 (Weapon)              │
└─────────────────────────────────────────────────────┘

    [Idle]
      ▲ ▼
      │ HasWeapon: false
      │
      ├──▶ [Armed Idle]
      │        ▲
      │        │ Attack trigger
      │        │
      │      [Attack]
      │    ╱   │    ╲
      │   ╱    │     ╲
      Combo   Combo   Combo
       0      1       2
       ▼
     [Attack Anim 0]
       │ (0.5s)
       ▼
     [Armed Idle]
       │ (if more attacks within 2s)
       ▼ ComboIndex++
     [Attack Anim 1]
       │ (0.5s)
       ▼
     [Armed Idle]
       │ (if more attacks within 2s)
       ▼ ComboIndex++
     [Attack Anim 2]
       │ (0.5s)
       ▼
     [Armed Idle] ─→ (timeout 2s) ─→ Reset ComboIndex to 0
```

## Stamina Integration

```
Player Stamina Pool: 100

Attack Cost: 15 stamina per swing

Timeline:
- T=0s:    Player has 100 stamina
- T=0s:    Player swings sword
           │
           ├─ Cost: 15 stamina
           └─ New total: 85 stamina
- T=1.5s:  Stamina regen: +30/sec
           │
           └─ New total: 100 stamina (max)
- T=1.5s:  Player can attack again
```

## File Dependency Map

```
MeleeWeapon.cs
├─ Requires: UnityEngine (core)
├─ Requires: IDamageable (same file)
├─ Requires: PlayerSkills (parent)
├─ Requires: Inventory (parent)
├─ Requires: Player (parent)
├─ Requires: Animator (assigned)
└─ Uses: Physics.OverlapSphere()

MeleeWeaponItem.cs
├─ Requires: Item.cs (parent class)
└─ No other dependencies

EquipmentManager.cs (modified)
├─ Existing: Gun, GunItem
├─ Added: MeleeWeapon, MeleeWeaponItem
└─ No circular dependencies

PlayerSkills.cs (modified)
├─ Added: Strength skill enum
├─ Added: GetStrengthDamageBonus()
├─ Added: RegisterMeleeAttack()
└─ Called by: MeleeWeapon.cs

Zombie.cs (modified)
├─ Changed: Now implements IDamageable
├─ Existing: TakeDamage() method
└─ Called by: MeleeWeapon.cs
```

---

## Performance Metrics

| Operation | Cost | Notes |
|-----------|------|-------|
| OverlapSphere | O(n) colliders | Frame-based, only during attack window |
| Damage calc | O(1) | Simple float math |
| Skill XP | O(1) | One AddGeneralXP call per hit |
| Animation | GPU | Layer weight blending |
| Memory | ~1-2 MB | Per weapon instance |

---

## Integration Points

✅ **Player System**: Stamina drain, health updates  
✅ **Inventory**: Add/remove weapons  
✅ **Equipment Manager**: Equip/unequip management  
✅ **Animation**: Combo routing, layer switching  
✅ **Skill System**: Strength XP, damage bonus  
✅ **Enemy AI**: Damage delivery via IDamageable  
✅ **Input**: Attack action shared with guns  

No conflicts with existing systems!
