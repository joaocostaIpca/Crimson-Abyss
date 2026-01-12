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

    [Header("Debug")]
    [SerializeField] private bool showDebugUI = true;

    [Header("Components")]
    private NavMeshAgent agent;
    private Rigidbody rb;
    private Animator animator;
    private GameObject fireballPrefab;

    // Variáveis de Estado Internas
    private float defaultAgentSpeed;
    private Coroutine reactionCoroutine;
    private string currentState = "Idle";
    
    // Variável para evitar spam de animações
    private string currentAnimState = "";

    // Memória da IA
    private Transform targetPlayer;
    private Vector3 lastKnownPosition;
    private bool hasLastKnownPosition = false;

    // Patrulha
    private Vector3 startPosition;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = false;
    private bool isWaitingAtPatrolPoint = false;

    // --- ARQUITETURA DE MODELO (O Cérebro) ---
    private AIDecisionModel aiModel;
    private AIContext currentContext;

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        fireballPrefab = Resources.Load("VFX/Fireball/Fireball") as GameObject;

        if (enemyType == EnemyType.Lancador && fireballPrefab == null)
            Debug.LogError($"{name}: Fireball prefab not found in Resources.");

        if (agent == null)
        {
            enabled = false;
            return;
        }

        defaultAgentSpeed = agent.speed;

        // Configuração específica do Lancador (Voador) vs Terrestre
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

        // Setup para testes offline (sem rede)
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (enemyType != EnemyType.Lancador && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }
            this.enabled = true;
            if (agent != null) agent.enabled = true;

            // Inicializa modelo offline
            aiModel = new AIDecisionModel();
            currentContext = new AIContext();

            SetPatrolMode(transform.position, patrolRadius);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // --- INICIALIZAÇÃO DO MODELO ---
            aiModel = new AIDecisionModel();
            currentContext = new AIContext();
            // ------------------------------

            if (agent != null) agent.enabled = true;

            // Garante que o agente está no NavMesh
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

        // Se for cliente, desliga a lógica (o ServerNetworkTransform trata da posição)
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
        // Segurança de Rede
        if (NetworkManager.Singleton != null && !IsServer) return;

        // Sensor de Jogadores (Corre periodicamente)
        if (reactionCoroutine == null)
        {
            reactionCoroutine = StartCoroutine(CheckPlayers());
        }

        // 1. RECOLHER DADOS (Contexto)
        UpdateContextData();

        // 2. PEDIR DECISÃO AO MODELO
        string nextState = aiModel.Evaluate(currentContext);

        // 3. APLICAR TRANSIÇÃO DE ESTADO
        if (nextState != currentState)
        {
            // Lógica específica: Se desistir de procurar, volta a patrulhar
            if (nextState == "Patrol" && currentState == "Searching")
            {
                hasLastKnownPosition = false;
                if (isPatrolling) FindNewPatrolPoint();
            }

            currentState = nextState;
            
            // Chama a função que sincroniza a animação quando o estado muda
            UpdateAnimationState(currentState);
        }

        // 4. EXECUTAR COMPORTAMENTO (Move, Ataca, etc)
        ExecuteCurrentState();
    }

    // Preenche o "Formulário" para o Modelo analisar
    private void UpdateContextData()
    {
        // A. Dados do Alvo
        currentContext.HasTarget = (targetPlayer != null);

        if (targetPlayer != null)
        {
            hasLastKnownPosition = true;
            lastKnownPosition = targetPlayer.position;
            currentContext.DistanceToTarget = Vector3.Distance(transform.position, targetPlayer.position);
        }
        else
        {
            currentContext.DistanceToTarget = 999f;
        }

        // B. Dados de Memória e Cooldown
        currentContext.HasLastKnownPos = hasLastKnownPosition;
        currentContext.IsAttackCooldownOver = (Time.time > lastAttackTime + attackCooldown);
        currentContext.AttackRange = agent.stoppingDistance + 0.5f;

        // C. Lógica de "Chegou ao Destino"
        bool arrived = false;
        if (currentState == "Searching")
        {
            if (enemyType == EnemyType.Lancador)
            {
                Vector3 dest = new Vector3(lastKnownPosition.x, (flightHeight != 0f ? flightHeight : startPosition.y), lastKnownPosition.z);
                if (Vector3.Distance(transform.position, dest) < 0.5f) arrived = true;
            }
            else
            {
                if (!agent.pathPending && agent.remainingDistance < 0.5f) arrived = true;
            }
        }
        currentContext.ArrivedAtDestination = arrived;
        currentContext.IsPatrolling = isPatrolling;
    }

    // ======================================================================================
    // SISTEMA DE ANIMAÇÃO SINCRONIZADA
    // ======================================================================================

    /// <summary>
    /// Chamado pelo Servidor quando o estado muda.
    /// Toca no servidor e manda para os clientes.
    /// </summary>
    private void UpdateAnimationState(string state)
    {
        // 1. Toca localmente no servidor
        PlayAnimationLocal(state);

        // 2. Manda para todos os clientes via RPC
        UpdateAnimationClientRpc(state);
    }

    [ClientRpc]
    private void UpdateAnimationClientRpc(string state)
    {
        // Se for o Host (Server+Client), já tocou acima, então ignora para não repetir
        if (IsServer) return; 
        
        PlayAnimationLocal(state);
    }

   /// <summary>
    /// A lógica real de tocar animação. É chamada tanto no Server como nos Clients.
    /// </summary>
    private void PlayAnimationLocal(string state)
    {
        if (animator == null) return;
        
        currentAnimState = state; // Guarda para o Debug

        // --- RESETAR ESTADOS ANTERIORES ---
        // Garante que não ficamos com o Bool "preso" se mudarmos para ataque
        if (state != "Walking" && state != "Searching" && state != "Patrol")
        {
            animator.SetBool("IsWalking", false);
        }

        switch (state)
        {
            case "Attack":
                // Ataque é uma ação única, usa Trigger
                animator.SetBool("IsWalking", false); // Para de andar para atacar
                animator.SetTrigger("Attack"); 
                break;

            case "Walking": 
            case "Searching": 
            case "Patrol": 
                // Estes estados implicam movimento, usa Bool = true
                animator.SetBool("IsWalking", true); 
                break;

            case "Idle": 
                // Parado, usa Bool = false
                animator.SetBool("IsWalking", false);
                break;
        }
    }

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
                
                // === AQUI ESTÁ A CORREÇÃO ===
                // Força a animação de ataque a tocar novamente a cada golpe
                // Mesmo que o estado já seja "Attack"
                UpdateAnimationState("Attack"); 
                // ============================
                
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

    // --- MÉTODOS AUXILIARES ---

    private IEnumerator AttackSequence()
    {
        yield return new WaitForSeconds(attackAnimDelay);

        if (enemyType != EnemyType.Lancador)
        {
            if (targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.position) <= agent.stoppingDistance + 0.5f)
            {
                TargetMultiplayer playerHealth = targetPlayer.GetComponent<TargetMultiplayer>();
                if (playerHealth != null)
                {
                    playerHealth.TakeDamageServerRpc(enemyDamage);
                }
            }
        }
        else
        {
            GameObject fireball = Instantiate(fireballPrefab, transform.position, Quaternion.identity);
            NetworkObject netObj = fireball.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsSpawned) netObj.Spawn();

            Vector3 targetPos = targetPlayer.position + Vector3.up * 1.0f;
            Vector3 direction = (targetPos - fireball.transform.position).normalized;
            Rigidbody fbRb = fireball.GetComponent<Rigidbody>();
            if (fbRb != null)
            {
                fbRb.AddForce(direction * 15f, ForceMode.VelocityChange);
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

        NavMeshPath path = new NavMeshPath();
        bool foundPoint = false;

        for (int attempts = 0; attempts < 5 && !foundPoint; attempts++)
        {
            Vector2 randomCirclePoint = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = startPosition + new Vector3(randomCirclePoint.x, 0, randomCirclePoint.y);

            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    currentPatrolTarget = hit.position;
                    foundPoint = true;
                }
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

        if (NetworkManager.Singleton != null)
        {
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

    // ======================================================================================
    // DEBUG VISUAL - SÓ APARECE NO EDITOR/BUILD SE showDebugUI = true
    // ======================================================================================
    private void OnGUI()
    {
        if (!showDebugUI) return;

        // Converte a posição do inimigo (acima da cabeça) para o ecrã
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.5f);

        // Se o inimigo estiver atrás da câmara, não desenha
        if (screenPos.z < 0) return;

        // Inverte o Y porque o GUI tem coordenadas invertidas
        screenPos.y = Screen.height - screenPos.y;

        // Cria uma caixa simples
        GUI.Box(new Rect(screenPos.x - 75, screenPos.y, 150, 70), "AI Debug: " + enemyType);
        
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.MiddleCenter;

        GUI.Label(new Rect(screenPos.x - 75, screenPos.y + 20, 150, 20), $"State: {currentState}", style);
        GUI.Label(new Rect(screenPos.x - 75, screenPos.y + 40, 150, 20), $"Anim: {currentAnimState}", style);
    }
}

