using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransitioner : MonoBehaviour
{
    [Header("Settings")]
    public KeyCode interactKey = KeyCode.E;
    public float holdTime = 2f;
    [SerializeField] private string gameSceneName = "SubLevel";

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
        if (NetworkManager.Singleton.IsServer)
        {
            if (!playerInTrigger) return;

            if (Input.GetKey(interactKey))
            {
                holdTimer += Time.deltaTime;

                if (progressIcon != null)
                    progressIcon.fillAmount = holdTimer / holdTime;

                if (holdTimer >= holdTime)
                {
                    MoveAllPlayers(2,-9,8,1);
                    NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);

                }

            }
            else if (Input.GetKeyUp(interactKey))
            {
                ResetProgress();
            }
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

    public void MoveAllPlayers(float targetY, float minX, float maxX, float minSpacing)
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        // Track used X positions to avoid overlap
        List<float> usedXPositions = new List<float>();

        foreach (GameObject p in players)
        {
            float randomX = 0f;
            bool valid = false;

            // Try up to 50 times to find a non-overlapping X
            for (int tries = 0; tries < 50; tries++)
            {
                float candidate = Random.Range(minX, maxX);
                bool tooClose = false;

                // Check spacing against all used positions
                foreach (float used in usedXPositions)
                {
                    if (Mathf.Abs(candidate - used) < minSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    randomX = candidate;
                    valid = true;
                    usedXPositions.Add(candidate);
                    break;
                }
            }

            // If no valid position found after many tries, just push outward
            if (!valid)
            {
                randomX = usedXPositions.Count * minSpacing + minX;
                usedXPositions.Add(randomX);
            }

            // Move the player
            Vector3 pos = p.transform.position;
            p.transform.position = new Vector3(randomX, targetY, 0);
        }
    }
}
