using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(PlayerController))]
public class PlayerAbilitySystem : NetworkBehaviour
{
    [Header("Definições de Habilidade")]
    [SerializeField] private float hCooldown = 30f; // Cooldown padrão
    
    [Header("Recursos (Audio/VFX)")]
    [SerializeField] private GameObject healingAuraVfxPrefab;
    [SerializeField] private string letsGoResourcePath = "Audio/LetsGo";
    [SerializeField] private string earthquakeResourcePath = "Audio/Earthquake";

    // Estado
    private NetworkVariable<double> nextHAvailable = new NetworkVariable<double>(0);
    
    // Referências
    private PlayerController playerController;
    private InterfaceController ui;
    
    // Variáveis Locais
    private Coroutine hActionCoroutine;
    private float hActionElapsed;
    private bool isHActionActive = false;
    private Coroutine localHoldCoroutine;
    private Coroutine medikitReenableCoroutine;

    // Variáveis de Servidor
    private Coroutine serverHoldCoroutine;
    private float serverHoldElapsed;
    private bool serverHoldActive = false;
    
    // Audio
    private AudioClip letsGoClip;
    private AudioClip earthquakeClip;
    private AudioSource earthquakeAudioSource;
    
    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            StartCoroutine(FindUI());
        }
    }

    private IEnumerator FindUI()
    {
        while (InterfaceController.Instance == null) yield return null;
        ui = InterfaceController.Instance;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // --- MUDANÇA 1: Apenas deteta o clique inicial (One-Shot) ---
        if (Input.GetKeyDown(KeyCode.H))
        {
            // Se não estiver a correr, inicia
            if (hActionCoroutine == null)
                hActionCoroutine = StartCoroutine(HActionCoroutine());
        }
        
        // REMOVIDO: O Input.GetKeyUp, pois agora não é preciso segurar
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer && serverHoldCoroutine != null)
        {
            StopCoroutine(serverHoldCoroutine);
            serverHoldCoroutine = null;
            serverHoldActive = false;
        }
    }

    private void OnDisable()
    {
        if (hActionCoroutine != null)
        {
            StopCoroutine(hActionCoroutine);
            hActionCoroutine = null;
            EndHAction();
        }
    }

    // --- LÓGICA PRINCIPAL ---

    private IEnumerator HActionCoroutine()
    {
        isHActionActive = true;
        hActionElapsed = 0f;
        
        // Pega a duração específica da personagem (ex: 10s para Freira)
        float duration = GetAbilityDuration(playerController.CharacterIndex.Value);
        
        OnHActionStart();

        // --- MUDANÇA 2: O loop corre até o tempo acabar, sem verificar a tecla ---
        while (hActionElapsed < duration)
        {
            hActionElapsed += Time.deltaTime;
            yield return null;
        }

        EndHAction();
        hActionCoroutine = null;
    }

    private void OnHActionStart()
    {
        // Fallback Singleplayer
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            ui?.SetMedikitEnabled(false);
            if (localHoldCoroutine == null)
                localHoldCoroutine = StartCoroutine(LocalHoldRoutine());
            
            // Efeitos locais instantâneos
            int idx = playerController.CharacterIndex.Value;
            if (idx == 1) PlayLetsGoClientRpc(); 
            else if (idx == 2) StartLocalEarthquake(); 
            
            return;
        }
        // Multiplayer
        else if (IsOwner)
        {
            StartHoldPowerServerRpc();
        }
    }

    private void EndHAction()
    {
        if (!isHActionActive) return;
        isHActionActive = false;

        // Parar Singleplayer
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (localHoldCoroutine != null)
            {
                StopCoroutine(localHoldCoroutine);
                localHoldCoroutine = null;
            }
            if (playerController.CharacterIndex.Value == 2) StopLocalEarthquake();
        }
        // Parar Multiplayer
        else if (IsOwner)
        {
            StopHoldPowerServerRpc();
        }
    }

    // --- LÓGICA ESPECÍFICA POR PERSONAGEM ---

    private float GetAbilityDuration(int charIndex)
    {
        switch (charIndex)
        {
            case 0: return 10f; // Freira (10 segundos de cura)
            case 1: return 25f; // Comandante
            case 2: return 5f;  // Templario
            default: return 1f;
        }
    }

    private float GetAbilityCooldown(int charIndex)
    {
        switch (charIndex)
        {
            case 0: return 45f;  // Freira (45 segundos)
            case 1: return 90f;  // Comandante (1:30 min)
            case 2: return 180f; // Templario (3 min)
            case 3: return 30f;  // Fuzileiro
            default: return hCooldown;
        }
    }

    private void ApplyHoldPowerTick()
    {
        int idx = playerController.CharacterIndex.Value;
        switch (idx)
        {
            case 0: ApplyFreiraTick(); break;
            case 1: ApplyComandanteTick(); break;
            case 2: ApplyTemplarioTick(); break;
        }
    }

    private void ApplyHoldPowerTickLocal()
    {
        ApplyHoldPowerTick();
    }

    // 1. FREIRA (Cura 33% ao longo de 10 segundos)
    private void ApplyFreiraTick()
    {
        float radius = 10f;
        
        // --- MUDANÇA 3: Cálculo da cura ---
        float duration = GetAbilityDuration(0); // 10 segundos
        float targetTotalHealPercent = 0.33f;   // 33% do total
        
        // Se cura 33% em 10s, cura 3.3% por segundo
        float healPercentPerSecond = targetTotalHealPercent / duration; 

        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;
            var tm = col.GetComponentInParent<TargetMultiplayer>();
            if (tm == null || tm.IsDead.Value) continue;

            float healAmount = 100f * healPercentPerSecond; // Assumindo MaxHealth 100
            
            if (tm.health.Value < 100f)
            {
                tm.health.Value = Mathf.Min(tm.health.Value + healAmount, 100f);
            }
        }
    }

    // 2. COMANDANTE (Buff)
    private void ApplyComandanteTick()
    {
        float speedMultiplier = 2f;
        float jumpMultiplier = 1.5f;
        float duration = GetAbilityDuration(1);

        if (!IsServer) return; 

        var allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var pc in allPlayers)
        {
            if (pc == null) continue;
            ulong targetClient = pc.OwnerClientId;
            
            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { targetClient } }
            };
            
            pc.ApplyComandanteBuffClientRpc(speedMultiplier, jumpMultiplier, duration, clientParams);
        }
    }

    // 3. TEMPLÁRIO (Dano em Área)
    private void ApplyTemplarioTick()
    {
        if (!IsServer) return;
        
        float radius = 30f;
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        HashSet<TargetMultiplayer> processed = new HashSet<TargetMultiplayer>();
        
        foreach (var col in hits)
        {
            if (col == null) continue;
            var enemyAI = col.GetComponentInParent<EnemyAI>(); 
            if (enemyAI == null) continue;

            var tm = enemyAI.GetComponent<TargetMultiplayer>();
            if (tm == null || processed.Contains(tm) || tm.IsDead.Value) continue;
            
            processed.Add(tm);
            tm.TakeDamageServerRpc(1000f); 
        }
    }

    // --- RPCs DE CONTROLO ---

    [ServerRpc]
    private void StartHoldPowerServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        
        int idx = playerController.CharacterIndex.Value;
        double now = Time.time;
        if (now < nextHAvailable.Value) return; 

        if (serverHoldCoroutine != null) return;

        float duration = GetAbilityDuration(idx);
        float cooldown = GetAbilityCooldown(idx);
        
        nextHAvailable.Value = now + cooldown; // Define quando pode usar de novo
        serverHoldActive = true;
        serverHoldElapsed = 0f;
        serverHoldCoroutine = StartCoroutine(ServerHoldRoutine(duration, idx));

        // Efeitos Visuais/Sonoros Globais
        if (idx == 0) SpawnHealingAuraClientRpc(duration);
        if (idx == 1) PlayLetsGoClientRpc();
        if (idx == 2) PlayEarthquakeClientRpc();

        ulong ownerClient = rpcParams.Receive.SenderClientId;
        var clientParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { ownerClient } } };
        // Envia o tempo exato em que estará disponível de novo
        NotifyHoldStartedClientRpc(nextHAvailable.Value, clientParams);
    }

    [ServerRpc]
    private void StopHoldPowerServerRpc(ServerRpcParams rpcParams = default)
    {
        // NOTA: Como agora é one-shot, o servidor normalmente para sozinho pelo tempo (duration).
        // Mas mantemos isto para cancelamentos forçados (morte, etc).
        if (!IsServer) return;
        if (serverHoldCoroutine != null)
        {
            StopCoroutine(serverHoldCoroutine);
            serverHoldCoroutine = null;
        }
        serverHoldActive = false;
    }

    private IEnumerator ServerHoldRoutine(float duration, int idx)
    {
        float tickInterval = 1f;
        while (serverHoldActive && serverHoldElapsed < duration)
        {
            ApplyHoldPowerTick();
            serverHoldElapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
        
        if (idx == 2) StopEarthquakeClientRpc(); 
        
        serverHoldActive = false;
        serverHoldCoroutine = null;
    }

    // --- RPCs VISUAIS E DE UI ---

    [ClientRpc]
    private void NotifyHoldStartedClientRpc(double nextAvailableTime, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;
        ui?.SetMedikitEnabled(false);
        
        // Calcula o tempo que falta até estar disponível
        float remaining = Mathf.Max(0f, (float)(nextAvailableTime - Time.time));
        
        if (medikitReenableCoroutine != null) StopCoroutine(medikitReenableCoroutine);
        medikitReenableCoroutine = StartCoroutine(EnableMedikitAfterDelay(remaining));
    }
    
    private IEnumerator EnableMedikitAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ui?.SetMedikitEnabled(true);
        medikitReenableCoroutine = null;
    }

    [ClientRpc]
    private void SpawnHealingAuraClientRpc(float duration)
    {
        if (healingAuraVfxPrefab == null) return;
        GameObject vfx = Instantiate(healingAuraVfxPrefab, transform);
        vfx.transform.localPosition = Vector3.zero;
        Destroy(vfx, duration);
    }

    [ClientRpc]
    private void PlayLetsGoClientRpc()
    {
        if (letsGoClip == null) letsGoClip = Resources.Load<AudioClip>(letsGoResourcePath);
        if (letsGoClip != null) AudioSource.PlayClipAtPoint(letsGoClip, transform.position, 1f);
    }

    [ClientRpc]
    private void PlayEarthquakeClientRpc()
    {
        if (earthquakeClip == null) earthquakeClip = Resources.Load<AudioClip>(earthquakeResourcePath);
        if (earthquakeClip == null) return;
        
        if (earthquakeAudioSource != null) Destroy(earthquakeAudioSource.gameObject);
        
        GameObject go = new GameObject("EarthquakeSfx");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        var src = go.AddComponent<AudioSource>();
        src.clip = earthquakeClip;
        src.loop = true;
        src.spatialBlend = 1f;
        src.Play();
        earthquakeAudioSource = src;
    }

    [ClientRpc]
    private void StopEarthquakeClientRpc()
    {
        if (earthquakeAudioSource != null) Destroy(earthquakeAudioSource.gameObject);
    }

    private IEnumerator LocalHoldRoutine()
    {
        float duration = GetAbilityDuration(playerController.CharacterIndex.Value);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            ApplyHoldPowerTickLocal();
            elapsed += 1f;
            yield return new WaitForSeconds(1f);
        }
    }
    
    private void StartLocalEarthquake() { PlayEarthquakeClientRpc(); } 
    private void StopLocalEarthquake() { StopEarthquakeClientRpc(); }
}