using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Crafting Recipe", menuName = "Crafting/Recipe")]
public class CraftingRecipe : ScriptableObject
{
    public enum CraftingCategory
    {
        Ammunition,
        Weapons,
        HealingItems,
        Food,
        Water
    }

    [Serializable]
    public class Requirement
    {
        [SerializeField] private Item item;
        [SerializeField] private int quantity = 1;

        public Item Item => item;
        public int Quantity => Mathf.Max(1, quantity);
    }

    [SerializeField] private string recipeName;
    [SerializeField] private string description;
    [SerializeField] private CraftingCategory category = CraftingCategory.Ammunition;
    [SerializeField] private List<Requirement> requirements = new();
    [SerializeField] private Item outputItem;
    [SerializeField] private int outputAmount = 1;
    [Min(0f)]
    [SerializeField] private float craftDurationSeconds = 1.5f;

    public string RecipeName => recipeName;
    public string Description => description;
    public CraftingCategory Category => category;
    public List<Requirement> Requirements => requirements;
    public Item OutputItem => outputItem;
    public int OutputAmount => Mathf.Max(1, outputAmount);
    public float CraftDurationSeconds => Mathf.Max(0f, craftDurationSeconds);
}
