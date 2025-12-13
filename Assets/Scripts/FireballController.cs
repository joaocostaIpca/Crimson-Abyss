using UnityEngine;

public class FireballController : MonoBehaviour
{
    [Header("Fireball settings")]
    [SerializeField] private float damage = 20f;
    [SerializeField] private float maxDistance = 40f;   // destroy after this many meters from spawn
    [SerializeField] private float maxLifetime = 10f;   // fallback in seconds

    private Vector3 spawnPosition;
    private float spawnTime;
    private Rigidbody rb;
    private bool hasHit;


    void Start()
    {
        spawnPosition = transform.position;
        spawnTime = Time.time;
        hasHit = false;
    }

    void Update()
    {
        if (hasHit) return;

        // distance-based lifetime
        if (Vector3.Distance(spawnPosition, transform.position) >= maxDistance)
        {
            Destroy(gameObject);
            hasHit = true;
            return;
        }

        // time-based fallback
        if (Time.time - spawnTime >= maxLifetime)
        {
            Destroy(gameObject);
            hasHit = true;
            return;
        }
    }

    // handle both trigger and normal collisions so prefab can be configured either way
    private void OnTriggerEnter(Collider other)
    {
        HandleCollision(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollision(collision.gameObject);
    }

    private void HandleCollision(GameObject other)
    {
        if (hasHit || other == null) return;

        // ignore collisions with other projectiles or the enemy that spawned this (if it has EnemyAI)
        if (other.GetComponentInParent<FireballController>() != null) return;
        if (other.GetComponentInParent<EnemyAI>() != null) return;

        // If we hit a player, apply damage using the project's TargetMultiplayer API (server-authoritative)
        if (other.CompareTag("Player"))
        {
            var tm = other.GetComponentInParent<TargetMultiplayer>();
            if (tm != null)
            {
                // ServerRpc will be routed to server and apply damage authoritatively.
                tm.TakeDamageServerRpc(damage);
            }
            else
            {
                // Fallback: call a local method if present
                other.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            }
        }

        // destroy the fireball on any hit
        hasHit = true;
        Destroy(gameObject);
    }
}
