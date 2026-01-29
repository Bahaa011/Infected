using UnityEngine;
using System;

/// <summary>
/// Represents a single injury on the player
/// </summary>
[System.Serializable]
public class Injury
{
    public BodyPart bodyPart;
    public InjuryType injuryType;
    public float timestamp; // Time when injury occurred
    public bool isInfected;
    public float infectionProgress; // 0-1, used for bitten injuries
    public bool isBandaged; // Whether the injury has been bandaged
    public float biteFatalElapsed; // Time elapsed for fatal bite countdown
    public float biteFatalDuration; // Random duration until death for this bite

    public Injury(BodyPart bodyPart, InjuryType injuryType)
    {
        this.bodyPart = bodyPart;
        this.injuryType = injuryType;
        this.timestamp = Time.time;
        this.isInfected = false;
        this.infectionProgress = 0f;
        this.isBandaged = false;
        this.biteFatalElapsed = 0f;
        this.biteFatalDuration = 0f;
    }

    /// <summary>
    /// Gets the base damage multiplier for this injury type
    /// </summary>
    public float GetDamageMultiplier()
    {
        switch (injuryType)
        {
            case InjuryType.Scratch:
                return 0.5f;
            case InjuryType.Laceration:
                return 1.0f;
            case InjuryType.Bitten:
                return 1.5f;
            default:
                return 1.0f;
        }
    }

    /// <summary>
    /// Gets additional damage multiplier based on body part
    /// </summary>
    public float GetBodyPartMultiplier()
    {
        switch (bodyPart)
        {
            case BodyPart.Head:
                return 2.0f; // Head injuries are most severe
            case BodyPart.Torso:
                return 1.5f;
            case BodyPart.LeftArm:
            case BodyPart.RightArm:
                return 0.8f;
            case BodyPart.LeftLeg:
            case BodyPart.RightLeg:
                return 1.0f;
            default:
                return 1.0f;
        }
    }

    /// <summary>
    /// Gets the total damage for this injury
    /// </summary>
    public float GetTotalDamage(float baseDamage)
    {
        float totalMultiplier = GetDamageMultiplier() * GetBodyPartMultiplier();
        if (isInfected)
            totalMultiplier *= 1.5f; // Infected wounds do more damage
        
        return baseDamage * totalMultiplier;
    }

    /// <summary>
    /// Gets the bleeding damage per second for unbandaged injuries
    /// </summary>
    public float GetBleedingDamage()
    {
        if (isBandaged)
            return 0f;

        float bleedDamage = 0f;
        
        switch (injuryType)
        {
            case InjuryType.Scratch:
                bleedDamage = 0.5f; // Minor bleeding
                break;
            case InjuryType.Laceration:
                bleedDamage = 2.0f; // Moderate bleeding
                break;
            case InjuryType.Bitten:
                bleedDamage = 1.5f; // Significant bleeding
                break;
        }

        // Head and torso injuries bleed more
        if (bodyPart == BodyPart.Head)
            bleedDamage *= 1.5f;
        else if (bodyPart == BodyPart.Torso)
            bleedDamage *= 1.3f;

        return bleedDamage;
    }

    /// <summary>
    /// Bandage this injury to stop bleeding
    /// </summary>
    public void Bandage()
    {
        isBandaged = true;
    }

    /// <summary>
    /// Check if this injury is currently bleeding
    /// </summary>
    public bool IsBleeding()
    {
        return !isBandaged;
    }

    public override string ToString()
    {
        string infection = isInfected ? " (INFECTED)" : "";
        string bandaged = isBandaged ? " [Bandaged]" : " [BLEEDING]";
        return $"{injuryType} on {bodyPart}{infection}{bandaged}";
    }
}