public class AIContext
{
    public bool HasTarget;
    public float DistanceToTarget;
    public bool HasLastKnownPos;
    public bool ArrivedAtDestination;
    public bool IsAttackCooldownOver;
    public float AttackRange;
    public bool IsPatrolling;
}

public class AIDecisionModel
{
    // Avalia os dados e devolve o nome do Estado Vencedor
    public string Evaluate(AIContext context)
    {
        // 1. Calcular Utilidade (Score) para cada ação possível
        float scoreAttack = CalculateAttackScore(context);
        float scoreChase = CalculateChaseScore(context); // Walking
        float scoreSearch = CalculateSearchScore(context);
        float scorePatrol = 0.15f; // Valor base (fallback)

        // 2. O maior score ganha
        if (scoreAttack > scoreChase && scoreAttack > scoreSearch && scoreAttack > scorePatrol)
            return "Attack";

        if (scoreChase > scoreAttack && scoreChase > scoreSearch && scoreChase > scorePatrol)
            return "Walking"; // Perseguir

        if (scoreSearch > scoreAttack && scoreSearch > scoreChase && scoreSearch > scorePatrol)
            return "Searching"; // Investigar

        if (context.IsPatrolling)
            return "Patrol";

        return "Idle";
    }

    private float CalculateAttackScore(AIContext ctx)
    {
        // Regra: Só ataca se tiver alvo
        if (!ctx.HasTarget) return 0f;

        // Regra: Se estiver dentro do alcance de ataque
        if (ctx.DistanceToTarget <= ctx.AttackRange)
        {
            // Se tiver cooldown, é prioridade máxima (1.0), se não, ainda quer atacar mas menos (0.7)
            return ctx.IsAttackCooldownOver ? 1.0f : 0.7f;
        }

        return 0f;
    }

    private float CalculateChaseScore(AIContext ctx)
    {
        // Regra: Persegue se vê o alvo, mas está longe
        if (ctx.HasTarget && ctx.DistanceToTarget > ctx.AttackRange)
        {
            return 0.9f; // Prioridade alta, mas menor que atacar com cooldown
        }
        return 0f;
    }

    private float CalculateSearchScore(AIContext ctx)
    {
        // Regra: Não vê o alvo, mas lembra-se onde ele estava
        if (!ctx.HasTarget && ctx.HasLastKnownPos)
        {
            // Se ainda não chegou ao ponto de investigação
            if (!ctx.ArrivedAtDestination)
            {
                return 0.8f;
            }
        }
        return 0f;
    }
}