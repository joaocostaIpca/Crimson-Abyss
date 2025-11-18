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
    public NetworkWeapon networkWeapon;
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

    // --- H Action (Ability) Variables ---
    private float hActionMaxDuration = 10f;
    private Coroutine hActionCoroutine;
    private float hActionElapsed;
    private bool isHActionActive = false;
    
    // Server-side ability logic
    private Coroutine serverHoldCoroutine;
    private float serverHoldElapsed;
    private bool serverHoldActive = false;
    private Coroutine localHoldCoroutine;

    // --- Animation Sync Variables ---
    private NetworkVariable<bool> netIsMoving = new NetworkVariable<bool>(false);
    private bool lastIsMovingState = false;

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (networkWeapon == null) networkWeapon = GetComponent<NetworkWeapon>();
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

        // Sync initial animation state
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
            networkWeapon.SetInterface(ui);
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

        // Cleanup ability coroutines
        if (IsServer && serverHoldCoroutine != null)
        {
            StopCoroutine(serverHoldCoroutine);
            serverHoldCoroutine = null;
            serverHoldActive = false;
        }
        
        netIsMoving.OnValueChanged -= OnMovingStateChanged;
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (ui != null) { ui.UpdatePlayerHealth(playerSlot, (int)newValue); }
    }

    // Ensure cleanup if disabled locally
    private void OnDisable()
    {
        if (hActionCoroutine != null)
        {
            StopCoroutine(hActionCoroutine);
            hActionCoroutine = null;
            EndHAction();
        }
    }

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
        if (networkWeapon != null && Input.GetKeyDown(KeyCode.Alpha1))
        {
             // ui.SwitchToNextWeapon(); // Uncomment if your UI has this method
        }

        // --- Ability (H Key) Logic ---
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (hActionCoroutine == null)
                hActionCoroutine = StartCoroutine(HActionCoroutine());
        }
        if (Input.GetKeyUp(KeyCode.H))
        {
            if (hActionCoroutine != null)
            {
                StopCoroutine(hActionCoroutine);
                hActionCoroutine = null;
                EndHAction();
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

        // Physics Movement (Corrected for rb.velocity)
        Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * speed;
        targetVelocity.y = rb.linearVelocity.y; // Preserve gravity
        rb.linearVelocity = targetVelocity;

        if (jumpInput && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

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

    // --- ABILITY LOGIC (H Key) ---

    private IEnumerator HActionCoroutine()
    {
        isHActionActive = true;
        hActionElapsed = 0f;
        OnHActionStart();

        while (Input.GetKey(KeyCode.H) && hActionElapsed < hActionMaxDuration)
        {
            hActionElapsed += Time.deltaTime;
            // OnHActionTick(hActionElapsed, hActionMaxDuration); // Optional UI update
            yield return null;
        }
        EndHAction();
        hActionCoroutine = null;
    }

    private void OnHActionStart()
    {
        // Start ability
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (localHoldCoroutine == null)
                localHoldCoroutine = StartCoroutine(LocalHoldRoutine());
        }
        else if (IsOwner)
        {
            StartHoldPowerServerRpc();
        }
    }

    private void EndHAction()
    {
        if (!isHActionActive) return;
        isHActionActive = false;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (localHoldCoroutine != null)
            {
                StopCoroutine(localHoldCoroutine);
                localHoldCoroutine = null;
            }
        }
        else if (IsOwner)
        {
            StopHoldPowerServerRpc();
        }
    }
    
    // --- Ability RPCs ---

    [ServerRpc]
    private void StartHoldPowerServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (serverHoldCoroutine != null) return;

        serverHoldActive = true;
        serverHoldElapsed = 0f;
        serverHoldCoroutine = StartCoroutine(ServerHoldRoutine());
    }

    [ServerRpc]
    private void StopHoldPowerServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (serverHoldCoroutine != null)
        {
            StopCoroutine(serverHoldCoroutine);
            serverHoldCoroutine = null;
        }
        serverHoldActive = false;
    }

    private IEnumerator ServerHoldRoutine()
    {
        if (!IsServer) yield break;

        float tickInterval = 1f;
        while (serverHoldActive && serverHoldElapsed < hActionMaxDuration)
        {
            ApplyHoldPowerTick(); 
            serverHoldElapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
        serverHoldActive = false;
        serverHoldCoroutine = null;
    }

    private IEnumerator LocalHoldRoutine()
    {
        float elapsed = 0f;
        float tickInterval = 1f;
        while (elapsed < hActionMaxDuration)
        {
            ApplyHoldPowerTickLocal();
            elapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
        localHoldCoroutine = null;
    }

    // Ability Effect (Heal nearby players if character is Freira)
    private void ApplyHoldPowerTick()
    {
        if (CharacterIndex.Value != 0) return; // Only Freira (Index 0)

        float radius = 10f;
        float healPercentPerSecond = 0.10f; 

        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;
            var tm = col.GetComponentInParent<TargetMultiplayer>();
            if (tm == null || tm.IsDead.Value) continue;

            // Server applies heal directly to NetworkVariable
            // Assuming MaxHealth is 100
            float healAmount = 100f * healPercentPerSecond; 
            float current = tm.health.Value;
            if (current < 100f)
            {
                tm.health.Value = Mathf.Min(current + healAmount, 100f);
            }
        }
    }

    private void ApplyHoldPowerTickLocal()
    {
        if (CharacterIndex.Value != 0) return; 

        float radius = 10f;
        float healPercentPerSecond = 0.10f; 

        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;
            var tm = col.GetComponentInParent<TargetMultiplayer>();
            if (tm == null || tm.IsDead.Value) continue;

            float healAmount = 100f * healPercentPerSecond;
            tm.health.Value = Mathf.Min(tm.health.Value + healAmount, 100f);
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