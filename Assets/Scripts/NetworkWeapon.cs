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
    public GameObject bulletTrailVFX;  // particle prefab
    public float trailSpeed = 300f;
    public Transform muzzleTransform; // optional, assign muzzle here (falls back to player root)
    public int NumberOfBullets = 1;   // number of bullets fired

    private float nextFireTime = 0f;
    private float nextReloadTime = 0f;
    private Camera cam;

    private void Start()
    {
        cam = GetComponentInParent<PlayerController>().playerCamera;
    }

    private void Awake()
    {
        UpdateAmmoUI();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (Time.time < nextFireTime)
            {
                print("Weapon on cooldown");
                return;
            }
            Debug.Log("Firing weapon");
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
        ShotFired(origin, direction, true);
        for (int i = 2; i < NumberOfBullets; i++)
        {
            direction = GetSpreadDirection(cam.transform.forward, 3f);
            ShotFired(origin, direction, false);
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
                ShowHitEffect(hitPoint, hitNormal);
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
        SpawnBulletTrail(startPos, rayDirection, hitPoint);
    }


    void ShowHitEffect(Vector3 point, Vector3 normal)
    {
        GameObject impact = Instantiate(hitEffect, point, Quaternion.LookRotation(normal));
        Destroy(impact, 1f);
    }

    void SpawnBulletTrail(Vector3 startPos, Vector3 direction, Vector3 hitPoint)
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
        // find the InterfaceController in the Canvas object
        Canvas canvas = FindFirstObjectByType<Canvas>();
        InterfaceController interfaceController = canvas.GetComponent<InterfaceController>();
        interfaceController.UpdateAmmo(currentAmmo, maxMagAmmo, maxAmmo);
    }

    // --- NOVA FUNÇÃO: CHAMADA PELA SUPPLY BOX ---
    public void RefillAmmo()
    {
        // Enche a munição total (ex: dá 4 pentes extra)
        maxAmmo = 120; 
        // Opcional: Enche também o pente atual
        currentAmmo = maxMagAmmo;
        UpdateAmmoUI();
    }

}