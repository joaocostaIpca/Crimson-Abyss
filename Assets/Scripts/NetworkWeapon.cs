using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkWeapon : NetworkBehaviour
{
    [Header("Weapon Settings")]
    public float range = 100f;
    public float damage = 20f;
    public float fireRate = 0.5f;
    public float reloadCooldown = 5f;

    [Header("Ammo Settings")]
    public int maxMagAmmo = 30;
    private NetworkVariable<int> currentAmmo = new NetworkVariable<int>(0);
    private NetworkVariable<int> maxAmmo = new NetworkVariable<int>(120);

    [Header("Effects")]
    public GameObject hitEffect;
    public GameObject bulletTrailVFX;  // particle prefab
    public float trailSpeed = 300f;
    public Transform muzzleTransform; // optional, assign muzzle here (falls back to player root)
    public int NumberOfBullets = 1;   // number of bullets fired


    // --- MUDANÇA 1: Referência da UI ---
    private InterfaceController ui;

    private float nextFireTime = 0f;
    private float nextReloadTime = 0f;
    private Camera cam;

    // --- MUDANÇA 2: Nova função para o PlayerController chamar ---
    public void SetInterface(InterfaceController interfaceController)
    {
        ui = interfaceController;
        // Atualiza a UI com a munição inicial
        UpdateAmmoUI();
    }

    public override void OnNetworkSpawn()
    {
        currentAmmo.OnValueChanged += OnAmmoChanged;
        maxAmmo.OnValueChanged += OnAmmoChanged;

        if (IsOwner)
        {
            cam = GetComponentInParent<PlayerController>().playerCamera;
            InitializeAmmoServerRpc();
        }
        else
        {
            this.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentAmmo.OnValueChanged -= OnAmmoChanged;
        maxAmmo.OnValueChanged -= OnAmmoChanged;
    }

    [ServerRpc]
    private void InitializeAmmoServerRpc()
    {
        currentAmmo.Value = maxMagAmmo;
        maxAmmo.Value = 120;
    }

    void Update()
    {
        if (!IsOwner) return;

        if (Input.GetMouseButtonDown(0) && Time.time >= nextFireTime && currentAmmo.Value > 0)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }

        if (Input.GetKeyDown(KeyCode.R) && Time.time >= nextReloadTime)
        {
            Reload();
        }
    }

    void Shoot()
    {
        Vector3 origin = cam.transform.position;
        Vector3 direction = cam.transform.forward;
        ShootServerRpc(origin, direction, true);
        for (int i = 2; i < NumberOfBullets; i++)
        {
            direction = GetSpreadDirection(cam.transform.forward, 3f);
            ShootServerRpc(origin, direction, false);
        }
    }

    Vector3 GetSpreadDirection(Vector3 forward, float angle)
    {
        float randX = UnityEngine.Random.Range(-angle, angle);
        float randY = UnityEngine.Random.Range(-angle, angle);

        Quaternion rot = Quaternion.Euler(randY, randX, 0);
        return rot * forward;
    }

    [ServerRpc]
    void ShootServerRpc(Vector3 rayOrigin, Vector3 rayDirection, bool reduceAmmo)
    {
        if (reduceAmmo)
        {
            if (currentAmmo.Value <= 0) return;
            currentAmmo.Value--;
        }

        RaycastHit hit;
        Vector3 hitPoint = rayOrigin + rayDirection * range;
        Vector3 hitNormal = Vector3.zero;

        if (Physics.Raycast(rayOrigin, rayDirection, out hit, range))
        {
            hitPoint = hit.point;
            hitNormal = hit.normal;

            if (!hit.transform.CompareTag("Player"))
            {
                var target = hit.transform.GetComponent<TargetMultiplayer>();
                if (target != null)
                    target.TakeDamageServerRpc(damage);
            }

            if (hitEffect != null)
                ShowHitEffectClientRpc(hitPoint, hitNormal);
        }

        // compute authoritative start position for the trail: prefer a muzzle Transform if assigned,
        // otherwise use the player's root position (this script is on the player).
        Vector3 startPos;
        if (muzzleTransform != null)
            startPos = muzzleTransform.position;
        else
        {
            startPos = new Vector3(transform.position.x, transform.position.y + 1.4f, transform.position.z);
        }
        // send start position + direction + hit point to all clients
        SpawnBulletTrailClientRpc(startPos, rayDirection, hitPoint);
    }


    [ClientRpc]
    void ShowHitEffectClientRpc(Vector3 point, Vector3 normal)
    {
        GameObject impact = Instantiate(hitEffect, point, Quaternion.LookRotation(normal));
        Destroy(impact, 1f);
    }

    // New: clients receive authoritative start + direction + hit point and instantiate a one-shot effect.
    [ClientRpc]
    void SpawnBulletTrailClientRpc(Vector3 startPos, Vector3 direction, Vector3 hitPoint)
    {
        Vector3 spawnPos = startPos;
        Quaternion rot = Quaternion.identity;
        if (direction.sqrMagnitude > 0.000001f)
            rot = Quaternion.LookRotation(direction);

        GameObject trail = Instantiate(bulletTrailVFX, spawnPos, rot);

        // If the prefab contains a ParticleSystem, play it once and destroy after its lifetime.
        var ps = trail.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;

            if (main.loop)
            {
                Debug.LogWarning($"{name}: bulletTrailVFX ParticleSystem is looping. For single-shot trails set Loop = false.");
            }

            // Ensure the system plays (PlayOnAwake is fine too; Play() is safe)
            ps.Play();

            // Compute a conservative destroy time: duration + max startLifetime
            float duration = main.duration;
            float startLifetimeMax = main.startLifetime.constant; // default
            var st = main.startLifetime;
            // handle two-constant mode
            if (st.mode == ParticleSystemCurveMode.TwoConstants)
                startLifetimeMax = st.constantMax;
            else
                startLifetimeMax = st.constant;

            float destroyAfter = duration + startLifetimeMax;
            if (destroyAfter <= 0f) destroyAfter = 2f; // fallback
            Destroy(trail, destroyAfter + 0.1f);
        }
        else
        {
            // fallback for non-particle prefab: move object from start->hit like old behaviour
            StartCoroutine(MoveTrail(trail, spawnPos, hitPoint));
        }
    }

    private IEnumerator MoveTrail(GameObject trail, Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(0.0001f, trailSpeed);
        // avoid division-by-zero and super-fast movement
        if (duration <= 0f) duration = 0.01f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            trail.transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        Destroy(trail, 0.05f);
    }


    void Reload()
    {
        ReloadServerRpc();
        nextReloadTime = Time.time + reloadCooldown;
    }

    [ServerRpc]
    void ReloadServerRpc()
    {
        int ammoNeeded = maxMagAmmo - currentAmmo.Value;
        if (ammoNeeded <= 0) return;

        if (maxAmmo.Value >= ammoNeeded)
        {
            maxAmmo.Value -= ammoNeeded;
            currentAmmo.Value = maxMagAmmo;
        }
        else
        {
            currentAmmo.Value += maxAmmo.Value;
            maxAmmo.Value = 0;
        }
    }

    void OnAmmoChanged(int previousValue, int newValue)
    {
        // Atualiza a UI se formos o dono
        if (IsOwner)
        {
            UpdateAmmoUI();
        }
    }

    public void UpdateAmmoUI()
    {
        // --- MUDANÇA 3: Usar a referência 'ui' ---
        if (ui != null)
        {
            ui.UpdateAmmo(currentAmmo.Value, maxMagAmmo, maxAmmo.Value);
        }
    }

    // --- NOVA FUNÇÃO: CHAMADA PELA SUPPLY BOX ---
    public void RefillAmmo()
    {
        // Só o dono pode pedir para recarregar
        if (IsOwner)
        {
            RefillAmmoServerRpc();
        }
    }

    [ServerRpc]
    private void RefillAmmoServerRpc()
    {
        // Enche a munição total (ex: dá 4 pentes extra)
        maxAmmo.Value = 120; 
        // Opcional: Enche também o pente atual
        currentAmmo.Value = maxMagAmmo;
    }

}