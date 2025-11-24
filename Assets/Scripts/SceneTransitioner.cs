using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(NetworkObject))]
public class SceneTransitioner : NetworkBehaviour
{
    [Header("Definições")]
    public float holdDuration = 3f;
    public KeyCode interactKey = KeyCode.E;
    [SerializeField] private string nextSceneName = "SubLevel";

    [Header("Spawn na Próxima Cena")]
    // As coordenadas que pediste (baseadas na tua imagem)
    [SerializeField] private Vector3 targetSpawnPosition = new Vector3(-16.348f, 1.967f, -0.37f);

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI promptText; 

    private NetworkVariable<bool> allPlayersReady = new NetworkVariable<bool>(false);
    private bool isLocalPlayerInZone = false;
    private float currentHoldTimer = 0f;
    private List<ulong> playersInZone = new List<ulong>();

    private void Start()
    {
        if (promptText != null)
        {
            promptText.gameObject.SetActive(false);
            promptText.text = "";
        }
    }

    public override void OnNetworkSpawn()
    {
        allPlayersReady.OnValueChanged += OnReadyStateChanged;
        if (IsServer) CheckIfAllReady();
        UpdateUI();
    }

    public override void OnNetworkDespawn()
    {
        allPlayersReady.OnValueChanged -= OnReadyStateChanged;
    }

    private void OnReadyStateChanged(bool oldVal, bool newVal)
    {
        UpdateUI();
    }

    void Update()
    {
        if (isLocalPlayerInZone)
        {
            UpdateUI(); // Garante que a UI está atualizada

            if (allPlayersReady.Value)
            {
                if (Input.GetKey(interactKey))
                {
                    currentHoldTimer += Time.deltaTime;
                    float timeLeft = Mathf.Max(0, holdDuration - currentHoldTimer);
                    
                    if (promptText != null)
                        promptText.text = $"A Viajar em {timeLeft:F1}s...";

                    if (currentHoldTimer >= holdDuration)
                    {
                        RequestLevelChangeServerRpc();
                        currentHoldTimer = 0f; 
                    }
                }
                else
                {
                    currentHoldTimer = 0f;
                    if (promptText != null)
                        promptText.text = $"Segura [{interactKey}] para Viajar";
                }
            }
            else
            {
                currentHoldTimer = 0f;
                if (promptText != null)
                    promptText.text = "À espera de outros jogadores...";
            }
        }
    }

    // --- FÍSICA ---

    void OnTriggerEnter(Collider other)
    {
        // Debug.Log($"[Zona] Entrou: {other.name}");

        if (other.CompareTag("Player"))
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                isLocalPlayerInZone = true;
                if (promptText != null) promptText.gameObject.SetActive(true);
                UpdateUI();
                
                SetPlayerInZoneServerRpc(true);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                isLocalPlayerInZone = false;
                currentHoldTimer = 0f;
                if (promptText != null) promptText.gameObject.SetActive(false);
                
                SetPlayerInZoneServerRpc(false);
            }
        }
    }

    private void UpdateUI()
    {
        if (!isLocalPlayerInZone || promptText == null) return;

        if (allPlayersReady.Value)
            promptText.text = $"Segura [{interactKey}] para Viajar";
        else
            promptText.text = "À espera de outros jogadores...";
    }

    // --- SERVIDOR ---

    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerInZoneServerRpc(bool entered, ServerRpcParams rpcParams = default)
    {
        if (!IsSpawned) return;

        ulong clientId = rpcParams.Receive.SenderClientId;

        if (entered)
        {
            if (!playersInZone.Contains(clientId))
                playersInZone.Add(clientId);
        }
        else
        {
            playersInZone.Remove(clientId);
        }

        CheckIfAllReady();
    }

    private void CheckIfAllReady()
    {
        if (!IsServer) return;

        int totalLiving = GameManagerHelper.GetLivingPlayerCount();
        // Debug.Log($"[Zona CHECK] Na Zona: {playersInZone.Count} | Vivos: {totalLiving}");

        if (totalLiving == 0)
        {
            allPlayersReady.Value = false;
            return;
        }

        bool ready = playersInZone.Count >= totalLiving;
        allPlayersReady.Value = ready;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLevelChangeServerRpc()
    {
        if (!IsServer) return;

        if (allPlayersReady.Value)
        {
            // 1. Teletransporta os jogadores para a coordenada fixa
            TeleportPlayersToTarget();

            Debug.Log($"[Zona] A mudar para cena: {nextSceneName}");
            NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
        }
    }

    // --- MUDANÇA: Teletransporte para ponto fixo com Z correto ---
    private void TeleportPlayersToTarget()
    {
        int index = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            // Desliga NavMeshAgent se existir para evitar conflitos
            var agent = client.PlayerObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            // Define a posição base
            Vector3 finalPos = targetSpawnPosition;

            // Adiciona um pequeno "offset" (desvio) no X para eles não ficarem
            // exatamente uns dentro dos outros (ex: -16, -14, -12...)
            finalPos.x += (index * 1.5f); 

            client.PlayerObject.transform.position = finalPos;
            
            // Reativa agente se necessário (normalmente na nova cena ele 'aterra' no NavMesh)
            if (agent != null) agent.enabled = true;

            index++;
        }
    }
}