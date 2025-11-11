using UnityEngine;
using Unity.Netcode;

// Este script vai nos 4 PREFABS de Jogador
public class PlayerTriggerCheck : NetworkBehaviour
{
    // Só o dono do jogador (o jogador local) é que corre o OnTriggerExit
    private void OnTriggerExit(Collider other)
    {
        // Se não formos nós, não faz nada
        if (!IsOwner) return;

        // Verifica se saímos de um "WaveController"
        WavControllerScript waveController = other.GetComponent<WavControllerScript>();
        if (waveController != null)
        {
            // Encontrámos! Diz ao servidor que saímos.
            Debug.Log("Saí de um trigger de wave. A avisar o servidor...");
            waveController.PlayerExitedTriggerServerRpc(OwnerClientId);
        }
    }
}