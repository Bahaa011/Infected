using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class GameSaveData
{
    public int version = 1;
    public int slotIndex = 1;
    public string slotName = "Slot 1";
    public string savedAtUtc;
    public float totalGameDays;
    public PlayerSaveData player = new PlayerSaveData();
    public List<LootContainerSaveData> lootContainers = new List<LootContainerSaveData>();
}

[Serializable]
public class PlayerSaveData
{
    public SerializableVector3 position;
    public SerializableQuaternion rotation;
    public float health;
    public float hunger;
    public float thirst;
    public float stamina;
    public bool isAlive;

    public List<ItemStackSaveData> inventoryItems = new List<ItemStackSaveData>();
    public PlayerSkills.SkillsSaveData skills = new PlayerSkills.SkillsSaveData();
    public List<InjurySystem.InjurySaveData> injuries = new List<InjurySystem.InjurySaveData>();
}

[Serializable]
public class LootContainerSaveData
{
    public string containerKey;
    public float nextRespawnGameDay;
    public List<ItemStackSaveData> items = new List<ItemStackSaveData>();
}

[Serializable]
public class ItemStackSaveData
{
    public int itemId;
    public string itemName;
    public int quantity;
}

[Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public SerializableVector3(Vector3 value)
    {
        x = value.x;
        y = value.y;
        z = value.z;
    }

    public Vector3 ToVector3() => new Vector3(x, y, z);
}

[Serializable]
public struct SerializableQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public SerializableQuaternion(Quaternion value)
    {
        x = value.x;
        y = value.y;
        z = value.z;
        w = value.w;
    }

    public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
}
