using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

public class PlayerSkills : MonoBehaviour
{
    [System.Serializable]
    public struct GunSkillSaveData
    {
        public Gun.GunType gunType;
        public int level;
        public float currentXP;
        public float totalShots;
    }

    [System.Serializable]
    public struct GeneralSkillSaveData
    {
        public SkillType skillType;
        public int level;
        public float currentXP;
        public float totalXP;
    }

    [System.Serializable]
    public class SkillsSaveData
    {
        public List<GunSkillSaveData> gunSkills = new List<GunSkillSaveData>();
        public List<GeneralSkillSaveData> generalSkills = new List<GeneralSkillSaveData>();
    }

    public enum SkillType
    {
        // Gun skills
        PistolAccuracy,
        AssaultRifleAccuracy,
        ShotgunAccuracy,
        SniperAccuracy,
        // General skills
        Stamina,
        Strength,
        Stealth,
        Metabolism,
        Vitality
    }

    [Header("Skill System Settings")]
    [SerializeField] private float accuracySkillMaxLevel = 10f;
    [SerializeField] private float shotsPerLevel = 100f;
    [SerializeField] private float maxAccuracyImprovement = 10f;
    
    [Header("Stamina Skill Settings")]
    [SerializeField] private float staminaXPPerSecondSprinting = 1f;
    [SerializeField] private float staminaMaxLevel = 10f;
    [SerializeField] private float staminaXPPerLevel = 60f; // 60 seconds of sprinting per level
    [SerializeField] private float maxStaminaDrainReduction = 0.5f; // 50% reduction at max level
    
    [Header("Stealth Skill Settings")]
    [SerializeField] private float stealthXPPerSecondCrouching = 0.5f;
    [SerializeField] private float stealthMaxLevel = 10f;
    [SerializeField] private float stealthXPPerLevel = 120f;
    [SerializeField] private float maxStealthPerceptionReduction = 0.5f;
    
    [Header("Metabolism Skill Settings")]
    [SerializeField] private float metabolismXPPerMinute = 1f;
    [SerializeField] private float metabolismMaxLevel = 10f;
    [SerializeField] private float metabolismXPPerLevel = 10f;
    [SerializeField] private float maxHungerThirstReduction = 0.5f; // 50% slower decay at max
    
    [Header("Vitality Skill Settings")]
    [SerializeField] private float vitalityXPPerDamageSurvived = 0.1f;
    [SerializeField] private float vitalityMaxLevel = 10f;
    [SerializeField] private float vitalityXPPerLevel = 100f;
    [SerializeField] private float maxHealthBonus = 50f; // +50 max health at max level

    [Header("Strength Skill Settings")]
    [SerializeField] private float strengthXPPerMeleeAttack = 1f;
    [SerializeField] private float strengthMaxLevel = 10f;
    [SerializeField] private float strengthXPPerLevel = 50f;
    [SerializeField] private float maxDamageBonus = 0.5f; // 50% damage increase at max level
    
    [Header("Skill Multipliers")]
    [SerializeField] private float headShotBonus = 2f;
    [SerializeField] private float hitBonus = 1.5f;
    [SerializeField] private float missBonus = 0.5f;
    
    // Skill tracking
    private Dictionary<Gun.GunType, GunSkill> gunSkills = new Dictionary<Gun.GunType, GunSkill>();
    private Dictionary<SkillType, GeneralSkill> generalSkills = new Dictionary<SkillType, GeneralSkill>();
    
    // Events
    public UnityEvent<Gun.GunType, int> onSkillLevelUp;
    public UnityEvent<Gun.GunType, float, float> onSkillProgressChanged;
    public UnityEvent<SkillType, int> onGeneralSkillLevelUp;
    public UnityEvent<SkillType, float, float> onGeneralSkillProgressChanged;
    
    private Player player;
    private float sprintTimer = 0f;
    private float crouchTimer = 0f;
    private float aliveTimer = 0f;

    [System.Serializable]
    private class GunSkill
    {
        public Gun.GunType gunType;
        public int level = 1;
        public float currentXP = 0f;
        public float totalShots = 0f;
        
        public float GetSpreadReduction(float maxLevel, float maxImprovement)
        {
            float denominator = Mathf.Max(1f, maxLevel - 1f);
            return Mathf.Clamp01((level - 1f) / denominator) * maxImprovement;
        }
        
        public float GetXPNeeded(float shotsPerLevel)
        {
            return level * shotsPerLevel;
        }
    }

