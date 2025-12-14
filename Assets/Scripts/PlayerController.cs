using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class PlayerController : NetworkBehaviour
{
    [Header("Componentes")]
    [SerializeField] private Rigidbody rb;
    public Camera playerCamera;
    public AudioListener playerAudioListener;
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
    
    // Variáveis de Buff (para o Comandante alterar)
    [HideInInspector] public float currentSpeedMultiplier = 1f;
    [HideInInspector] public float currentJumpMultiplier = 1f;
    private Coroutine buffCoroutine;

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

    // --- Sincronização Manual de Animação ---
    private NetworkVariable<bool> netIsMoving = new NetworkVariable<bool>(false);
    private bool lastIsMovingState = false;

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        target = GetComponent<TargetMultiplayer>();
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
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
            if (spectatorCamera != null)
            {
                spectatorCamera.enabled = false;
                spectatorCamera.gameObject.SetActive(false);
            }
            this.enabled = false;
        }

        // Sincronizar estado inicial da animação
        netIsMoving.OnValueChanged += OnMovingStateChanged;
        if(animator != null) animator.SetBool("isMoving", netIsMoving.Value);
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
        }
    }

    private void OnCharacterIndexChanged(int previousValue, int newValue)
    {
        if (ui != null && playerSlot != -1) { UpdatePlayerSlotUI(); }
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
        
        netIsMoving.OnValueChanged -= OnMovingStateChanged;
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (ui != null) { ui.UpdatePlayerHealth(playerSlot, (int)newValue); }
    }

    // --- MODO ESPECTADOR ---
    public void EnableSpectatorMode()
    {
        foreach (var renderer in playerRenderers)
        {
            if (renderer != null) renderer.enabled = false;
        }
        if (playerCollider != null) playerCollider.enabled = false;
        if (rb != null) rb.isKinematic = true;

        if (IsOwner)
        {
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

    // --- BUFF DO COMANDANTE (Chamado pelo AbilitySystem) ---
    [ClientRpc]
    public void ApplyComandanteBuffClientRpc(float speedMult, float jumpMult, float duration, ClientRpcParams clientRpcParams = default)
    {
        // Só aplica se formos o alvo deste RPC (o OwnerClientId é verificado no envio)
        if (buffCoroutine != null) StopCoroutine(buffCoroutine);
        
        currentSpeedMultiplier = speedMult;
        currentJumpMultiplier = jumpMult;
        // Debug.Log("Buff do Comandante Recebido!");

        buffCoroutine = StartCoroutine(RemoveBuffRoutine(duration));
    }

    private IEnumerator RemoveBuffRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        currentSpeedMultiplier = 1f;
        currentJumpMultiplier = 1f;
        // Debug.Log("Buff do Comandante Acabou.");
    }

    void Update()
    {
        // Camera Input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 80f);
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);

        // Movement Input
        moveInput.x = Input.GetAxis("Horizontal");
        moveInput.y = Input.GetAxis("Vertical");
        if (Input.GetButtonDown("Jump"))
        {
            jumpInput = true;
        }
        
        // Weapon Switching (Example: Key 1)
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
             ui.SwitchToNextWeapon(); // Uncomment if your UI has this method
        }

        // --- Animation Sync ---
        if (animator != null)
        {
            bool isCurrentlyMoving = moveInput.magnitude > 0.1f;
            animator.SetBool("isMoving", isCurrentlyMoving);

            if (isCurrentlyMoving != lastIsMovingState)
            {
                UpdateMovingStateServerRpc(isCurrentlyMoving);
                lastIsMovingState = isCurrentlyMoving;
            }
        }

        // --- Animation Sync ---
        if (animator != null)
        {
            bool isCurrentlyMoving = moveInput.magnitude > 0.1f;
            animator.SetBool("isMoving", isCurrentlyMoving);

            if (isCurrentlyMoving != lastIsMovingState)
            {
                UpdateMovingStateServerRpc(isCurrentlyMoving);
                lastIsMovingState = isCurrentlyMoving;
            }
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

        // Physics Movement (com Multiplicador de Buff)
        float finalSpeed = speed * currentSpeedMultiplier;
        Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * finalSpeed;
        
        targetVelocity.y = rb.linearVelocity.y; // Preserve gravity
        rb.linearVelocity = targetVelocity;

        if (jumpInput && isGrounded)
        {
            // Aplica Multiplicador de Pulo
            float finalJump = jumpForce * currentJumpMultiplier;
            rb.AddForce(Vector3.up * finalJump, ForceMode.Impulse);

            // Animation Trigger Sync
            if (animator != null)
            {
                animator.SetTrigger("doJump");
                UpdateJumpStateServerRpc();
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

    // --- Animation RPCs (Manual Sync) ---

    private void OnMovingStateChanged(bool previousValue, bool newValue)
    {
        if (!IsOwner && animator != null)
        {
            animator.SetBool("isMoving", newValue);
        }
    }

    [ServerRpc]
    private void UpdateMovingStateServerRpc(bool newState)
    {
        netIsMoving.Value = newState;
    }

    [ServerRpc]
    private void UpdateJumpStateServerRpc()
    {
        DoJumpClientRpc();
    }

    [ClientRpc]
    private void DoJumpClientRpc()
    {
        if (IsOwner) return;
        if (animator != null) animator.SetTrigger("doJump");
    }
}