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
        // Subscrever à mudança de estado
        allPlayersReady.OnValueChanged += OnReadyStateChanged;
        
        // Se formos o servidor, verificar estado inicial
        if (IsServer)
        {
            CheckIfAllReady();
        }
        
        // Atualizar UI inicial
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

    // --- FÍSICA (Deteta quem entra) ---

    void OnTriggerEnter(Collider other)
    {
        // Log local para debug
        Debug.Log($"[Zona] Entrou: {other.name}");

        // 1. Lógica Local (Para a UI do próprio jogador)
        if (other.CompareTag("Player"))
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                isLocalPlayerInZone = true;
                if (promptText != null) promptText.gameObject.SetActive(true);
                UpdateUI();
                
                // Avisa o servidor
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
                
                // Avisa o servidor
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
        if (!IsSpawned) return; // Previne o erro RpcException

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
        Debug.Log($"[Zona] Jogadores na Zona: {playersInZone.Count} / Vivos: {totalLiving}");

        if (totalLiving == 0)
        {
            allPlayersReady.Value = false;
            return;
        }

        // Se o número na zona for igual ou maior que o total de vivos, estamos prontos
        bool ready = playersInZone.Count >= totalLiving;
        allPlayersReady.Value = ready;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLevelChangeServerRpc()
    {
        if (!IsServer) return;

        if (allPlayersReady.Value)
        {
            Debug.Log($"[Zona] A mudar para cena: {nextSceneName}");
            NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
        }
    }
}