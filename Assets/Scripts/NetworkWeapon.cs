using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkWeapon : MonoBehaviour
{
    [Header("Weapon Settings")]
    public float range = 100f;
    public float damage = 20f;
    public float fireRate = 0.5f;
    public float reloadCooldown = 5f;

    [Header("Ammo Settings")]
    public int maxMagAmmo = 30;
    public int currentAmmo = 30;
    public int maxAmmo = 120;

    [Header("Effects")]
    public GameObject hitEffect;
    public GameObject bulletTrailVFX;
    public float trailSpeed = 300f;
    public Transform muzzleTransform;
    public int NumberOfBullets = 1;

    private float nextFireTime = 0f;
    private float nextReloadTime = 0f;
    private Camera cam;

    // --- FIX: Referência para o Manager que sabe quem é o Dono ---
    private PlayerWeaponManager weaponManager;

    private void Start()
    {
        // Procura o PlayerController e a Camera
        var pc = GetComponentInParent<PlayerController>();
        if (pc != null) cam = pc.playerCamera;

        // --- FIX: Guarda referência do WeaponManager para checar IsOwner ---
        weaponManager = GetComponentInParent<PlayerWeaponManager>();
    }

    private void Awake()
    {
        UpdateAmmoUI();
    }

    void Update()
    {
        // --- FIX CRITICO: Só processa input se formos o dono deste jogador ---
        // Se weaponManager for nulo OU se NÃO formos o dono, não fazemos nada.
        if (weaponManager == null || !weaponManager.IsOwner) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (Time.time < nextFireTime)
            {
                print("Weapon on cooldown");
                return;
            }
            // Debug.Log removido para evitar spam, podes recolocar se quiseres
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
        // Verifica se a camara existe (o Client pode não ter a camera do Proxy ativada)
        if (cam == null) return;

        Vector3 origin = cam.transform.position;
        Vector3 direction = cam.transform.forward;

        ShotFired(origin, direction, true);

        for (int i = 1; i < NumberOfBullets; i++) // Corrigido de '2' para '1' se quiseres loops corretos
        {
            Vector3 spreadDir = GetSpreadDirection(cam.transform.forward, 3f);
            ShotFired(origin, spreadDir, false);
        }
    }

    Vector3 GetSpreadDirection(Vector3 forward, float angle)
    {
        float randX = UnityEngine.Random.Range(-angle, angle);
        float randY = UnityEngine.Random.Range(-angle, angle);

        Quaternion rot = Quaternion.Euler(randY, randX, 0);
        return rot * forward;
    }

    void ShotFired(Vector3 rayOrigin, Vector3 rayDirection, bool reduceAmmo)
    {
        if (reduceAmmo)
        {
            if (currentAmmo <= 0)
            {
                Debug.Log("No ammo to shoot, reload!");
                return;
            }
            currentAmmo--;
            UpdateAmmoUI();
        }

        RaycastHit hit;
        Vector3 hitPoint = rayOrigin + rayDirection * range;
        Vector3 hitNormal = Vector3.zero;

        // Lógica de Raycast (Apenas o dono calcula o hit, o servidor valida o dano depois)
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

            // Efeito visual local IMEDIATO (Hit)
            if (hitEffect != null)
                ShowHitEffect(hitPoint, hitNormal);
        }

        Vector3 startPos;
        if (muzzleTransform != null)
            startPos = muzzleTransform.position;
        else
            startPos = new Vector3(transform.position.x, transform.position.y + 1.4f, transform.position.z);

        // 1) Feedback Visual Local Imediato (Dono vê o tiro instantaneamente)
        SpawnLocalBulletTrail(startPos, rayDirection, hitPoint);

        // 2) Avisar o Servidor para mostrar aos outros
        if (weaponManager != null)
        {
            weaponManager.RequestFireServerRpc(startPos, rayDirection, hitPoint);
        }
    }

    void ShowHitEffect(Vector3 point, Vector3 normal)
    {
        GameObject impact = Instantiate(hitEffect, point, Quaternion.LookRotation(normal));
        Destroy(impact, 1f);
    }

    private void SpawnLocalBulletTrail(Vector3 startPos, Vector3 direction, Vector3 hitPoint)
    {
        Vector3 spawnPos = startPos;
        Quaternion rot = Quaternion.identity;
        if (direction.sqrMagnitude > 0.000001f)
            rot = Quaternion.LookRotation(direction);

        GameObject trail = Instantiate(bulletTrailVFX, spawnPos, rot);

        var ps = trail.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            if (main.loop)
            {
                // Aviso removido ou mantido conforme preferência
            }
            ps.Play();
            float duration = main.duration;
            // Simplificação para destruir
            Destroy(trail, duration + 0.2f);
        }
        else
        {
            StartCoroutine(MoveTrail(trail, spawnPos, hitPoint));
        }
    }

    private IEnumerator MoveTrail(GameObject trail, Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(0.0001f, trailSpeed);
        if (duration <= 0f) duration = 0.01f;

        float t = 0f;
        while (t < 1f)
        {
            if (trail == null) yield break; // Segurança
            t += Time.deltaTime / duration;
            trail.transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }
        if (trail != null) Destroy(trail, 0.05f);
    }

    void Reload()
    {
        int ammoNeeded = maxMagAmmo - currentAmmo;
        if (ammoNeeded <= 0) return;

        if (maxAmmo >= ammoNeeded)
        {
            maxAmmo -= ammoNeeded;
            currentAmmo = maxMagAmmo;
        }
        else
        {
            currentAmmo += maxAmmo;
            maxAmmo = 0;
        }
        nextReloadTime = Time.time + reloadCooldown;
        UpdateAmmoUI();
    }

    public void UpdateAmmoUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            InterfaceController interfaceController = canvas.GetComponent<InterfaceController>();
            interfaceController?.UpdateAmmo(currentAmmo, maxMagAmmo, maxAmmo);
        }
    }

    public void RefillAmmo()
    {
        maxAmmo = 120;
        currentAmmo = maxMagAmmo;
        UpdateAmmoUI();
    }
}