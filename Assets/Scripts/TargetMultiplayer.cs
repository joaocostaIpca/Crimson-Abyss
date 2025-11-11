using UnityEngine;
using Unity.Netcode;
using System; // Precisamos disto para o 'Action'

public class TargetMultiplayer : NetworkBehaviour
{
    // Evento que o WavControllerScript vai "ouvir"
    public event Action<TargetMultiplayer> OnHealthZero;

    public NetworkVariable<float> health = new NetworkVariable<float>(
        100f, 
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float amount)
    {
        if (!IsServer) return; 

        // Só aplica dano se a vida estiver acima de 0
        if (health.Value <= 0) return;

        health.Value -= amount;

        if (health.Value <= 0)
        {
            health.Value = 0; // Garante que não fica negativo
            
            // Avisa quem estiver a "ouvir" (o spawner) que morremos
            OnHealthZero?.Invoke(this); 
            
            // Se for um inimigo (tem o script EnemyAI), "despawna"
            if (GetComponent<EnemyAI>() != null) 
            {
                NetworkObject.Despawn(true); // Despawna e destrói
            }
            else // Se for um jogador
            {
                // Por agora, só repomos a vida
                // (Mais tarde, podes adicionar uma lógica de "caído" ou "respawn")
                health.Value = 100f; 
            }
        }
    }
}