    [System.Serializable]
    private class GeneralSkill
    {
        public SkillType skillType;
        public int level = 1;
        public float currentXP = 0f;
        public float totalXP = 0f;
        
        public float GetXPNeeded(float xpPerLevel)
        {
            return level * xpPerLevel;
        }
    }

    private void Awake()
    {
        player = GetComponent<Player>();
        InitializeSkills();
    }

    private void InitializeSkills()
    {
        // Initialize gun skills
        foreach (Gun.GunType gunType in System.Enum.GetValues(typeof(Gun.GunType)))
        {
            gunSkills[gunType] = new GunSkill { gunType = gunType };
        }
        
        // Initialize general skills
        generalSkills[SkillType.Stamina] = new GeneralSkill { skillType = SkillType.Stamina };
        generalSkills[SkillType.Stealth] = new GeneralSkill { skillType = SkillType.Stealth };
        generalSkills[SkillType.Strength] = new GeneralSkill { skillType = SkillType.Strength };
        generalSkills[SkillType.Metabolism] = new GeneralSkill { skillType = SkillType.Metabolism };
        generalSkills[SkillType.Vitality] = new GeneralSkill { skillType = SkillType.Vitality };
    }

    private void Update()
    {
        if (player == null) return;
        
        // Track sprinting for Stamina XP
        if (player.IsCurrentlySprinting())
        {
            sprintTimer += Time.deltaTime;
            if (sprintTimer >= 1f)
            {
                AddGeneralXP(SkillType.Stamina, staminaXPPerSecondSprinting);
                sprintTimer = 0f;
            }
        }
        
        // Track crouching for Stealth XP
        if (player.IsCrouching())
        {
            crouchTimer += Time.deltaTime;
            if (crouchTimer >= 1f)
            {
                AddGeneralXP(SkillType.Stealth, stealthXPPerSecondCrouching);
                crouchTimer = 0f;
            }
        }
        
        // Track time alive for Metabolism XP
        aliveTimer += Time.deltaTime;
        if (aliveTimer >= 60f)
        {
            AddGeneralXP(SkillType.Metabolism, metabolismXPPerMinute);
            aliveTimer = 0f;
        }
    }

    public void RegisterDamageSurvived(float damage)
    {
        AddGeneralXP(SkillType.Vitality, damage * vitalityXPPerDamageSurvived);
    }

    public void RegisterShot(Gun.GunType gunType, bool hitTarget = false, bool headShot = false)
    {
        if (!gunSkills.ContainsKey(gunType))
            return;

        GunSkill skill = gunSkills[gunType];
        skill.totalShots++;
        
        float xpGain = 1f;
        if (headShot)
            xpGain = headShotBonus;
        else if (hitTarget)
            xpGain = hitBonus;
        else
            xpGain = missBonus;
        
        skill.currentXP += xpGain;
        
        while (skill.currentXP >= skill.GetXPNeeded(shotsPerLevel) && skill.level < accuracySkillMaxLevel)
        {
            skill.currentXP -= skill.GetXPNeeded(shotsPerLevel);
            skill.level++;
            
            onSkillLevelUp?.Invoke(gunType, skill.level);
            Debug.Log($"[PlayerSkills] {gunType} accuracy leveled up to {skill.level}!");
        }
        
        if (skill.level >= accuracySkillMaxLevel)
            skill.currentXP = 0f;
        
        onSkillProgressChanged?.Invoke(gunType, skill.currentXP, skill.GetXPNeeded(shotsPerLevel));
    }

    public void AddGeneralXP(SkillType skillType, float amount)
    {
        if (!generalSkills.ContainsKey(skillType))
            return;

        GeneralSkill skill = generalSkills[skillType];
        int previousLevel = skill.level;
        float maxLevel = GetMaxLevelForSkill(skillType);
        float xpPerLevel = GetXPPerLevelForSkill(skillType);
        
        skill.currentXP += amount;
        skill.totalXP += amount;
        
        while (skill.currentXP >= skill.GetXPNeeded(xpPerLevel) && skill.level < maxLevel)
        {
            skill.currentXP -= skill.GetXPNeeded(xpPerLevel);
            skill.level++;
            
            onGeneralSkillLevelUp?.Invoke(skillType, skill.level);
            Debug.Log($"[PlayerSkills] {skillType} leveled up to {skill.level}!");
        }
        
        if (skill.level >= maxLevel)
            skill.currentXP = 0f;

        if (skillType == SkillType.Vitality && skill.level != previousLevel && player != null)
            player.RefreshSkillDerivedStats();
        
        onGeneralSkillProgressChanged?.Invoke(skillType, skill.currentXP, skill.GetXPNeeded(xpPerLevel));
    }

