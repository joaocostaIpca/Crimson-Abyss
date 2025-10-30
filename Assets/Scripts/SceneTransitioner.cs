using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class SceneTransitioner : MonoBehaviour
{
    [Header("Settings")]
    public KeyCode interactKey = KeyCode.E;
    public float holdTime = 2f;

    [Header("UI")]
    public Image progressIcon;      // UI Image (set Fill Method to Radial or Horizontal)
    public TextMeshProUGUI promptText;         // UI Text element to show the "Hold [Key]" message

    private float holdTimer = 0f;
    private bool playerInTrigger = false;

    void Start()
    {
        if (progressIcon != null)
            progressIcon.fillAmount = 0f;

        if (promptText != null)
            promptText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!playerInTrigger) return;

        if (Input.GetKey(interactKey))
        {
            holdTimer += Time.deltaTime;

            if (progressIcon != null)
                progressIcon.fillAmount = holdTimer / holdTime;

            if (holdTimer >= holdTime)
                LoadNextScene();
        }
        else if (Input.GetKeyUp(interactKey))
        {
            ResetProgress();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInTrigger = true;
            if (promptText != null)
            {
                promptText.gameObject.SetActive(true);
                promptText.text = $"Hold [{interactKey}] to continue";
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInTrigger = false;
            ResetProgress();

            if (promptText != null)
                promptText.gameObject.SetActive(false);
        }
    }

    void ResetProgress()
    {
        holdTimer = 0f;
        if (progressIcon != null)
            progressIcon.fillAmount = 0f;
    }

    void LoadNextScene()
    {
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        int nextIndex = currentIndex + 1;

        if (nextIndex >= SceneManager.sceneCountInBuildSettings)
            nextIndex = 0;

        SceneManager.LoadScene(nextIndex);
    }
}
