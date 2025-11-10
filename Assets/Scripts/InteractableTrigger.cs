using UnityEngine;
using Unity.Netcode;

// Este script vai no objeto "Trigger" (o filho)
public class InteractableTrigger : MonoBehaviour
{
    private Interactable parentInteractable;

    private void Awake()
    {
        // Encontra o "cérebro" no objeto Pai
        parentInteractable = GetComponentInParent<Interactable>();
        if (parentInteractable == null)
        {
            Debug.LogError("Este trigger não conseguiu encontrar um script 'Interactable' no seu pai!");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (parentInteractable == null) return;

        // Se o que entrou for o JOGADOR LOCAL (IsOwner)
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>().IsOwner)
        {
            // Avisa o "cérebro" (Interactable) que o jogador local entrou
            parentInteractable.OnPlayerEntered(other.gameObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (parentInteractable == null) return;
        
        // Se o que saiu for o JOGADOR LOCAL (IsOwner)
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>().IsOwner)
        {
            // Avisa o "cérebro" (Interactable) que o jogador local saiu
            parentInteractable.OnPlayerExited();
        }
    }
}