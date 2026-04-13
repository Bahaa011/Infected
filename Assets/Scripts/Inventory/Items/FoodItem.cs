using UnityEngine;

[CreateAssetMenu(fileName = "New Food Item", menuName = "Inventory/Consumables/Food Item")]
public class FoodItem : Item
{
    [SerializeField] private float hungerRestore = 25f;
    [SerializeField] private float thirstRestore = 0f;

    public float HungerRestore => hungerRestore;
    public float ThirstRestore => thirstRestore;
}
