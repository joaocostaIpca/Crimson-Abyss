using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class PlayerWeaponManager : NetworkBehaviour
{
    [Header("Assigned weapon prefabs (Inspector)")]
    [SerializeField] public List<GameObject> weaponPrefabs = new List<GameObject>();
    [SerializeField] public Transform weaponHolder;

    [Header("Networking")]
    // Variável que diz a todos qual é a arma atual (controlada pelo Servidor)
    public NetworkVariable<int> CurrentWeaponIndex = new NetworkVariable<int>(0);

    // Sincroniza a rotação da arma (para os outros verem para onde apontas)
    public NetworkVariable<Quaternion> WeaponRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // networked lists to keep per-weapon ammo (server-authoritative)
    public NetworkList<int> AmmoCounts = new NetworkList<int>();
    public NetworkList<int> MaxAmmoCounts = new NetworkList<int>();

    [Header("Aim Settings")]
    [SerializeField] private float angleSendThreshold = 0.5f;
    [SerializeField] private float rotationLerpSpeed = 20f;

    // Variáveis Locais
    private Camera playerCamera;
    private GameObject localVisualInstance; // A arma visual que vemos na mão

    // --- REFERÊNCIA PARA O RIGGING (IK) ---
    private PlayerRigHandler rigHandler;

    private void Awake()
    {
        // Agarra o script que controla o braço direito
        rigHandler = GetComponent<PlayerRigHandler>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscreve às mudanças de variáveis de rede
        CurrentWeaponIndex.OnValueChanged += OnWeaponIndexChanged;
        WeaponRotation.OnValueChanged += OnWeaponRotationChanged;

        // Subscribe to ammo list changes (clients update UI when the server changes ammo)
        AmmoCounts.OnListChanged += OnAmmoListChanged;
        MaxAmmoCounts.OnListChanged += OnMaxAmmoListChanged;

        // If server, initialize the AmmoCounts / MaxAmmoCounts lists from the prefabs (server authoritative)
        if (IsServer)
        {
            AmmoCounts.Clear();
            MaxAmmoCounts.Clear();
            for (int i = 0; i < weaponPrefabs.Count; i++)
            {
                int initialAmmo = 0;
                int initialMaxAmmo = 0;
                if (weaponPrefabs[i] != null)
                {
                    var ws = weaponPrefabs[i].GetComponent<NetworkWeapon>();
                    if (ws != null)
                    {
                        initialAmmo = ws.currentAmmo;
                        initialMaxAmmo = ws.maxAmmo;
                    }
                }
                AmmoCounts.Add(initialAmmo);
                MaxAmmoCounts.Add(initialMaxAmmo);
            }
        }

        // Se for o dono, encontra a câmara
        if (IsOwner)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null) playerCamera = Camera.main;
        }

        // Garante que a arma visual aparece assim que entramos no jogo
        if (IsClient)
        {
            UpdateLocalWeaponVisual(CurrentWeaponIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentWeaponIndex.OnValueChanged -= OnWeaponIndexChanged;
        WeaponRotation.OnValueChanged -= OnWeaponRotationChanged;
        AmmoCounts.OnListChanged -= OnAmmoListChanged;
        MaxAmmoCounts.OnListChanged -= OnMaxAmmoListChanged;
        if (localVisualInstance != null) Destroy(localVisualInstance);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // Lógica de Rotação da Arma
        if (IsOwner)
        {
            // O Dono escreve a rotação da câmara para a variável de rede
            if (playerCamera != null)
            {
                Quaternion camRot = playerCamera.transform.rotation;
                if (Quaternion.Angle(WeaponRotation.Value, camRot) > angleSendThreshold)
                {
                    WeaponRotation.Value = camRot;
                }
            }
        }
        else
        {
            // Os outros jogadores leem a variável e rodam a arma visual suavemente
            if (weaponHolder != null && localVisualInstance != null)
            {
                Quaternion current = localVisualInstance.transform.rotation;
                Quaternion target = WeaponRotation.Value;
                localVisualInstance.transform.rotation = Quaternion.Slerp(current, target, Time.deltaTime * rotationLerpSpeed);
            }
        }
    }

    // Called when the server modifies AmmoCounts (clients react to keep UI in sync)
    private void OnAmmoListChanged(NetworkListEvent<int> changeEvent)
    {
        // Only update local owner's UI if the changed index is the currently equipped weapon
        if (!IsClient) return;
        if (!IsOwner) return;

        if (changeEvent.Index == CurrentWeaponIndex.Value && localVisualInstance != null)
        {
            var weaponScript = localVisualInstance.GetComponent<NetworkWeapon>();
            if (weaponScript != null)
            {
                // Apply authoritative current ammo from server and refresh UI
                if (changeEvent.Index >= 0 && changeEvent.Index < AmmoCounts.Count)
                {
                    weaponScript.currentAmmo = AmmoCounts[changeEvent.Index];
                }
                weaponScript.UpdateAmmoUI();
            }
        }
    }

    // Called when the server modifies MaxAmmoCounts (clients react to keep UI in sync)
    private void OnMaxAmmoListChanged(NetworkListEvent<int> changeEvent)
    {
        if (!IsClient) return;
        if (!IsOwner) return;

        if (changeEvent.Index == CurrentWeaponIndex.Value && localVisualInstance != null)
        {
            var weaponScript = localVisualInstance.GetComponent<NetworkWeapon>();
            if (weaponScript != null)
            {
                // Apply authoritative max ammo (reserve) from server and refresh UI
                if (changeEvent.Index >= 0 && changeEvent.Index < MaxAmmoCounts.Count)
                {
                    weaponScript.maxAmmo = MaxAmmoCounts[changeEvent.Index];
                }
                weaponScript.UpdateAmmoUI();
            }
        }
    }

    // Chamado automaticamente quando a variável CurrentWeaponIndex muda na rede
    private void OnWeaponIndexChanged(int previous, int current)
    {
        if (IsClient)
        {
            UpdateLocalWeaponVisual(current);
        }
    }

    // --- O CORAÇÃO DO SISTEMA ---
    private void UpdateLocalWeaponVisual(int index)
    {
        if (!IsClient) return;
        if (weaponHolder == null) return;

        // 1. Destroi a arma visual antiga
        if (localVisualInstance != null)
        {
            Destroy(localVisualInstance);
            localVisualInstance = null;
        }

        if (index < 0 || index >= weaponPrefabs.Count) return;

        // 2. Instancia a nova arma
        var prefab = weaponPrefabs[index];
        localVisualInstance = Instantiate(prefab, weaponHolder);
        localVisualInstance.transform.localPosition = Vector3.zero;
        localVisualInstance.transform.localRotation = Quaternion.identity;

        // 3. Remove componente de rede do visual (para evitar conflitos, já que é apenas visual)
        var netComp = localVisualInstance.GetComponent<NetworkObject>();
        if (netComp != null) Destroy(netComp);

        // 4. Configura IK e UI
        var weaponScript = localVisualInstance.GetComponent<NetworkWeapon>();
        if (weaponScript != null)
        {
            // Apply authoritative ammo values from AmmoCounts / MaxAmmoCounts (if available)
            if (index >= 0 && index < AmmoCounts.Count)
            {
                weaponScript.currentAmmo = AmmoCounts[index];
            }
            if (index >= 0 && index < MaxAmmoCounts.Count)
            {
                weaponScript.maxAmmo = MaxAmmoCounts[index];
            }

            // LIGA O IK DA MÃO DIREITA AQUI
            if (rigHandler != null)
            {
                rigHandler.SetWeaponTarget(weaponScript);
            }

            // Atualiza UI se for o dono
            if (IsOwner)
            {
                weaponScript.UpdateAmmoUI();
                InterfaceController.Instance?.UpdateWeapon(OwnerClientId, prefab.name);
            }
        }
    }

    // New helper: remove local visual and optionally disable manager for the owner
    public void RemoveLocalWeaponVisualAndDisable()
    {
        // Remove visually instantiated weapon (client-side)
        if (localVisualInstance != null)
        {
            Destroy(localVisualInstance);
            localVisualInstance = null;
        }

        // Update UI to empty for local player
        if (IsOwner)
        {
            InterfaceController.Instance?.UpdateWeapon(OwnerClientId, "Empty");
            // Optionally disable this component on owner to stop local rotation updates
            this.enabled = false;
        }
    }

    // Método chamado manualmente quando rodam a rotação da arma (clientes)
    private void OnWeaponRotationChanged(Quaternion oldRot, Quaternion newRot)
    {
        if (!IsOwner && localVisualInstance != null)
        {
            localVisualInstance.transform.rotation = newRot;
        }
    }

    // --- TROCA DE ARMAS ---
    public void CycleWeaponLocal()
    {
        if (!IsOwner) return;
        CycleWeaponServerRpc();
    }

    [ServerRpc(RequireOwnership = true)]
    private void CycleWeaponServerRpc()
    {
        if (weaponPrefabs.Count == 0) return;
        int next = (CurrentWeaponIndex.Value + 1) % weaponPrefabs.Count;
        CurrentWeaponIndex.Value = next;
    }

    // --- VFX DE TIRO (RPCs) ---
    // Chamado pelo NetworkWeapon quando disparas
    [ServerRpc(RequireOwnership = true)]
    public void RequestFireServerRpc(Vector3 startPos, Vector3 direction, Vector3 hitPoint, ServerRpcParams rpcParams = default)
    {
        // don't process fire requests from dead players
        var tm = GetComponent<TargetMultiplayer>();
        if (tm != null && tm.IsDead.Value)
        {
            return;
        }

        SpawnBulletTrailClientRpc(startPos, direction, hitPoint);
    }

    [ClientRpc]
    private void SpawnBulletTrailClientRpc(Vector3 startPos, Vector3 direction, Vector3 hitPoint, ClientRpcParams clientRpcParams = default)
    {
        // O dono já viu o rasto localmente no NetworkWeapon, não precisa ver de novo
        if (IsOwner) return;

        GameObject trailPrefab = null;
        GameObject impactPrefab = null;
        float trailSpeedLocal = 300f;

        // Procura os efeitos no prefab da arma atual
        int index = CurrentWeaponIndex.Value;
        if (index >= 0 && index < weaponPrefabs.Count && weaponPrefabs[index] != null)
        {
            var weaponScript = weaponPrefabs[index].GetComponent<NetworkWeapon>();
            if (weaponScript == null) weaponScript = weaponPrefabs[index].GetComponentInChildren<NetworkWeapon>();

            if (weaponScript != null)
            {
                trailPrefab = weaponScript.bulletTrailVFX;
                impactPrefab = weaponScript.hitEffect;
                trailSpeedLocal = weaponScript.trailSpeed;
            }
        }

        if (trailPrefab == null) return;

        // Cria o rasto
        Quaternion rotation = Quaternion.identity;
        if (direction != Vector3.zero) rotation = Quaternion.LookRotation(direction);

        GameObject trail = Instantiate(trailPrefab, startPos, rotation);

        // Anima o rasto
        var ps = trail.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Play();
            float duration = ps.main.duration;
            float lifeTime = ps.main.startLifetime.mode == ParticleSystemCurveMode.Constant ? ps.main.startLifetime.constant : ps.main.startLifetime.constantMax;
            Destroy(trail, duration + lifeTime + 0.1f);
        }
        else
        {
            StartCoroutine(MoveTrail(trail, startPos, hitPoint, trailSpeedLocal));
        }

        // Cria o impacto
        if (impactPrefab != null && (hitPoint - startPos).sqrMagnitude > 0.0001f)
        {
            GameObject impact = Instantiate(impactPrefab, hitPoint, Quaternion.identity);
            Destroy(impact, 2f);
        }
    }

    private IEnumerator MoveTrail(GameObject trail, Vector3 start, Vector3 end, float trailSpeed)
    {
        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(0.0001f, trailSpeed);
        if (duration <= 0f) duration = 0.01f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            if (trail != null) trail.transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }
        if (trail != null) Destroy(trail, 0.05f);
    }

    // NEW: Called by the local NetworkWeapon when its ammo changes (owner only).
    // This tells the server to update the authoritative AmmoCounts & MaxAmmoCounts lists.
    public void ReportCurrentWeaponAmmoFromOwner(int newAmmo, int newMaxAmmo)
    {
        if (!IsOwner) return;
        int index = CurrentWeaponIndex.Value;
        UpdateAmmoServerRpc(index, newAmmo, newMaxAmmo);
    }

    [ServerRpc(RequireOwnership = true)]
    private void UpdateAmmoServerRpc(int weaponIndex, int newAmmo, int newMaxAmmo)
    {
        if (weaponIndex < 0) return;

        // Ensure the lists are large enough on the server (defensive)
        while (weaponIndex >= AmmoCounts.Count)
        {
            AmmoCounts.Add(0);
        }
        while (weaponIndex >= MaxAmmoCounts.Count)
        {
            MaxAmmoCounts.Add(0);
        }

        AmmoCounts[weaponIndex] = Mathf.Max(0, newAmmo);
        MaxAmmoCounts[weaponIndex] = Mathf.Max(0, newMaxAmmo);
    }

    // Called by the local player when they should refill ALL their weapons (fills mag and reserve per-weapon)
    public void RefillAllWeaponsLocal()
    {
        // If running without a network (singleplayer / host), do it directly on server logic
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsServer)
        {
            ApplyRefillOnServer();
        }
        else if (IsOwner)
        {
            // Ask server to refill all weapons (will run on server)
            RefillAllWeaponsServerRpc();
        }
    }

    // Server-side implementation that sets AmmoCounts and MaxAmmoCounts according to each prefab's default values
    private void ApplyRefillOnServer()
    {
        if (!IsServer)
        {
            // Defensive: only server should mutate the authoritative lists
            return;
        }

        for (int i = 0; i < weaponPrefabs.Count; i++)
        {
            var prefab = weaponPrefabs[i];
            int mag = 0;
            int reserve = 0;

            if (prefab != null)
            {
                var ws = prefab.GetComponent<NetworkWeapon>();
                if (ws == null) ws = prefab.GetComponentInChildren<NetworkWeapon>();
                if (ws != null)
                {
                    mag = ws.maxMagAmmo;
                    reserve = ws.WeaponMaxAmmo;
                }
            }

            // Ensure lists are large enough
            while (i >= AmmoCounts.Count) AmmoCounts.Add(0);
            while (i >= MaxAmmoCounts.Count) MaxAmmoCounts.Add(0);

            AmmoCounts[i] = Mathf.Max(0, mag);
            MaxAmmoCounts[i] = Mathf.Max(0, reserve);
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void RefillAllWeaponsServerRpc(ServerRpcParams rpcParams = default)
    {
        // When called from owner client, this runs on the server and applies the refill.
        ApplyRefillOnServer();
    }
}