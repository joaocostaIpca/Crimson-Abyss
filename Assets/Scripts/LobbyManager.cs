using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Unity.Collections; // Continua a precisar disto!

[DefaultExecutionOrder(100)] 
public class LobbyManager : NetworkBehaviour 
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
    
    [Header("Character Selection UI")]
    [SerializeField] private List<TextMeshProUGUI> characterStatusTexts; 
    [SerializeField] private Image previewImage; 
    [SerializeField] private List<Sprite> characterPreviews; 

    [Header("Game Settings")]
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private string gameSceneName = "Level";
    
    [SerializeField] private List<GameObject> characterPrefabs; 

    public static List<Sprite> CharacterPreviews { get; private set; }

    public NetworkList<ulong> characterLocks = new NetworkList<ulong>();
    
    // --- MUDANÇA AQUI: Trocado NetworkString por FixedString ---
    public NetworkList<FixedString64Bytes> PlayerNames = new NetworkList<FixedString64Bytes>();
    
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
        
        // PlayerNames já é criado na definição
        CharacterPreviews = characterPreviews; 
        
        buttonShowCreate.onClick.AddListener(OnShowCreatePanel);
        buttonShowJoin.onClick.AddListener(OnShowJoinPanel);
        buttonDoConnect.onClick.AddListener(OnJoinServer);
        buttonStartGame.onClick.AddListener(OnStartGame);
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            characterLocks.Clear();
            for (int i = 0; i < maxPlayers; i++)
            {
                characterLocks.Add(99); 
            }
        }
        
        characterLocks.OnListChanged += OnCharacterLocksChanged;
        PlayerNames.OnListChanged += OnPlayerListChanged; 
        
        UpdateCharacterSelectionUI();
        UpdatePlayerListUI();
    }
    
    private void Start()
    {
        ShowPanel(panelMainMenu);

        if (NetworkManager.Singleton == null) return;
        
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCheck;
        SceneManager.sceneLoaded += OnSceneWasLoaded;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }
        SceneManager.sceneLoaded -= OnSceneWasLoaded;
        
        if(characterLocks != null)
        {
            characterLocks.OnListChanged -= OnCharacterLocksChanged;
            characterLocks.Dispose();
        }
        if(PlayerNames != null)
        {
            PlayerNames.OnListChanged -= OnPlayerListChanged;
            PlayerNames.Dispose();
        }
        
        base.OnDestroy(); 
    }

    private void ShowPanel(GameObject panelToShow)
    {
        panelMainMenu.SetActive(panelToShow == panelMainMenu);
        panelJoin.SetActive(panelToShow == panelJoin);
        panelWaiting.SetActive(panelToShow == panelWaiting);
    }
    
    private void OnShowCreatePanel()
    {
        ShowPanel(panelWaiting);
        buttonStartGame.gameObject.SetActive(true);
        textHostIP.text = $"IP da Sala: {GetLocalIPv4()}";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        NetworkManager.Singleton.StartHost();
        
        if (!GetComponent<NetworkObject>().IsSpawned)
        {
            GetComponent<NetworkObject>().Spawn(); 
        }
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

    public bool TryLockCharacter(int charIndex, ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return false;
        if (characterLocks[charIndex] != 99) return false; 
        for (int i = 0; i < characterLocks.Count; i++)
        {
            if (characterLocks[i] == clientId)
            {
                characterLocks[i] = 99;
            }
        }
        characterLocks[charIndex] = clientId;
        return true;
    }

    private void OnCharacterLocksChanged(NetworkListEvent<ulong> changeEvent)
    {
        UpdateCharacterSelectionUI();
    }
    
    // --- MUDANÇA AQUI: O evento agora é para FixedString ---
    private void OnPlayerListChanged(NetworkListEvent<FixedString64Bytes> changeEvent)
    {
        UpdatePlayerListUI();
    }


    private void UpdateCharacterSelectionUI()
    {
        if (characterStatusTexts == null || characterStatusTexts.Count == 0) return;
        for (int i = 0; i < characterStatusTexts.Count; i++)
        {
            if (i >= characterLocks.Count) break; 
            if (characterLocks[i] == 99)
            {
                characterStatusTexts[i].text = "Livre";
                characterStatusTexts[i].color = Color.green;
            }
            else
            {
                characterStatusTexts[i].text = $"Pego por: Jogador {characterLocks[i]}";
                characterStatusTexts[i].color = Color.red;
            }
        }
    }

    private void OnStartGame()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            if (lobbyCanvas != null)
            {
                lobbyCanvas.gameObject.SetActive(false);
            }
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

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
        if (NetworkManager.Singleton.IsServer)
        {
            UpdateServerPlayerNameList();
        }
        if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
        {
            textHostIP.text = "Ligado! A aguardar que o Host comece...";
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            UpdateServerPlayerNameList();
            for (int i = 0; i < characterLocks.Count; i++)
            {
                if (characterLocks[i] == clientId)
                {
                    characterLocks[i] = 99;
                    break;
                }
            }
        }
    }
    
    private void OnSceneWasLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == gameSceneName && NetworkManager.Singleton.IsServer)
        {
            Vector3 spawnPos = new Vector3(-215, 1, -19);
            Quaternion spawnRot = Quaternion.identity;
            
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                ulong clientId = client.ClientId;
                PlayerLobbyShell shell = client.PlayerObject.GetComponent<PlayerLobbyShell>();
                int charIndex = shell.SelectedCharacterIndex.Value;
                if (charIndex == -1)
                {
                    charIndex = FindFirstFreeCharacter();
                    if (charIndex != -1) TryLockCharacter(charIndex, clientId);
                    else charIndex = 0; 
                }
                GameObject prefabToSpawn = characterPrefabs[charIndex];
                GameObject playerInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);
                spawnPos.x += 2.0f; 
                playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
                playerInstance.GetComponent<PlayerController>().CharacterIndex.Value = charIndex;
                shell.NetworkObject.Despawn(true);
            }
            NetworkObject.Despawn(true);
        }
    }

    private int FindFirstFreeCharacter()
    {
        for (int i = 0; i < characterLocks.Count; i++)
        {
            if (characterLocks[i] == 99) return i;
        }
        return -1; 
    }
    
    private void UpdatePlayerListUI()
    {
        string playerList = "Jogadores Ligados:\n";
        // Ler a lista é igual, a conversão para string é automática
        foreach (var name in PlayerNames)
        {
            playerList += $"- {name}\n";
        }
        textPlayerList.text = playerList;
        
        if (IsServer)
        {
            buttonStartGame.interactable = (PlayerNames.Count >= 1 && PlayerNames.Count <= maxPlayers);
        }
    }
    
    private void UpdateServerPlayerNameList()
    {
        PlayerNames.Clear();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            string name = "Jogador " + client.ClientId;
            if (client.ClientId == 0) name += " (Host)";
            // A conversão de string para FixedString é automática
            PlayerNames.Add(name);
        }
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