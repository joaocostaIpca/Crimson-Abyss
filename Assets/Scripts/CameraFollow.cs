using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;       // O jogador
    public Vector3 thirdPersonOffset = new Vector3(0, 3, -6);
    public float sensitivity = 3f;

    private float rotationY;
    private float rotationX;
    private bool isFirstPerson = true;

    // Referência ao PlayerMovement
    private PlayerMovement playerMovement;

    // Altura dos olhos em 1ª pessoa
    public float eyeHeight = 1.7f; 

    
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (target != null)
        {
            playerMovement = target.GetComponent<PlayerMovement>();

           
        }
    }

    void Update()
    {
        if (!target) return;

        // Alternar entre 1ª e 3ª pessoa
        if (Input.GetKeyDown(KeyCode.V))
        {
            isFirstPerson = !isFirstPerson;

           
        }

        // Input do rato
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        rotationY += mouseX;
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -80f, 80f);

        // Rodar o jogador no eixo Y
        target.rotation = Quaternion.Euler(0, rotationY, 0);
    }

    void LateUpdate()
    {
        if (!target) return;

        if (isFirstPerson)
        {
            // Posição da câmara = olhos
            float height = eyeHeight;

            if (playerMovement != null && playerMovement.isCrouching)
            {
                height -= 0.8f; 
            }

            // offset ligeiro para cima e para a frente (10cm)
            Vector3 headOffset = target.forward * 0.3f + Vector3.up * 0.3f;

            transform.position = target.position + new Vector3(0, height, 0) + headOffset;
            transform.rotation = Quaternion.Euler(rotationX, rotationY, 0);
        }

        else
        {
            // 3ª pessoa
            float baseHeight = 1.5f;
            if (playerMovement != null && playerMovement.isCrouching)
            {
                baseHeight -= 1.0f;
            }

            Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
            Vector3 desiredPosition = target.position + rotation * thirdPersonOffset;

            // aplica a diferença da altura do crouch
            desiredPosition.y += (baseHeight - 1.5f);

            transform.position = desiredPosition;
            transform.LookAt(target.position + Vector3.up * baseHeight);
        }
    }
}
