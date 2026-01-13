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
    [SerializeField] private Vector3 targetSpawnPosition = new Vector3(-16.348f, 1.9f, -0.37f);

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI promptText; 

    private NetworkVariable<bool> allPlayersReady = new NetworkVariable<bool>(false);
    
    private bool isLocalPlayerInZone = false;
    private float currentHoldTimer = 0f;
    private List<ulong> playersInZone = new List<ulong>();
    
    // Variável para impedir spam do comando de transição
    private bool isTransitioning = false;

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
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    private void OnReadyStateChanged(bool oldVal, bool newVal)
    {
        UpdateUI();
    }

    void Update()
    {
        // Se já estamos a transitar, não fazemos mais nada
        if (isTransitioning) return;

        if (isLocalPlayerInZone)
        {
            UpdateUI(); 

            if (allPlayersReady.Value)
            {
                if (Input.GetKey(interactKey))
                {
                    currentHoldTimer += Time.deltaTime;
                    float timeLeft = Mathf.Max(0, holdDuration - currentHoldTimer);
                    
                    if (promptText != null)
                        promptText.text = $"Travelling in {timeLeft:F1}s...";

                    // --- AQUI ESTÁ A MUDANÇA ---
                    if (currentHoldTimer >= holdDuration)
                    {
                        StartCoroutine(TransitionSequence());
                    }
                }
                else
                {
                    currentHoldTimer = 0f;
                    if (promptText != null)
                        promptText.text = $"Hold [{interactKey}] to travel";
                }
            }
            else
            {
                currentHoldTimer = 0f;
                if (promptText != null)
                    promptText.text = "Waiting for the other players...";
            }
        }
    }

    // --- NOVA CORROTINA DE TRANSIÇÃO ---
    private IEnumerator TransitionSequence()
    {
        isTransitioning = true; // Bloqueia novos inputs

        // 1. Limpa o Texto IMEDIATAMENTE (sem apagar o Canvas)
        if (promptText != null)
        {
            promptText.text = "";
            promptText.gameObject.SetActive(false);
        }

        // 2. Espera os 0.2 segundos que pediste
        yield return new WaitForSeconds(0.2f);

        // 3. Chama o Servidor para mudar de cena
        RequestLevelChangeServerRpc();
        
        currentHoldTimer = 0f;
        // isTransitioning mantém-se true até mudarmos de cena
    }

    // --- FÍSICA ---

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                isLocalPlayerInZone = true;
                // Só mostra o texto se NÃO estivermos já a transitar
                if (promptText != null && !isTransitioning) promptText.gameObject.SetActive(true);
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
        // Se estivermos a transitar, não queremos que a UI reapareça
        if (!isLocalPlayerInZone || promptText == null || isTransitioning) return;

        if (allPlayersReady.Value)
            promptText.text = $"Hold [{interactKey}] to travek´l";
        else
            promptText.text = "Waiting for the other Players...";
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
            TeleportAllPlayers();
            Debug.Log($"[SceneTransitioner] A carregar cena: {nextSceneName}");
            NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
        }
    }

    private void TeleportAllPlayers()
    {
        int index = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            Transform pTransform = client.PlayerObject.transform;
            var agent = client.PlayerObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            
            Vector3 finalPos = targetSpawnPosition;
            finalPos.x += (index * 1.5f); 

            pTransform.position = finalPos;
            
            var rb = client.PlayerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero; 
                rb.position = finalPos;
            }

            if (agent != null) agent.enabled = true;
            index++;
        }
    }
}