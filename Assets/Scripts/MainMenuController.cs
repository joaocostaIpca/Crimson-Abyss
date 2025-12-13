using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("Paineis")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject optionsPanel;

    [Header("Botões Menu")]
    [SerializeField] private Button buttonPlay;
    [SerializeField] private Button buttonOptions;
    [SerializeField] private Button buttonQuit;

    [Header("Opções UI")]
    [SerializeField] private Button buttonBackFromOptions;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle muteToggle;

    [Header("Cenas")]
    [SerializeField] private string lobbySceneName = "LobbyScene";

    private void Awake()
    {
        // Listeners Menu Principal
        buttonPlay.onClick.AddListener(OnPlayClicked);
        buttonOptions.onClick.AddListener(OnOptionsClicked);
        buttonQuit.onClick.AddListener(OnQuitClicked);

        // Listeners Opções
        buttonBackFromOptions.onClick.AddListener(OnBackFromOptionsClicked);
        
        if (volumeSlider != null) 
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            
        if (muteToggle != null) 
            muteToggle.onValueChanged.AddListener(OnMuteChanged);
    }

    private void Start()
    {
        // Inicializa a UI das opções com os valores guardados no AudioManager
        if (AudioManager.Instance != null)
        {
            if (volumeSlider != null) volumeSlider.value = AudioManager.Instance.GetCurrentVolume();
            if (muteToggle != null) muteToggle.isOn = AudioManager.Instance.GetCurrentMute();
        }

        ShowMainPanel();
    }

    private void OnPlayClicked()
    {
        SceneManager.LoadScene(lobbySceneName);
    }

    private void OnOptionsClicked()
    {
        mainPanel.SetActive(false);
        optionsPanel.SetActive(true);
    }

    private void OnBackFromOptionsClicked()
    {
        ShowMainPanel();
    }

    private void ShowMainPanel()
    {
        mainPanel.SetActive(true);
        optionsPanel.SetActive(false);
    }

    private void OnQuitClicked()
    {
        Debug.Log("A sair do jogo...");
        Application.Quit();
    }

    // --- Eventos de Áudio ---

    private void OnVolumeChanged(float value)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetVolume(value);
        }
    }

    private void OnMuteChanged(bool isMuted)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.ToggleMute(isMuted);
        }
    }
}