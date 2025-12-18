using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(TargetMultiplayer))]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    public enum EnemyType { Diabrete, Lancador, Bruto }

    #region variables

    [Header("IA Settings")]
    [SerializeField] int reactionDelay = 500;
    [SerializeField] float minimumDistance = 30f;
    [SerializeField] private float flightHeight = 0f;
    [SerializeField] private EnemyType enemyType = EnemyType.Diabrete; 

    [Header("Attack Settings")]
    [SerializeField] private float enemyDamage = 10f;
    [SerializeField] private float attackCooldown = 2f; 
    [SerializeField] private float attackAnimDelay = 0.5f; 
    private float lastAttackTime = 0f;

    [Header("Patrol Settings")]
    [SerializeField] private float patrolSpeedMultiplier = 0.5f;
    [SerializeField] private float patrolWaitTime = 2f;
    [SerializeField] private float patrolRadius = 15f;

    [Header("Flight Settings (Lancador)")]
    [SerializeField] private float flightSpeed = 4f;
    [SerializeField] private float flightRotateSpeed = 5f;
    [SerializeField] private float flightArrivalDistance = 0.5f;

    private NavMeshAgent agent;
    private Rigidbody rb;
    private float defaultAgentSpeed;

    private Coroutine reactionCoroutine;
    
    
    private string currentState = "Idle"; // O estado atual da FSM

    private Transform targetPlayer;
    private Vector3 lastKnownPosition;
    private bool hasLastKnownPosition = false;

    private Vector3 startPosition;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = false;
    private bool isWaitingAtPatrolPoint = false;

    private Animator animator;

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();

        if (agent == null)
        {
            enabled = false;
            return;
        }

        defaultAgentSpeed = agent.speed;

        if (enemyType == EnemyType.Lancador)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            if (flightHeight != 0f) agent.baseOffset = flightHeight;
        }
        else
        {
            agent.updatePosition = true;
            agent.updateRotation = true;
        }

        //Setup para testes offline 
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (enemyType != EnemyType.Lancador && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }
            this.enabled = true;
            if (agent != null) agent.enabled = true;
            SetPatrolMode(transform.position, patrolRadius);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            if (agent != null) agent.enabled = true;

            if (enemyType != EnemyType.Lancador)
            {
                if (!agent.isOnNavMesh)
                {
                    if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                        agent.Warp(hit.position);
                    else
                    {
                        agent.enabled = false;
                        return;
                    }
                }
            }
            else
            {
                if (flightHeight != 0f) agent.baseOffset = flightHeight;
            }

            SetPatrolMode(transform.position, patrolRadius);
            return;
        }

        this.enabled = false;
        if (agent != null) agent.enabled = false;
    }

    public void SetPatrolMode(Vector3 startPos, float radius)
    {
        if (enemyType == EnemyType.Lancador)
        {
            startPosition = startPos;
        }
        else
        {
            Vector3 navStart = startPos;
            if (agent != null && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(startPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    navStart = hit.position;
                    agent.Warp(hit.position);
                }
                else return;
            }
            startPosition = navStart;
        }

        patrolRadius = radius;
        isPatrolling = true;
        currentState = "Patrol";
        FindNewPatrolPoint();
    }

    private void Update()
    {
        if (NetworkManager.Singleton != null && !IsServer) return;

        if (reactionCoroutine == null)
        {
            reactionCoroutine = StartCoroutine(CheckPlayers());
        }

        // A Árvore decide qual deve ser o estado
        string nextState = RunDecisionTree();

        
        if (nextState != currentState)
        {
            currentState = nextState;
            UpdateAnimationState(currentState);
        }

      
        ExecuteCurrentState();
    }

    private void UpdateAnimationState(string state)
    {
        if (animator == null) return;
        
        switch (state)
        {
            case "Attack": animator.SetTrigger("Attack"); break;
            case "Walking": animator.SetTrigger("Walk"); break;
            case "Searching": animator.SetTrigger("Walk"); break;
            case "Patrol": animator.SetTrigger("Walk"); break;
            case "Idle": animator.SetTrigger("Idle"); break;
        }
    }

    // ========================================================================================
    // --- ÁRVORE DE DECISÃO 
    // Estrutura hierárquica: Cada método é um "Nó" que faz uma pergunta.
    // ========================================================================================

    private string RunDecisionTree()
    {
        return Node_HasTarget();
    }

    // Nó 1: Tenho um alvo vivo e visível?
    private string Node_HasTarget()
    {
        if (targetPlayer != null)
        {
            // Sim -> Passa para o próximo nó de decisão
            return Node_IsTargetInAttackRange();
        }
        else
        {
            // Não -> Vamos verificar se temos uma última posição conhecida
            return Node_HasLastKnownPosition();
        }
    }

    // Nó 2: O alvo está perto o suficiente para atacar?
    private string Node_IsTargetInAttackRange()
    {
        // Atualiza LKP já que estamos a ver o alvo
        hasLastKnownPosition = true;
        lastKnownPosition = targetPlayer.position;

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        
        if (distance <= agent.stoppingDistance + 0.5f)
        {
            return "Attack"; // FOLHA DA ÁRVORE (Resultado Final)
        }
        else
        {
            return "Walking"; // FOLHA DA ÁRVORE (Resultado Final - Perseguir)
        }
    }

    // Nó 3: Tenho uma memória de onde o jogador estava?
    private string Node_HasLastKnownPosition()
    {
        if (hasLastKnownPosition)
        {
            return Node_ArrivedAtLastKnownPosition();
        }
        else
        {
            return Node_IsPatrolMode();
        }
    }

    // Nó 4: Já cheguei ao local que estou a investigar?
    private string Node_ArrivedAtLastKnownPosition()
    {
        // Lógica de chegada (funciona para voador e terrestre)
        bool arrived = false;

        if (enemyType == EnemyType.Lancador)
        {
            Vector3 searchTarget = new Vector3(lastKnownPosition.x, (flightHeight != 0f ? flightHeight : startPosition.y), lastKnownPosition.z);
            if (Vector3.Distance(transform.position, searchTarget) < 0.5f) arrived = true;
        }
        else
        {
            if (!agent.pathPending && agent.remainingDistance < 0.5f) arrived = true;
        }

        if (arrived)
        {
            hasLastKnownPosition = false; // Esquecer posição
            if (isPatrolling) FindNewPatrolPoint();
            return Node_IsPatrolMode(); // Reavaliar
        }
        else
        {
            return "Searching"; // FOLHA (Continuar a investigar)
        }
    }

    // Nó 5: Estou em modo patrulha?
    private string Node_IsPatrolMode()
    {
        if (isPatrolling)
        {
            return "Patrol"; // FOLHA
        }
        else
        {
            return "Idle"; // FOLHA (Default)
        }
    }

    // ========================================================================================
    // --- FIM DA ÁRVORE DE DECISÃO ---
    // ========================================================================================

    private void ExecuteCurrentState()
    {
        if (currentState == "Walking" && targetPlayer != null)
        {
            if (enemyType == EnemyType.Lancador)
            {
                Vector3 flyTarget = new Vector3(targetPlayer.position.x, (flightHeight != 0f ? flightHeight : startPosition.y), targetPlayer.position.z);
                float speed = flightSpeed > 0f ? flightSpeed : defaultAgentSpeed;
                MoveLancadorTowards(flyTarget, speed);
            }
            else
            {
                agent.speed = defaultAgentSpeed;
                agent.SetDestination(targetPlayer.position);
            }
        }
        else if (currentState == "Attack")
        {
            if (enemyType != EnemyType.Lancador)
                agent.SetDestination(transform.position); // Parar para atacar

            if (Time.time > lastAttackTime + attackCooldown && targetPlayer != null)
            {
                lastAttackTime = Time.time;
                StartCoroutine(AttackSequence());
            }
        }
        else if (currentState == "Searching")
        {
            if (enemyType == EnemyType.Lancador)
            {
                Vector3 searchTarget = new Vector3(lastKnownPosition.x, (flightHeight != 0f ? flightHeight : startPosition.y), lastKnownPosition.z);
                float speed = flightSpeed > 0f ? flightSpeed : defaultAgentSpeed;
                MoveLancadorTowards(searchTarget, speed);
            }
            else
            {
                agent.speed = defaultAgentSpeed;
                agent.SetDestination(lastKnownPosition);
            }
        }
        else if (currentState == "Patrol" && !isWaitingAtPatrolPoint)
        {
            if (enemyType == EnemyType.Lancador)
            {
                float speed = flightSpeed > 0f ? flightSpeed : defaultAgentSpeed * patrolSpeedMultiplier;
                MoveLancadorTowards(currentPatrolTarget, speed);

                if (Vector3.Distance(transform.position, currentPatrolTarget) <= flightArrivalDistance)
                {
                    StartCoroutine(WaitAtPatrolPoint());
                }
            }
            else
            {
                agent.speed = defaultAgentSpeed * patrolSpeedMultiplier;
                if (!agent.isOnNavMesh) return;
                   
                agent.SetDestination(currentPatrolTarget);

                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                    StartCoroutine(WaitAtPatrolPoint());
                else if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial)
                    FindNewPatrolPoint();
            }
        }
        else if (currentState == "Idle")
        {
            if (enemyType != EnemyType.Lancador)
                agent.SetDestination(transform.position);
        }
    }

    private IEnumerator AttackSequence()
    {
        yield return new WaitForSeconds(attackAnimDelay);

        if (targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.position) <= agent.stoppingDistance + 0.5f)
        {
            TargetMultiplayer playerHealth = targetPlayer.GetComponent<TargetMultiplayer>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamageServerRpc(enemyDamage);
            }
        }
    }

    private void FindNewPatrolPoint()
    {
        if (agent == null) return;

        if (enemyType == EnemyType.Lancador)
        {
            Vector2 randomCirclePoint = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = startPosition + new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y);
            float targetY = (flightHeight != 0f) ? flightHeight : startPosition.y;
            randomPos.y = targetY;
            currentPatrolTarget = randomPos;
            return;
        }

        bool foundPoint = false;
        NavMeshPath path = new NavMeshPath();

        for (int attempts = 0; attempts < 8 && !foundPoint; attempts++)
        {
            Vector2 randomCirclePoint = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = startPosition + new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y);

            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    currentPatrolTarget = hit.position;
                    foundPoint = true;
                    break;
                }
            }
        }

        if (!foundPoint)
        {
            if (NavMesh.SamplePosition(startPosition, out NavMeshHit hit2, patrolRadius, NavMesh.AllAreas) &&
               agent.CalculatePath(hit2.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                currentPatrolTarget = hit2.position;
                foundPoint = true;
            }
        }

        if (!foundPoint)
        {
            currentPatrolTarget = transform.position;
            isPatrolling = false;
            currentState = "Idle"; 
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
                TargetMultiplayer playerHealth = client.PlayerObject.GetComponent<TargetMultiplayer>();
                if (playerHealth != null && playerHealth.IsDead.Value) continue;

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

    private void MoveLancadorTowards(Vector3 target, float speed)
    {
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude <= 0.000001f) return;

        Vector3 move = dir.normalized * speed * Time.deltaTime;
        if (move.sqrMagnitude > dir.sqrMagnitude) move = dir;
        transform.position += move;

        Quaternion desired = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * flightRotateSpeed);
    }
}