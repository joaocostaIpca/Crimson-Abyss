using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class PauseMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button optionsButton; // Opcional, por agora não faz nada ou abre o teu painel de opções
    [SerializeField] private Button quitButton;

    private bool isPaused = false;
    private PlayerController localPlayerController;

    private void Start()
    {
        // Garante que o menu começa fechado
        if (pausePanel != null) pausePanel.SetActive(false);

        resumeButton.onClick.AddListener(ResumeGame);
        quitButton.onClick.AddListener(QuitMatch);
        
        // Se tiveres botão de opções:
        if(optionsButton != null) optionsButton.onClick.AddListener(() => Debug.Log("Opções no jogo (Por implementar)"));
    }

    private void Update()
    {
        // Tecla ESC ou P para pausar
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
        {
            TogglePause();
        }
    }

    private void TogglePause()
    {
        isPaused = !isPaused;

        if (pausePanel != null) pausePanel.SetActive(isPaused);

        if (isPaused)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (localPlayerController == null) FindLocalPlayer();
            
            // --- CORREÇÃO: Desativa o objeto todo temporariamente ou scripts específicos ---
            if (localPlayerController != null)
            {
                // Opção A: Desativar scripts de controlo
                localPlayerController.enabled = false; 
                var weaponManager = localPlayerController.GetComponent<PlayerWeaponManager>();
                if (weaponManager != null) weaponManager.enabled = false;
            }
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (localPlayerController == null) FindLocalPlayer();

            if (localPlayerController != null)
            {
                localPlayerController.enabled = true;
                var weaponManager = localPlayerController.GetComponent<PlayerWeaponManager>();
                if (weaponManager != null) weaponManager.enabled = true;
            }
        }
    }

    public void ResumeGame()
    {
        if (isPaused) TogglePause();
    }

    public void QuitMatch()
    {
        Debug.Log("A sair da partida...");
        
        // Usa o LobbyManager para sair corretamente (limpa a rede e volta ao menu)
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.ShutdownAndReturnToMenu();
        }
        else
        {
            // Fallback se o LobbyManager tiver morrido (não devia acontecer)
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene("MainMenu");
        }
    }

    private void FindLocalPlayer()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;
        
        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj != null)
        {
            localPlayerController = playerObj.GetComponent<PlayerController>();
        }
    }
}