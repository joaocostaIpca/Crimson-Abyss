using UnityEngine;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : NetworkBehaviour
{
    [Header("Componentes")]
    [SerializeField] private Rigidbody rb;
    public Camera playerCamera;
    public AudioListener playerAudioListener;
    public NetworkWeapon networkWeapon; 
    
    [Header("Movimento")]
    public float speed = 10f;
    public float jumpForce = 7f;
    [Header("Câmara")]
    public float mouseSensitivity = 100f;
    private float xRotation = 0f;
    [Header("Ground Check")]
    private bool isGrounded;
    private float groundCheckTimer; 
    
    private Vector2 moveInput;
    private bool jumpInput;
    
    private InterfaceController ui;
    private TargetMultiplayer target;
    private int playerSlot = -1; // Começa como -1 (inválido)

    // NetworkVariable para o nome
    public NetworkVariable<int> CharacterIndex = new NetworkVariable<int>(-1);

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (networkWeapon == null) networkWeapon = GetComponent<NetworkWeapon>();
        target = GetComponent<TargetMultiplayer>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Subscreve à mudança de nome
        CharacterIndex.OnValueChanged += OnCharacterIndexChanged;

        StartCoroutine(InitializeUI());

        if (IsOwner)
        {
            playerCamera.enabled = true;
            if (playerAudioListener != null) playerAudioListener.enabled = true;
            if (networkWeapon != null) networkWeapon.enabled = true; 
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            playerCamera.enabled = false;
            if (playerAudioListener != null) playerAudioListener.enabled = false;
            if (networkWeapon != null) networkWeapon.enabled = false;
            this.enabled = false; 
        }
    }

    private IEnumerator InitializeUI()
    {
        while (InterfaceController.Instance == null)
        {
            yield return null; // Espera 1 frame
        }
        ui = InterfaceController.Instance;
        
        // Usa o novo Gestor de Slots
        playerSlot = ui.GetOrAssignUISlot(OwnerClientId, IsOwner);

        // Se o valor do CharacterIndex já chegou, atualiza a UI
        if (CharacterIndex.Value != -1)
        {
            UpdatePlayerSlotUI();
        }
    
        // Subscreve ao evento de mudança de vida
        target.health.OnValueChanged += OnHealthChanged;

        if (IsOwner)
        {
            ui.SetLocalPlayer(this.gameObject); 
            networkWeapon.SetInterface(ui); 
        }
    }
    
    // Função chamada quando o nome muda
    private void OnCharacterIndexChanged(int previousValue, int newValue)
    {
        // Se a UI já estiver pronta, atualiza
        if (ui != null && playerSlot != -1)
        {
            UpdatePlayerSlotUI();
        }
    }
    
    // Nova função para atualizar o slot
    private void UpdatePlayerSlotUI()
    {
        if (ui == null || playerSlot == -1) return;
        
        int charIndex = CharacterIndex.Value;
        
        ui.UpdatePlayer(true, playerSlot, charIndex, (int)target.health.Value, null);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (ui != null)
        {
            // Desativa e liberta o slot
            ui.UpdatePlayer(false, playerSlot, 0, 0, null);
            ui.FreePlayerSlot(OwnerClientId);
            
            target.health.OnValueChanged -= OnHealthChanged;
            CharacterIndex.OnValueChanged -= OnCharacterIndexChanged;
        }
    }
    
    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (ui != null)
        {
            ui.UpdatePlayerHealth(playerSlot, (int)newValue);
        }
    }
    
    void Update()
    {
        // Input da Câmara
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);

        // Input de Movimento
        moveInput.x = Input.GetAxis("Horizontal");
        moveInput.y = Input.GetAxis("Vertical");
        if (Input.GetButtonDown("Jump"))
        {
            jumpInput = true;
        }
    }

    void FixedUpdate()
    {
        if (groundCheckTimer > 0)
        {
            groundCheckTimer -= Time.fixedDeltaTime;
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
        
        Vector3 move = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized;
        rb.MovePosition(rb.position + move * speed * Time.fixedDeltaTime);
        
        if (jumpInput && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            jumpInput = false; 
            groundCheckTimer = 0; 
        }
        else
        {
            jumpInput = false;
        }
    }
    
    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            groundCheckTimer = 0.1f;
        }
    }
}