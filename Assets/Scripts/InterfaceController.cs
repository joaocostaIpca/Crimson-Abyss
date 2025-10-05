using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InterfaceController : MonoBehaviour
{
    [SerializeField] int currentAmmo = 30;
    [SerializeField] float minimapUpdateDelay = 50f; // in milliseconds
    [SerializeField] float minimapRange = 50f; // in world units

    private TextMeshProUGUI ammoText;
    private TextMeshProUGUI[] playerNames;
    private Coroutine updateCoroutine = null;
    private float minimapUpdateDelaySeconds;
    private GameObject minimapCompass;
    private Sprite minimapEnemy;

    private void Start()
    {
        GameData.InterfaceController = this;

        minimapUpdateDelaySeconds = minimapUpdateDelay / 1000f;
        // find in canvas the AmmoCounter
        ammoText = GameObject.Find("Canvas/Ammo/AmmoCounter").GetComponent<TextMeshProUGUI>();
        // find in canvas the PlayerName objects
        playerNames = new TextMeshProUGUI[4];
        for (int i = 0; i < 4; i++)
        {
            playerNames[i] = GameObject.Find($"Canvas/Health/Health{i + 1}/PlayerName").GetComponent<TextMeshProUGUI>();
            playerNames[i].text = "Teste";
        }
        // find in canvas the MinimapCompass object
        minimapCompass = GameObject.Find("Canvas/Minimap/MinimapImage");
        // load the enemy icon sprite from Resources/Images/MinimapEnemy
        minimapEnemy = Resources.Load<Sprite>("Images/MinimapEnemy");


    }

    private void Update()
    {
        if (updateCoroutine == null)
        {
            // start minimap update coroutine
            updateCoroutine = StartCoroutine(UpdateMinimap());
        }
    }

    public void UpdateAmmo(int newAmmo, int maxMagAmmo, int maxAmmo)
    {
        currentAmmo = newAmmo;
        ammoText.text = "Ammo: " + currentAmmo + " / " + maxMagAmmo + " | Total: " + maxAmmo;
    }

    public IEnumerator UpdateMinimap()
    {
        yield return new WaitForSeconds(minimapUpdateDelaySeconds);
        
        GameObject player = GameData.LocalPlayer;
        // Update minimapcompass according to player rotation
        if (player != null)
        {            
            minimapCompass.transform.rotation = Quaternion.Euler(0, 0, player.transform.eulerAngles.y);
        }

        // Clear previous minimap enemies
        foreach (Transform child in minimapCompass.transform)
        {
            if (child.name == "MinimapEnemy")
            {
                Destroy(child.gameObject);
            }
        }

        // Update minimap enemies with the enemies positions
        foreach (GameObject enemy in GameData.Enemies)
        {
            //get enemy position relative to player
            if (player != null && enemy != null)
            {
                Vector3 relativePosition = enemy.transform.position - player.transform.position;
                // check if the enemy is within range
                if (relativePosition.magnitude <= minimapRange)
                {
                    // calculate the position on the minimap in percentage
                    float xPercent = (relativePosition.x / (minimapRange * 2)) + 0.5f;
                    float yPercent = (relativePosition.z / (minimapRange * 2)) + 0.5f;
                    // create new UI Image to display the enemy on the minimap
                    GameObject enemyIcon = new GameObject("MinimapEnemy");
                    enemyIcon.transform.SetParent(minimapCompass.transform);
                    enemyIcon.AddComponent<RectTransform>();
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

        updateCoroutine = null;
    }
}
