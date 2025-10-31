using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement; 

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
    
    private bool isGameScene = false;
    private bool teleportedOnce = false; 

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (networkWeapon == null) networkWeapon = GetComponent<NetworkWeapon>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            playerCamera.enabled = false;
            if (playerAudioListener != null) playerAudioListener.enabled = false;
            this.enabled = false;
            return; 
        }

        SceneManager.sceneLoaded += OnSceneWasLoaded;
        HandleSceneChange(SceneManager.GetActiveScene());
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            SceneManager.sceneLoaded -= OnSceneWasLoaded;
        }
        base.OnNetworkDespawn();
    }

    // Esta função é chamada sempre que uma nova cena é carregada
    private void OnSceneWasLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleSceneChange(scene);
    }

    // A nossa função "cérebro"
    private void HandleSceneChange(Scene scene)
    {
        if (scene.name == "Level") // O nome da tua cena de jogo
        {
            isGameScene = true;
            
            // Ativa os componentes do jogador
            playerCamera.enabled = true;
            if (playerAudioListener != null) playerAudioListener.enabled = true;
            if (networkWeapon != null) networkWeapon.enabled = true; 

            // Tranca o rato
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            // Pede o teletransporte (agora local)
            if (!teleportedOnce)
            {
                // --- MUDANÇA AQUI ---
                // Já não pedimos ao servidor. Como somos o "Owner",
                // teletransportamo-nos a nós mesmos e o NetworkTransform
                // encarrega-se de sincronizar.
                TeleportPlayer(new Vector3(-215, 1, -19));
                // --- FIM DA MUDANÇA ---
                
                teleportedOnce = true;
            }
        }
        else // Se for a LobbyScene
        {
            isGameScene = false;
            
            playerCamera.enabled = false;
            if (playerAudioListener != null) playerAudioListener.enabled = false;
            if (networkWeapon != null) networkWeapon.enabled = false; 

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // --- MUDANÇA AQUI ---
    // Removemos o [ServerRpc] e esta é agora uma função local
    private void TeleportPlayer(Vector3 position)
    {
        // Desativa o Rigidbody temporariamente para o teletransporte
        if (rb != null)
        {
            rb.isKinematic = true; 
        }

        // Teletransporta o jogador
        transform.position = position;
        
        // Reativa o Rigidbody
        if (rb != null)
        {
            rb.isKinematic = false; 
            rb.linearVelocity = Vector3.zero; // Limpa qualquer velocidade antiga
        }
    }
    // --- FIM DA MUDANÇA ---

    // --- LÓGICA DE INPUT (SÓ CORRE NA CENA DO JOGO) ---
    void Update()
    {
        if (!IsOwner || !isGameScene) return;

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

    // --- LÓGICA DE FÍSICA (SÓ CORRE NA CENA DO JOGO) ---
    void FixedUpdate()
    {
        if (!IsOwner || !isGameScene) return;

        // Ground Check
        if (groundCheckTimer > 0)
        {
            groundCheckTimer -= Time.fixedDeltaTime;
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
        
        // Movimento
        Vector3 move = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized;
        rb.MovePosition(rb.position + move * speed * Time.fixedDeltaTime);
        
        // Pulo
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