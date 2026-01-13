using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    private void OnReadyStateChanged(bool oldVal, bool newVal) => UpdateUI();

    void Update()
    {
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
                    if (promptText != null) promptText.text = $"Travelling in {timeLeft:F1}s...";

                    if (currentHoldTimer >= holdDuration) StartCoroutine(TransitionSequence());
                }
                else
                {
                    currentHoldTimer = 0f;
                    if (promptText != null) promptText.text = $"Hold [{interactKey}] to travel";
                }
            }
            else
            {
                currentHoldTimer = 0f;
                if (promptText != null) promptText.text = "Waiting for other players...";
            }
        }
    }

    private IEnumerator TransitionSequence()
    {
        isTransitioning = true; 
        if (promptText != null) promptText.text = "Loading...";
        yield return new WaitForSeconds(0.2f);
        
        // APENAS PEDE A MUDANÇA DE CENA
        // O trabalho pesado agora é feito pelo LevelManager no NetworkManager
        RequestLevelChangeServerRpc();
    }

    // --- FÍSICA E RPCs (Mantém-se igual) ---
    void OnTriggerEnter(Collider other) { /* ...Igual ao anterior... */ 
        if (other.CompareTag("Player")) {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner) {
                isLocalPlayerInZone = true;
                if (promptText != null && !isTransitioning) promptText.gameObject.SetActive(true);
                UpdateUI();
                SetPlayerInZoneServerRpc(true);
            }
        }
    }

    void OnTriggerExit(Collider other) { /* ...Igual ao anterior... */ 
        if (other.CompareTag("Player")) {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.IsOwner) {
                isLocalPlayerInZone = false;
                currentHoldTimer = 0f;
                if (promptText != null) promptText.gameObject.SetActive(false);
                SetPlayerInZoneServerRpc(false);
            }
        }
    }

    private void UpdateUI() { /* ...Igual... */ 
        if (!isLocalPlayerInZone || promptText == null || isTransitioning) return;
        if (allPlayersReady.Value) promptText.text = $"Hold [{interactKey}] to travel";
        else promptText.text = "Waiting for other players...";
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerInZoneServerRpc(bool entered, ServerRpcParams rpcParams = default)
    {
        if (!IsSpawned) return;
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (entered) { if (!playersInZone.Contains(clientId)) playersInZone.Add(clientId); }
        else { playersInZone.Remove(clientId); }
        CheckIfAllReady();
    }

    private void CheckIfAllReady()
    {
        if (!IsServer) return;
        int totalLiving = GameManagerHelper.GetLivingPlayerCount();
        if (totalLiving == 0) { allPlayersReady.Value = false; return; }
        allPlayersReady.Value = playersInZone.Count >= totalLiving;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLevelChangeServerRpc()
    {
        if (!IsServer) return;
        if (allPlayersReady.Value)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
        }
    }
}