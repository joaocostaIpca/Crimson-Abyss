using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class WavControllerScript : NetworkBehaviour
{
    // Classe para configurar um tipo de inimigo dentro de uma wave
    [System.Serializable]
    public class EnemyWaveConfig
    {
        public string enemyName;        // Ex: "Diabrete" (só para organização)
        public GameObject prefab;       // O prefab do inimigo
        public Transform[] spawnPoints; // Onde este tipo nasce
        public int count;               // Quantos nascem
        public float patrolRadius = 10f; // Raio de patrulha para este tipo
    }

    // Classe para configurar uma Wave completa
    [System.Serializable]
    public class Wave
    {
        public string waveName; // Ex: "Wave 1"
        public List<EnemyWaveConfig> enemies; // Lista de inimigos nesta wave
        public float timeBetweenSpawns = 1f;  // Tempo entre spawns nesta wave
    }

    [Header("Configuração das Waves")]
    public List<Wave> waves = new List<Wave>(); // A tua lista de waves
    
    [Header("Definições Gerais")]
    public float timeBetweenWaves = 5f;
    [SerializeField] private List<Interactable> objectsToUnlock = new List<Interactable>();

    private int currentWaveIndex = 0;
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
        if (!IsServer) this.enabled = false;
    }
    
    public void CheckTriggerStateAfterPlayerDeath()
    {
        if (!IsServer || hasTriggered) return;
        
        playersWhoExited.RemoveAll(id => 
            !NetworkManager.Singleton.ConnectedClients.ContainsKey(id) || 
            NetworkManager.Singleton.ConnectedClients[id].PlayerObject.GetComponent<TargetMultiplayer>().IsDead.Value
        );

        int totalLivingPlayers = GameManagerHelper.GetLivingPlayerCount();
        if (playersWhoExited.Count >= totalLivingPlayers && totalLivingPlayers > 0)
        {
            StartWaves();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlayerExitedTriggerServerRpc(ulong clientId)
    {
        if (!IsServer) return;
        
        var target = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponent<TargetMultiplayer>();
        if (target != null && target.IsDead.Value) return; 
        
        if (!playersWhoExited.Contains(clientId))
        {
            playersWhoExited.Add(clientId);
        }

        int totalLivingPlayers = GameManagerHelper.GetLivingPlayerCount();
        if (playersWhoExited.Count >= totalLivingPlayers && !hasTriggered && totalLivingPlayers > 0)
        {
            StartWaves();
        }
    }

    private void StartWaves()
    {
        if (hasTriggered) return;
        hasTriggered = true;
        CloseGateClientRpc();
        StartCoroutine(WaveRoutine());
    }

    private IEnumerator WaveRoutine()
    {
        currentWaveIndex = 0;

        // Loop por todas as waves configuradas
        while (currentWaveIndex < waves.Count)
        {
            Wave currentWave = waves[currentWaveIndex];
            
            // Spawnar todos os grupos desta wave
            foreach (var enemyGroup in currentWave.enemies)
            {
                if (enemyGroup.prefab == null || enemyGroup.spawnPoints.Length == 0) continue;

                for (int i = 0; i < enemyGroup.count; i++)
                {
                    SpawnEnemy(enemyGroup);
                    yield return new WaitForSeconds(currentWave.timeBetweenSpawns); // Pequeno delay entre monstros
                }
            }

            // Espera que todos morram antes de passar à próxima wave
            while (enemiesAlive > 0)
            {
                yield return null;
            }

            // Intervalo entre waves
            yield return new WaitForSeconds(timeBetweenWaves);
            currentWaveIndex++;
        }

        // Acabaram todas as waves
        if (objectsToUnlock != null && objectsToUnlock.Count > 0)
        {
            foreach (Interactable obj in objectsToUnlock)
            {
                if (obj != null) obj.Unlock();
            }
        }
        OpenGateClientRpc();
    }

    private void SpawnEnemy(EnemyWaveConfig config)
    {
        Transform spawnPoint = config.spawnPoints[Random.Range(0, config.spawnPoints.Length)];
        Vector3 spawnPos = spawnPoint.position;

        // Pequena variação aleatória para não nascerem todos no mesmo pixel
        Vector2 randomCircle = Random.insideUnitCircle * 1f; 
        spawnPos += new Vector3(randomCircle.x, 0, randomCircle.y);

        GameObject enemy = Instantiate(config.prefab, spawnPos, spawnPoint.rotation);
        
        TargetMultiplayer enemyHealth = enemy.GetComponent<TargetMultiplayer>();
        if (enemyHealth != null) enemyHealth.OnHealthZero += OnEnemyDied; 
        
        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.SetPatrolMode(spawnPos, config.patrolRadius);
        }
        
        enemy.GetComponent<NetworkObject>().Spawn(true); 
        enemiesAlive++;
    }
    
    private void OnEnemyDied(TargetMultiplayer deadEnemy)
    {
        deadEnemy.OnHealthZero -= OnEnemyDied; 
        enemiesAlive--;
    }

    [ClientRpc]
    private void CloseGateClientRpc()
    {
        if (meshRenderer != null) meshRenderer.enabled = true;
        foreach (Collider col in allColliders) col.enabled = true;
    }

    [ClientRpc]
    private void OpenGateClientRpc()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
        foreach (Collider col in allColliders) col.enabled = false;
    }
}