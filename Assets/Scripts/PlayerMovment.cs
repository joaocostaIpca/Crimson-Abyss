using UnityEngine;
using UnityEngine.UI; // necessário para usar Slider

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveForce = 30f;
    public float maxSpeed = 5f;
    public float sprintMultiplier = 1.8f; // Velocidade extra ao correr
    public float jumpForce = 7f;

    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 20f;   // gasta por segundo
    public float staminaRegenRate = 30f;   // regenera por segundo
    public float staminaCooldown = 2f;     // tempo de espera quando chega a 0
    private float currentStamina;
    private float lastExhaustedTime;
    private bool isSprinting;

    [Header("UI")]
    public Slider staminaBar; // arrastar no inspector

    private Rigidbody rb;
    private bool isGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        currentStamina = maxStamina;

        if (staminaBar != null)
        {
            staminaBar.maxValue = maxStamina;
            staminaBar.value = currentStamina;
        }
    }

    void FixedUpdate()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // Movimento relativo à câmara
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0;
        camRight.y = 0;

        Vector3 moveDir = (camForward * moveZ + camRight * moveX).normalized;

        float finalForce = moveForce;

        // Sprint se houver stamina
        if (isSprinting && currentStamina > 0)
        {
            finalForce *= sprintMultiplier;
        }

        rb.AddForce(moveDir * finalForce);

        // Limitar velocidade
        if (rb.linearVelocity.magnitude > maxSpeed * (isSprinting ? sprintMultiplier : 1f))
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed * (isSprinting ? sprintMultiplier : 1f);
        }
    }

    void Update()
    {
        // Sprint: Shift esquerdo
        if (Input.GetKey(KeyCode.LeftShift) && currentStamina > 0)
        {
            isSprinting = true;
            currentStamina -= staminaDrainRate * Time.deltaTime;

            if (currentStamina <= 0)
            {
                currentStamina = 0;
                isSprinting = false;
                lastExhaustedTime = Time.time; // começa cooldown
            }
        }
        else
        {
            isSprinting = false;

            // Só regenera stamina se passou o cooldown
            if (Time.time >= lastExhaustedTime + staminaCooldown)
            {
                currentStamina += staminaRegenRate * Time.deltaTime;
                currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
            }
        }

        // Atualizar barra
        if (staminaBar != null)
        {
            staminaBar.value = currentStamina;
        }

        // Salto
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
        }
    }
}
