using UnityEngine;
using Unity.Netcode;

// Mudamos de MonoBehaviour para NetworkBehaviour
public class FireballController : NetworkBehaviour
{
    [Header("Fireball settings")]
    [SerializeField] private float damage = 20f;
    [SerializeField] private float maxDistance = 40f;
    [SerializeField] private float maxLifetime = 10f;

    private Vector3 spawnPosition;
    private float spawnTime;
    private bool hasHit;

    // Usamos OnNetworkSpawn em vez de Start para garantir que a rede está pronta
    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            // Se formos Cliente, desativamos este script. 
            // O NetworkTransform vai tratar de mover a bola visualmente.
            // Não queremos que o Cliente calcule colisões ou destrua a bola sozinho.
            enabled = false;
            return;
        }

        spawnPosition = transform.position;
        spawnTime = Time.time;
        hasHit = false;
    }

    void Update()
    {
        // Dupla segurança: Apenas o servidor executa lógica
        if (!IsServer || hasHit) return;

        // Verifica distância
        if (Vector3.Distance(spawnPosition, transform.position) >= maxDistance)
        {
            DespawnFireball();
            return;
        }

        // Verifica tempo
        if (Time.time - spawnTime >= maxLifetime)
        {
            DespawnFireball();
            return;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Apenas o servidor processa colisões
        if (!IsServer) return;
        HandleCollision(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;
        HandleCollision(collision.gameObject);
    }

    private void HandleCollision(GameObject other)
    {
        if (hasHit || other == null) return;

        // Ignora colisões com outras bolas ou com o inimigo
        if (other.GetComponentInParent<FireballController>() != null) return;
        if (other.GetComponentInParent<EnemyAI>() != null) return;

        // Aplica dano
        if (other.CompareTag("Player"))
        {
            var tm = other.GetComponentInParent<TargetMultiplayer>();
            if (tm != null)
            {
                tm.TakeDamage(damage);
            }
        }

        DespawnFireball();
    }

    private void DespawnFireball()
    {
        if (hasHit) return;
        hasHit = true;
        GetComponent<NetworkObject>().Despawn();
    }
}