using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class WavControllerScript : NetworkBehaviour
{
    [Header("Enemy Settings")]
    public GameObject enemyPrefab;
    public Transform[] spawnPoints;
    public int totalEnemiesToSpawn = 5;
    public int releasePerWave = 2;
    public float spawnRadius = 2f;

    [Header("Wave Settings")]
    public bool unlimitedWaves = false;
    public float waveDelay = 5f;
    
    [SerializeField] private List<Interactable> objectsToUnlock = new List<Interactable>();

    private int enemiesSpawned = 0;
    private bool hasTriggered = false;
    private int enemiesAlive = 0;

    private MeshRenderer meshRenderer;
    private Collider[] allColliders;
    
    private List<ulong> playersWhoExited = new List<ulong>();

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        allColliders = GetComponents<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            this.enabled = false;
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void PlayerExitedTriggerServerRpc(ulong clientId)
    {
        if (!IsServer) return;
        
        if (!playersWhoExited.Contains(clientId))
        {
            playersWhoExited.Add(clientId);
        }

        int totalPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        if (playersWhoExited.Count >= totalPlayers && !hasTriggered)
        {
            StartWave();
        }
    }

    private void StartWave()
    {
        if (hasTriggered) return;
        hasTriggered = true;

        CloseGateClientRpc();
        StartCoroutine(SpawnWaveRoutine());
    }

    private IEnumerator SpawnWaveRoutine()
    {
        if (unlimitedWaves)
        {
            while (true)
            {
                SpawnWave();
                yield return new WaitForSeconds(waveDelay);
            }
        }
        else
        {
            while (enemiesSpawned < totalEnemiesToSpawn)
            {
                SpawnWave();
                yield return new WaitForSeconds(waveDelay);
            }

            while (enemiesAlive > 0)
                yield return null;
            Debug.Log($"[WavController] Wave terminada! A tentar destrancar {objectsToUnlock.Count} objeto(s).");
       
            // Verifica se a lista não é nula E se tem pelo menos 1 item
            if (objectsToUnlock != null && objectsToUnlock.Count > 0)
            {
          
                foreach (Interactable obj in objectsToUnlock)
                {
                    if (obj != null)
                    {
                        obj.Unlock();
                    }
                }
            }
            
            OpenGateClientRpc();
        }
    }

    private void SpawnWave()
    {
        int toSpawn = unlimitedWaves
            ? releasePerWave
            : Mathf.Min(releasePerWave, totalEnemiesToSpawn - enemiesSpawned);
        
        for (int i = 0; i < toSpawn; i++)
        {
            if (!unlimitedWaves && enemiesSpawned >= totalEnemiesToSpawn) break;

            Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
            Vector2 circleOffset = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = new Vector3(
                spawnPoint.position.x + circleOffset.x,
                spawnPoint.position.y,
                spawnPoint.position.z + circleOffset.y
            );

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, spawnPoint.rotation);
            
            TargetMultiplayer enemyHealth = enemy.GetComponent<TargetMultiplayer>();
            enemyHealth.OnHealthZero += OnEnemyDied; 
            
            enemy.GetComponent<NetworkObject>().Spawn(true); 

            enemiesSpawned++;
            enemiesAlive++;
        }
    }
    
    private void OnEnemyDied(TargetMultiplayer deadEnemy)
    {
        deadEnemy.OnHealthZero -= OnEnemyDied; 
        enemiesAlive--;
    }

    [ClientRpc]
    private void CloseGateClientRpc()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = true;

        foreach (Collider col in allColliders)
            col.enabled = true;
    }

    [ClientRpc]
    private void OpenGateClientRpc()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = false;

        foreach (Collider col in allColliders)
            col.enabled = false;
    }
}