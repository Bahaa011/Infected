using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

/// <summary>
/// Manages the player's injury system including body parts, injury types, and infection
/// </summary>
public class InjurySystem : MonoBehaviour
{
    [Header("Injury Probabilities")]
    [SerializeField] [Range(0f, 1f)] private float scratchChance = 0.5f;      // 50% chance
    [SerializeField] [Range(0f, 1f)] private float lacerationChance = 0.35f;  // 35% chance
    [SerializeField] [Range(0f, 1f)] private float bittenChance = 0.15f;      // 15% chance

    [Header("Body Part Hit Probabilities")]
    [SerializeField] [Range(0f, 1f)] private float headChance = 0.1f;         // 10% chance
    [SerializeField] [Range(0f, 1f)] private float torsoChance = 0.35f;       // 35% chance
    [SerializeField] [Range(0f, 1f)] private float leftArmChance = 0.15f;     // 15% chance
    [SerializeField] [Range(0f, 1f)] private float rightArmChance = 0.15f;    // 15% chance
    [SerializeField] [Range(0f, 1f)] private float leftLegChance = 0.125f;    // 12.5% chance
    [SerializeField] [Range(0f, 1f)] private float rightLegChance = 0.125f;   // 12.5% chance

    [Header("Infection Settings")]
    [SerializeField] [Range(0f, 1f)] private float infectionChanceOnBite = 0.3f; // 30% chance for bite to become infected
    [SerializeField] private float infectionProgressRate = 0.05f; // How fast infection progresses per second
    [SerializeField] private float infectionDamagePerSecond = 2f; // Damage dealt by infected wounds

    [Header("Bite Fatality")]
    [SerializeField] private float biteFatalDurationMin = 129600f; // 1.5 days in seconds
    [SerializeField] private float biteFatalDurationMax = 216000f; // 2.5 days in seconds
    [SerializeField] private float bandageSlowdownMultiplier = 0.5f; // Bandages slow fatal progression to 50% speed

    [Header("Max Injuries")]
    [SerializeField] private int maxInjuriesPerBodyPart = 3;

    private List<Injury> activeInjuries = new List<Injury>();
    private Player player;

    // Events
    public UnityEvent<Injury> onInjuryReceived;
    public UnityEvent<Injury> onInjuryInfected;
    public UnityEvent<Injury> onInjuryHealed;

    private void Awake()
    {
        player = GetComponent<Player>();
    }

    private void Update()
    {
        // Process infections and bleeding
        ProcessInfections();
        ProcessBleeding();
        ProcessBiteFatality();
    }

    /// <summary>
    /// Apply a random injury to the player
    /// </summary>
    public Injury ApplyRandomInjury()
    {
        BodyPart bodyPart = SelectRandomBodyPart();
        InjuryType injuryType = SelectRandomInjuryType();
        
        return ApplyInjury(bodyPart, injuryType);
    }

    /// <summary>
    /// Apply a specific injury to a specific body part
    /// </summary>
    public Injury ApplyInjury(BodyPart bodyPart, InjuryType injuryType)
    {
        // Check if body part already has max injuries
        int injuriesOnPart = GetInjuryCountOnBodyPart(bodyPart);
        if (injuriesOnPart >= maxInjuriesPerBodyPart)
        {
            Debug.Log($"Body part {bodyPart} already has maximum injuries ({maxInjuriesPerBodyPart})");
            return null;
        }

        Injury injury = new Injury(bodyPart, injuryType);

        // Check if bite should become infected and set random fatal duration
        if (injuryType == InjuryType.Bitten)
        {
            // Set random fatal duration for this bite
            injury.biteFatalDuration = Random.Range(biteFatalDurationMin, biteFatalDurationMax);
            
            if (Random.value < infectionChanceOnBite)
            {
                injury.isInfected = true;
                onInjuryInfected?.Invoke(injury);
            }
        }

        activeInjuries.Add(injury);
        onInjuryReceived?.Invoke(injury);

        Debug.Log($"Player received injury: {injury}");
        return injury;
    }

