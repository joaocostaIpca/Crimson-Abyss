using TMPro;
using UnityEngine;

public class SupplyBox : MonoBehaviour
{
    [Header("Configura��es")]
    public string playerTag = "Player";
    public KeyCode useKey = KeyCode.F;
    public float interactionDistance = 3f;

    [Header("UI")]
    public TextMeshProUGUI interactionText; // arrasta o texto do Canvas aqui
    public string message = "Press [F] to ressuply your Ammo";

    private Transform player;
    private bool isNear = false;
    private NetworkWeapon playerWeapon;

    void Start()
    {
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
                interactionText.text = message;
                interactionText.gameObject.SetActive(true);
            }

            if (Input.GetKeyDown(useKey))
            {
                RearmarJogador();
            }
        }
        else if (isNear)
        {
            isNear = false;
            interactionText.gameObject.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            player = other.transform;
            playerWeapon = player.GetComponentInChildren<NetworkWeapon>(); // pega a arma do jogador
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            player = null;
            playerWeapon = null;
            isNear = false;
            if (interactionText != null)
                interactionText.gameObject.SetActive(false);
        }
    }

    void RearmarJogador()
    {
        if (playerWeapon != null)
        {
            // Recarrega a muni��o m�xima da arma
          //  playerWeapon.maxAmmo = playerWeapon.maxMagAmmo * 4; // exemplo: 4 carregadores extras
           // playerWeapon.UpdateAmmoUI();

           // Debug.Log("Muni��o total restaurada!");
        }
    }
}
