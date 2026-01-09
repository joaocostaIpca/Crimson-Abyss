# Crimson-Abyss
A game created by  Team Brick
<img width="1536" height="1024" alt="raw" src="https://github.com/user-attachments/assets/bff714b5-3231-4949-b4ce-7179e2c54c21" />



O Jogo é um FPS multiplayer Local inspirado por jogos como o Left 4 Dead 2 e Warhammer 40k Space Marine 2
A história segue um esquadrão de soldados que cai no inferno e lutam o seu caminho até sair, composto por uma freira, um templario e dois soldados, um comandante e um especialista de armas.

Este projeto implementa uma arquitetura de IA moderna e modular, desenhada para ambientes Multiplayer. O sistema abandona os if/else tradicionais em favor de uma abordagem baseada em Utility AI (IA Baseada em Utilidade) combinada com uma Máquina de Estados (FSM).

<h2> 1. O Cérebro: Modelo de Utilidade (Utility AI) </h2>
Em vez de seguir uma árvore fixa, o inimigo "pensa" em termos de utilidade. A cada frame, o sistema avalia o contexto do jogo e atribui uma pontuação (Score) a cada ação possível. A ação com a pontuação mais alta é a escolhida.

Isto permite comportamentos mais orgânicos e dinâmicos.

<h2>Fluxo de Dados (Data Flow)</h2>

<h3>Contexto (AIContext)</h3>

O sistema recolhe dados brutos do mundo (distância ao jogador, se tem linha de visão, cooldowns, última posição conhecida).

<h3>Modelo (AIDecisionModel)</h3>

Uma classe matemática pura processa esses dados e calcula pontuações de 0.0 a 1.0 para cada estado.

<h3>Decisão</h3>

O estado vencedor é enviado para execução. </br></br>



<h2>Diagrama Lógico (Mermaid)</h2>

  <img width="5848" height="1730" alt="mermaid-ai-diagram-2026-01-09-145554" src="https://github.com/user-attachments/assets/b85e73c4-619d-407b-b31a-7d013c0cac62" />

    
<h3>Implementação no Código:</h3>
O modelo avalia fatores como distância e cooldowns para gerar o score. Exemplo da lógica de pontuação de ataque:



    // Exemplo: Cálculo de Utilidade de Ataque
    private float GetAttackScore(AIContext ctx)
    {
    // Se não vê o alvo, a utilidade é 0
    if (!ctx.HasTarget) return 0f;

    // Se está perto o suficiente
    if (ctx.DistanceToTarget <= ctx.AttackRange)
    {
        // Se o ataque está pronto (sem cooldown), prioridade máxima (1.0)
        // Se ainda tem cooldown, a vontade é menor (0.7), mas ainda quer estar perto
        return ctx.IsAttackCooldownOver ? 1.0f : 0.7f;
    }
    
    return 0f;
    }

<h2>2. O Corpo: Máquina de Estados (FSM)</h2>
Enquanto o Modelo decide "O Quê" fazer, a Máquina de Estados decide "Como" fazer. O EnemyAI atua como o executor, aplicando o movimento, animações e física correspondentes à decisão do modelo.

Esta separação (Decoupling) facilita a manutenção e testes.


    private void Update()
    {
    // 1. O Cérebro decide com base no contexto
    string nextState = aiModel.Evaluate(currentContext);

    // 2. O Corpo executa a transição se necessário
    if (nextState != currentState)
    {
        currentState = nextState;
        UpdateAnimationState(currentState);
    }

    // 3. Execução do comportamento contínuo
    ExecuteCurrentState();
    }
    
## Pathfinding e Algoritmos de Navegação
O sistema de movimento utiliza Polimorfismo de Comportamento para diferenciar inimigos terrestres de inimigos voadores, garantindo que cada tipo de inimigo navega no ambiente da forma mais eficiente.

### Inimigos Terrestres (NavMesh / A*)
Os inimigos terrestres (ex: Diabrete, Bruto) utilizam o algoritmo A* através do sistema de NavMesh da Unity.

Capacidades: Cálculo de rotas complexas, evitamento de obstáculos estáticos e navegação em terreno irregular.

Validação: Antes de iniciar uma patrulha, o sistema valida se o ponto aleatório é alcançável no grafo de navegação.



    // Validação de ponto de patrulha no NavMesh
    if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
    {
    // Apenas move se o caminho for válido e completo
    if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
    {
        currentPatrolTarget = hit.position;
    }
    }
    
### Inimigos Voadores (Movimento Vetorial)
Os inimigos voadores (ex: Lançador) ignoram o NavMesh para movimento, utilizando álgebra vetorial direta em 3D. Isto permite-lhes flutuar sobre obstáculos e manter uma altura constante (flightHeight).

Movimento: Linear direto em direção ao alvo.

Rotação: Interpolação esférica (Slerp) para rotações suaves.



    private void MoveLancadorTowards(Vector3 target, float speed)
    {
    Vector3 dir = target - transform.position;
    
    // Movimento direto ignorando o chão
    transform.position += dir.normalized * speed * Time.deltaTime;

    // Rotação suave para olhar para o alvo
    Quaternion desired = Quaternion.LookRotation(dir.normalized);
    transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * flightRotateSpeed);
    }