    /// <summary>
    /// Select a random body part based on configured probabilities
    /// </summary>
    private BodyPart SelectRandomBodyPart()
    {
        float roll = Random.value;
        float cumulative = 0f;

        cumulative += headChance;
        if (roll < cumulative) return BodyPart.Head;

        cumulative += torsoChance;
        if (roll < cumulative) return BodyPart.Torso;

        cumulative += leftArmChance;
        if (roll < cumulative) return BodyPart.LeftArm;

        cumulative += rightArmChance;
        if (roll < cumulative) return BodyPart.RightArm;

        cumulative += leftLegChance;
        if (roll < cumulative) return BodyPart.LeftLeg;

        cumulative += rightLegChance;
        if (roll < cumulative) return BodyPart.RightLeg;

        // Default to torso if probabilities don't add up to 1
        return BodyPart.Torso;
    }

    /// <summary>
    /// Select a random injury type based on configured probabilities
    /// </summary>
    private InjuryType SelectRandomInjuryType()
    {
        float roll = Random.value;
        float cumulative = 0f;

        cumulative += scratchChance;
        if (roll < cumulative) return InjuryType.Scratch;

        cumulative += lacerationChance;
        if (roll < cumulative) return InjuryType.Laceration;

        cumulative += bittenChance;
        if (roll < cumulative) return InjuryType.Bitten;

        // Default to scratch if probabilities don't add up to 1
        return InjuryType.Scratch;
    }

    /// <summary>
    /// Process all infected injuries, dealing damage and progressing infection
    /// </summary>
    private void ProcessInfections()
    {
        foreach (Injury injury in activeInjuries)
        {
            if (injury.isInfected)
            {
                // Progress infection
                injury.infectionProgress += infectionProgressRate * Time.deltaTime;
                injury.infectionProgress = Mathf.Clamp01(injury.infectionProgress);

                // Deal damage based on infection progress
                float damage = infectionDamagePerSecond * injury.infectionProgress * Time.deltaTime;
                if (player != null && damage > 0)
                {
                    player.TakeDamage(damage);
                }
            }
        }
    }

    /// <summary>
    /// Process bleeding damage from all unbandaged injuries
    /// </summary>
    private void ProcessBleeding()
    {
        float totalBleedDamage = 0f;

        foreach (Injury injury in activeInjuries)
        {
            if (!injury.isBandaged)
            {
                totalBleedDamage += injury.GetBleedingDamage() * Time.deltaTime;
            }
        }

        if (totalBleedDamage > 0 && player != null)
        {
            player.TakeDamage(totalBleedDamage);
        }
    }

    /// <summary>
    /// Process inevitable death from bitten injuries over the fatal duration
    /// Bandages slow progression but cannot cure
    /// </summary>
    private void ProcessBiteFatality()
    {
        if (player == null) return;

        foreach (Injury injury in activeInjuries)
        {
            if (injury.injuryType != InjuryType.Bitten)
                continue;
            
            if (injury.biteFatalDuration <= 0f)
                continue;

            // Calculate progression rate (bandages slow it down)
            float progressionRate = injury.isBandaged ? bandageSlowdownMultiplier : 1f;
            injury.biteFatalElapsed += Time.deltaTime * progressionRate;

            // Calculate damage per second for this specific bite
            float fatalDps = player.GetMaxHealth() / injury.biteFatalDuration;
            float damage = fatalDps * Time.deltaTime * progressionRate;
            player.TakeDamage(damage);

            // Safety: if somehow still alive after the duration, finish them
            if (injury.biteFatalElapsed >= injury.biteFatalDuration)
            {
                player.TakeDamage(player.GetMaxHealth());
            }
        }
    }

    /// <summary>
    /// Bandage a specific injury to stop bleeding
    /// </summary>
    public void BandageInjury(Injury injury)
    {
        if (activeInjuries.Contains(injury))
        {
            injury.Bandage();
            Debug.Log($"Bandaged injury: {injury}");
        }
    }

    /// <summary>
    /// Bandage all unbandaged injuries
    /// </summary>
    public void BandageAllInjuries()
    {
        foreach (Injury injury in activeInjuries)
        {
            if (!injury.isBandaged)
            {
                injury.Bandage();
            }
        }
        Debug.Log("Bandaged all injuries");
    }

    /// <summary>
    /// Bandage all injuries on a specific body part
    /// </summary>
    public void BandageBodyPart(BodyPart bodyPart)
    {
        var injuries = GetInjuriesOnBodyPart(bodyPart);
        foreach (Injury injury in injuries)
        {
            if (!injury.isBandaged)
            {
                injury.Bandage();
            }
        }
        Debug.Log($"Bandaged all injuries on {bodyPart}");
    }

