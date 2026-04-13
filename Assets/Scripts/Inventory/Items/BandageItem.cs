using UnityEngine;

[CreateAssetMenu(fileName = "New Bandage Item", menuName = "Inventory/Medical/Bandage Item")]
public class BandageItem : Item
{
    [SerializeField] private float healModifier = 1f;

    public float HealModifier => healModifier;
}
