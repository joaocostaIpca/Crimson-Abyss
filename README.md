# Crimson-Abyss
A game created by  Team Brick
<img width="1536" height="1024" alt="raw" src="https://github.com/user-attachments/assets/bff714b5-3231-4949-b4ce-7179e2c54c21" />

(Imagem Temporaria)

O Jogo é um FPS multiplayer Local inspirado por jogos como o Left 4 Dead 2 e Warhammer 40k Space Marine 2
A história segue um esquadrão de soldados que cai no inferno e lutam o seu caminho até sair, composto por uma freira, um templario e dois soldados, um comandante e um especialista de armas.

Sistema de Inteligência Artificial e Navegação

Este projeto implementa uma arquitetura de IA híbrida, desenhada para ambientes Multiplayer, combinando a estrutura lógica de uma Árvore de Decisões com a execução robusta de uma Máquina de Estados (FSM).

Arquitetura: Árvore de Decisões (O "Cérebro")

A lógica de decisão não utiliza if/else encadeados aleatoriamente.
Em vez disso, é utilizada uma Árvore de Decisões estruturada.

A cada frame, o NPC avalia uma série de perguntas (Nós) para determinar a sua intenção.

Fluxo Lógico

O método principal RunDecisionTree() inicia a avaliação da árvore.

Diagrama de Decisão (Mermaid)
graph TD
    A[Início: RunDecisionTree] --> B{Tem Alvo?}
    B -- Sim --> C{Está no Alcance?}
    B -- Não --> D{Tem Memória da Posição?}
    C -- Sim --> E[RESULTADO: ATACAR]
    C -- Não --> F[RESULTADO: PERSEGUIR]
    D -- Sim --> G[RESULTADO: INVESTIGAR]
    D -- Não --> H[RESULTADO: PATRULHAR]

Implementação no Código

Cada Nó da árvore é um método que retorna:

Outro nó (continuação da decisão)

Ou um estado final (folha)

// Exemplo do Nó Raiz da Árvore
private string Node_HasTarget()
{
    if (targetPlayer != null)
    {
        // Se tem alvo, pergunta: "Estou perto para atacar?"
        return Node_IsTargetInAttackRange(); 
    }
    else
    {
        // Se não tem alvo, pergunta: "Lembro-me onde ele estava?"
        return Node_HasLastKnownPosition(); 
    }
}

Execução: Máquina de Estados (O "Corpo")

Enquanto a Árvore de Decisões decide o que fazer,
a Máquina de Estados (FSM) decide como fazer.

O estado resultante é responsável por:

Execução de animações

Movimento

Física

Cooldowns

Lógica contínua do comportamento

Existe uma separação clara entre Decisão e Execução.

private void Update()
{
    // 1. O Cérebro decide
    string nextState = RunDecisionTree();

    // 2. O Corpo executa
    if (nextState != currentState)
    {
        currentState = nextState;
        UpdateAnimationState(currentState);
    }

    ExecuteCurrentState();
}

Pathfinding e Algoritmos de Navegação

O sistema de movimento utiliza Polimorfismo de Comportamento para diferenciar inimigos terrestres de inimigos voadores.

Inimigos Terrestres (NavMesh / A*)

Os inimigos terrestres utilizam o algoritmo A* através do sistema de NavMesh da Unity, permitindo:

Cálculo de rotas complexas

Evitar obstáculos estáticos

Navegação eficiente em ambientes dinâmicos

Lógica Principal
agent.SetDestination(target);

Patrulha com Validação de NavMesh

Antes de se mover, o inimigo valida se o ponto aleatório é alcançável no grafo de navegação.

if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
{
    agent.CalculatePath(hit.position, path); // Calcula rota A* válida
}

Inimigos Voadores (Movimento Vetorial)

Os inimigos voadores ignoram completamente o NavMesh.
Utilizam movimento vetorial direto em 3D, mantendo uma altura constante de voo (flightHeight).

Características do Movimento

Movimento linear direto

Rotação suave para o alvo

Independente da topologia do terreno

private void MoveLancadorTowards(Vector3 target, float speed)
{
    Vector3 dir = target - transform.position;
    
    // Movimento linear
    transform.position += dir.normalized * speed * Time.deltaTime;

    // Rotação suave para olhar para o alvo
    Quaternion desired = Quaternion.LookRotation(dir.normalized);
    transform.rotation = Quaternion.Slerp(
        transform.rotation,
        desired,
        Time.deltaTime * flightRotateSpeed
    );
}

