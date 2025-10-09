using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class EnemyAI : MonoBehaviour
{

    [SerializeField] int reactionDelay = 500;
    [SerializeField] float minimumDistance = 30f;
    [SerializeField] float attackDistance = 2f;
    [SerializeField] float rotationSpeed = 1f;
    [SerializeField] float moveSpeed = 1f;

    private Coroutine reactionCoroutine;

    private List<string> states = new List<string>() { "Idle", "Walking", "Attack" };

    private string currentState = "Idle";

    private GameObject targetPlayer;

    private void Start() 
    {
            
    }

    private void Update()
    {
        if (reactionCoroutine == null)
        {
            reactionCoroutine = StartCoroutine(CheckPlayers());
        }

        // if state is walking move towards the player
        if (currentState == "Walking")
        {
            // Walking logic here
               //Debug.Log($"Enemy {gameObject.name} is walking!");
            // Rotate towards the player over time
            Vector3 direction = (targetPlayer.transform.position - transform.position).normalized;
            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * rotationSpeed);
            // Move towards the player over time
            transform.position += transform.forward * Time.deltaTime * moveSpeed;

        }
        else if (currentState == "Attack")
        {
            // Attack logic here
            Debug.Log($"Enemy {gameObject.name} is attacking!");
        }

    }

    private IEnumerator CheckPlayers()
    {
        yield return new WaitForSeconds(reactionDelay / 1000f);

        currentState = "Idle";

        foreach (var player in GameData.Players)
        {
            if (player != null)
            {
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance < minimumDistance)
                {
                       //Debug.Log($"Enemy {gameObject.name} detected Player {player.name} within range!");
                    // Use ray casting to know if there is an object between the player and the enemy
                    if (Physics.Raycast(transform.position, (player.transform.position - transform.position).normalized, out RaycastHit hit, minimumDistance))
                    {
                        if (hit.collider.gameObject != player)
                        {
                               //Debug.Log($"Enemy {gameObject.name} cannot see Player {player.name} due to an obstacle: {hit.collider.gameObject.name}");
                            continue; // Skip to the next player if there's an obstacle
                        }
                        else
                        {
                               //Debug.Log($"Enemy {gameObject.name} has a clear line of sight to Player {player.name}");
                            currentState = "Walking";
                            targetPlayer = player;
                            if (distance < attackDistance)
                            {
                                currentState = "Attack";
                            }
                            
                            break; // React to the first player detected
                        }
                    }
                }
            }
        }
           //Debug.Log($"Enemy {gameObject.name} is now in state: {currentState}");
        reactionCoroutine = null;
    }
}