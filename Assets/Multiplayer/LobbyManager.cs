using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using UnityEngine.SceneManagement; // Precisamos disto!

[DefaultExecutionOrder(100)] 
public class LobbyManager : MonoBehaviour
{
    [Header("UI General")]
    [SerializeField] private Canvas lobbyCanvas; 

    [Header("UI Panels")]
    [SerializeField] private GameObject panelMainMenu;
    [SerializeField] private GameObject panelJoin;
    [SerializeField] private GameObject panelWaiting;

    [Header("Main Menu")]
    [SerializeField] private Button buttonShowCreate;
    [SerializeField] private Button buttonShowJoin;

    [Header("Join Panel")]
    [SerializeField] private TMP_InputField inputIP;
    [SerializeField] private Button buttonDoConnect;

    [Header("Waiting Panel")]
    [SerializeField] private TextMeshProUGUI textHostIP;
    [SerializeField] private TextMeshProUGUI textPlayerList;
    [SerializeField] private Button buttonStartGame;

    [Header("Game Settings")]
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private string gameSceneName = "Level";

    public static LobbyManager Instance;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject); 
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); 

        buttonShowCreate.onClick.AddListener(OnShowCreatePanel);
        buttonShowJoin.onClick.AddListener(OnShowJoinPanel);
        buttonDoConnect.onClick.AddListener(OnJoinServer);
        buttonStartGame.onClick.AddListener(OnStartGame);
    }
    
    private void Start()
    {
        ShowPanel(panelMainMenu);

        if (NetworkManager.Singleton == null) return;
        
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCheck;
        
        // --- MUDANÇA 1: Subscrever ao evento de cena carregada ---
        SceneManager.sceneLoaded += OnSceneWasLoaded;
    }

    private void OnDestroy()
    {
        // Esta função agora só limpa os eventos se for destruída
        // ANTES de o jogo começar (ex: se for um duplicado)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }
        // --- MUDANÇA 2: Limpar também o evento de cena ---
        SceneManager.sceneLoaded -= OnSceneWasLoaded;
    }

    // --- Gestão da UI ---
    private void ShowPanel(GameObject panelToShow)
    {
        panelMainMenu.SetActive(panelToShow == panelMainMenu);
        panelJoin.SetActive(panelToShow == panelJoin);
        panelWaiting.SetActive(panelToShow == panelWaiting);
    }

    // --- Lógica de Rede ---
    private void OnShowCreatePanel()
    {
        ShowPanel(panelWaiting);
        buttonStartGame.gameObject.SetActive(true);
        textHostIP.text = $"IP da Sala: {GetLocalIPv4()}";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        NetworkManager.Singleton.StartHost();
    }

    private void OnShowJoinPanel()
    {
        ShowPanel(panelJoin);
    }
    
    private void OnJoinServer()
    {
        string ip = inputIP.text;
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(ip, transport.ConnectionData.Port);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        NetworkManager.Singleton.StartClient();
        ShowPanel(panelWaiting);
        buttonStartGame.gameObject.SetActive(false);
        textHostIP.text = $"A ligar a {ip}...";
    }

    private void OnStartGame()
    {
        int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
        if (playerCount >= 1 && playerCount <= maxPlayers)
        {
            if (lobbyCanvas != null)
            {
                lobbyCanvas.gameObject.SetActive(false);
            }
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

    // --- Eventos do NetworkManager ---
    private void ConnectionApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        if (NetworkManager.Singleton.ConnectedClients.Count >= maxPlayers)
        {
            response.Approved = false;
        }
        else
        {
            response.Approved = true;
            response.CreatePlayerObject = true; 
        }
        response.Pending = false;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer) UpdatePlayerListUI();
        if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
        {
            textHostIP.text = "Ligado! A aguardar que o Host comece...";
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer) UpdatePlayerListUI();
    }
    
    // --- MUDANÇA 3: A nossa nova função de "auto-destruição" ---
    private void OnSceneWasLoaded(Scene scene, LoadSceneMode mode)
    {
        // Se a cena que acabou de carregar é a nossa cena de jogo...
        if (scene.name == gameSceneName)
        {
            Debug.Log("[LobbyManager] Cena 'Level' carregada. A auto-destruir-me...");
            
            // Limpa todos os eventos para não deixar lixo
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                NetworkManager.Singleton.ConnectionApprovalCallback = null;
            }
            SceneManager.sceneLoaded -= OnSceneWasLoaded;
            
            // Destrói o GameObject do LobbyManager (e o seu filho, o Canvas)
            Destroy(gameObject);
        }
    }

    // --- Funções de Ajuda ---
    private void UpdatePlayerListUI()
    {
        string playerList = "Jogadores Ligados:\n";
        int playerCount = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            playerCount++;
            string playerLabel = $" - Jogador {client.ClientId}";
            if (client.ClientId == NetworkManager.Singleton.LocalClientId) playerLabel += " (Host)";
            playerList += playerLabel + "\n";
        }
        textPlayerList.text = playerList;
        buttonStartGame.interactable = (playerCount >= 1 && playerCount <= maxPlayers);
    }
    
    private string GetLocalIPv4()
    {
        foreach (var hostEntry in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (hostEntry.AddressFamily == AddressFamily.InterNetwork)
            {
                return hostEntry.ToString();
            }
        }
        return "127.0.0.1";
    }
}