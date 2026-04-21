using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HouseDistanceStreamer : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";

    [Header("House Sources")]
    [SerializeField] private bool autoFindHousesByTag = true;
    [SerializeField] private string houseTag = "House";
    [SerializeField] private List<GameObject> houses = new();

    [Header("Streaming Distances")]
    [SerializeField] private float unloadDistance = 120f;
    [SerializeField] private float reloadDistance = 90f;
    [SerializeField] private float checkInterval = 0.5f;

    private float checkTimer;

    private void Awake()
    {
        ResolvePlayer();
        if (autoFindHousesByTag)
            AutoCollectHouses();

        if (reloadDistance > unloadDistance)
            reloadDistance = unloadDistance * 0.8f;
    }

    private void Update()
    {
        if (player == null)
            ResolvePlayer();

        if (player == null || houses == null || houses.Count == 0)
            return;

        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval)
            return;

        checkTimer = 0f;
        UpdateHouseStates();
    }

    [ContextMenu("Auto Collect Houses")]
    public void AutoCollectHouses()
    {
        houses.Clear();

        if (string.IsNullOrWhiteSpace(houseTag))
            return;

        GameObject[] found = GameObject.FindGameObjectsWithTag(houseTag);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && !houses.Contains(found[i]))
                houses.Add(found[i]);
        }
    }

    private void ResolvePlayer()
    {
        if (player != null)
            return;

        if (!string.IsNullOrWhiteSpace(playerTag))
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null)
                player = playerObj.transform;
        }

        if (player == null)
        {
            Player p = FindAnyObjectByType<Player>();
            if (p != null)
                player = p.transform;
        }
    }

    private void UpdateHouseStates()
    {
        float unloadSqr = unloadDistance * unloadDistance;
        float reloadSqr = reloadDistance * reloadDistance;
        Vector3 playerPos = player.position;

        for (int i = 0; i < houses.Count; i++)
        {
            GameObject house = houses[i];
            if (house == null)
                continue;

            float sqrDistance = (house.transform.position - playerPos).sqrMagnitude;

            if (house.activeSelf)
            {
                if (sqrDistance > unloadSqr)
                    house.SetActive(false);
            }
            else
            {
                if (sqrDistance < reloadSqr)
                    house.SetActive(true);
            }
        }
    }
}
