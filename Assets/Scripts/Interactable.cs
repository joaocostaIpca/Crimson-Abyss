using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

// --- MUDANÇA 1: Adicionar a Ordem de Execução ---
// (Isto força o script a esperar pelo NetworkManager)
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

    // (Corre SÓ no Cliente Local)
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

        Debug.Log($"[Interactable {gameObject.name}] FUI DESTRANCADO!");
        
        isLocked.Value = false;
        CheckInteractionState(); 
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayerChangedTriggerStateServerRpc(bool entered, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        if (entered)
        {
            if (!playersInTrigger.Contains(clientId))
                playersInTrigger.Add(clientId);
        }
        else
        {
            playersInTrigger.Remove(clientId);
        }
        
        CheckInteractionState(); 
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void TryInteractServerRpc()
    {

        Debug.Log($"[Interactable {gameObject.name}] Servidor recebeu pedido de interação. Verificando... (canInteract.Value = {canInteract.Value})");
        if (canInteract.Value)
        {
            Debug.Log("...Interação APROVADA. A DESTRUIR.");
            NetworkObject.Despawn(true); 
        }
    }

    private void CheckInteractionState()
    {
        if (!IsServer) return;

        int totalPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        bool allInside = playersInTrigger.Count == totalPlayers;

        canInteract.Value = (!isLocked.Value && allInside);
        Debug.Log($"[Interactable {gameObject.name}] CheckState: Trancado={isLocked.Value}, JogadoresDentro={playersInTrigger.Count}, TodosDentro={allInside}. => Posso Interagir? {canInteract.Value}");
    }
}