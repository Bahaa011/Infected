using System;

[Serializable]
public class SaveSlotInfo
{
    public int slotIndex;
    public bool exists;
    public bool corrupted;
    public string filePath;
    public string slotName;
    public string savedAtUtc;
    public float totalGameDays;
}
