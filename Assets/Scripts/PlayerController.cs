using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq; 

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : NetworkBehaviour
{
    [Header("Componentes")]
    [SerializeField] private Rigidbody rb;
    public Camera playerCamera;
    public AudioListener playerAudioListener;
    public NetworkWeapon networkWeapon; 
    
    // --- MUDANÇA 1: Adicionar referência ao Animator ---
    private Animator animator;
    
    [Header("Modo Espectador")]
    [SerializeField] private Camera spectatorCamera;
    [SerializeField] private Renderer[] playerRenderers; 
    [SerializeField] private Collider playerCollider;
    
    [Header("Definições de Espectador")]
    [SerializeField] private float spectatorDistance = 5.0f;
    [SerializeField] private float spectatorMinPitch = -30f;
    [SerializeField] private float spectatorMaxPitch = 80f;
    private float spectatorYaw = 0.0f;  
    private float spectatorPitch = 20.0f; 

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
    private int playerSlot = -1; 

    public NetworkVariable<int> CharacterIndex = new NetworkVariable<int>(-1);

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (networkWeapon == null) networkWeapon = GetComponent<NetworkWeapon>();
        target = GetComponent<TargetMultiplayer>();
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        
        // --- MUDANÇA 2: Ligar o Animator ---
        animator = GetComponent<Animator>();

        if (playerRenderers == null || playerRenderers.Length == 0)
        {
            playerRenderers = GetComponentsInChildren<Renderer>();
        }
        
        if (spectatorCamera == null)
        {
            Transform specCamTransform = transform.Find("SpectatorCamera");
            if (specCamTransform != null)
            {
                spectatorCamera = specCamTransform.GetComponent<Camera>();
            }
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        CharacterIndex.OnValueChanged += OnCharacterIndexChanged;
        StartCoroutine(InitializeUI());

        if (IsOwner)
        {
            playerCamera.enabled = true;
            if (playerAudioListener != null) playerAudioListener.enabled = true;
            if (networkWeapon != null) networkWeapon.enabled = true; 
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            if (spectatorCamera != null)
            {
                spectatorCamera.enabled = false;
                spectatorCamera.gameObject.SetActive(false); 
            }
        }
        else
        {
            playerCamera.enabled = false;
            if (playerAudioListener != null) playerAudioListener.enabled = false;
            if (networkWeapon != null) networkWeapon.enabled = false;
            if (spectatorCamera != null)
            {
                spectatorCamera.enabled = false;
                spectatorCamera.gameObject.SetActive(false);
            }
            this.enabled = false; 
        }
    }

    private IEnumerator InitializeUI()
    {
        while (InterfaceController.Instance == null)
        {
            yield return null; 
        }
        ui = InterfaceController.Instance;
        
        playerSlot = ui.GetOrAssignUISlot(OwnerClientId, IsOwner);

        if (CharacterIndex.Value != -1)
        {
            UpdatePlayerSlotUI();
        }
    
        target.health.OnValueChanged += OnHealthChanged;

        if (IsOwner)
        {
            ui.SetLocalPlayer(this.gameObject); 
            networkWeapon.SetInterface(ui); 
        }
    }
    
    private void OnCharacterIndexChanged(int previousValue, int newValue)
    {
        if (ui != null && playerSlot != -1)
        {
            UpdatePlayerSlotUI();
        }
    }
    
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
    
    public void EnableSpectatorMode()
    {
        foreach(var renderer in playerRenderers)
        {
            if (renderer != null) renderer.enabled = false;
        }
        if (playerCollider != null) playerCollider.enabled = false;
        if (rb != null) rb.isKinematic = true; 
        
        if (IsOwner)
        {
            if (networkWeapon != null) networkWeapon.enabled = false;
            
            playerCamera.enabled = false;
            if (playerAudioListener != null) playerAudioListener.enabled = false;
            
            if (ui != null)
            {
                ui.gameObject.SetActive(false);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (spectatorCamera != null)
            {
                spectatorCamera.gameObject.SetActive(true); 
                spectatorCamera.enabled = true;             
                
                spectatorYaw = transform.eulerAngles.y;
                spectatorPitch = 20f;
                
                StartCoroutine(SpectatorUpdateLoop()); 
            }
            
            this.enabled = false; 
        }
    }
    
    private IEnumerator SpectatorUpdateLoop()
    {
        List<Transform> targets = new List<Transform>();
        int currentTargetIndex = 0;
        
        while (true)
        {
            targets = GameManagerHelper.GetLivingPlayerTransforms();
            
            if (targets.Count > 0)
            {
                if (currentTargetIndex >= targets.Count)
                {
                    currentTargetIndex = 0;
                }
                
                Transform target = targets[currentTargetIndex];
                
                spectatorYaw += Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
                spectatorPitch -= Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
                spectatorPitch = Mathf.Clamp(spectatorPitch, spectatorMinPitch, spectatorMaxPitch);

                Quaternion rotation = Quaternion.Euler(spectatorPitch, spectatorYaw, 0);
                Vector3 offset = new Vector3(0, 0, -spectatorDistance); 
                Vector3 desiredPosition = target.position + (Vector3.up * 1f) + (rotation * offset); 

                spectatorCamera.transform.position = desiredPosition;
                spectatorCamera.transform.LookAt(target.position + (Vector3.up * 1f));
                
                if (Input.GetMouseButtonDown(0))
                {
                    currentTargetIndex = (currentTargetIndex + 1) % targets.Count;
                }
            }
            
            yield return null; 
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

        // --- MUDANÇA 3: Atualizar o Animator ---
        if (animator != null)
        {
            // Verifica se o input (horizontal ou vertical) é maior que 0.1
            bool isCurrentlyMoving = moveInput.magnitude > 0.1f;
            // Define o parâmetro no "cérebro"
            animator.SetBool("isMoving", isCurrentlyMoving);
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
        
        Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * speed;
        targetVelocity.y = rb.linearVelocity.y;
        rb.linearVelocity = targetVelocity;
        
        if (jumpInput && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            
            // --- MUDANÇA 4: Disparar o Trigger de Pulo ---
            if (animator != null)
            {
                animator.SetTrigger("doJump");
            }
            
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