    private float GetMaxLevelForSkill(SkillType skillType)
    {
        switch (skillType)
        {
            case SkillType.Stamina: return staminaMaxLevel;
            case SkillType.Stealth: return stealthMaxLevel;
            case SkillType.Strength: return strengthMaxLevel;
            case SkillType.Metabolism: return metabolismMaxLevel;
            case SkillType.Vitality: return vitalityMaxLevel;
            default: return 10f;
        }
    }

    private float GetXPPerLevelForSkill(SkillType skillType)
    {
        switch (skillType)
        {
            case SkillType.Stamina: return staminaXPPerLevel;
            case SkillType.Stealth: return stealthXPPerLevel;
            case SkillType.Strength: return strengthXPPerLevel;
            case SkillType.Metabolism: return metabolismXPPerLevel;
            case SkillType.Vitality: return vitalityXPPerLevel;
            default: return 100f;
        }
    }

    // ============ GUN SKILL GETTERS ============
    
    public float GetAccuracyImprovement(Gun.GunType gunType)
    {
        if (!gunSkills.ContainsKey(gunType))
            return 0f;
        
        return gunSkills[gunType].GetSpreadReduction(accuracySkillMaxLevel, maxAccuracyImprovement);
    }

    public int GetSkillLevel(Gun.GunType gunType)
    {
        if (!gunSkills.ContainsKey(gunType))
            return 1;
        
        return gunSkills[gunType].level;
    }

    public float GetTotalShots(Gun.GunType gunType)
    {
        if (!gunSkills.ContainsKey(gunType))
            return 0f;
        
        return gunSkills[gunType].totalShots;
    }

    public float GetCurrentXP(Gun.GunType gunType)
    {
        if (!gunSkills.ContainsKey(gunType))
            return 0f;
        
        return gunSkills[gunType].currentXP;
    }

    public float GetXPNeeded(Gun.GunType gunType)
    {
        if (!gunSkills.ContainsKey(gunType))
            return 0f;
        
        return gunSkills[gunType].GetXPNeeded(shotsPerLevel);
    }

    public float GetSkillProgress(Gun.GunType gunType)
    {
        float current = GetCurrentXP(gunType);
        float needed = GetXPNeeded(gunType);
        
        if (needed <= 0) return 0f;
        
        return Mathf.Clamp01(current / needed);
    }

    // ============ GENERAL SKILL GETTERS ============
    
    public int GetGeneralSkillLevel(SkillType skillType)
    {
        if (!generalSkills.ContainsKey(skillType))
            return 1;
        
        return generalSkills[skillType].level;
    }

    public float GetGeneralSkillXP(SkillType skillType)
    {
        if (!generalSkills.ContainsKey(skillType))
            return 0f;
        
        return generalSkills[skillType].currentXP;
    }

    public float GetGeneralSkillXPNeeded(SkillType skillType)
    {
        if (!generalSkills.ContainsKey(skillType))
            return 0f;
        
        return generalSkills[skillType].GetXPNeeded(GetXPPerLevelForSkill(skillType));
    }

    public float GetGeneralSkillProgress(SkillType skillType)
    {
        float current = GetGeneralSkillXP(skillType);
        float needed = GetGeneralSkillXPNeeded(skillType);
        
        if (needed <= 0) return 0f;
        
        return Mathf.Clamp01(current / needed);
    }

    // ============ SKILL BONUSES ============
    
    public float GetStaminaDrainMultiplier()
    {
        float reduction = GetSkillBonusRatio(SkillType.Stamina) * maxStaminaDrainReduction;
        return 1f - reduction;
    }

    public float GetMetabolismMultiplier()
    {
        float reduction = GetSkillBonusRatio(SkillType.Metabolism) * maxHungerThirstReduction;
        return 1f - reduction;
    }

    public float GetVitalityHealthBonus()
    {
        return GetSkillBonusRatio(SkillType.Vitality) * maxHealthBonus;
    }

    public float GetStealthPerceptionMultiplier()
    {
        float reduction = GetSkillBonusRatio(SkillType.Stealth) * maxStealthPerceptionReduction;
        return 1f - reduction;
    }

