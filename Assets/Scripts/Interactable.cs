using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using TMPro; // Necessário para mexer no Texto

[DefaultExecutionOrder(100)]
public class Interactable : NetworkBehaviour
{
    [Header("Configuração")]
    public KeyCode interactKey = KeyCode.E;
    
    [Header("UI")]
    // MUDANÇA: Agora é um TextMeshProUGUI para podermos mudar o texto
    [SerializeField] private TextMeshProUGUI promptText; 

    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(true);
    private NetworkVariable<bool> canInteract = new NetworkVariable<bool>(false);

    private bool localPlayerIsInside = false;
    private List<ulong> playersInTrigger = new List<ulong>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (promptText != null)
        {
            promptText.gameObject.SetActive(false);
            // Define o texto inicial
            promptText.text = $"Press [{interactKey}] to interact";
        }
            
        if (!IsClient) return; 
        
        canInteract.OnValueChanged += OnCanInteractChanged;
        OnCanInteractChanged(false, canInteract.Value);
    }

    public override void OnNetworkDespawn()
    {
        // Garante que a UI se esconde ao destruir
        if (promptText != null)
        {
            promptText.gameObject.SetActive(false);
        }

        if (IsClient)
        {
            canInteract.OnValueChanged -= OnCanInteractChanged;
        }
        base.OnNetworkDespawn();
    }
    
    private void OnCanInteractChanged(bool previousValue, bool newValue)
    {
        UpdateUI(newValue && localPlayerIsInside);
    }

    void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) 
        {
            return;
        }

        if (localPlayerIsInside && canInteract.Value && Input.GetKeyDown(interactKey))
        {
            TryInteractServerRpc();
        }
    }

    // --- Funções Públicas (chamadas pelo InteractableTrigger) ---

    public void OnPlayerEntered(GameObject playerObject)
    {
        localPlayerIsInside = true;
        PlayerChangedTriggerStateServerRpc(true);
        
        // Atualiza a UI se pudermos interagir
        UpdateUI(canInteract.Value);
    }

    public void OnPlayerExited()
    {
        localPlayerIsInside = false;
        PlayerChangedTriggerStateServerRpc(false);

        // Esconde a UI
        UpdateUI(false);
    }

    // Função auxiliar para controlar a UI e o Texto
    private void UpdateUI(bool show)
    {
        if (promptText != null)
        {
            promptText.gameObject.SetActive(show);
            if (show)
            {
                // Escreve o texto explicitamente
                promptText.text = $"Press [{interactKey}] to interact";
            }
        }
    }

    // --- FUNÇÕES DO SERVIDOR ---
    public void Unlock()
    {
        if (!IsServer) return; 
        
        isLocked.Value = false;
        ServerCheckInteractionState(); 
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayerChangedTriggerStateServerRpc(bool entered, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        TargetMultiplayer target = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponent<TargetMultiplayer>();
        if (target != null && target.IsDead.Value)
        {
            return; 
        }
        
        if (entered)
        {
            if (!playersInTrigger.Contains(clientId))
                playersInTrigger.Add(clientId);
        }
        else
        {
            playersInTrigger.Remove(clientId);
        }
        
        ServerCheckInteractionState(); 
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void TryInteractServerRpc()
    {
        if (canInteract.Value)
        {
            NetworkObject.Despawn(true); 
        }
    }
    
    public void ServerCheckInteractionState()
    {
        if (!IsServer) return;

        int totalLivingPlayers = GameManagerHelper.GetLivingPlayerCount();
        
        if (totalLivingPlayers == 0)
        {
            canInteract.Value = false;
            return;
        }

        bool allInside = playersInTrigger.Count == totalLivingPlayers;
        canInteract.Value = (!isLocked.Value && allInside);
    }
}