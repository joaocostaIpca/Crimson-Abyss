using UnityEngine;
using Unity.Netcode;

public class TargetMultiplayer : NetworkBehaviour
{
    // NetworkVariable sincroniza a vida
    public NetworkVariable<float> health = new NetworkVariable<float>(
        100f, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server   // Só o servidor pode mudar
    );

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float amount)
    {
        if (!IsServer) return;

        health.Value -= amount;

        if (health.Value <= 0)
        {
            Debug.Log("Morri!");
            health.Value = 100f; // Respawn simples
        }
    }

    // Podes usar isto para ligar a uma barra de vida
    public override void OnNetworkSpawn()
    {
        health.OnValueChanged += (float previousValue, float newValue) =>
        {
            Debug.Log($"Vida: {newValue}");
        };
    }
}