using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI; 

[RequireComponent(typeof(TargetMultiplayer))] 
[RequireComponent(typeof(Rigidbody))] 
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    [Header("IA Settings")]
    [SerializeField] int reactionDelay = 500;
    [SerializeField] float minimumDistance = 30f; 
    
    [Header("Patrol Settings")]
    [SerializeField] private float patrolSpeedMultiplier = 0.5f; 
    [SerializeField] private float patrolWaitTime = 3f; 

    private NavMeshAgent agent;
    private float defaultAgentSpeed; 
    
    private Coroutine reactionCoroutine;
    private string currentState = "Idle";
    private Transform targetPlayer;

    private Vector3 lastKnownPosition;
    private bool hasLastKnownPosition = false; 

    private Vector3 startPosition;
    private float patrolRadius;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = false;
    private bool isWaitingAtPatrolPoint = false;
    
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        defaultAgentSpeed = agent.speed; 
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer)
        {
            this.enabled = false;
            agent.enabled = false; 
        }
    }

    public void SetPatrolMode(Vector3 startPos, float radius)
    {
        startPosition = startPos;
        patrolRadius = radius;
        isPatrolling = true;
        currentState = "Patrol";
        FindNewPatrolPoint(); 
    }

    private void Update()
    {
        if (!IsServer) return; 

        if (reactionCoroutine == null)
        {
            reactionCoroutine = StartCoroutine(CheckPlayers());
        }

        DecideState();
        ExecuteCurrentState();
    }
    
    private void DecideState()
    {
        if (targetPlayer != null)
        {
            hasLastKnownPosition = true;
            lastKnownPosition = targetPlayer.position;

            float distance = Vector3.Distance(transform.position, targetPlayer.position);
            if (distance <= agent.stoppingDistance) 
            {
                currentState = "Attack";
            }
            else
            {
                currentState = "Walking"; // Perseguir
            }
        }
        else
        {
            if (hasLastKnownPosition)
            {
                if (currentState != "Searching")
                {
                    currentState = "Searching";
                }
                else if (currentState == "Searching")
                {
                    if (!agent.pathPending && agent.remainingDistance < 0.5f)
                    {
                        hasLastKnownPosition = false; 
                        if(isPatrolling)
                        {
                            FindNewPatrolPoint();
                        }
                    }
                }
            }
            
            if (!hasLastKnownPosition)
            {
                if (isPatrolling)
                    currentState = "Patrol";
                else
                    currentState = "Idle";
            }
        }
    }


    private void ExecuteCurrentState()
    {
        if (currentState == "Walking" && targetPlayer != null)
        {
            agent.speed = defaultAgentSpeed;
            agent.SetDestination(targetPlayer.position);
        }
        else if (currentState == "Attack")
        {
            agent.SetDestination(transform.position); 
            // (Lógica de ataque...)
        }
        else if (currentState == "Searching")
        {
            agent.speed = defaultAgentSpeed;
            agent.SetDestination(lastKnownPosition);
        }
        else if (currentState == "Patrol" && !isWaitingAtPatrolPoint)
        {
            agent.speed = defaultAgentSpeed * patrolSpeedMultiplier; 
            agent.SetDestination(currentPatrolTarget);
            
            // --- MUDANÇA 1: Lógica para "des-prender" ---
            // Se chegámos
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                StartCoroutine(WaitAtPatrolPoint());
            }
            // Se o caminho for inválido (ex: o ponto está numa parede)
            else if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                // Esquece este ponto, encontra um novo.
                Debug.LogWarning($"[EnemyAI] Não consigo chegar a {currentPatrolTarget}. A encontrar novo ponto.");
                FindNewPatrolPoint(); 
            }
        }
        else if (currentState == "Idle")
        {
            agent.SetDestination(transform.position); 
        }
    }
    
    // --- MUDANÇA 2: Função de patrulha muito mais robusta ---
    private void FindNewPatrolPoint()
    {
        bool foundPoint = false;
        
        // Tenta 30 vezes encontrar um ponto VÁLIDO
        for (int i = 0; i < 30; i++)
        {
            // 1. Gera um ponto aleatório
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = startPosition + new Vector3(randomCircle.x, 0, randomCircle.y);
            
            NavMeshHit hit;
            // 2. Tenta "snapar" esse ponto ao NavMesh (com uma tolerância pequena de 1.0f)
            if (NavMesh.SamplePosition(randomPos, out hit, 1.0f, NavMesh.AllAreas))
            {
                // 3. SUCESSO! Encontrámos um ponto que está NO NavMesh.
                currentPatrolTarget = hit.position;
                foundPoint = true;
                break; // Sai do loop 'for'
            }
        }

        // Se, depois de 30 tentativas, não encontrámos um ponto bom...
        if (!foundPoint)
        {
            // ...desiste e volta para o início (startPosition)
            Debug.LogWarning($"[EnemyAI] Não conseguiu encontrar um ponto de patrulha aleatório. A voltar ao início.");
            
            NavMeshHit hit;
            if (NavMesh.SamplePosition(startPosition, out hit, patrolRadius, NavMesh.AllAreas))
            {
                currentPatrolTarget = hit.position;
            }
            else
            {
                // Falha total, fica parado
                currentPatrolTarget = transform.position;
                isPatrolling = false;
            }
        }
    }
    
    private IEnumerator WaitAtPatrolPoint()
    {
        isWaitingAtPatrolPoint = true;
        yield return new WaitForSeconds(patrolWaitTime);
        FindNewPatrolPoint(); 
        isWaitingAtPatrolPoint = false;
    }

    private IEnumerator CheckPlayers()
    {
        yield return new WaitForSeconds(reactionDelay / 1000f);

        targetPlayer = null; 
        float closestDistance = minimumDistance;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                Transform player = client.PlayerObject.transform;
                float distance = Vector3.Distance(transform.position, player.position);

                if (distance < closestDistance)
                {
                    if (Physics.Raycast(transform.position, (player.position - transform.position).normalized, out RaycastHit hit, minimumDistance))
                    {
                        if (hit.collider.transform == player)
                        {
                            closestDistance = distance;
                            targetPlayer = player;
                        }
                    }
                }
            }
        }
        
        reactionCoroutine = null;
    }
}