using System;
using System.Collections.Generic;
using UnityEngine;

public enum LootRarity
{
    Common,
    Uncommon,
    Rare,
    Epic
}

[Serializable]
public struct LootItemRarityOverride
{
    public Item item;
    public LootRarity rarity;
}

[Serializable]
public class LootRarityDropSettings
{
    [SerializeField] public bool autoDiscoverLoadedItemAssets = true;
    [SerializeField] public List<Item> lootItemPool = new List<Item>();
    [SerializeField] public int minLootEntries = 2;
    [SerializeField] public int maxLootEntries = 6;
    [SerializeField] public int maxRollAttempts = 40;

    [SerializeField] public float commonWeight = 60f;
    [SerializeField] public float uncommonWeight = 25f;
    [SerializeField] public float rareWeight = 12f;
    [SerializeField] public float epicWeight = 3f;

    [SerializeField] public Vector2Int commonQuantityRange = new Vector2Int(1, 3);
    [SerializeField] public Vector2Int uncommonQuantityRange = new Vector2Int(1, 2);
    [SerializeField] public Vector2Int rareQuantityRange = new Vector2Int(1, 1);
    [SerializeField] public Vector2Int epicQuantityRange = new Vector2Int(1, 1);

    [SerializeField] public LootItemRarityOverride[] rarityOverrides;
}

public static class LootRarityDropGenerator
{
    public static int GenerateLoot(Inventory inventory, LootRarityDropSettings settings)
    {
        if (inventory == null || settings == null)
            return 0;

        List<Item> pool = BuildLootPool(settings);
        if (pool.Count == 0)
            return 0;

        int minEntries = Mathf.Max(1, settings.minLootEntries);
        int maxEntries = Mathf.Max(minEntries, settings.maxLootEntries);
        int targetEntries = UnityEngine.Random.Range(minEntries, maxEntries + 1);
        int attempts = 0;
        int createdEntries = 0;
        int maxAttemptsSafe = Mathf.Max(targetEntries * 4, settings.maxRollAttempts);

        while (createdEntries < targetEntries && attempts < maxAttemptsSafe)
        {
            attempts++;

            LootRarity rolledRarity = RollRarity(settings);
            Item chosenItem = RollItem(pool, rolledRarity, settings);
            if (chosenItem == null)
                continue;

            int quantity = RollQuantity(rolledRarity, chosenItem, settings);
            if (!inventory.AddItem(chosenItem, quantity))
                break;

            createdEntries++;
        }

        return createdEntries;
    }

    private static List<Item> BuildLootPool(LootRarityDropSettings settings)
    {
        HashSet<Item> uniqueItems = new HashSet<Item>();

        if (settings.lootItemPool != null)
        {
            for (int i = 0; i < settings.lootItemPool.Count; i++)
            {
                Item item = settings.lootItemPool[i];
                if (item != null)
                    uniqueItems.Add(item);
            }
        }

        if (settings.autoDiscoverLoadedItemAssets)
        {
            Item[] discovered = Resources.FindObjectsOfTypeAll<Item>();
            for (int i = 0; i < discovered.Length; i++)
            {
                Item item = discovered[i];
                if (item != null)
                    uniqueItems.Add(item);
            }
        }

        return new List<Item>(uniqueItems);
    }

    private static LootRarity RollRarity(LootRarityDropSettings settings)
    {
        float c = Mathf.Max(0f, settings.commonWeight);
        float u = Mathf.Max(0f, settings.uncommonWeight);
        float r = Mathf.Max(0f, settings.rareWeight);
        float e = Mathf.Max(0f, settings.epicWeight);
        float total = c + u + r + e;

        if (total <= 0f)
            return LootRarity.Common;

        float roll = UnityEngine.Random.value * total;
        if (roll < c) return LootRarity.Common;
        roll -= c;
        if (roll < u) return LootRarity.Uncommon;
        roll -= u;
        if (roll < r) return LootRarity.Rare;
        return LootRarity.Epic;
    }

    private static Item RollItem(List<Item> pool, LootRarity targetRarity, LootRarityDropSettings settings)
    {
        if (pool == null || pool.Count == 0)
            return null;

        List<Item> filtered = new List<Item>();
        for (int i = 0; i < pool.Count; i++)
        {
            Item item = pool[i];
            if (item == null)
                continue;

            if (GetItemRarity(item, settings.rarityOverrides) == targetRarity)
                filtered.Add(item);
        }

        List<Item> source = filtered.Count > 0 ? filtered : pool;
        return source[UnityEngine.Random.Range(0, source.Count)];
    }

    private static LootRarity GetItemRarity(Item item, LootItemRarityOverride[] overrides)
    {
        if (item == null)
            return LootRarity.Common;

        if (overrides != null)
        {
            for (int i = 0; i < overrides.Length; i++)
            {
                if (overrides[i].item == item)
                    return overrides[i].rarity;
            }
        }

        if (item is GunItem)
            return LootRarity.Rare;
        if (item is MeleeWeaponItem)
            return LootRarity.Uncommon;
        if (item is MagazineItem)
            return LootRarity.Uncommon;
        if (item is BandageItem)
            return LootRarity.Common;
        if (item is FoodItem)
            return LootRarity.Common;
        if (item is WaterItem)
            return LootRarity.Common;

        return LootRarity.Common;
    }

    private static int RollQuantity(LootRarity rarity, Item item, LootRarityDropSettings settings)
    {
        Vector2Int range = rarity switch
        {
            LootRarity.Uncommon => settings.uncommonQuantityRange,
            LootRarity.Rare => settings.rareQuantityRange,
            LootRarity.Epic => settings.epicQuantityRange,
            _ => settings.commonQuantityRange
        };

        int minQty = Mathf.Max(1, Mathf.Min(range.x, range.y));
        int maxQty = Mathf.Max(minQty, Mathf.Max(range.x, range.y));

        if (item is GunItem || item is MeleeWeaponItem)
            return 1;

        return UnityEngine.Random.Range(minQty, maxQty + 1);
    }
}
