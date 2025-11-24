using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;


public static class GameManagerHelper
{
    // Retorna o NÚMERO de jogadores vivos
    public static int GetLivingPlayerCount()
    {
        if (NetworkManager.Singleton == null) return 0;
        
        int count = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            // Verifica se o objeto do jogador existe E está spawnado
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            {
                TargetMultiplayer target = client.PlayerObject.GetComponent<TargetMultiplayer>();
                
                // Conta se tiver vida e não estiver morto
                if (target != null && !target.IsDead.Value)
                {
                    count++;
                }
            }
        }
        
        // Debug para vermos o que se passa na consola
        Debug.Log($"[GameManager] Jogadores Vivos Contados: {count}");
        return count;
    }

    // Retorna uma LISTA de jogadores vivos (para o modo espectador)
    public static List<Transform> GetLivingPlayerTransforms()
    {
        List<Transform> list = new List<Transform>();
        if (NetworkManager.Singleton == null) return list;
        
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                TargetMultiplayer target = client.PlayerObject.GetComponent<TargetMultiplayer>();
                if (target != null && !target.IsDead.Value)
                {
                    list.Add(client.PlayerObject.transform);
                }
            }
        }
        return list;
    }
    
    // Esta função é chamada pelo TargetMultiplayer quando alguém morre
    // (Para verificar o estado do jogo, como "Todos os jogadores morreram")
    public static void CheckGameLogicAfterPlayerChange()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        
        int livingPlayers = GetLivingPlayerCount();
        Debug.Log($"[GameManager] Jogador morreu. Jogadores vivos: {livingPlayers}");
        
        // Ex: Se todos os jogadores morreram
        if (livingPlayers <= 0)
        {
            Debug.Log("[GameManager] Todos os jogadores estão mortos! Game Over.");
         
        }
     
    }
}