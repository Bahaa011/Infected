using UnityEngine;
using System;

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
    public bool isHealing; // Whether this injury is currently healing over time
    public float healingElapsedDays; // In-game days elapsed since treatment started
    public float healingDurationDays; // In-game days needed to fully heal this injury

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
        this.isHealing = false;
        this.healingElapsedDays = 0f;
        this.healingDurationDays = 0f;
    }

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

    public float GetTotalDamage(float baseDamage)
    {
        float totalMultiplier = GetDamageMultiplier() * GetBodyPartMultiplier();
        if (isInfected)
            totalMultiplier *= 1.5f; // Infected wounds do more damage
        
        return baseDamage * totalMultiplier;
    }

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

    public void Bandage()
    {
        isBandaged = true;
    }

    public void StartHealing(float durationDays)
    {
        isBandaged = true;
        isHealing = true;
        healingElapsedDays = 0f;
        healingDurationDays = Mathf.Max(0.0001f, durationDays);
    }

    public void RemoveBandage()
    {
        isBandaged = false;
        isHealing = false;
        healingElapsedDays = 0f;
    }

    public void ProgressHealing(float deltaDays)
    {
        if (!isHealing)
            return;

        healingElapsedDays += Mathf.Max(0f, deltaDays);
    }

    public float GetHealingProgress01()
    {
        if (!isHealing || healingDurationDays <= 0f)
            return 0f;

        return Mathf.Clamp01(healingElapsedDays / healingDurationDays);
    }

    public bool IsFullyHealed()
    {
        return isHealing && healingElapsedDays >= healingDurationDays;
    }

    public bool IsBleeding()
    {
        return !isBandaged;
    }

    public override string ToString()
    {
        string infection = isInfected ? " (INFECTED)" : "";
        string bandaged = isBandaged ? " [Bandaged]" : " [BLEEDING]";
        string healing = isHealing ? $" [Healing {Mathf.RoundToInt(GetHealingProgress01() * 100f)}%]" : "";
        return $"{injuryType} on {bodyPart}{infection}{bandaged}{healing}";
    }
}
