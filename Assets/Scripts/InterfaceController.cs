using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode; // Precisamos disto

public class InterfaceController : MonoBehaviour
{
    public static InterfaceController Instance { get; private set; }
    
    // --- LÓGICA DE PAUSA REMOVIDA ---

    [Header("Configurações")]
    [SerializeField] float minimapUpdateDelay = 50f;
    [SerializeField] float minimapRange = 50f;
    public string[] characterNames = new string[] { "Freira", "Comandante", "Templario", "Fuzileiro", "Desconhecido" };

    private TextMeshProUGUI ammoText;
    private TextMeshProUGUI[] playerNames = new TextMeshProUGUI[4];
    private Image[] playerPictures = new Image[4];
    private Slider[] playerHealth = new Slider[4];
    private GameObject[] playerDisplay = new GameObject[4];
    private Image weaponPicture;
    private GameObject minimapCompass;
    private Sprite minimapEnemy;
    private GameObject localPlayer;
    
    private Coroutine updateCoroutine = null;
    private float minimapUpdateDelaySeconds;
    private List<string> weaponImageNames = new List<string>();
    private List<Sprite> weaponImages = new List<Sprite>();
    
    private Dictionary<ulong, int> clientSlotMap = new Dictionary<ulong, int>();
    private List<int> freeSlots = new List<int> { 2, 3, 4 }; 

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // --- LÓGICA DE PAUSA REMOVIDA ---
        
        minimapUpdateDelaySeconds = minimapUpdateDelay / 1000f;
        clientSlotMap = new Dictionary<ulong, int>();
        freeSlots = new List<int> { 2, 3, 4 };
        ammoText = GameObject.Find("Canvas/Ammo/AmmoCounter").GetComponent<TextMeshProUGUI>();
        weaponPicture = GameObject.Find("Canvas/Ammo/WeaponPicture").GetComponent<Image>();
        minimapCompass = GameObject.Find("Canvas/Minimap/MinimapImage");
        
        minimapEnemy = Resources.Load<Sprite>("Images/MinimapEnemy");
        Object[] loadedImages = Resources.LoadAll("Images/WeaponPictures", typeof(Sprite));
        foreach (Object obj in loadedImages)
        {
            weaponImageNames.Add(obj.name);
            weaponImages.Add((Sprite)obj);
        }

        for (int i = 0; i < 4; i++)
        {
            playerDisplay[i] = GameObject.Find($"Canvas/Health/Health{i + 1}");
            playerNames[i] = playerDisplay[i].transform.Find("PlayerName").GetComponent<TextMeshProUGUI>();
            playerPictures[i] = playerDisplay[i].transform.Find("PlayerPicture").GetComponent<Image>();
            playerHealth[i] = playerDisplay[i].transform.Find("PlayerHealth").GetComponent<Slider>();
            playerDisplay[i].SetActive(false);
        }
    }
    
    // --- FUNÇÕES DE PAUSA REMOVIDAS ---
    
    // --- O resto do script (Gestão de Slots, Minimapa, UI) ---
    
    public int GetOrAssignUISlot(ulong clientId, bool isLocalPlayer)
    {
        if (clientSlotMap.ContainsKey(clientId))
        {
            return clientSlotMap[clientId];
        }
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
            else
            {
                return -1; 
            }
        }
    }

    public void FreePlayerSlot(ulong clientId)
    {
        if (clientSlotMap.ContainsKey(clientId))
        {
            int slot = clientSlotMap[clientId];
            clientSlotMap.Remove(clientId);
            if (slot > 1)
            {
                freeSlots.Add(slot);
            }
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
            
            minimapCompass.transform.rotation = Quaternion.Euler(0, 0, localPlayer.transform.eulerAngles.y);
            foreach (Transform child in minimapCompass.transform)
            {
                if (child.name == "MinimapEnemy") Destroy(child.gameObject);
            }
            
            EnemyAI[] allEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            foreach (EnemyAI enemy in allEnemies) 
            {
                if (enemy != null)
                {
                    Vector3 relativePosition = enemy.transform.position - localPlayer.transform.position;
                    if (relativePosition.magnitude <= minimapRange)
                    {
                        float xPercent = (relativePosition.x / (minimapRange * 2)) + 0.5f;
                        float yPercent = (relativePosition.z / (minimapRange * 2)) + 0.5f;
                        GameObject enemyIcon = new GameObject("MinimapEnemy");
                        enemyIcon.transform.SetParent(minimapCompass.transform);
                        Image image = enemyIcon.AddComponent<Image>();
                        image.sprite = minimapEnemy;
                        image.rectTransform.sizeDelta = new Vector2(3, 3);
                        image.rectTransform.anchorMin = new Vector2(xPercent, yPercent);
                        image.rectTransform.anchorMax = new Vector2(xPercent, yPercent);
                        image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                        image.rectTransform.anchoredPosition = Vector2.zero;
                    }
                }
            }
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
        if (weaponPicture == null) return;
        int index = weaponImageNames.IndexOf(weaponName);
        if (index >= 0)
        {
            weaponPicture.sprite = weaponImages[index];
        }
        else
        {
            weaponPicture.sprite = null;
        }
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
}