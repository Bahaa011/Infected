using UnityEngine;

[CreateAssetMenu(fileName = "New Water Item", menuName = "Inventory/Consumables/Water Item")]
public class WaterItem : Item
{
    [SerializeField] private float thirstRestore = 30f;
    [SerializeField] private float hungerRestore = 0f;

    public float ThirstRestore => thirstRestore;
    public float HungerRestore => hungerRestore;
}
