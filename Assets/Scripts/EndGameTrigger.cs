using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public class EndGameTrigger : NetworkBehaviour
{
    [Header("Configuração")]
    [SerializeField] private string endSceneName = "EndOfDemo";
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Limpeza de UI")]
    // IMPORTANTE: Escreve aqui o nome exato do teu objeto Canvas na cena
    [SerializeField] private string canvasObjectName = "Canvas"; 

    [Header("UI do Trigger")]
    [SerializeField] private TextMeshProUGUI promptText; 

    private bool isLocalPlayerInZone = false;

    private void Start()
    {
        if (promptText != null)
        {
            promptText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (isLocalPlayerInZone && Input.GetKeyDown(interactKey))
        {
            FinishDemoServerRpc();
        }
    }

    // --- FÍSICA ---

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                isLocalPlayerInZone = true;
                if (promptText != null)
                {
                    promptText.text = $"Press [{interactKey}] to End Demo";
                    promptText.gameObject.SetActive(true);
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                isLocalPlayerInZone = false;
                if (promptText != null) promptText.gameObject.SetActive(false);
            }
        }
    }

    // --- LÓGICA DE REDE ---

    [ServerRpc(RequireOwnership = false)]
    private void FinishDemoServerRpc()
    {
        if (!IsServer) return;

        Debug.Log("A terminar Demo...");

        // 1. Manda ordem a todos os clientes para apagarem o Canvas antigo
        DestroyOldUIClientRpc();

        // 2. Destruir os Bonecos dos Jogadores
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                client.PlayerObject.Despawn(true); 
            }
        }

        // 3. Carregar a cena final
        NetworkManager.Singleton.SceneManager.LoadScene(endSceneName, LoadSceneMode.Single);
    }

    [ClientRpc]
    private void DestroyOldUIClientRpc()
    {
        // Procura o Canvas pelo nome e destroi-o
        GameObject oldCanvas = GameObject.Find(canvasObjectName);
        
        if (oldCanvas != null)
        {
            Destroy(oldCanvas);
            Debug.Log("Canvas antigo destruído com sucesso.");
        }
        else
        {
            // Tenta procurar pela Tag se o nome falhar (Opcional)
            GameObject oldCanvasByTag = GameObject.FindGameObjectWithTag("UI"); // ou outra tag que uses
            if (oldCanvasByTag != null) Destroy(oldCanvasByTag);
        }
    }
}