    /// <summary>
    /// Heal a specific injury (bites cannot be healed)
    /// </summary>
    public void HealInjury(Injury injury)
    {
        if (activeInjuries.Contains(injury))
        {
            // Bites are fatal and cannot be cured
            if (injury.injuryType == InjuryType.Bitten)
            {
                Debug.Log($"Cannot heal bite injury - bites are fatal and incurable");
                return;
            }
            
            activeInjuries.Remove(injury);
            onInjuryHealed?.Invoke(injury);
            Debug.Log($"Healed injury: {injury}");
        }
    }

    /// <summary>
    /// Heal all injuries of a specific type (bites cannot be healed)
    /// </summary>
    public void HealInjuriesByType(InjuryType injuryType)
    {
        // Bites cannot be healed
        if (injuryType == InjuryType.Bitten)
        {
            Debug.Log($"Cannot heal bite injuries - bites are fatal and incurable");
            return;
        }
        
        List<Injury> toRemove = new List<Injury>();
        foreach (Injury injury in activeInjuries)
        {
            if (injury.injuryType == injuryType)
            {
                toRemove.Add(injury);
            }
        }

        foreach (Injury injury in toRemove)
        {
            HealInjury(injury);
        }
    }

    /// <summary>
    /// Heal all injuries on a specific body part
    /// </summary>
    public void HealInjuriesOnBodyPart(BodyPart bodyPart)
    {
        List<Injury> toRemove = new List<Injury>();
        foreach (Injury injury in activeInjuries)
        {
            if (injury.bodyPart == bodyPart)
            {
                toRemove.Add(injury);
            }
        }

        foreach (Injury injury in toRemove)
        {
            HealInjury(injury);
        }
    }

    /// <summary>
    /// Heal all injuries
    /// </summary>
    public void HealAllInjuries()
    {
        List<Injury> toRemove = new List<Injury>(activeInjuries);
        foreach (Injury injury in toRemove)
        {
            HealInjury(injury);
        }
    }

    /// <summary>
    /// Cure infection on a specific injury
    /// </summary>
    public void CureInfection(Injury injury)
    {
        if (injury.isInfected)
        {
            injury.isInfected = false;
            injury.infectionProgress = 0f;
            Debug.Log($"Cured infection on: {injury}");
        }
    }

    /// <summary>
    /// Get count of injuries on a specific body part
    /// </summary>
    public int GetInjuryCountOnBodyPart(BodyPart bodyPart)
    {
        int count = 0;
        foreach (Injury injury in activeInjuries)
        {
            if (injury.bodyPart == bodyPart)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Get all active injuries
    /// </summary>
    public List<Injury> GetActiveInjuries()
    {
        return new List<Injury>(activeInjuries);
    }

    /// <summary>
    /// Get total number of active injuries
    /// </summary>
    public int GetTotalInjuryCount()
    {
        return activeInjuries.Count;
    }

    /// <summary>
    /// Get number of infected injuries
    /// </summary>
    public int GetInfectedInjuryCount()
    {
        int count = 0;
        foreach (Injury injury in activeInjuries)
        {
            if (injury.isInfected)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Check if player has any infected injuries
    /// </summary>
    public bool HasInfection()
    {
        return GetInfectedInjuryCount() > 0;
    }

    /// <summary>
    /// Get number of bleeding (unbandaged) injuries
    /// </summary>
    public int GetBleedingInjuryCount()
    {
        int count = 0;
        foreach (Injury injury in activeInjuries)
        {
            if (injury.IsBleeding())
                count++;
        }
        return count;
    }

    /// <summary>
    /// Check if player has any bleeding injuries
    /// </summary>
    public bool IsBleeding()
    {
        return GetBleedingInjuryCount() > 0;
    }

    /// <summary>
    /// Get total bleeding damage per second from all injuries
    /// </summary>
    public float GetTotalBleedingDamage()
    {
        float total = 0f;
        foreach (Injury injury in activeInjuries)
        {
            total += injury.GetBleedingDamage();
        }
        return total;
    }

    /// <summary>
    /// Get injuries on a specific body part
    /// </summary>
    public List<Injury> GetInjuriesOnBodyPart(BodyPart bodyPart)
    {
        List<Injury> injuries = new List<Injury>();
        foreach (Injury injury in activeInjuries)
        {
            if (injury.bodyPart == bodyPart)
                injuries.Add(injury);
        }
        return injuries;
    }
}
