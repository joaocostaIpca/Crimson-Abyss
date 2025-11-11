using UnityEngine;
using Unity.Netcode;
using System; 

public class TargetMultiplayer : NetworkBehaviour
{
    // Evento que o WavControllerScript vai "ouvir"
    public event Action<TargetMultiplayer> OnHealthZero;

    // --- MUDANÇA 1: Estado de Morte ---
    // (true=Morto, false=Vivo)
    public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, // Todos podem ler
        NetworkVariableWritePermission.Server   // Só o servidor pode mudar
    );

    public NetworkVariable<float> health = new NetworkVariable<float>(
        100f, 
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Referência ao PlayerController (para o modo espectador)
    private PlayerController playerController;

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerController>();
        IsDead.OnValueChanged += OnDeathStateChanged; // Subscreve à mudança de estado
    }

    public override void OnNetworkDespawn()
    {
        IsDead.OnValueChanged -= OnDeathStateChanged;
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float amount)
    {
        if (!IsServer) return; 

        // Se já estiver morto, não pode levar mais dano
        if (IsDead.Value) return;
        if (health.Value <= 0) return;

        health.Value -= amount;

        if (health.Value <= 0)
        {
            health.Value = 0;
            
            // --- MUDANÇA 2: Lógica de Morte ---
            
            // 1. Dispara o evento (para o WavController, se for um inimigo)
            OnHealthZero?.Invoke(this); 
            
            // 2. Se for um inimigo, "despawna"
            if (GetComponent<EnemyAI>() != null) 
            {
                NetworkObject.Despawn(true); 
            }
            // 3. Se for um JOGADOR
            else if (GetComponent<PlayerController>() != null)
            {
                // Define o estado como Morto
                IsDead.Value = true; 
                // (O OnValueChanged vai tratar de chamar o ClientRpc)
            }
        }
    }

    // --- MUDANÇA 3: Esta função corre em TODOS os clientes quando 'IsDead' muda ---
    private void OnDeathStateChanged(bool previousValue, bool newValue)
    {
        // Se o novo valor for 'true' (acabou de morrer)
        if (newValue == true)
        {
            // Diz ao PlayerController para ativar o modo espectador
            if (playerController != null)
            {
                playerController.EnableSpectatorMode();
            }

            // Avisa o servidor (Host) para verificar a lógica do jogo
            if (IsServer)
            {
                GameManagerHelper.CheckGameLogicAfterPlayerChange();
            }
        }
        // (Podes adicionar um 'else' aqui para lógica de "Respawn" no futuro)
    }
}