    public float GetStrengthDamageBonus()
    {
        return GetSkillBonusRatio(SkillType.Strength) * maxDamageBonus;
    }

    private float GetSkillBonusRatio(SkillType skillType)
    {
        float maxLevel = Mathf.Max(1f, GetMaxLevelForSkill(skillType));
        float denominator = Mathf.Max(1f, maxLevel - 1f);
        return Mathf.Clamp01((GetGeneralSkillLevel(skillType) - 1f) / denominator);
    }

    public void RegisterMeleeAttack()
    {
        AddGeneralXP(SkillType.Strength, strengthXPPerMeleeAttack);
    }

    // ============ UTILITY ============
    
    public void AddXP(Gun.GunType gunType, float amount)
    {
        if (!gunSkills.ContainsKey(gunType))
            return;

        GunSkill skill = gunSkills[gunType];
        skill.currentXP += amount;
        
        while (skill.currentXP >= skill.GetXPNeeded(shotsPerLevel) && skill.level < accuracySkillMaxLevel)
        {
            skill.currentXP -= skill.GetXPNeeded(shotsPerLevel);
            skill.level++;
            
            onSkillLevelUp?.Invoke(gunType, skill.level);
            Debug.Log($"[PlayerSkills] {gunType} accuracy leveled up to {skill.level}!");
        }
        
        if (skill.level >= accuracySkillMaxLevel)
            skill.currentXP = 0f;
        
        onSkillProgressChanged?.Invoke(gunType, skill.currentXP, skill.GetXPNeeded(shotsPerLevel));
    }

    public void ResetAllSkills()
    {
        foreach (var skill in gunSkills.Values)
        {
            skill.level = 1;
            skill.currentXP = 0f;
            skill.totalShots = 0f;
        }
        
        foreach (var skill in generalSkills.Values)
        {
            skill.level = 1;
            skill.currentXP = 0f;
            skill.totalXP = 0f;
        }
    }

    public Dictionary<Gun.GunType, (int level, float xp, float totalShots)> GetAllSkillData()
    {
        var data = new Dictionary<Gun.GunType, (int, float, float)>();
        foreach (var kvp in gunSkills)
        {
            data[kvp.Key] = (kvp.Value.level, kvp.Value.currentXP, kvp.Value.totalShots);
        }
        return data;
    }

    public SkillsSaveData CaptureSaveData()
    {
        SkillsSaveData data = new SkillsSaveData();

        foreach (var kvp in gunSkills)
        {
            data.gunSkills.Add(new GunSkillSaveData
            {
                gunType = kvp.Key,
                level = Mathf.Max(1, kvp.Value.level),
                currentXP = Mathf.Max(0f, kvp.Value.currentXP),
                totalShots = Mathf.Max(0f, kvp.Value.totalShots)
            });
        }

        foreach (var kvp in generalSkills)
        {
            data.generalSkills.Add(new GeneralSkillSaveData
            {
                skillType = kvp.Key,
                level = Mathf.Max(1, kvp.Value.level),
                currentXP = Mathf.Max(0f, kvp.Value.currentXP),
                totalXP = Mathf.Max(0f, kvp.Value.totalXP)
            });
        }

        return data;
    }

    public void ApplySaveData(SkillsSaveData data)
    {
        if (data == null)
            return;

        ResetAllSkills();

        if (data.gunSkills != null)
        {
            for (int i = 0; i < data.gunSkills.Count; i++)
            {
                GunSkillSaveData save = data.gunSkills[i];
                if (!gunSkills.TryGetValue(save.gunType, out GunSkill skill) || skill == null)
                    continue;

                skill.level = Mathf.Max(1, save.level);
                skill.currentXP = Mathf.Max(0f, save.currentXP);
                skill.totalShots = Mathf.Max(0f, save.totalShots);
            }
        }

        if (data.generalSkills != null)
        {
            for (int i = 0; i < data.generalSkills.Count; i++)
            {
                GeneralSkillSaveData save = data.generalSkills[i];
                if (!generalSkills.TryGetValue(save.skillType, out GeneralSkill skill) || skill == null)
                    continue;

                skill.level = Mathf.Max(1, save.level);
                skill.currentXP = Mathf.Max(0f, save.currentXP);
                skill.totalXP = Mathf.Max(0f, save.totalXP);
            }
        }

        if (player != null)
            player.RefreshSkillDerivedStats();
    }
}
