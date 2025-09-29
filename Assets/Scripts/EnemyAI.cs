using System.Collections;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{

    [SerializeField] int reactionDelay = 500;
    [SerializeField] float minimumDistance = 10f; 

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
                if (distance < minimumDistance)
                {
                    Debug.Log($"Enemy {gameObject.name} detected Player {player.name} within range!");
                    // Use ray casting to know if there is an object between the player and the enemy
                    if (Physics.Raycast(transform.position, (player.transform.position - transform.position).normalized, out RaycastHit hit, minimumDistance))
                    {
                        if (hit.collider.gameObject != player)
                        {
                            Debug.Log($"Enemy {gameObject.name} cannot see Player {player.name} due to an obstacle: {hit.collider.gameObject.name}");
                            continue; // Skip to the next player if there's an obstacle
                        }
                        else
                        {
                            Debug.Log($"Enemy {gameObject.name} has a clear line of sight to Player {player.name}");
                            // Add logic to chase or attack the player
                            // For example, just rotate to face the player
                            //Vector3 direction = (player.transform.position - transform.position).normalized;
                            //Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
                            //transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
                            break; // React to the first player detected
                        }
                    }
                }
            }
        }
        reactionCoroutine = null;
    }
}