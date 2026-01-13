using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Agora herda de MonoBehaviour para poderes colocar no NetworkManager sem erros
public class LevelManager : MonoBehaviour
{
    [Header("Configuração de Spawn")]
    [SerializeField] private string spawnPointName = "SpawnPoint";

    private void Start()
    {
        // Inscreve-se para saber quando o Servidor arranca
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }

    private void OnDestroy()
    {
        // Limpeza de eventos para não dar erros ao fechar o jogo
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
            }
        }
    }

    private void OnServerStarted()
    {
        // Assim que o servidor estiver online, começamos a ouvir as mudanças de cena
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        // Como isto é um MonoBehaviour, confirmamos manualmente se somos o Servidor
        if (!NetworkManager.Singleton.IsServer) return;

        GameObject spawnPoint = GameObject.Find(spawnPointName);

        if (spawnPoint != null)
        {
            Debug.Log($"LevelManager: A teleportar jogadores para {spawnPointName}...");
            TeleportPlayersTo(spawnPoint.transform.position);
        }
    }

    private void TeleportPlayersTo(Vector3 targetPos)
    {
        int index = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            Transform pTransform = client.PlayerObject.transform;
            
            // 1. Desligar NavMeshAgent
            var agent = client.PlayerObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            // 2. Calcular posição em fila
            Vector3 finalPos = targetPos;
            finalPos.x += (index * 1.5f); 

            // 3. Teleportar Transform
            pTransform.position = finalPos;

            // 4. Resetar Física
            var rb = client.PlayerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.position = finalPos;
            }

            // 5. Religar NavMeshAgent
            if (agent != null)
            {
                agent.Warp(finalPos);
                agent.enabled = true;
            }

            index++;
        }
    }
}