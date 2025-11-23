using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransitioner : NetworkBehaviour
{
    [Header("Settings")]
    public float holdTime = 5f; // 5 Segundos de espera
    [SerializeField] private string gameSceneName = "SubLevel";

    [Header("UI")]
    // Arrastar a imagem de "loading" (ex: um círculo) do Canvas
    [SerializeField] private Image progressIcon; 
    // Arrastar o texto "À espera de jogadores..."
    [SerializeField] private TextMeshProUGUI promptText; 

    // Variável de rede para sincronizar o progresso (0 a 1) com todos
    private NetworkVariable<float> progress = new NetworkVariable<float>(0f);
    
    // Lista de jogadores dentro da área (apenas no Servidor)
    private List<ulong> playersInZone = new List<ulong>();

    private void Start()
    {
        if (progressIcon != null) progressIcon.fillAmount = 0f;
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        // Todos os clientes subscrevem à mudança do progresso para atualizar a UI
        progress.OnValueChanged += OnProgressChanged;
    }

    public override void OnNetworkDespawn()
    {
        progress.OnValueChanged -= OnProgressChanged;
    }

    private void OnProgressChanged(float oldVal, float newVal)
    {
        // Atualiza a barra de progresso visualmente
        if (progressIcon != null)
        {
            progressIcon.fillAmount = newVal;
        }
    }

    void Update()
    {
        // A lógica corre APENAS no Servidor
        if (!IsServer) return;

        // Verifica quantos jogadores estão ligados ao servidor
        int totalPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        
        // Se TODOS os jogadores estiverem na zona
        if (playersInZone.Count >= totalPlayers && totalPlayers > 0)
        {
            // Aumenta o progresso
            float newProgress = progress.Value + (Time.deltaTime / holdTime);
            progress.Value = Mathf.Clamp01(newProgress);

            // Se chegou ao fim (5 segundos)
            if (progress.Value >= 1f)
            {
                // 1. Teletransporta todos
                MoveAllPlayers(2, -9, 8, 1);
                
                // 2. Carrega a nova cena (O Netcode trata de carregar para todos)
                NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
                
                // Desliga este script para não correr mais
                this.enabled = false;
            }
        }
        else
        {
            // Se alguém sair, o progresso desce (ou vai a 0)
            if (progress.Value > 0)
            {
                progress.Value = Mathf.Max(0, progress.Value - Time.deltaTime);
            }
        }
    }

    // --- DETEÇÃO DE JOGADORES (Servidor) ---

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return; // Só o servidor conta

        if (other.CompareTag("Player"))
        {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && !playersInZone.Contains(netObj.OwnerClientId))
            {
                playersInZone.Add(netObj.OwnerClientId);
                UpdatePromptClientRpc(true, playersInZone.Count, NetworkManager.Singleton.ConnectedClients.Count);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player"))
        {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && playersInZone.Contains(netObj.OwnerClientId))
            {
                playersInZone.Remove(netObj.OwnerClientId);
                UpdatePromptClientRpc(true, playersInZone.Count, NetworkManager.Singleton.ConnectedClients.Count);
            }
        }
    }

    // --- UI e MOVIMENTO ---

    [ClientRpc]
    private void UpdatePromptClientRpc(bool show, int current, int total)
    {
        if (promptText != null)
        {
            promptText.gameObject.SetActive(show);
            if (show)
            {
                if (current == total)
                    promptText.text = "A viajar...";
                else
                    promptText.text = $"À espera de jogadores: {current}/{total}";
            }
        }
    }

    public void MoveAllPlayers(float targetY, float minX, float maxX, float minSpacing)
    {
        // Função chamada no Servidor antes de mudar de cena
        List<float> usedXPositions = new List<float>();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            float randomX = 0f;
            bool valid = false;

            // Tenta encontrar posição aleatória sem sobreposição
            for (int tries = 0; tries < 50; tries++)
            {
                float candidate = Random.Range(minX, maxX);
                bool tooClose = false;
                foreach (float used in usedXPositions)
                {
                    if (Mathf.Abs(candidate - used) < minSpacing) { tooClose = true; break; }
                }

                if (!tooClose)
                {
                    randomX = candidate;
                    valid = true;
                    usedXPositions.Add(candidate);
                    break;
                }
            }

            if (!valid)
            {
                randomX = usedXPositions.Count * minSpacing + minX;
                usedXPositions.Add(randomX);
            }

            // Teleporta o jogador (Servidor tem autoridade, logo funciona)
            // Importante: Desativar CharacterController se estiveres a usar um, mas com Rigidbody é direto
            client.PlayerObject.transform.position = new Vector3(randomX, targetY, 0);
        }
    }
}