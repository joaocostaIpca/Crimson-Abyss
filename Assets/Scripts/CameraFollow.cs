using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;       // O jogador
    public Vector3 thirdPersonOffset = new Vector3(0, 3, -6);
    public float sensitivity = 3f;

    private float rotationY;
    private float rotationX;
    private bool isFirstPerson = true;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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
        rotationX = Mathf.Clamp(rotationX, -60f, 60f);

        // Rodar o cubo no eixo Y (apenas horizontal)
        target.rotation = Quaternion.Euler(0, rotationY, 0);
    }

    void LateUpdate()
    {
        if (!target) return;

        if (isFirstPerson)
        {
            // 1ª pessoa: câmara na cabeça do cubo
            transform.position = target.position + new Vector3(0, 2.5f, 0);
            transform.rotation = Quaternion.Euler(rotationX, rotationY, 0);
        }
        else
        {
            // 3ª pessoa: atrás do cubo
            Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
            Vector3 desiredPosition = target.position + rotation * thirdPersonOffset;

            transform.position = desiredPosition;
            transform.LookAt(target.position + Vector3.up * 1.5f);
        }
    }
}
