using UnityEngine;

public class LevelManager : MonoBehaviour
{
    private void Start()
    {
        // get the list of players in scene using tag Player
        GameData.Players.AddRange(GameObject.FindGameObjectsWithTag("Player"));

    }
}
