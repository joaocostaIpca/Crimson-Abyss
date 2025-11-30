using TMPro;
using UnityEngine;

public class SupplyBox : MonoBehaviour
{
    [Header("Configurações")]
    public string playerTag = "Player";
    public KeyCode useKey = KeyCode.F;
    public float interactionDistance = 3f;

    [Header("UI")]
    public TextMeshProUGUI interactionText; // Arrasta o texto do Canvas aqui (ex: "Press F...")
    public string message = "Press [F] to resupply your Ammo";

    private Transform player;
    private bool isNear = false;
    private NetworkWeapon playerWeapon;

    void Start()
    {
        // Tenta encontrar o texto automaticamente se não tiver sido arrastado
        if (interactionText == null)
        {
            // Ajusta o caminho "Canvas/..." conforme a tua hierarquia real se necessário
            var textObj = GameObject.Find("Canvas/InteractionText"); 
            if (textObj != null) interactionText = textObj.GetComponent<TextMeshProUGUI>();
        }

        if (interactionText != null)
            interactionText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(player.position, transform.position);

        if (distance <= interactionDistance)
        {
            if (!isNear)
            {
                isNear = true;
                if (interactionText != null)
                {
                    interactionText.text = message;
                    interactionText.gameObject.SetActive(true);
                }
            }

            if (Input.GetKeyDown(useKey))
            {
                RearmarJogador();
            }
        }
        else if (isNear)
        {
            isNear = false;
            if (interactionText != null)
                interactionText.gameObject.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            // Verifica se é o jogador LOCAL (importante para não ativar UI pelos outros)
            var netObj = other.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                player = other.transform;
                playerWeapon = player.GetComponentInChildren<NetworkWeapon>();
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            var netObj = other.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                player = null;
                playerWeapon = null;
                isNear = false;
                if (interactionText != null)
                    interactionText.gameObject.SetActive(false);
            }
        }
    }

    void RearmarJogador()
    {
        if (playerWeapon != null)
        {
            // 1. Enche a munição (envia RPC ao servidor)
            playerWeapon.RefillAmmo(); 

            // 2. Esconde o texto da UI
            if (interactionText != null)
                interactionText.gameObject.SetActive(false);

            // 3. Desativa a caixa APENAS neste computador
            // Como não é um NetworkObject, isto não se propaga pela rede.
            gameObject.SetActive(false);
        }
    }
}