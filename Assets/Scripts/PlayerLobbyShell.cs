using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI; // Para os botões
using TMPro; // Para o texto

// Este script vai no teu PREFAB de "Player" do lobby (o manequim)
public class PlayerLobbyShell : NetworkBehaviour
{
    // Variável de rede que guarda a escolha (-1 = Nenhuma)
    public NetworkVariable<int> SelectedCharacterIndex = new NetworkVariable<int>(-1);

    // Variáveis que só o dono vai ligar
    private Button btnFreira, btnComandante, btnTemplario, btnFuzileiro;
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Se este objeto for o "nosso", encontramos os botões da UI
        if (IsOwner)
        {
            FindAndLinkUI();
        }

        // Todos (incluindo o host) subscrevem à mudança
        // para atualizar a sua própria "NetworkVariable" (embora não a usem)
        SelectedCharacterIndex.OnValueChanged += (int oldVal, int newVal) =>
        {
            // Poderíamos pôr lógica de UI aqui, mas o LobbyManager vai tratar disso
        };
    }

    // (Dentro do script PlayerLobbyShell.cs)

    private void FindAndLinkUI()
    {
        // Encontra os botões no Canvas do LobbyManager (que é DontDestroyOnLoad)
        try
        {
            // --- MUDANÇA AQUI: Adicionámos os 2 que faltavam ---
            btnFreira = GameObject.Find("Button_Pick_Freira").GetComponent<Button>();
            btnComandante = GameObject.Find("Button_Pick_Comandante").GetComponent<Button>();
            btnTemplario = GameObject.Find("Button_Pick_Templario").GetComponent<Button>();
            btnFuzileiro = GameObject.Find("Button_Pick_Fuzileiro").GetComponent<Button>();

            // Liga os botões às funções
            // --- MUDANÇA AQUI: Adicionámos os 2 que faltavam ---
            btnFreira.onClick.AddListener(() => RequestCharacterLockServerRpc(0));
            btnComandante.onClick.AddListener(() => RequestCharacterLockServerRpc(1));
            btnTemplario.onClick.AddListener(() => RequestCharacterLockServerRpc(2));
            btnFuzileiro.onClick.AddListener(() => RequestCharacterLockServerRpc(3));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerLobbyShell] Não consegui encontrar os botões da UI. " +
                           "Garante que os nomes na Hierarquia estão EXATOS (ex: 'Button_Pick_Freira'). " +
                           $"{e.Message}");
        }
    }

    // O Cliente (dono) chama esta função, que corre no Servidor
    [ServerRpc]
    public void RequestCharacterLockServerRpc(int charIndex)
    {
        // Pede ao LobbyManager (que é o "cérebro") para tentar "trancar" esta personagem
        bool success = LobbyManager.Instance.TryLockCharacter(charIndex, OwnerClientId);

        if (success)
        {
            // Se conseguimos, atualizamos a nossa própria variável
            SelectedCharacterIndex.Value = charIndex;
        }
        
        // Se falhou (porque alguém já pegou), não fazemos nada.
        // O LobbyManager vai atualizar a UI de todos.
    }
}