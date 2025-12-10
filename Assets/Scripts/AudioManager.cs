using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;

    [Header("Músicas")]
    public AudioClip menuMusic;
    public AudioClip gameMusic;

    private const string PREF_VOLUME = "MusicVolume";
    private const string PREF_MUTE = "MusicMute";

    private void Awake()
    {
        // Singleton: Garante que só há um AudioManager e que ele sobrevive a trocas de cena
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSettings();
    }

    private void Start()
    {
        // Toca a música baseada na cena atual
        CheckSceneMusic(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CheckSceneMusic(scene);
    }

    private void CheckSceneMusic(Scene scene)
    {
        // Se for MainMenu, toca música do menu. Se for nível, toca música de jogo.
        if (scene.name == "MainMenu" || scene.name == "LobbyScene")
        {
            PlayMusic(menuMusic);
        }
        else
        {
            PlayMusic(gameMusic);
        }
    }

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        
        // Se já estiver a tocar a mesma música, não reinicia
        if (musicSource.clip == clip && musicSource.isPlaying) return;

        musicSource.clip = clip;
        musicSource.Play();
    }

    // --- CONTROLO DE VOLUME (Chamado pelos Sliders/Toggles) ---

    public void SetVolume(float volume)
    {
        musicSource.volume = volume;
        PlayerPrefs.SetFloat(PREF_VOLUME, volume);
        PlayerPrefs.Save();
    }

    public void ToggleMute(bool isMuted)
    {
        musicSource.mute = isMuted;
        PlayerPrefs.SetInt(PREF_MUTE, isMuted ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Carrega definições salvas
    private void LoadSettings()
    {
        float savedVolume = PlayerPrefs.GetFloat(PREF_VOLUME, 0.5f); // 0.5 padrão
        bool savedMute = PlayerPrefs.GetInt(PREF_MUTE, 0) == 1;

        musicSource.volume = savedVolume;
        musicSource.mute = savedMute;
    }
    
    public float GetCurrentVolume()
    {
        return PlayerPrefs.GetFloat(PREF_VOLUME, 0.5f);
    }
    
    public bool GetCurrentMute()
    {
        return PlayerPrefs.GetInt(PREF_MUTE, 0) == 1;
    }
}