using UnityEngine;

/// <summary>
/// Defines the different body parts that can be injured
/// </summary>
public enum BodyPart
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

/// <summary>
/// Defines the types of injuries that can occur
/// </summary>
public enum InjuryType
{
    Scratch,      // Minor injury
    Laceration,   // Medium injury
    Bitten        // Severe injury with infection risk
}
