using UnityEngine;

public class Interactable : MonoBehaviour
{
    [Header("Configuração")]
    public KeyCode interactKey = KeyCode.E; // tecla para interagir
    public bool destroyParent = true;        // se deve apagar o pai

    private bool playerInRange = false;      // se o jogador está dentro da área
    private Transform player;

    void Start()
    {
        // procura automaticamente o jogador (tem de ter tag "Player")
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(interactKey))
        {
            Interact();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            Debug.Log("Jogador entrou na área de interação.");
            // aqui podes chamar um UI de "Pressiona E"
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            Debug.Log("Jogador saiu da área de interação.");
            // aqui podes esconder o UI de "Pressiona E"
        }
    }

    void Interact()
    {
        Debug.Log("Interagiu com " + gameObject.name);

        if (destroyParent && transform.parent != null)
        {
            Destroy(transform.parent.gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
