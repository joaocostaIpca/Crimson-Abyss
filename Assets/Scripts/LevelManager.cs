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
            for (int i = 1; i < 5; i++)
            {
                if (i <= GameData.Players.Count)
                {
                    GameData.InterfaceController.UpdatePlayer(true, i, "Player" + i, 100, null);
                }
                else
                {
                    GameData.InterfaceController.UpdatePlayer(false, i, "Player" + i, 100, null);
                }
            }
        }

        // get the list of enemies in scene using tag Enemy
        GameData.Enemies.AddRange(GameObject.FindGameObjectsWithTag("Enemy"));

        if (GameData.Enemies.Count == 0)
        {
            Debug.LogWarning("No enemies found in the scene with tag 'Enemy'.");
        }
    }
}
