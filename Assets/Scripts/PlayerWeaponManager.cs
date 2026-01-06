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
            // LIGA O IK DA MÃO DIREITA AQUI
            if (rigHandler != null)
            {
                rigHandler.SetWeaponTarget(weaponScript);
            }

            // Atualiza UI se for o dono
            if (IsOwner)
            {
                weaponScript.UpdateAmmoUI();
                InterfaceController.Instance?.UpdateWeapon(prefab.name);
            }
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
}