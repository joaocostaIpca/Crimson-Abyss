using UnityEngine;
using Unity.Netcode;
using System.Collections; // Precisamos disto para a Co-rotina

public class StaticEnemySpawner : NetworkBehaviour 
{
    [Header("Configuração")]
    [SerializeField] private GameObject enemyPrefab; 

    [Header("Patrulha (Opcional)")]
    [SerializeField] private float patrolRadius = 10f;
    
    // --- MUDANÇA 1: Adicionar um 'delay' ---
    [Header("Spawn Settings")]
    [SerializeField] private float spawnDelay = 2.0f; // 2 segundos de espera

    // OnNetworkSpawn é demasiado cedo
    // Vamos usar Start()
    public override void OnNetworkSpawn()
    {
        // Só o Host é que faz alguma coisa
        if (!IsServer)
        {
            this.enabled = false;
        }
    }

    private void Start()
    {
        // Se não formos o Host, não fazemos nada
        if (!IsServer) return;
        
        // --- MUDANÇA 2: Chamar a Co-rotina ---
        // Em vez de "spawnar" já, esperamos uns segundos
        StartCoroutine(DelayedSpawn());
    }

    // --- MUDANÇA 3: A nova lógica de Spawn ---
    private IEnumerator DelayedSpawn()
    {
        // Espera X segundos para garantir que os Clientes
        // já carregaram a cena e os seus jogadores
        yield return new WaitForSeconds(spawnDelay);
        
        // 1. Cria o inimigo
        GameObject enemyInstance = Instantiate(enemyPrefab, transform.position, transform.rotation);
        
        // 2. "Carimba" a patrulha
        EnemyAI ai = enemyInstance.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.SetPatrolMode(transform.position, patrolRadius);
        }

        // 3. "Spawna" o inimigo na rede
        enemyInstance.GetComponent<NetworkObject>().Spawn(true);
        
        // 4. "Despawna" este spawner (em rede)
        NetworkObject.Despawn(true);
    }
}