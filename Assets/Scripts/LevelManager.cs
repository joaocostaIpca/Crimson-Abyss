using UnityEngine;

public class LevelManager : MonoBehaviour
{
    private void Start()
    {
        // get the list of players in scene using tag Player
        GameData.Players.AddRange(GameObject.FindGameObjectsWithTag("Player"));
        // temporaly set the local player as the first player in the list
        if (GameData.Players.Count > 0)
        {
            GameData.LocalPlayer = GameData.Players[0];
        }

        // get the list of enemies in scene using tag Enemy
        GameData.Enemies.AddRange(GameObject.FindGameObjectsWithTag("Enemy"));

        if (GameData.Enemies.Count == 0)
        {
            Debug.LogWarning("No enemies found in the scene with tag 'Enemy'.");
        }
    }
}
