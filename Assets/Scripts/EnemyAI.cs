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
    #region variables

    [Header("IA Settings")]
    [SerializeField] int reactionDelay = 500;
    [SerializeField] float minimumDistance = 30f;
    [SerializeField] private string enemyType = "Diabrete"; // Diabrete, Lancador, Bruto

    // --- MUDANÇA 1: Variáveis de Ataque ---
    [Header("Attack Settings")]
    [SerializeField] private float enemyDamage = 10f;
    [SerializeField] private float attackCooldown = 2f; // Só ataca a cada 2s
    [SerializeField] private float attackAnimDelay = 0.5f; // Placeholder para a animação
    private float lastAttackTime = 0f;
    
    [Header("Patrol Settings")]
    [SerializeField] private float patrolSpeedMultiplier = 0.5f; 
    [SerializeField] private float patrolWaitTime = 1f; 

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

    private Animator animator;

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
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

            // --- MUDANÇA 2: Usar 'stoppingDistance' do Agente ---
            float distance = Vector3.Distance(transform.position, targetPlayer.position);
            if (distance <= agent.stoppingDistance) 
            {
                currentState = "Attack";
                animator?.SetTrigger("Attack");
            }
            else
            {
                currentState = "Walking";
                animator?.SetTrigger("Walk");
            }
        }
        else
        {
            if (hasLastKnownPosition)
            {
                if (currentState != "Searching")
                {
                    currentState = "Searching";
                    animator?.SetTrigger("Walk");
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
                {
                    currentState = "Patrol";
                    animator?.SetTrigger("Walk");
                }
                else
                {
                    currentState = "Idle";
                    animator?.SetTrigger("Idle");
                }
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
            
            // --- MUDANÇA 3: Lógica de Ataque ---
            if (Time.time > lastAttackTime + attackCooldown && targetPlayer != null)
            {
                lastAttackTime = Time.time;
                // Começa a sequência de ataque (pronta para animação)
                StartCoroutine(AttackSequence());
            }
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
            
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                StartCoroutine(WaitAtPatrolPoint());
            }
            else if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                FindNewPatrolPoint(); 
            }
        }
        else if (currentState == "Idle")
        {
            agent.SetDestination(transform.position); 
        }
    }
    
    // --- MUDANÇA 4: Nova Co-rotina de Ataque ---
    private IEnumerator AttackSequence()
    {
        // 1. Espera pelo "ponto de dano" da animação
        yield return new WaitForSeconds(attackAnimDelay);
        
        // 2. Verifica se o jogador ainda está ao alcance
        if (targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.position) <= agent.stoppingDistance + 0.5f)
        {
            // 3. Aplica o dano (o TargetMultiplayer no jogador vai tratar da rede)
            TargetMultiplayer playerHealth = targetPlayer.GetComponent<TargetMultiplayer>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamageServerRpc(enemyDamage);
            }
        }
    }

    private void FindNewPatrolPoint()
    {
        bool foundPoint = false;
        for (int i = 0; i < 30; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = startPosition + new Vector3(randomCircle.x, 0, randomCircle.y);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPos, out hit, 1.0f, NavMesh.AllAreas))
            {
                currentPatrolTarget = hit.position;
                foundPoint = true;
                break; 
            }
        }
        if (!foundPoint)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(startPosition, out hit, patrolRadius, NavMesh.AllAreas))
            {
                currentPatrolTarget = hit.position;
            }
            else
            {
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
            // --- MUDANÇA 5: Só ataca jogadores VIVOS ---
            if (client.PlayerObject != null)
            {
                // Verifica se o jogador está morto
                TargetMultiplayer playerHealth = client.PlayerObject.GetComponent<TargetMultiplayer>();
                if (playerHealth != null && playerHealth.IsDead.Value)
                {
                    continue; // Ignora este jogador
                }

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