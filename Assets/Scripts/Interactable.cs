using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[DefaultExecutionOrder(100)]
public class Interactable : NetworkBehaviour
{
    [Header("Configuração")]
    public KeyCode interactKey = KeyCode.E;
    
    [Header("UI")]
    [SerializeField] private GameObject pressE_Prompt_UI; 

    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(true);
    private NetworkVariable<bool> canInteract = new NetworkVariable<bool>(false);

    private bool localPlayerIsInside = false;
    private List<ulong> playersInTrigger = new List<ulong>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (pressE_Prompt_UI != null)
            pressE_Prompt_UI.SetActive(false);
            
        if (!IsClient) return; 
        
        canInteract.OnValueChanged += OnCanInteractChanged;
        OnCanInteractChanged(false, canInteract.Value);
    }

    public override void OnNetworkDespawn()
    {
        // --- A CORREÇÃO ESTÁ AQUI ---
        // Quando este objeto é destruído (Despawned),
        // garante que a UI "Pressiona E" se esconde.
        if (pressE_Prompt_UI != null)
        {
            pressE_Prompt_UI.SetActive(false);
        }
        // --- FIM DA CORREÇÃO ---

        if (IsClient)
        {
            canInteract.OnValueChanged -= OnCanInteractChanged;
        }
        base.OnNetworkDespawn();
    }
    
    private void OnCanInteractChanged(bool previousValue, bool newValue)
    {
        if (pressE_Prompt_UI != null)
        {
            // (A lógica de mostrar continua igual)
            pressE_Prompt_UI.SetActive(newValue && localPlayerIsInside);
        }
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
            
        if (pressE_Prompt_UI != null)
            pressE_Prompt_UI.SetActive(canInteract.Value);
    }

    public void OnPlayerExited()
    {
        localPlayerIsInside = false;
        PlayerChangedTriggerStateServerRpc(false);

        if (pressE_Prompt_UI != null)
            pressE_Prompt_UI.SetActive(false);
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
    
    // (Esta função é pública para o GameManagerHelper a poder chamar)
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