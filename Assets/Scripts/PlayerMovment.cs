using UnityEngine;
using UnityEngine.UI;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveForce = 30f;
    public float maxSpeed = 5f;
    public float sprintMultiplier = 1.8f; 
    public float jumpForce = 7f;

    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 20f;   
    public float staminaRegenRate = 30f;   
    public float staminaCooldown = 2f;     
    private float currentStamina;
    private float lastExhaustedTime;
    private bool isSprinting;
    public bool isCrouching;

    [Header("UI")]
    public Slider staminaBar; 

    private Rigidbody rb;
    private bool isGrounded;

    // Placeholder para animações
    private Animator anim;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        anim = GetComponent<Animator>(); 
        currentStamina = maxStamina;

        if (staminaBar != null)
        {
            staminaBar.maxValue = maxStamina;
            staminaBar.value = currentStamina;
        }
    }

    void FixedUpdate()
    {
        // Se estiver em crouch não anda
        if (isCrouching) return;

        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // Movimento relativo à câmara
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0;
        camRight.y = 0;

        Vector3 moveDir = (camForward * moveZ + camRight * moveX).normalized;

        float finalForce = moveForce;

        // Sprint
        if (isSprinting && currentStamina > 0)
        {
            finalForce *= sprintMultiplier;
        }

        rb.AddForce(moveDir * finalForce);

        // Limitar velocidade
        float maxAllowedSpeed = maxSpeed * (isSprinting ? sprintMultiplier : 1f);
        if (rb.linearVelocity.magnitude > maxAllowedSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxAllowedSpeed;
        }
    }

    void Update()
    {
        // Sprint (não pode correr se crouchado)
        if (Input.GetKey(KeyCode.LeftShift) && currentStamina > 0 && !isCrouching)
        {
            isSprinting = true;
            currentStamina -= staminaDrainRate * Time.deltaTime;

            if (currentStamina <= 0)
            {
                currentStamina = 0;
                isSprinting = false;
                lastExhaustedTime = Time.time;
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

        // Crouch como toggle (CTRL ativa/desativa)
        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            isCrouching = !isCrouching; // troca estado

            if (anim != null)
            {
                anim.SetBool("Crouch", isCrouching);
            }
        }

        // Atualizar barra stamina
        if (staminaBar != null)
        {
            staminaBar.value = currentStamina;
        }

        // Salto (não salta se estiver crouch)
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded && !isCrouching)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }

        // Placeholder para animações de movimento
        if (anim != null)
        {
            float speedPercent = rb.linearVelocity.magnitude / maxSpeed;
            anim.SetFloat("Speed", speedPercent); 
            anim.SetBool("Sprint", isSprinting);
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
