using System.Collections;
using UnityEngine;

public class WavControllerScript : MonoBehaviour
{
    [Header("Enemy Settings")]
    public GameObject enemyPrefab;       // Enemy prefab to spawn
    public Transform[] spawnPoints;      // Set spawn points in inspector
    public int totalEnemiesToSpawn = 5;  // Total amount to eventually release
    public int releasePerWave = 2;       // How many to release per wave (1 or 2)
    public float spawnRadius = 2f;

    [Header("Wave Settings")]
    public float waveDelay = 5f;

    private int enemiesSpawned = 0;
    private bool hasTriggered = false;
    private int enemiesAlive = 0;

    private MeshRenderer meshRenderer;
    private Collider[] allColliders;

    private void Awake()
    {
        // Automatically get components from THIS object
        meshRenderer = GetComponent<MeshRenderer>();
        allColliders = GetComponents<Collider>();
    }

    private void OnTriggerExit(Collider other)
    {
        if (hasTriggered) return;
        hasTriggered = true;

        //  Reactivate mesh
        if (meshRenderer != null)
            meshRenderer.enabled = true;

        //  Reactivate all colliders (solid wall/gate again)
        foreach (Collider col in allColliders)
            col.enabled = true;

        //  Spawn first enemy wave
        StartCoroutine(SpawnWaveRoutine());
    }

    private IEnumerator SpawnWaveRoutine()
    {
        while (enemiesSpawned < totalEnemiesToSpawn)
        {
            SpawnWave();
            Debug.Log("Wave Spawned");
            yield return new WaitForSeconds(waveDelay);
        }

        while (enemiesAlive > 0)
        {
            yield return null;
        }
        Debug.Log("All enemies were killed");
        OpenGate();

    }

    private void SpawnWave()
    {
        int toSpawn = Mathf.Min(releasePerWave, totalEnemiesToSpawn - enemiesSpawned);

        for (int i = 0; i < toSpawn; i++)
        {
            if (enemiesSpawned >= totalEnemiesToSpawn) break;

            Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

            // Random circle on ground (X/Z)
            Vector2 circleOffset = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = new Vector3(
                spawnPoint.position.x + circleOffset.x,
                spawnPoint.position.y,
                spawnPoint.position.z + circleOffset.y
            );

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, spawnPoint.rotation);

            // Register enemy death callback
            EnemyDeathNotifier notifier = enemy.AddComponent<EnemyDeathNotifier>();
            notifier.spawner = this;

            enemiesSpawned++;
            enemiesAlive++;
        }
    }

    public void EnemyDied()
    {
        Debug.Log("enemy died");
        enemiesAlive--;
    }

    private void OpenGate()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = false;

        foreach (Collider col in allColliders)
            col.enabled = false;
    }


}
