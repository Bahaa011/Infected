using System.Collections.Generic;
using UnityEngine;

public static class ZombieNoiseSystem
{
    public enum NoiseType
    {
        Footstep,
        Gunshot,
        Impact,
        Custom
    }

    private static readonly Collider[] overlapBuffer = new Collider[256];

    public static void EmitNoise(Vector3 worldPosition, float radius, float strength = 1f, NoiseType noiseType = NoiseType.Custom)
    {
        if (radius <= 0f || strength <= 0f)
            return;

        int hitCount = Physics.OverlapSphereNonAlloc(worldPosition, radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        if (hitCount <= 0)
            return;

        HashSet<Zombie> notified = new HashSet<Zombie>();

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null)
                continue;

            Zombie zombie = col.GetComponentInParent<Zombie>();
            if (zombie == null || notified.Contains(zombie))
                continue;

            notified.Add(zombie);
            zombie.RegisterNoise(worldPosition, radius, strength, noiseType);
        }
    }
}
