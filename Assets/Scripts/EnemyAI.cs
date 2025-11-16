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
    [SerializeField] private EnemyType enemyType = EnemyType.Diabrete; // Diabrete, Lancador, Bruto

    // --- MUDANÇA 1: Variáveis de Ataque ---
    [Header("Attack Settings")]
    [SerializeField] private float enemyDamage = 10f;
    [SerializeField] private float attackCooldown = 2f; // Só ataca a cada 2s
    [SerializeField] private float attackAnimDelay = 0.5f; // Placeholder para a animação
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
    private string currentState = "Idle";
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
            Debug.LogError($"{name}: NavMeshAgent missing — disabling EnemyAI.");
            enabled = false;
            return;
        }

        defaultAgentSpeed = agent.speed;

        // For flying enemies we will drive transform manually (disable agent's transform updates).
        if (enemyType == EnemyType.Lancador)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            if (flightHeight != 0f)
                agent.baseOffset = flightHeight;
        }
        else
        {
            agent.updatePosition = true;
            agent.updateRotation = true;
        }

        // If there's no NetworkManager or Netcode isn't listening, treat as test mode and start patrol directly.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            // Only warp to NavMesh for ground agents
            if (enemyType != EnemyType.Lancador && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    Debug.Log($"{name}: warped to NavMesh at {hit.position} (Awake test-mode).");
                }
                else
                {
                    Debug.LogWarning($"{name}: no NavMesh near spawn position (Awake test-mode). Patrol may not start.");
                }
            }

            // Ensure agent and this script are enabled for local testing.
            this.enabled = true;
            if (agent != null) agent.enabled = true;
            SetPatrolMode(transform.position, patrolRadius);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"{name}: agent.isOnNavMesh={agent.isOnNavMesh} enabled={agent.enabled} speed={agent.speed} updatePosition={agent.updatePosition} baseOffset={agent.baseOffset}");

        // If we're running under Netcode the server is authoritative for AI movement.
        if (IsServer)
        {
            if (agent != null) agent.enabled = true;

            // Skip NavMesh placement for flying enemies (they don't need to sit on the NavMesh).
            if (enemyType != EnemyType.Lancador)
            {
                if (!agent.isOnNavMesh)
                {
                    if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                        Debug.Log($"{name}: warped to NavMesh at {hit.position} (OnNetworkSpawn server).");
                    }
                    else
                    {
                        Debug.LogWarning($"{name}: no NavMesh under spawn position (OnNetworkSpawn). Agent disabled.");
                        agent.enabled = false;
                        return;
                    }
                }
            }
            else
            {
                // Keep baseOffset for visual height if specified
                if (flightHeight != 0f) agent.baseOffset = flightHeight;
            }

            // start patrol on the server
            SetPatrolMode(transform.position, patrolRadius);
            return;
        }

        // For clients disable server-only logic
        this.enabled = false;
        if (agent != null) agent.enabled = false;
    }

    public void SetPatrolMode(Vector3 startPos, float radius)
    {
        Debug.Log($"Setting patrol mode for {name}.");

        // For flying enemies use the provided startPos directly (no NavMesh sampling)
        if (enemyType == EnemyType.Lancador)
        {
            startPosition = startPos;
        }
        else
        {
            // Make sure we are on the NavMesh and use the sampled nav position as start
            Vector3 navStart = startPos;
            if (agent != null && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(startPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    navStart = hit.position;
                    agent.Warp(hit.position);
                    Debug.Log($"{name}: warped to NavMesh at {hit.position} (SetPatrolMode).");
                }
                else
                {
                    Debug.LogWarning($"{name}: cannot set patrol mode, no NavMesh near startPos.");
                    return;
                }
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
        // If Netcode is present, only the server should run AI.
        // If Netcode is NOT present (single‑player / test mode), allow Update to run.
        if (NetworkManager.Singleton != null && !IsServer) return;

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

            // Attack/walking decision uses direct distance (works for flying too)
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
                    // Arrival check: use agent.remainingDistance for ground agents, distance check for flying
                    if (enemyType == EnemyType.Lancador)
                    {
                        Vector3 searchTarget = new Vector3(lastKnownPosition.x, (flightHeight != 0f ? flightHeight : startPosition.y), lastKnownPosition.z);
                        if (Vector3.Distance(transform.position, searchTarget) < 0.5f)
                        {
                            hasLastKnownPosition = false;
                            if (isPatrolling) FindNewPatrolPoint();
                        }
                    }
                    else
                    {
                        if (!agent.pathPending && agent.remainingDistance < 0.5f)
                        {
                            hasLastKnownPosition = false;
                            if (isPatrolling) FindNewPatrolPoint();
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
            // move toward the player
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
                agent.SetDestination(transform.position);
            // --- Attack timing logic unchanged ---
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
                // Flying movement ignores NavMesh topology — simple direct movement in 3D
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
                if (!agent.isOnNavMesh)
                    Debug.LogWarning($"{name}: agent not on NavMesh when trying to patrol.");
                else
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
        if (agent == null) return;

        // Flying enemy: pick a random world-space point inside the circle and set Y to flightHeight
        if (enemyType == EnemyType.Lancador)
        {
            Vector2 randomCirclePoint = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = startPosition + new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y);
            float targetY = (flightHeight != 0f) ? flightHeight : startPosition.y;
            randomPos.y = targetY;
            currentPatrolTarget = randomPos;
            return;
        }

        // Ground agents: pathfind on NavMesh as before
        bool foundPoint = false;
        NavMeshPath path = new NavMeshPath();

        // Try a few random samples to find a valid path
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
            Debug.Log($"No Path found for {name}. Stop patrolling.");
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
            // --- Só ataca jogadores VIVOS ---
            if (client.PlayerObject != null)
            {
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

    // ----- New helper for flying movement -----
    private void MoveLancadorTowards(Vector3 target, float speed)
    {
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude <= 0.000001f) return;

        // Move
        Vector3 move = dir.normalized * speed * Time.deltaTime;
        if (move.sqrMagnitude > dir.sqrMagnitude) move = dir;
        transform.position += move;

        // Rotate smoothly towards movement direction
        Quaternion desired = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * flightRotateSpeed);
    }
}