using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class InjuryStatusUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;

    private VisualElement injuryPanel;
    private Dictionary<string, Button> bodyPartButtons = new Dictionary<string, Button>();
    private Dictionary<BodyPart, string> bodyPartMap = new Dictionary<BodyPart, string>();
    private Dictionary<BodyPart, Button> bodyPartToButton = new Dictionary<BodyPart, Button>();
    private InjurySystem injurySystem;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var root = uiDocument.rootVisualElement;
        injuryPanel = root.Q<VisualElement>(className: "injury-panel");

        // Get body part buttons
        bodyPartButtons["head"] = root.Q<Button>("body-head");
        bodyPartButtons["torso"] = root.Q<Button>("body-torso");
        bodyPartButtons["leftarm"] = root.Q<Button>("body-leftarm");
        bodyPartButtons["rightarm"] = root.Q<Button>("body-rightarm");
        bodyPartButtons["leftleg"] = root.Q<Button>("body-leftleg");
        bodyPartButtons["rightleg"] = root.Q<Button>("body-rightleg");

        // Map body parts to button keys and vice versa
        bodyPartMap[BodyPart.Head] = "head";
        bodyPartMap[BodyPart.Torso] = "torso";
        bodyPartMap[BodyPart.LeftArm] = "leftarm";
        bodyPartMap[BodyPart.RightArm] = "rightarm";
        bodyPartMap[BodyPart.LeftLeg] = "leftleg";
        bodyPartMap[BodyPart.RightLeg] = "rightleg";

        bodyPartToButton[BodyPart.Head] = bodyPartButtons["head"];
        bodyPartToButton[BodyPart.Torso] = bodyPartButtons["torso"];
        bodyPartToButton[BodyPart.LeftArm] = bodyPartButtons["leftarm"];
        bodyPartToButton[BodyPart.RightArm] = bodyPartButtons["rightarm"];
        bodyPartToButton[BodyPart.LeftLeg] = bodyPartButtons["leftleg"];
        bodyPartToButton[BodyPart.RightLeg] = bodyPartButtons["rightleg"];

        // Setup right-click callbacks for bandaging
        bodyPartButtons["head"]?.RegisterCallback<ContextClickEvent>(evt => OnBodyPartRightClicked(BodyPart.Head));
        bodyPartButtons["torso"]?.RegisterCallback<ContextClickEvent>(evt => OnBodyPartRightClicked(BodyPart.Torso));
        bodyPartButtons["leftarm"]?.RegisterCallback<ContextClickEvent>(evt => OnBodyPartRightClicked(BodyPart.LeftArm));
        bodyPartButtons["rightarm"]?.RegisterCallback<ContextClickEvent>(evt => OnBodyPartRightClicked(BodyPart.RightArm));
        bodyPartButtons["leftleg"]?.RegisterCallback<ContextClickEvent>(evt => OnBodyPartRightClicked(BodyPart.LeftLeg));
        bodyPartButtons["rightleg"]?.RegisterCallback<ContextClickEvent>(evt => OnBodyPartRightClicked(BodyPart.RightLeg));
    }

    private void OnEnable()
    {
        if (player == null)
            player = FindFirstObjectByType<Player>();

        if (player != null)
        {
            injurySystem = player.GetInjurySystem();
            if (injurySystem != null)
            {
                injurySystem.onInjuryReceived.AddListener(OnInjuryChanged);
                injurySystem.onInjuryHealed.AddListener(OnInjuryChanged);
                injurySystem.onInjuryInfected.AddListener(OnInjuryChanged);
            }
        }
    }

    private void OnDisable()
    {
        if (injurySystem != null)
        {
            injurySystem.onInjuryReceived.RemoveListener(OnInjuryChanged);
            injurySystem.onInjuryHealed.RemoveListener(OnInjuryChanged);
            injurySystem.onInjuryInfected.RemoveListener(OnInjuryChanged);
        }
    }

    private void SetUIOpen(bool open)
    {
        if (injuryPanel != null)
        {
            injuryPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (open)
        {
            // Ensure injury system is initialized
            if (injurySystem == null && player != null)
            {
                injurySystem = player.GetInjurySystem();
            }
            
            UpdateBodyPartColors();
        }
    }

    private void Update()
    {
        // Ensure player is found if not assigned
        if (player == null)
            player = FindFirstObjectByType<Player>();
        
        // Check if inventory is open and sync with it
        if (InventoryUIToolkit.IsInventoryOpen)
        {
            SetUIOpen(true);
            if (injurySystem != null)
            {
                UpdateBodyPartColors();
            }
        }
        else
        {
            SetUIOpen(false);
        }
    }

    private void OnInjuryChanged(Injury injury)
    {
        if (InventoryUIToolkit.IsInventoryOpen)
        {
            UpdateBodyPartColors();
        }
    }

    private void UpdateBodyPartColors()
    {
        if (injurySystem == null) return;

        // Update each body part button based on injuries
        foreach (var bodyPart in bodyPartToButton.Keys)
        {
            UpdateBodyPartButton(bodyPart);
        }
    }

    private void UpdateBodyPartButton(BodyPart bodyPart)
    {
        if (!bodyPartToButton.ContainsKey(bodyPart)) return;

        var button = bodyPartToButton[bodyPart];
        var injuries = injurySystem.GetInjuriesOnBodyPart(bodyPart);

        if (injuries.Count == 0)
        {
            // No injury - keep transparent interior
            button.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }
        else
        {
            // Has injury - determine worst injury for color
            Injury worstInjury = injuries[0];
            float totalSeverity = 0f;
            
            foreach (var injury in injuries)
            {
                totalSeverity += GetInjurySeverity(injury);
                if (GetInjurySeverity(injury) > GetInjurySeverity(worstInjury))
                {
                    worstInjury = injury;
                }
            }

            var color = GetInjuryDisplayColor(worstInjury, totalSeverity);
            button.style.backgroundColor = color;
        }
    }

    private float GetInjurySeverity(Injury injury)
    {
        float severity = 0f;
        
        // Base severity by type
        switch (injury.injuryType)
        {
            case InjuryType.Scratch: severity = 1f; break;
            case InjuryType.Laceration: severity = 2.5f; break;
            case InjuryType.Bitten: severity = 3f; break;
        }
        
        // Infection multiplier
        if (injury.isInfected) severity *= 1.5f;
        
        return severity;
    }

    private Color GetInjuryDisplayColor(Injury injury, float totalSeverity)
    {
        if (injury.isInfected)
        {
            // Infected wounds - greenish yellow (PZ style)
            return new Color(0.8f, 0.7f, 0.2f, 0.9f);
        }
        
        // Color based on severity (PZ color palette)
        if (totalSeverity >= 4f)
        {
            // Critical - deep red
            return new Color(0.8f, 0.1f, 0.1f, 0.9f);
        }
        else if (totalSeverity >= 2.5f)
        {
            // Severe - bright red
            return new Color(0.9f, 0.2f, 0.2f, 0.9f);
        }
        else if (totalSeverity >= 1.5f)
        {
            // Moderate - orange-red
            return new Color(0.9f, 0.4f, 0.1f, 0.9f);
        }
        else
        {
            // Minor - orange
            return new Color(0.9f, 0.6f, 0.2f, 0.9f);
        }
    }

    private void OnBodyPartRightClicked(BodyPart bodyPart)
    {
        if (injurySystem == null) return;

        // Bandage all injuries on this body part
        injurySystem.BandageBodyPart(bodyPart);
        UpdateBodyPartColors();
    }
}
