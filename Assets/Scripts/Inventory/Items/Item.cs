using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class Item : ScriptableObject
{
    [SerializeField] private string itemName;
    [SerializeField] private string description;
    [SerializeField] private float weight; // Weight per unit
    [SerializeField] protected int maxStackSize = 1;
    [SerializeField] private Sprite icon;
    [SerializeField] private int id; // Unique ID for this item

    public string ItemName => itemName;
    public string Description => description;
    public float Weight => weight;
    public virtual int MaxStackSize => maxStackSize;
    public Sprite Icon => icon;
    public int ID => id;

    private void OnValidate()
    {
        if (weight < 0)
            weight = 0;
        if (maxStackSize < 1)
            maxStackSize = 1;
    }
}
