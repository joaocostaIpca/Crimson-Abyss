using System.Collections;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{

    [SerializeField] int reactionDelay = 500;

    private Coroutine reactionCoroutine;

    private void Start() 
    {
            
    }

    private void Update()
    {
        if (reactionCoroutine == null)
        {
            reactionCoroutine = StartCoroutine(CheckPlayers());
        }
    }

    private IEnumerator CheckPlayers()
    {
        yield return new WaitForSeconds(reactionDelay / 1000f);
        foreach (var player in GameData.Players)
        {
            if (player != null)
            {
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance < 10f)
                {
                    Debug.Log($"Enemy {gameObject.name} detected Player {player.name} within range!");
                    // Add logic to chase or attack the player
                    break; // React to the first player detected
                }
            }
        }
        reactionCoroutine = null;
    }

}