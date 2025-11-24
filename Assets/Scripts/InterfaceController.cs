using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class InterfaceController : MonoBehaviour
{
    public static InterfaceController Instance { get; private set; }

    // --- PAUSE LOGIC REMOVED ---

    [Header("Configurações")]
    [SerializeField] float minimapUpdateDelay = 50f;
    [SerializeField] float minimapRange = 50f;
    public string[] characterNames = new string[] { "Freira", "Comandante", "Templario", "Fuzileiro", "Desconhecido" };

    [Header("Minimap Icons")]
    // You can drag these in the Inspector or rely on Resources.Load
    public Sprite minimapBoxIcon; 
    public Sprite minimapOtherPlayerIcon;

    private TextMeshProUGUI ammoText;
    private TextMeshProUGUI[] playerNames = new TextMeshProUGUI[4];
    private Image[] playerPictures = new Image[4];
    private Slider[] playerHealth = new Slider[4];
    private GameObject[] playerDisplay = new GameObject[4];
    
    private Image weaponPicture;
    private Image weaponPicture2;
    private Image weaponPicture3;
    private GameObject minimapCompass;
    private Sprite minimapEnemy;
    private GameObject localPlayer;
    
    private Coroutine updateCoroutine = null;
    private float minimapUpdateDelaySeconds;
    private List<string> weaponImageNames = new List<string>();
    private List<Sprite> weaponImages = new List<Sprite>();
    
    private Dictionary<ulong, int> clientSlotMap = new Dictionary<ulong, int>();
    private List<int> freeSlots = new List<int> { 2, 3, 4 }; 

    private Image medikitImage;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // --- PAUSE LOGIC REMOVED ---
        
        minimapUpdateDelaySeconds = minimapUpdateDelay / 1000f;
        clientSlotMap = new Dictionary<ulong, int>();
        freeSlots = new List<int> { 2, 3, 4 };
        
        // Find UI Elements
        ammoText = GameObject.Find("Canvas/WeaponSystem/Ammo/AmmoCounter").GetComponent<TextMeshProUGUI>();
        weaponPicture = GameObject.Find("Canvas/WeaponSystem/Ammo/WeaponPicture").GetComponent<Image>();
        weaponPicture2 = GameObject.Find("Canvas/WeaponSystem/WeaponPicture2").GetComponent<Image>();
        weaponPicture3 = GameObject.Find("Canvas/WeaponSystem/WeaponPicture3").GetComponent<Image>();
        minimapCompass = GameObject.Find("Canvas/Minimap/MinimapImage");
        medikitImage = GameObject.Find("Canvas/WeaponSystem/Medikit").GetComponent<Image>();
        
        // Load Icons
        minimapEnemy = Resources.Load<Sprite>("Images/MinimapEnemy");
        // Try to load if not assigned in inspector
        if (minimapBoxIcon == null) minimapBoxIcon = Resources.Load<Sprite>("Images/MinimapBox");
        if (minimapOtherPlayerIcon == null) minimapOtherPlayerIcon = Resources.Load<Sprite>("Images/MinimapPlayer");

        Object[] loadedImages = Resources.LoadAll("Images/WeaponPictures", typeof(Sprite));
        foreach (Object obj in loadedImages)
        {
            weaponImageNames.Add(obj.name);
            weaponImages.Add((Sprite)obj);
        }

        for (int i = 0; i < 4; i++)
        {
            playerDisplay[i] = GameObject.Find($"Canvas/Health/PlayerInfo{i + 1}");
            playerNames[i] = playerDisplay[i].transform.Find("PlayerName").GetComponent<TextMeshProUGUI>();
            playerPictures[i] = playerDisplay[i].transform.Find("PlayerPicture").GetComponent<Image>();
            playerHealth[i] = playerDisplay[i].transform.Find("PlayerHealth").GetComponent<Slider>();
            playerDisplay[i].SetActive(false);
        }

        DontDestroyOnLoad(gameObject);
    }
    
    // --- PAUSE FUNCTIONS REMOVED ---
    
    // --- Slot Management & UI Updates ---
    
    public int GetOrAssignUISlot(ulong clientId, bool isLocalPlayer)
    {
        if (clientSlotMap.ContainsKey(clientId)) return clientSlotMap[clientId];
        
        if (isLocalPlayer)
        {
            clientSlotMap[clientId] = 1;
            return 1;
        }
        else 
        {
            if (freeSlots.Count > 0)
            {
                int slot = freeSlots[0]; 
                freeSlots.RemoveAt(0); 
                clientSlotMap[clientId] = slot;
                return slot;
            }
            else return -1; 
        }
    }

    public void FreePlayerSlot(ulong clientId)
    {
        if (clientSlotMap.ContainsKey(clientId))
        {
            int slot = clientSlotMap[clientId];
            clientSlotMap.Remove(clientId);
            if (slot > 1) freeSlots.Add(slot);
        }
    }
    
    public void SetLocalPlayer(GameObject player)
    {
        localPlayer = player;
        if (updateCoroutine == null)
        {
            updateCoroutine = StartCoroutine(UpdateMinimap());
        }
    }

    private IEnumerator UpdateMinimap()
    {
        while (true)
        {
            yield return new WaitForSeconds(minimapUpdateDelaySeconds);
            if (localPlayer == null) continue;
            
            // Rotate minimap
            minimapCompass.transform.rotation = Quaternion.Euler(0, 0, localPlayer.transform.eulerAngles.y);
            
            // Clear old icons (Enemy, Box, Player)
            foreach (Transform child in minimapCompass.transform)
            {
                if (child.name == "MinimapEnemy" || child.name == "MinimapBox" || child.name == "MinimapPlayer") 
                    Destroy(child.gameObject);
            }
            
            // 1. ENEMIES
            EnemyAI[] allEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            foreach (EnemyAI enemy in allEnemies) 
            {
                if (enemy != null)
                {
                    // Check if alive
                    var tm = enemy.GetComponent<TargetMultiplayer>();
                    if (tm != null && tm.health.Value <= 0) continue;

                    DrawIcon(enemy.transform.position, minimapEnemy, "MinimapEnemy", Color.red);
                }
            }

            // 2. SUPPLY BOXES (Stash) - NEW
            SupplyBox[] allBoxes = FindObjectsByType<SupplyBox>(FindObjectsSortMode.None);
            foreach (SupplyBox box in allBoxes)
            {
                if (box != null && box.gameObject.activeSelf) // Only show if active
                {
                    DrawIcon(box.transform.position, minimapBoxIcon, "MinimapBox", Color.green);
                }
            }

            // 3. OTHER PLAYERS - NEW
            PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (PlayerController p in allPlayers)
            {
                if (p.gameObject != localPlayer)
                {
                    var tm = p.GetComponent<TargetMultiplayer>();
                    // Only show alive players
                    if (tm != null && tm.health.Value > 0)
                    {
                        DrawIcon(p.transform.position, minimapOtherPlayerIcon, "MinimapPlayer", Color.blue);
                    }
                }
            }
        }
    }

    // Helper function to draw icons on the minimap
    private void DrawIcon(Vector3 worldPos, Sprite icon, string iconName, Color color)
    {
        if (icon == null) return;

        Vector3 relativePosition = worldPos - localPlayer.transform.position;
        if (relativePosition.magnitude <= minimapRange)
        {
            float xPercent = (relativePosition.x / (minimapRange * 2)) + 0.5f;
            float yPercent = (relativePosition.z / (minimapRange * 2)) + 0.5f;
            
            GameObject iconObj = new GameObject(iconName);
            iconObj.transform.SetParent(minimapCompass.transform);
            Image image = iconObj.AddComponent<Image>();
            image.sprite = icon;
            image.color = color;
            image.rectTransform.sizeDelta = new Vector2(4, 4); 
            image.rectTransform.anchorMin = new Vector2(xPercent, yPercent);
            image.rectTransform.anchorMax = new Vector2(xPercent, yPercent);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    public void UpdateAmmo(int newAmmo, int maxMagAmmo, int maxAmmo)
    {
        if (ammoText != null)
        {
            ammoText.text = "Ammo: " + newAmmo + " / " + maxMagAmmo + " | Total: " + maxAmmo;
        }
    }

    public void UpdateWeapon(string weaponName)
    {
        // Debug.Log("Updating weapon to: " + weaponName);

        if (weaponPicture == null) return;
        
        int index = weaponImageNames.IndexOf(weaponName);
        int emptyIndex = weaponImageNames.IndexOf("Empty");
        
        if (index >= 0) weaponPicture.sprite = weaponImages[index];
        else weaponPicture.sprite = (emptyIndex >= 0) ? weaponImages[emptyIndex] : null;

        // Logic for secondary/tertiary weapons
        PlayerWeaponManager weaponManager = localPlayer.GetComponent<PlayerWeaponManager>();
        if (weaponManager != null)
        {
            UpdateWeaponSlot(weaponManager, 1, weaponPicture2, emptyIndex);
            UpdateWeaponSlot(weaponManager, 2, weaponPicture3, emptyIndex);
        }
    }

    // Helper to clean up UpdateWeapon code
    private void UpdateWeaponSlot(PlayerWeaponManager mgr, int offset, Image imgSlot, int emptyIndex)
    {
        if (imgSlot == null) return;
        int count = mgr.weaponPrefabs.Count;
        if (count == 0) return;

        int nextIndex = (mgr.CurrentWeaponIndex.Value + offset) % count;
        string name = mgr.weaponPrefabs[nextIndex].name;
        int imgIndex = weaponImageNames.IndexOf(name);
        
        if (imgIndex >= 0) imgSlot.sprite = weaponImages[imgIndex];
        else imgSlot.sprite = (emptyIndex >= 0) ? weaponImages[emptyIndex] : null;
    }

    public void UpdatePlayer(bool isActive, int playerSlot, int charIndex, int playerHealth, Sprite playerPicture)
    {
        if (playerSlot < 1 || playerSlot > 4 || playerDisplay[0] == null) 
            return;
            
        int index = playerSlot - 1; 
        playerDisplay[index].SetActive(isActive);
        if(!isActive)
            return;
        if (charIndex < 0 || charIndex >= characterNames.Length) charIndex = characterNames.Length - 1; 
        playerNames[index].text = characterNames[charIndex];
        this.playerHealth[index].value = playerHealth;
        this.playerPictures[index].sprite = playerPicture;
    }

    public void UpdatePlayerHealth(int playerSlot, int healthValue)
    {
        if (playerSlot < 1 || playerSlot > 4 || playerHealth[0] == null)
            return;
        int index = playerSlot - 1;
        playerHealth[index].value = healthValue;
    }

    // --- Medikit ---
    public void SetMedikitEnabled(bool enabled)
    {
        if (medikitImage == null) return;
        Color c = medikitImage.color;
        c.a = enabled ? 1f : (50f / 255f);
        medikitImage.color = c;
    }

    public void SwitchToNextWeapon()
    {
        PlayerWeaponManager weaponManager = localPlayer.GetComponent<PlayerWeaponManager>();
        weaponManager.CycleWeaponLocal();
    }
}