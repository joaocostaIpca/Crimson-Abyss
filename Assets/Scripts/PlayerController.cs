using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class PlayerController : NetworkBehaviour
{
    public enum PlayerType { Freira, Comandante, Templario, Fuzileiro }

    public NetworkVariable<int> CharacterIndex = new NetworkVariable<int>(-1);

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

    [Header("Personagem")]
    public PlayerType characterType = PlayerType.Freira;
    public bool hasMedikit = false;

    [Header("Audio")]
    [SerializeField] private string letsGoResourcePath = "Sounds/LetsGo";
    [SerializeField] private string earthquakeResourcePath = "Sounds/Earthquake";
    private AudioClip letsGoClip;
    private AudioClip earthquakeClip;
    private AudioSource earthquakeAudioSource;

    private Vector2 moveInput;
    private bool jumpInput;

    private InterfaceController ui;
    private TargetMultiplayer target;
    private int playerSlot = -1;
      

    // --- H Action (Ability) Variables ---
    private float hActionMaxDuration = 10f;
    private Coroutine hActionCoroutine;
    private float hActionElapsed;
    private bool isHActionActive = false;
    private GameObject healingAuraVfxPrefab;
    private Coroutine medikitReenableCoroutine;

    // Server-side ability logic
    private Coroutine serverHoldCoroutine;
    private float serverHoldElapsed;
    private bool serverHoldActive = false;
    private Coroutine localHoldCoroutine;

    // Cooldown (3 minutes) and server-authoritative next-available timestamp
    private float hCooldown = 180f; // seconds (3 minutes)
    private NetworkVariable<double> nextHAvailable = new NetworkVariable<double>(
        0.0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // --- Animation Sync Variables ---
    private NetworkVariable<bool> netIsMoving = new NetworkVariable<bool>(false);
    private bool lastIsMovingState = false;
       
    // --- Movement backup for temporary buffs (Comandante) ---
    private float baseSpeed;
    private float baseJumpForce;
    private Coroutine comandanteBuffCoroutine;


    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (networkWeapon == null) networkWeapon = GetComponent<NetworkWeapon>();
        target = GetComponent<TargetMultiplayer>();
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        animator = GetComponent<Animator>();
        // load HealingAura from Resources/VFX
        healingAuraVfxPrefab = Resources.Load<GameObject>("VFX/HealingAura");
        
         

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

        // store base movement values so temporary buffs can be applied/reverted
        baseSpeed = speed;
        baseJumpForce = jumpForce;

        // Load the audio clips from Resources
        letsGoClip = Resources.Load<AudioClip>(letsGoResourcePath);
        earthquakeClip = Resources.Load<AudioClip>(earthquakeResourcePath);
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
        ui.SetMedikitEnabled(true);
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

        // cleanup temporary buffs
        if (comandanteBuffCoroutine != null)
        {
            StopCoroutine(comandanteBuffCoroutine);
            comandanteBuffCoroutine = null;
            speed = baseSpeed;
            jumpForce = baseJumpForce;
        }
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
        if (medikitReenableCoroutine != null) { StopCoroutine(medikitReenableCoroutine); medikitReenableCoroutine = null; }

        if (comandanteBuffCoroutine != null)
        {
            StopCoroutine(comandanteBuffCoroutine);
            comandanteBuffCoroutine = null;
            speed = baseSpeed;
            jumpForce = baseJumpForce;
        }

        if (earthquakeAudioSource != null) { StopLocalEarthquake(); }
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
            // Prevent starting locally if still on cooldown (uses networked timestamp)
            if (hActionCoroutine == null && HCooldownRemaining() <= 0f)
            {
                hActionCoroutine = StartCoroutine(HActionCoroutine());
            }
            else
            {
                // Optional feedback: still on cooldown
                Debug.Log($"Ability on cooldown: {HCooldownRemaining():F1}s");
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

    #region  --- Habilidades especiais (Tecla H) ---

    private IEnumerator HActionCoroutine()
    {
        isHActionActive = true;
        hActionElapsed = 0f;
        OnHActionStart();

        // Run for the configured duration regardless of key release
        while (hActionElapsed < hActionMaxDuration)
        {
            hActionElapsed += Time.deltaTime;
            // optional per-frame UI update: OnHActionTick(hActionElapsed, hActionMaxDuration);
            yield return null;
        }

        EndHAction();
        hActionCoroutine = null;
    }

    private void OnHActionStart()
    {
        // Single-player/local fallback: immediate UI + local routine
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            ui?.SetMedikitEnabled(false);
            if (localHoldCoroutine == null)
                localHoldCoroutine = StartCoroutine(LocalHoldRoutine());

            // schedule local re-enable after cooldown
            if (medikitReenableCoroutine != null) { StopCoroutine(medikitReenableCoroutine); medikitReenableCoroutine = null; }
            float cooldown = GetAbilityCooldown(CharacterIndex.Value);
            medikitReenableCoroutine = StartCoroutine(EnableMedikitAfterDelay(cooldown));

            float duration = GetAbilityDuration(CharacterIndex.Value);
            if (healingAuraVfxPrefab != null)
            {
                GameObject vfx = Instantiate(healingAuraVfxPrefab, transform);
                vfx.transform.localPosition = Vector3.zero; // adjust Y offset if needed (e.g. new Vector3(0,1.5f,0))
                Destroy(vfx, duration);
            }

            // Play per-character local SFX in single-player fallback (only once) — use start/stop so it ends with the effect
            if (CharacterIndex.Value == 1) // Comandante
            {
                if (letsGoClip == null) letsGoClip = Resources.Load<AudioClip>(letsGoResourcePath);
                if (letsGoClip != null) AudioSource.PlayClipAtPoint(letsGoClip, transform.position, 1f);
            }
            else if (CharacterIndex.Value == 2) // Templario
            {
                StartLocalEarthquake();
            }

            return;
        }
        // Networked: ask server to start; server will call NotifyHoldStartedClientRpc (owner) to disable UI and schedule re-enable
        else if (IsOwner)
        {
            StartHoldPowerServerRpc();
        }
    }

    // Start/stop local looping earthquake for single‑player or host-local case
    private void StartLocalEarthquake()
    {
        if (earthquakeClip == null) earthquakeClip = Resources.Load<AudioClip>(earthquakeResourcePath);
        if (earthquakeClip == null) return;

        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.Stop();
            if (earthquakeAudioSource.gameObject != null) Destroy(earthquakeAudioSource.gameObject);
            earthquakeAudioSource = null;
        }

        GameObject go = new GameObject("EarthquakeSfx_Local");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        var src = go.AddComponent<AudioSource>();
        src.clip = earthquakeClip;
        src.loop = true;
        src.spatialBlend = 1f;
        src.minDistance = 1f;
        src.maxDistance = 60f;
        src.playOnAwake = false;
        src.Play();
        earthquakeAudioSource = src;
    }

    private void StopLocalEarthquake()
    {
        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.Stop();
            if (earthquakeAudioSource.gameObject != null) Destroy(earthquakeAudioSource.gameObject);
            earthquakeAudioSource = null;
        }
    }

    private void EndHAction()
    {
        if (!isHActionActive) return;
        isHActionActive = false;

        // Stop local fallback coroutine if any
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (localHoldCoroutine != null)
            {
                StopCoroutine(localHoldCoroutine);
                localHoldCoroutine = null;
            }
        }
    }

    #endregion

    #region --- Ability RPCs ---

    private float GetAbilityDuration(int charIndex)
    {
        // per-character ability active time (seconds)
        switch (charIndex)
        {
            case 0: // Freira
                return 10f;
            case 1: // Comandante
                return 25f;
            case 2: // Templario
                return 5f;
            default:
                return 1f;
        }
    }

    private float GetAbilityCooldown(int charIndex)
    {
        // per-character cooldown (seconds)
        switch (charIndex)
        {
            case 0: return 180f; // Freira
            case 1: return 120f; // Comandante
            case 2: return 200f; // Templario
            case 3: return 90f;  // Fuzileiro
            default: return hCooldown;
        }
    }

    [ServerRpc]
    private void StartHoldPowerServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        int idx = CharacterIndex.Value;

        double now = Time.time;
        // server-stored nextHAvailable prevents reactivation during cooldown
        if (now < nextHAvailable.Value) return;

        if (serverHoldCoroutine != null) return; // already running on this player

        float duration = GetAbilityDuration(idx);
        float cooldown = GetAbilityCooldown(idx);

        // set server authoritative next available time
        nextHAvailable.Value = now + cooldown;

        serverHoldActive = true;
        serverHoldElapsed = 0f;
        serverHoldCoroutine = StartCoroutine(ServerHoldRoutine(duration, idx));

        // If Freira, ask clients to spawn the local VFX attached to this player's transform
        if (idx == 0)
        {
            SpawnHealingAuraClientRpc(duration);
        }
        // play LetsGo globally for Comandante
        if (idx == 1)
        {
            PlayLetsGoClientRpc();
        }
        // play Earthquake globally for Templario (starts looping SFX on clients)
        if (idx == 2)
        {
            PlayEarthquakeClientRpc();
        }

        // Notify owner immediately (so owner UI can schedule medikit re-enable)
        ulong ownerClient = rpcParams.Receive.SenderClientId;
        var clientParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { ownerClient } }
        };
        NotifyHoldStartedClientRpc(nextHAvailable.Value, clientParams);
    }

    private IEnumerator ServerHoldRoutine(float duration, int idx)
    {
        if (!IsServer) yield break;

        float tickInterval = 1f;
        while (serverHoldActive && serverHoldElapsed < duration)
        {
            // call the per-character tick handler (server-side authoritative)
            ApplyHoldPowerTick();
            serverHoldElapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }

        // Stop the earthquake on clients when the effect duration ends
        if (idx == 2)
        {
            StopEarthquakeClientRpc();
        }

        serverHoldActive = false;
        serverHoldCoroutine = null;
    }

    private IEnumerator LocalHoldRoutine()
    {
        // use per-character duration so local behaviour matches server
        float duration = GetAbilityDuration(CharacterIndex.Value);
        float elapsed = 0f;
        float tickInterval = 1f;

        // If local host start earthquake here (already started in OnHActionStart for single-player fallback),
        // but ensure it's started if LocalHoldRoutine is entered from somewhere else.
        if (CharacterIndex.Value == 2 && earthquakeAudioSource == null)
        {
            StartLocalEarthquake();
        }

        while (elapsed < duration)
        {
            ApplyHoldPowerTick();
            elapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }

        // stop local earthquake when effect ends
        if (CharacterIndex.Value == 2)
        {
            StopLocalEarthquake();
        }

        localHoldCoroutine = null;
    }

    #region special ability implementations

    private void ApplyHoldPowerTick()
    {
        // Dispatch to the character-specific handler
        switch (CharacterIndex.Value)
        {
            case 0: // Freira
                ApplyFreiraTick();
                break;
            case 1: // Comandante
                ApplyComandanteTick();
                break;
            case 2: // Templario
                ApplyTemplarioTick();
                break;
            case 3: // Fuzileiro
                // no special tick effect, only a weapon no one else has
                break;
            default:
                break;
        }
    }

    // --- Freira: heal nearby players in 10m range ---
    private void ApplyFreiraTick()
    {
        float radius = 10f;
        float healPercentPerSecond = 0.10f; 

        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;
            TargetMultiplayer tm = col.GetComponentInParent<TargetMultiplayer>();
            if (tm == null || tm.IsDead.Value) continue;

            float maxH = 100f;
            float healAmount = maxH * healPercentPerSecond;
            tm.health.Value = Mathf.Min(tm.health.Value + healAmount, maxH);
        }
    }

    // --- Comandante: says words of courage and everyone gets more speed walking, jumping and more fire speed ---
    private void ApplyComandanteTick()
    {
        // Increase walking speed by 50% and jump by 30% for the ability duration,
        // applied to all players in the game (regardless of distance).
        float speedMultiplier = 2f;
        float jumpMultiplier = 1.5f;
        float duration = GetAbilityDuration(CharacterIndex.Value);

        // Single-player / local fallback: apply buff locally
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (comandanteBuffCoroutine != null)
            {
                StopCoroutine(comandanteBuffCoroutine);
                comandanteBuffCoroutine = null;
            }

            baseSpeed = baseSpeed == 0f ? speed : baseSpeed;
            baseJumpForce = baseJumpForce == 0f ? jumpForce : baseJumpForce;

            speed = baseSpeed * speedMultiplier;
            jumpForce = baseJumpForce * jumpMultiplier;

            comandanteBuffCoroutine = StartCoroutine(ComandanteBuffRoutine(duration));
            return;
        }

        // Server: send a targeted ClientRpc to every player's owner so they apply the buff locally.
        if (!IsServer) return;

        var allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var pc in allPlayers)
        {
            if (pc == null) continue;

            ulong targetClient = pc.OwnerClientId;
            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { targetClient } }
            };

            pc.ApplyComandanteBuffClientRpc(speedMultiplier, jumpMultiplier, duration, clientParams);
        }
    }

    // --- Templario: kills enemies in 30m range ---
    private void ApplyTemplarioTick()
    {
        // This must run server-side so the kills/despawns are authoritative.
        if (!IsServer) return;

        float radius = 30f;
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);

        // Avoid double-processing the same TargetMultiplayer (multiple colliders)
        HashSet<TargetMultiplayer> processed = new HashSet<TargetMultiplayer>();
        foreach (var col in hits)
        {
            if (col == null) continue;

            var enemyAI = col.GetComponentInParent<EnemyAI>();
            if (enemyAI == null) continue;

            var tm = enemyAI.GetComponent<TargetMultiplayer>();
            if (tm == null) continue;

            if (processed.Contains(tm)) continue;
            processed.Add(tm);

            // Skip already-dead enemies
            if (tm.IsDead.Value) continue;

            // Kill the enemy via the server-side TakeDamageServerRpc (ensures OnHealthZero + despawn run consistently)
            // Use MaxHealth to ensure death; TakeDamageServerRpc is server-side safe to call from server.
            tm.TakeDamageServerRpc(tm.MaxHealth);
        }
    }

    #endregion

    // Returns cooldown remaining in seconds (clients can call this to update UI)
    public float HCooldownRemaining()
    {
        // nextHAvailable is server-written; subtract local Time.time (may be slightly off).
        return Mathf.Max(0f, (float)(nextHAvailable.Value - Time.time));
    }

    [ClientRpc]
    private void NotifyHoldStartedClientRpc(double nextAvailableTime, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        // immediate UI feedback: disable medikit
        ui?.SetMedikitEnabled(false);

        // compute remaining seconds and schedule re-enable
        float remaining = Mathf.Max(0f, (float)(nextAvailableTime - Time.time));
        if (medikitReenableCoroutine != null)
        {
            StopCoroutine(medikitReenableCoroutine);
            medikitReenableCoroutine = null;
        }
        medikitReenableCoroutine = StartCoroutine(EnableMedikitAfterDelay(remaining));
    }

    [ClientRpc]
    private void SpawnHealingAuraClientRpc(float duration, ClientRpcParams clientRpcParams = default)
    {
        if (healingAuraVfxPrefab == null) return;

        // Instantiate as child so it follows the player automatically
        GameObject vfx = Instantiate(healingAuraVfxPrefab, transform);
        vfx.transform.localPosition = Vector3.zero; // change to new Vector3(0,1.5f,0) if prefab pivot is centered
        Destroy(vfx, duration);
    }

    // add this ClientRpc (can be placed after SpawnHealingAuraClientRpc)
    [ClientRpc]
    private void PlayLetsGoClientRpc(ClientRpcParams clientRpcParams = default)
    {
        // ensure clip is loaded on the client
        if (letsGoClip == null) letsGoClip = Resources.Load<AudioClip>(letsGoResourcePath);
        if (letsGoClip == null) return;

        // play a 3D sound at this player's position (everyone hears it coming from the Comandante)
        AudioSource.PlayClipAtPoint(letsGoClip, transform.position, 1f);
    }

    [ClientRpc]
    private void PlayEarthquakeClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (earthquakeClip == null) earthquakeClip = Resources.Load<AudioClip>(earthquakeResourcePath);
        if (earthquakeClip == null) return;

        // avoid multiple instances
        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.Stop();
            if (earthquakeAudioSource.gameObject != null) Destroy(earthquakeAudioSource.gameObject);
            earthquakeAudioSource = null;
        }

        GameObject go = new GameObject("EarthquakeSfx");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        var src = go.AddComponent<AudioSource>();
        src.clip = earthquakeClip;
        src.loop = true;
        src.spatialBlend = 1f;
        src.minDistance = 1f;
        src.maxDistance = 60f; // tune if necessary
        src.playOnAwake = false;
        src.Play();
        earthquakeAudioSource = src;
    }

    // Add RPC to stop/destroy the looping AudioSource on clients
    [ClientRpc]
    private void StopEarthquakeClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (earthquakeAudioSource != null)
        {
            earthquakeAudioSource.Stop();
            if (earthquakeAudioSource.gameObject != null) Destroy(earthquakeAudioSource.gameObject);
            earthquakeAudioSource = null;
        }
    }

    // Client RPC to apply the comandante buff locally and schedule revert
    [ClientRpc]
    public void ApplyComandanteBuffClientRpc(float speedMultiplier, float jumpMultiplier, float duration, ClientRpcParams clientRpcParams = default)
    {
        // apply only on the targeted client (ownership ensured by clientRpcParams)
        // Reset existing buff timer if present
        if (comandanteBuffCoroutine != null)
        {
            StopCoroutine(comandanteBuffCoroutine);
            comandanteBuffCoroutine = null;
        }

        // store base values if not already stored (Awake should have set them)
        baseSpeed = baseSpeed == 0f ? speed : baseSpeed;
        baseJumpForce = baseJumpForce == 0f ? jumpForce : baseJumpForce;

        speed = baseSpeed * speedMultiplier;
        jumpForce = baseJumpForce * jumpMultiplier;

        comandanteBuffCoroutine = StartCoroutine(ComandanteBuffRoutine(duration));
    }

    private IEnumerator ComandanteBuffRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        speed = baseSpeed;
        jumpForce = baseJumpForce;
        comandanteBuffCoroutine = null;
    }

    private IEnumerator EnableMedikitAfterDelay(float seconds)
    {
        if (seconds <= 0f)
        {
            ui?.SetMedikitEnabled(true);
            medikitReenableCoroutine = null;
            yield break;
        }

        yield return new WaitForSeconds(seconds);
        ui?.SetMedikitEnabled(true);
        medikitReenableCoroutine = null;
    }

    #endregion

    #region --- Animation RPCs (Manual Sync) ---

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

    #endregion


}