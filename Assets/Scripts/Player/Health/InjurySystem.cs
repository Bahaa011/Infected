using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

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

    [Header("Bandage Healing")]
    [SerializeField] private float bandageHealDurationDaysMin = 1f;
    [SerializeField] private float bandageHealDurationDaysMax = 2f;

    [Header("Max Injuries")]
    [SerializeField] private int maxInjuriesPerBodyPart = 3;

    private List<Injury> activeInjuries = new List<Injury>();
    private Player player;
    private DayNightManager dayNightManager;

    // Events
    public UnityEvent<Injury> onInjuryReceived;
    public UnityEvent<Injury> onInjuryInfected;
    public UnityEvent<Injury> onInjuryHealed;

    private void Awake()
    {
        player = GetComponent<Player>();
        dayNightManager = FindFirstObjectByType<DayNightManager>();
    }

    private void Update()
    {
        // Process infections and bleeding
        ProcessInfections();
        ProcessBleeding();
        ProcessBiteFatality();
        ProcessBandagedHealing();
    }

    public Injury ApplyRandomInjury()
    {
        BodyPart bodyPart = SelectRandomBodyPart();
        InjuryType injuryType = SelectRandomInjuryType();
        
        return ApplyInjury(bodyPart, injuryType);
    }

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

    public void BandageInjury(Injury injury)
    {
        if (activeInjuries.Contains(injury))
        {
            StartHealingForInjury(injury);
            Debug.Log($"Bandaged injury: {injury}");
        }
    }

    public void BandageAllInjuries()
    {
        foreach (Injury injury in activeInjuries)
        {
            if (!injury.isBandaged)
            {
                StartHealingForInjury(injury);
            }
        }
        Debug.Log("Bandaged all injuries");
    }

    public void BandageBodyPart(BodyPart bodyPart)
    {
        var injuries = GetInjuriesOnBodyPart(bodyPart);
        foreach (Injury injury in injuries)
        {
            if (!injury.isBandaged)
            {
                StartHealingForInjury(injury);
            }
        }
        Debug.Log($"Bandaged all injuries on {bodyPart}");
    }

    private void StartHealingForInjury(Injury injury)
    {
        float durationDays = Random.Range(bandageHealDurationDaysMin, bandageHealDurationDaysMax);
        injury.StartHealing(durationDays);
    }

    private void ProcessBandagedHealing()
    {
        if (activeInjuries.Count == 0)
            return;

        float deltaDays;
        if (dayNightManager != null)
        {
            deltaDays = dayNightManager.GetGameDaysPerSecond() * Time.deltaTime;
        }
        else
        {
            // Fallback assumes 1 in-game day is 20 real minutes
            deltaDays = Time.deltaTime / 1200f;
        }

        List<Injury> healedInjuries = null;

        foreach (Injury injury in activeInjuries)
        {
            if (!injury.isBandaged || !injury.isHealing)
                continue;

            injury.ProgressHealing(deltaDays);
            if (injury.IsFullyHealed())
            {
                if (healedInjuries == null)
                    healedInjuries = new List<Injury>();

                healedInjuries.Add(injury);
            }
        }

        if (healedInjuries == null)
            return;

        foreach (Injury healed in healedInjuries)
        {
            activeInjuries.Remove(healed);
            onInjuryHealed?.Invoke(healed);
            Debug.Log($"Injury fully healed over time: {healed}");
        }
    }

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

    public void HealAllInjuries()
    {
        List<Injury> toRemove = new List<Injury>(activeInjuries);
        foreach (Injury injury in toRemove)
        {
            HealInjury(injury);
        }
    }

    public void CureInfection(Injury injury)
    {
        if (injury.isInfected)
        {
            injury.isInfected = false;
            injury.infectionProgress = 0f;
            Debug.Log($"Cured infection on: {injury}");
        }
    }

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

    public List<Injury> GetActiveInjuries()
    {
        return new List<Injury>(activeInjuries);
    }

    public int GetTotalInjuryCount()
    {
        return activeInjuries.Count;
    }

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

    public bool HasInfection()
    {
        return GetInfectedInjuryCount() > 0;
    }

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

    public bool IsBleeding()
    {
        return GetBleedingInjuryCount() > 0;
    }

    public float GetTotalBleedingDamage()
    {
        float total = 0f;
        foreach (Injury injury in activeInjuries)
        {
            total += injury.GetBleedingDamage();
        }
        return total;
    }

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
