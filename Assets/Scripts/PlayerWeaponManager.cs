using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class PlayerWeaponManager : NetworkBehaviour
{
    [Header("Assigned weapon prefabs (inspector)")]
    [SerializeField] public List<GameObject> weaponPrefabs = new List<GameObject>();
    [SerializeField] public Transform weaponHolder;

    // networked current selection (server-authoritative via ServerRpc)
    public NetworkVariable<int> CurrentWeaponIndex = new NetworkVariable<int>(0);

    // Sync owner's camera/world rotation so other clients can orient the weapon visual.
    public NetworkVariable<Quaternion> WeaponRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // local caching
    private Camera playerCamera;
    [Header("Aim sync settings")]
    [SerializeField] private float angleSendThreshold = 0.5f; // degrees
    [SerializeField] private float rotationLerpSpeed = 20f; // smoothing for non-owners

    // keep a reference to the locally-instantiated visual (non-networked)
    private GameObject localVisualInstance;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CurrentWeaponIndex.OnValueChanged += OnWeaponIndexChanged;
        WeaponRotation.OnValueChanged += OnWeaponRotationChanged;

        // find local camera for owner (Main Camera on the player hierarchy)
        if (IsOwner)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null) playerCamera = Camera.main;
        }

        // Ensure clients (including owner) have a visual on connect
        if (IsClient)
        {
            UpdateLocalWeaponVisual(CurrentWeaponIndex.Value);

            // update HUD only for the owner
            if (IsOwner)
            {
                string name = (CurrentWeaponIndex.Value >= 0 && CurrentWeaponIndex.Value < weaponPrefabs.Count) ? weaponPrefabs[CurrentWeaponIndex.Value].name : null;
                InterfaceController.Instance?.UpdateWeapon(name);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentWeaponIndex.OnValueChanged -= OnWeaponIndexChanged;
        WeaponRotation.OnValueChanged -= OnWeaponRotationChanged;
        // cleanup local visual
        if (localVisualInstance != null)
        {
            Destroy(localVisualInstance);
            localVisualInstance = null;
        }
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (localVisualInstance != null)
        {
            Destroy(localVisualInstance);
            localVisualInstance = null;
        }
    }

    private void OnWeaponIndexChanged(int previous, int current)
    {
        // Run visual update on all clients (owner and non-owner).
        if (IsClient)
        {
            UpdateLocalWeaponVisual(current);

            // update HUD only for the owner
            if (IsOwner)
            {
                string name = (current >= 0 && current < weaponPrefabs.Count) ? weaponPrefabs[current].name : null;
                InterfaceController.Instance?.UpdateWeapon(name);
            }
        }
    }

    private void UpdateLocalWeaponVisual(int index)
    {
        if (!IsClient) return; // don't instantiate visuals on a dedicated server

        if (weaponHolder == null)
        {
            Debug.LogWarning("[PlayerWeaponManager] weaponHolder is null.");
            return;
        }

        // destroy the existing local visual (only non-networked visuals)
        if (localVisualInstance != null)
        {
            Destroy(localVisualInstance);
            localVisualInstance = null;
        }

        if (index < 0 || index >= weaponPrefabs.Count) return;

        var prefab = weaponPrefabs[index];

        // Instantiate local-only visual for everyone (owner and non-owners).
        localVisualInstance = Instantiate(prefab, weaponHolder);
        localVisualInstance.transform.localPosition = Vector3.zero;
        localVisualInstance.transform.localRotation = Quaternion.identity;

        // If by chance the prefab contains a NetworkObject (legacy), remove or disable it to avoid network conflicts.
        var netComp = localVisualInstance.GetComponent<NetworkObject>();
        if (netComp != null)
        {
            Destroy(netComp);
        }
    }

    // Called locally by PlayerController input to request a weapon cycle
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
        CurrentWeaponIndex.Value = next; // replicated automatically
    }

    private void Update()
    {
        // Owner writes current camera/world rotation each frame (but only when changed enough)
        if (IsOwner)
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
                if (playerCamera == null) playerCamera = Camera.main;
            }

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
            // non-owner clients: smoothly apply replicated rotation to the weapon visual under weaponHolder
            if (weaponHolder != null && localVisualInstance != null)
            {
                Quaternion current = localVisualInstance.transform.rotation;
                Quaternion target = WeaponRotation.Value;
                localVisualInstance.transform.rotation = Quaternion.Slerp(current, target, Mathf.Clamp01(rotationLerpSpeed * Time.deltaTime));
            }
        }
    }

    private void OnWeaponRotationChanged(Quaternion oldRot, Quaternion newRot)
    {
        // Immediately apply on change for clients (runs on clients).
        if (!IsOwner && weaponHolder != null && localVisualInstance != null)
        {
            localVisualInstance.transform.rotation = newRot;
        }
    }

    // ----------------------------
    // Networking for replicated VFX
    // ----------------------------

    // Called by the owner's client via ServerRpc: server will relay a ClientRpc to all clients
    [ServerRpc(RequireOwnership = true)]
    public void RequestFireServerRpc(Vector3 startPos, Vector3 direction, Vector3 hitPoint, ServerRpcParams rpcParams = default)
    {
        // Basic server-side validation could be added here (range checks, rate-limits, anti-spam, etc.)
        // Broadcast to all clients that this player fired (this runs on the PlayerWeaponManager instance
        // belonging to the same NetworkObject on each client).
        SpawnBulletTrailClientRpc(startPos, direction, hitPoint);
    }

    [ClientRpc]
    private void SpawnBulletTrailClientRpc(Vector3 startPos, Vector3 direction, Vector3 hitPoint, ClientRpcParams clientRpcParams = default)
    {
        // 1. Se eu sou o dono, saio (já desenhei o rasto instantaneamente no meu NetworkWeapon)
        if (IsOwner) return;

        GameObject trailPrefab = null;
        GameObject impactPrefab = null;
        float trailSpeedLocal = 300f;

        // --- CORREÇÃO: Busca Segura ---
        // Em vez de procurar na cena, vamos buscar as configurações ao PREFAB original na lista.
        // O 'CurrentWeaponIndex' diz-nos qual é a arma que o jogador está a usar.
        int index = CurrentWeaponIndex.Value;

        if (index >= 0 && index < weaponPrefabs.Count && weaponPrefabs[index] != null)
        {
            // Pega o script NetworkWeapon diretamente do ficheiro do Prefab
            var weaponScript = weaponPrefabs[index].GetComponent<NetworkWeapon>();

            // Se não estiver na raiz, procura nos filhos do prefab
            if (weaponScript == null)
                weaponScript = weaponPrefabs[index].GetComponentInChildren<NetworkWeapon>();

            if (weaponScript != null)
            {
                trailPrefab = weaponScript.bulletTrailVFX;
                impactPrefab = weaponScript.hitEffect;
                trailSpeedLocal = weaponScript.trailSpeed;
            }
        }

        // Fallback: Se por acaso a lista falhar, tenta procurar na cena como antes
        if (trailPrefab == null)
        {
            var nw = GetComponentInChildren<NetworkWeapon>(true);
            if (nw != null)
            {
                trailPrefab = nw.bulletTrailVFX;
                impactPrefab = nw.hitEffect;
                trailSpeedLocal = nw.trailSpeed;
            }
        }

        // Se mesmo assim não houver rasto (null), cancela para não dar erro
        if (trailPrefab == null) return;

        // 2. Instancia o Rasto (Trail)
        // Usa Quaternion.LookRotation para o rasto apontar na direção do tiro
        Quaternion rotation = Quaternion.identity;
        if (direction != Vector3.zero) rotation = Quaternion.LookRotation(direction);

        GameObject trail = Instantiate(trailPrefab, startPos, rotation);

        // 3. Configura a animação do rasto
        var ps = trail.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Play();
            var main = ps.main;
            float duration = main.duration;
            // Cálculo seguro do tempo de vida
            float lifeTime = main.startLifetime.mode == ParticleSystemCurveMode.Constant ? main.startLifetime.constant : main.startLifetime.constantMax;

            Destroy(trail, duration + lifeTime + 0.1f);
        }
        else
        {
            // Se não for sistema de particulas, move manualmente
            StartCoroutine(MoveTrail(trail, startPos, hitPoint, trailSpeedLocal));
        }

        // 4. Instancia o Impacto (Hit Effect)
        if (impactPrefab != null && (hitPoint - startPos).sqrMagnitude > 0.0001f)
        {
            // LookRotation(hitNormal) seria ideal, mas aqui usamos identity ou direção inversa
            GameObject impact = Instantiate(impactPrefab, hitPoint, Quaternion.identity);
            Destroy(impact, 2f);
        }
    }

    // helper adjusted to accept speed
    private IEnumerator MoveTrail(GameObject trail, Vector3 start, Vector3 end, float trailSpeed)
    {
        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(0.0001f, trailSpeed);
        if (duration <= 0f) duration = 0.01f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            if (trail != null)
                trail.transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        if (trail != null)
            Destroy(trail, 0.05f);
    }
}