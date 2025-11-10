using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI; 
using TMPro; 

public class PlayerLobbyShell : NetworkBehaviour
{
    public NetworkVariable<int> SelectedCharacterIndex = new NetworkVariable<int>(-1);

    private Button btnFreira, btnComandante, btnTemplario, btnFuzileiro;
    
    // --- MUDANÇA 1: Referência para a Imagem ---
    private Image previewImage;
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            // Se formos o dono, encontramos a UI
            FindAndLinkUI();
            
            // E atualizamos a imagem para a nossa seleção (ou nenhuma)
            UpdatePreviewImage(SelectedCharacterIndex.Value);
        }

        // --- MUDANÇA 2: Subscrever à nossa própria seleção ---
        SelectedCharacterIndex.OnValueChanged += (int oldVal, int newVal) =>
        {
            // Se formos o dono, atualiza a imagem
            if (IsOwner)
            {
                UpdatePreviewImage(newVal);
            }
        };
    }

    private void FindAndLinkUI()
    {
        try
        {
            // --- Botões ---
            btnFreira = GameObject.Find("Button_Pick_Freira").GetComponent<Button>();
            btnComandante = GameObject.Find("Button_Pick_Comandante").GetComponent<Button>();
            btnTemplario = GameObject.Find("Button_Pick_Templario").GetComponent<Button>();
            btnFuzileiro = GameObject.Find("Button_Pick_Fuzileiro").GetComponent<Button>();

            btnFreira.onClick.AddListener(() => RequestCharacterLockServerRpc(0));
            btnComandante.onClick.AddListener(() => RequestCharacterLockServerRpc(1));
            btnTemplario.onClick.AddListener(() => RequestCharacterLockServerRpc(2));
            btnFuzileiro.onClick.AddListener(() => RequestCharacterLockServerRpc(3));
            
            // --- MUDANÇA 3: Encontrar a Imagem ---
            previewImage = GameObject.Find("Image_Preview").GetComponent<Image>();
            
            // Esconde a imagem (até escolhermos um)
            if (SelectedCharacterIndex.Value == -1)
            {
                previewImage.enabled = false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerLobbyShell] Não consegui encontrar os botões/imagem da UI. {e.Message}");
        }
    }
    
    // --- MUDANÇA 4: Nova função para atualizar a imagem local ---
    private void UpdatePreviewImage(int charIndex)
    {
        if (previewImage == null) return; 

        if (charIndex >= 0 && charIndex < LobbyManager.CharacterPreviews.Count)
        {
            previewImage.sprite = LobbyManager.CharacterPreviews[charIndex];
            previewImage.enabled = true;
        }
        else
        {
            previewImage.enabled = false; // Esconde se não tivermos seleção
        }
    }

    [ServerRpc]
    public void RequestCharacterLockServerRpc(int charIndex)
    {
        bool success = LobbyManager.Instance.TryLockCharacter(charIndex, OwnerClientId);

        if (success)
        {
            SelectedCharacterIndex.Value = charIndex;
        }
    }
}