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

    [Header("Modo Espectador")]
    [SerializeField] private Camera spectatorCamera;
    [SerializeField] private MeshRenderer[] playerMeshes;
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

    // --- H key action fields ---
    private float hActionMaxDuration = 10f; // seconds
    private Coroutine hActionCoroutine;
    private float hActionElapsed;
    private bool isHActionActive = false;

    // Server-side coroutine (runs on server instance when owner requests power)
    private Coroutine serverHoldCoroutine;
    private float serverHoldElapsed;
    private bool serverHoldActive = false;

    // Local fallback (single-player/test mode)
    private Coroutine localHoldCoroutine;

    // --- MUDANÇA 1: Sincronização Manual de Animação ---
    private NetworkVariable<bool> netIsMoving = new NetworkVariable<bool>(false);
    private bool lastIsMovingState = false; // Para evitar spam de RPC
    
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
            if (spectatorCamera == null)
            {
                Debug.LogError("[PlayerController] FALHA AO ENCONTRAR 'SpectatorCamera'!");
            }
            else if (spectatorCamera == playerCamera)
            {
                Debug.LogError("[PlayerController] ERRO! A 'spectatorCamera' é a mesma que a 'playerCamera'!");
                spectatorCamera = null;
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

        // --- MUDANÇA 2: Subscrever à NetworkVariable ---
        netIsMoving.OnValueChanged += OnMovingStateChanged;
        // Sincroniza o estado inicial
        animator.SetBool("isMoving", netIsMoving.Value);
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
            Debug.Log("[PlayerController] Definindo jogador local na UI.");
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

        // Clean up server coroutine if this object is being despawned on server
        if (IsServer && serverHoldCoroutine != null)
        {
            StopCoroutine(serverHoldCoroutine);
            serverHoldCoroutine = null;
            serverHoldActive = false;
        }
    }

        // --- MUDANÇA 3: Limpar a subscrição ---
        netIsMoving.OnValueChanged -= OnMovingStateChanged;
    }
    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (ui != null) { ui.UpdatePlayerHealth(playerSlot, (int)newValue); }
    }

    public void EnableSpectatorMode()
    {
        foreach (var mesh in playerMeshes)
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

                Transform target = targets[currentTargetIndex];
                spectatorYaw += Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
                spectatorPitch -= Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
                spectatorPitch = Mathf.Clamp(spectatorPitch, spectatorMinPitch, spectatorMaxPitch);
                Quaternion rotation = Quaternion.Euler(spectatorPitch, spectatorYaw, 0);
                Vector3 offset = new Vector3(0, 0, -spectatorDistance);
                Vector3 desiredPosition = target.position + (Vector3.up * 1f) + (rotation * offset);

                spectatorCamera.transform.position = desiredPosition;
                spectatorCamera.transform.LookAt(target.position + (Vector3.up * 1f));

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

    // --- LÓGICA DE PAUSA REMOVIDA ---
            yield return null; 
        }
    }
    
    
    void Update()
    {
        // O OnNetworkSpawn() desliga o Update() se não formos o dono
        
        // Input da Câmara
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 80f); 
        playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);

        // Input de Movimento
        moveInput.x = Input.GetAxis("Horizontal");
        moveInput.y = Input.GetAxis("Vertical");
        if (Input.GetButtonDown("Jump"))
        {
            jumpInput = true;
        }
        // troca de arma
        if (networkWeapon != null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ui.SwitchToNextWeapon();
            }
        }

        // --- Hold H action: start on KeyDown, stops on KeyUp or after max duration ---
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
    }

    // Coroutine handling the hold action
    private IEnumerator HActionCoroutine()
    {
        isHActionActive = true;
        hActionElapsed = 0f;
        OnHActionStart();

        while (Input.GetKey(KeyCode.H) && hActionElapsed < hActionMaxDuration)
        {
            hActionElapsed += Time.deltaTime;
            OnHActionTick(hActionElapsed, hActionMaxDuration);
            yield return null;
        }

        // finished either by release or timeout
        EndHAction();
        hActionCoroutine = null;
    }

    // Hook: called once when action starts
    private void OnHActionStart()
    {
        Debug.Log($"{name}: H action started.");
        // Start server-authoritative effect (owner requests)
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            // local fallback (single-player)
            if (localHoldCoroutine == null)
                localHoldCoroutine = StartCoroutine(LocalHoldRoutine());
        }
        else if (IsOwner)
        {
            StartHoldPowerServerRpc();
        }
    }

    // Hook: called each frame while action is active
    private void OnHActionTick(float elapsed, float maxDuration)
    {
        // Example: update UI progress, etc.
        Debug.Log($"H action progress: {elapsed:F2}/{maxDuration:F2}");
        // Use this to update UI (progress bar) or apply continuous effects.
    }

    // Hook: called when the action ends (released or timed out)
    private void EndHAction()
    {
        if (!isHActionActive) return;
        isHActionActive = false;
        Debug.Log($"{name}: H action ended after {hActionElapsed:F2}s.");

        // Stop server/local effect
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

    // ensure cleanup if object disabled/destroyed while action running
    private void OnDisable()
    {
        if (hActionCoroutine != null)
        {
            StopCoroutine(hActionCoroutine);
            hActionCoroutine = null;
            EndHAction();
        }

        if (IsServer && serverHoldCoroutine != null)
        {
            StopCoroutine(serverHoldCoroutine);
            serverHoldCoroutine = null;
            serverHoldActive = false;
        }
    }

    // --- Server RPCs to control the hold action (owner calls these) ---
    [ServerRpc(RequireOwnership = true)]
    private void StartHoldPowerServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (serverHoldCoroutine != null) return; // already running

        serverHoldActive = true;
        serverHoldElapsed = 0f;
        serverHoldCoroutine = StartCoroutine(ServerHoldRoutine());
    }

    [ServerRpc(RequireOwnership = true)]
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

    // Server-side coroutine: applies effect once per second while active (up to max duration)
    private IEnumerator ServerHoldRoutine()
    {
        if (!IsServer) yield break;

        float tickInterval = 1f;
        while (serverHoldActive && serverHoldElapsed < hActionMaxDuration)
        {
            // Apply effect according to player class/name
            ApplyHoldPowerTick(); // server-side application
            serverHoldElapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }

        serverHoldActive = false;
        serverHoldCoroutine = null;
    }

    // Local fallback for single-player (applies same effect locally)
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

    // Server-side logic: determine which power based on character and apply
    private void ApplyHoldPowerTick()
    {
        if (CharacterIndex.Value != 0) return; // Freira only

        float radius = 10f;
        float healPercentPerSecond = 0.10f; // 10% per second

        // Use Physics.OverlapSphere to find nearby colliders and filter to TargetMultiplayer.
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;

            // Use GetComponentInParent to be resilient to collider on children
            var tm = col.GetComponentInParent<TargetMultiplayer>();
            if (tm == null) continue;
            if (tm.IsDead.Value) continue;

            // Server-authoritative heal
            float healAmount = tm.MaxHealth * healPercentPerSecond;
            tm.Heal(healAmount);
        }
    }

    // Local (single-player) version - operates on local objects
    private void ApplyHoldPowerTickLocal()
    {
        if (CharacterIndex.Value != 0) return; // Freira local fallback

        float radius = 10f;
        float healPercentPerSecond = 0.10f; // 10% per second

        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;

            var tm = col.GetComponentInParent<TargetMultiplayer>();
            if (tm == null) continue;
            if (tm.IsDead.Value) continue;

            float healAmount = tm.MaxHealth * healPercentPerSecond;
            tm.health.Value = Mathf.Min(tm.health.Value + healAmount, tm.MaxHealth);
        }
    }

    // --- LÓGICA DE PAUSA REMOVIDA ---

        // --- MUDANÇA 4: Ligar ao Animator e ao Servidor ---
        if (animator != null)
        {
            bool isCurrentlyMoving = moveInput.magnitude > 0.1f;
            
            // 1. Define o animator local
            animator.SetBool("isMoving", isCurrentlyMoving);
            
            // 2. Se o estado mudou, avisa o servidor
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

        // Movimento com 'rb.velocity' (corrige o "deslize")
        Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * speed;
        targetVelocity.y = rb.linearVelocity.y;
        rb.linearVelocity = targetVelocity;

        // Pulo
        if (jumpInput && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            jumpInput = false;
            groundCheckTimer = 0;
        
        // Versão de Física (rb.velocity)
        Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * speed;
        targetVelocity.y = rb.linearVelocity.y;
        rb.linearVelocity = targetVelocity;
        
        if (jumpInput && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            
            // --- MUDANÇA 5: Avisar o Servidor do Pulo ---
            if (animator != null)
            {
                animator.SetTrigger("doJump"); // Toca o pulo local
                UpdateJumpStateServerRpc();    // Avisa o servidor
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

    // --- MUDANÇA 6: Novas Funções de Sincronização ---

    // Esta função corre em TODOS os clientes quando 'netIsMoving' muda
    private void OnMovingStateChanged(bool previousValue, bool newValue)
    {
        // Se não formos o dono, atualiza o nosso animator
        if (!IsOwner)
        {
            animator.SetBool("isMoving", newValue);
        }
    }

    // O Dono (Cliente) chama isto, e corre no Servidor (Host)
    [ServerRpc]
    private void UpdateMovingStateServerRpc(bool newState)
    {
        netIsMoving.Value = newState;
    }
    
    // O Dono (Cliente) chama isto, e corre no Servidor (Host)
    [ServerRpc]
    private void UpdateJumpStateServerRpc()
    {
        // O Servidor diz a TODOS os clientes para tocarem o pulo
        DoJumpClientRpc();
    }

    // O Servidor (Host) chama isto, e corre em TODOS os clientes
    [ClientRpc]
    private void DoJumpClientRpc()
    {
        // Se formos o dono, já tocámos o pulo. Ignora.
        if (IsOwner) return;
        
        // Se formos um "clone", toca o pulo
        animator.SetTrigger("doJump");
    }
}