using TMPro;
using UnityEngine;

public class InterfaceController : MonoBehaviour
{
    [SerializeField] int currentAmmo = 30;

    private TextMeshProUGUI ammoText;
    private TextMeshProUGUI[] playerNames;

    private void Start()
    {
        GameData.InterfaceController = this;
        // find in canvas the AmmoCounter
        ammoText = GameObject.Find("Canvas/Ammo/AmmoCounter").GetComponent<TextMeshProUGUI>();
        // find in canvas the PlayerName objects
        playerNames = new TextMeshProUGUI[4];
        for (int i = 0; i < 4; i++)
        {
            playerNames[i] = GameObject.Find($"Canvas/Health/Health{i + 1}/PlayerName").GetComponent<TextMeshProUGUI>();
            playerNames[i].text = "Teste";
        }
        // other objects

    }

    public void UpdateAmmo(int newAmmo, int maxMagAmmo, int maxAmmo)
    {
        currentAmmo = newAmmo;
        ammoText.text = "Ammo: " + currentAmmo + " / " + maxMagAmmo + " | Total: " + maxAmmo;
    }
}
