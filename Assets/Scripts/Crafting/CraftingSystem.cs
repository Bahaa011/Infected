using System;
using System.Collections.Generic;
using UnityEngine;

public class CraftingSystem : MonoBehaviour
{
    [Serializable]
    private struct ConsumedRequirement
    {
        public Item item;
        public int quantity;

        public ConsumedRequirement(Item item, int quantity)
        {
            this.item = item;
            this.quantity = quantity;
        }
    }

    [SerializeField] private Inventory inventory;
    [SerializeField] private List<CraftingRecipe> craftingRecipes = new();

    private readonly List<ConsumedRequirement> activeCraftConsumed = new();
    private bool isCraftingInProgress;
    private CraftingRecipe activeCraftRecipe;
    private float activeCraftEndTimeRealtime;
    private string statusMessage = "Select a recipe to craft.";
    private Color statusColor = new Color(0.82f, 0.9f, 0.82f, 0.95f);

    public IReadOnlyList<CraftingRecipe> CraftingRecipes => craftingRecipes;
    public bool IsCraftingInProgress => isCraftingInProgress;
    public string StatusMessage => statusMessage;
    public Color StatusColor => statusColor;
    public float CraftingProgress01 => GetCraftingProgress01();
    public Inventory BoundInventory => inventory;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateActiveCraftingTimer();
    }

    public void SetRuntimeInventory(Inventory runtimeInventory)
    {
        if (runtimeInventory != null)
            inventory = runtimeInventory;
    }

    public bool CanCraftRecipe(CraftingRecipe recipe)
    {
        if (recipe == null || inventory == null || recipe.OutputItem == null || recipe.OutputAmount <= 0)
            return false;

        if (recipe.Requirements != null)
        {
            foreach (var req in recipe.Requirements)
            {
                if (req == null || req.Item == null || req.Quantity <= 0)
                    continue;

                if (inventory.GetItemQuantity(req.Item) < req.Quantity)
                    return false;
            }
        }

        // Do not require output space before consuming ingredients, since crafting ingredients
        // can free up slots and make room for crafted output.
        return true;
    }

    public bool TryStartCraft(CraftingRecipe recipe)
    {
        if (isCraftingInProgress)
        {
            SetStatus("Wait until the current recipe finishes.", new Color(1f, 0.77f, 0.5f, 0.95f));
            return false;
        }

        if (inventory == null)
        {
            SetStatus("Craft failed: inventory not found.", new Color(1f, 0.55f, 0.55f, 0.95f));
            return false;
        }

        if (!CanCraftRecipe(recipe))
        {
            SetStatus("Missing required items.", new Color(1f, 0.67f, 0.52f, 0.95f));
            return false;
        }

        activeCraftConsumed.Clear();

        if (recipe.Requirements != null)
        {
            foreach (var req in recipe.Requirements)
            {
                if (req == null || req.Item == null || req.Quantity <= 0)
                    continue;

                bool removed = inventory.RemoveItem(req.Item, req.Quantity);
                if (!removed)
                {
                    RollbackConsumedIngredients();
                    SetStatus("Failed to consume ingredients.", new Color(1f, 0.55f, 0.55f, 0.95f));
                    return false;
                }

                activeCraftConsumed.Add(new ConsumedRequirement(req.Item, req.Quantity));
            }
        }

        if (!inventory.CanAddItem(recipe.OutputItem, recipe.OutputAmount))
        {
            RollbackConsumedIngredients();
            SetStatus("Not enough space for crafted output.", new Color(1f, 0.67f, 0.52f, 0.95f));
            return false;
        }

        activeCraftRecipe = recipe;
        isCraftingInProgress = true;
        activeCraftEndTimeRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, recipe.CraftDurationSeconds);

        if (recipe.CraftDurationSeconds <= 0.01f)
            CompleteActiveCraft();
        else
            SetStatus($"Crafting {GetRecipeDisplayName(recipe)}...", new Color(0.75f, 0.92f, 1f, 0.95f));

        return true;
    }

    private void UpdateActiveCraftingTimer()
    {
        if (!isCraftingInProgress || activeCraftRecipe == null)
            return;

        if (Time.realtimeSinceStartup >= activeCraftEndTimeRealtime)
        {
            CompleteActiveCraft();
        }
        else
        {
            float remaining = Mathf.Max(0f, activeCraftEndTimeRealtime - Time.realtimeSinceStartup);
            SetStatus($"Crafting {GetRecipeDisplayName(activeCraftRecipe)}... {remaining:0.0}s", new Color(0.75f, 0.92f, 1f, 0.95f));
        }
    }

    private void CompleteActiveCraft()
    {
        if (inventory == null || activeCraftRecipe == null)
        {
            ResetActiveCraftState();
            return;
        }

        bool added = inventory.AddItem(activeCraftRecipe.OutputItem, activeCraftRecipe.OutputAmount);
        if (added)
        {
            SetStatus($"Craft complete: {activeCraftRecipe.OutputItem.ItemName} x{activeCraftRecipe.OutputAmount}", new Color(0.66f, 0.95f, 0.66f, 0.95f));
        }
        else
        {
            RollbackConsumedIngredients();
            SetStatus("Craft failed: no room for output. Ingredients returned.", new Color(1f, 0.67f, 0.52f, 0.95f));
        }

        ResetActiveCraftState();
    }

    private float GetCraftingProgress01()
    {
        if (!isCraftingInProgress || activeCraftRecipe == null)
            return 0f;

        float duration = Mathf.Max(0.01f, activeCraftRecipe.CraftDurationSeconds);
        float elapsed = duration - Mathf.Max(0f, activeCraftEndTimeRealtime - Time.realtimeSinceStartup);
        return Mathf.Clamp01(elapsed / duration);
    }

    private void RollbackConsumedIngredients()
    {
        if (inventory == null)
            return;

        for (int i = 0; i < activeCraftConsumed.Count; i++)
        {
            var consumed = activeCraftConsumed[i];
            if (consumed.item == null || consumed.quantity <= 0)
                continue;

            inventory.AddItem(consumed.item, consumed.quantity);
        }

        activeCraftConsumed.Clear();
    }

    private static string GetRecipeDisplayName(CraftingRecipe recipe)
    {
        if (recipe == null)
            return "Recipe";

        if (!string.IsNullOrWhiteSpace(recipe.RecipeName))
            return recipe.RecipeName;

        if (recipe.OutputItem != null)
            return recipe.OutputItem.ItemName;

        return "Recipe";
    }

    private void ResolveReferences()
    {
        if (inventory == null || !inventory.gameObject.activeInHierarchy)
            inventory = GetComponent<Inventory>();

        if (inventory == null)
            inventory = FindAnyObjectByType<Inventory>();
    }

    private void ResetActiveCraftState()
    {
        isCraftingInProgress = false;
        activeCraftRecipe = null;
        activeCraftEndTimeRealtime = 0f;
        activeCraftConsumed.Clear();
    }

    private void SetStatus(string message, Color color)
    {
        statusMessage = string.IsNullOrWhiteSpace(message) ? "Ready." : message;
        statusColor = color;
    }
}
