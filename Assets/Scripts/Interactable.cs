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
        if (pressE_Prompt_UI != null) pressE_Prompt_UI.SetActive(false);
        if (!IsClient) return; 
        canInteract.OnValueChanged += OnCanInteractChanged;
        OnCanInteractChanged(false, canInteract.Value);
    }

    public override void OnNetworkDespawn()
    {
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

    public void OnPlayerEntered(GameObject playerObject)
    {
        localPlayerIsInside = true;
        PlayerChangedTriggerStateServerRpc(true);
        if (pressE_Prompt_UI != null) pressE_Prompt_UI.SetActive(canInteract.Value);
    }

    public void OnPlayerExited()
    {
        localPlayerIsInside = false;
        PlayerChangedTriggerStateServerRpc(false);
        if (pressE_Prompt_UI != null) pressE_Prompt_UI.SetActive(false);
    }

    // --- FUNÇÕES DO SERVIDOR ---
    public void Unlock()
    {
        if (!IsServer) return; 
        isLocked.Value = false;
        ServerCheckInteractionState(); // Usa a nova função
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
        
        ServerCheckInteractionState(); // Usa a nova função
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void TryInteractServerRpc()
    {
        if (canInteract.Value)
        {
            NetworkObject.Despawn(true); 
        }
    }

    // --- MUDANÇA: Esta função agora é pública ---
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