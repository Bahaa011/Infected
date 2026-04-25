using UnityEngine;

[DisallowMultipleComponent]
public class ZombieSpawnVolume : MonoBehaviour
{
    [SerializeField] private ZombieSpawner spawner;
    [SerializeField] private bool disableSpawnerOutsideVolume = true;

    private void Awake()
    {
        if (spawner == null)
            spawner = FindAnyObjectByType<ZombieSpawner>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!disableSpawnerOutsideVolume || spawner == null)
            return;

        Player player = other.GetComponent<Player>();
        if (player != null)
            spawner.enabled = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!disableSpawnerOutsideVolume || spawner == null)
            return;

        Player player = other.GetComponent<Player>();
        if (player != null)
            spawner.enabled = false;
    }
}
