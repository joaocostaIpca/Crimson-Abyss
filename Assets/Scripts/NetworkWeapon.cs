using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkWeapon : MonoBehaviour
{
    [Header("Animation Rigging")]
    [Tooltip("Cria um objeto vazio na Pega/Gatilho da arma e arrasta para aqui.")]
    public Transform rightHandGrip; // <--- MUDADO PARA RIGHT

    [Header("Weapon Settings")]
    public float range = 100f;
    public float damage = 20f;
    public float reloadCooldown = 5f;

    [SerializeField] WeaponFireSettings fireSettings;

    public enum FireMode
    {
        Semi,
        Auto,
        Burst
    }


    [System.Serializable]
    public class FireModeSettings
    {
        public FireMode mode;

        public float fireRate = 0.1f;      // Semi / Auto
        public int burstCount = 3;         // Burst only
        public float burstInterval = 0.08f;// Burst only
    }


    [Header("Ammo Settings")]
    public int maxMagAmmo = 30;
    public int currentAmmo = 30;
    public int maxAmmo = 120;
    public int WeaponMaxAmmo = 120;

    [Header("Effects")]
    public GameObject hitEffect;
    public GameObject bulletTrailVFX;
    public float trailSpeed = 300f;
    public Transform muzzleTransform;
    public int NumberOfBullets = 1;

    private float nextFireTime = 0f;
    private float nextReloadTime = 0f;
    private Camera cam;
    bool isBursting;

    [System.Serializable]
    public class WeaponFireSettings
    {
        public List<FireModeSettings> modes;
        public FireMode currentFireMode;

    }

    private PlayerWeaponManager weaponManager;




    private void Start()
    {
        var pc = GetComponentInParent<PlayerController>();
        if (pc != null) cam = pc.playerCamera;
        weaponManager = GetComponentInParent<PlayerWeaponManager>();
    }

    private void Awake()
    {
        UpdateAmmoUI();
    }

    void Update()
    {
        if (weaponManager == null || !weaponManager.IsOwner) return;

        if (Input.GetKeyDown(KeyCode.B))
            CycleFireMode();

        HandleFireInput();

        if (Input.GetKeyDown(KeyCode.R) && Time.time >= nextReloadTime)
        {
            Reload();
        }
    }

    FireModeSettings CurrentModeSettings =>
    fireSettings.modes.Find(m => m.mode == fireSettings.currentFireMode);

    void HandleFireInput()
    {
        FireModeSettings mode = CurrentModeSettings;
        if (mode == null) return;

        switch (fireSettings.currentFireMode)
        {
            case FireMode.Semi:
                if (Input.GetButtonDown("Fire1") && Time.time >= nextFireTime)
                {
                    nextFireTime = Time.time + mode.fireRate;
                    Shoot();
                }
                break;

            case FireMode.Auto:
                if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
                {
                    nextFireTime = Time.time + mode.fireRate;
                    Shoot();
                }
                break;

            case FireMode.Burst:
                if (Input.GetButtonDown("Fire1") && !isBursting)
                    StartCoroutine(BurstFire(mode));
                break;
        }
    }


    IEnumerator BurstFire(FireModeSettings mode)
    {
        isBursting = true;

        int shotsToFire = Mathf.Min(
            mode.burstCount,
            currentAmmo
        );

        for (int i = 0; i < shotsToFire; i++)
        {
            Shoot();

            if (currentAmmo <= 0)
                break;

            yield return new WaitForSeconds(mode.burstInterval);
        }

        nextFireTime = Time.time + mode.fireRate; // burst cooldown
        isBursting = false;
    }



    public void CycleFireMode()
    {
        if (fireSettings.modes == null || fireSettings.modes.Count == 0)
            return;

        int currentIndex = fireSettings.modes.FindIndex(
            m => m.mode == fireSettings.currentFireMode
        );

        // If current mode not found, fallback to first
        if (currentIndex < 0)
        {
            fireSettings.currentFireMode = fireSettings.modes[0].mode;
            return;
        }

        int nextIndex = (currentIndex + 1) % fireSettings.modes.Count;
        fireSettings.currentFireMode = fireSettings.modes[nextIndex].mode;
    }



    void Shoot()
    {
        if (cam == null) return;
        if (currentAmmo <= 0) return;
        Vector3 origin = cam.transform.position;
        Vector3 direction = cam.transform.forward;
        ShotFired(origin, direction, true);

        for (int i = 1; i < NumberOfBullets; i++)
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
            if (currentAmmo <= 0) return;
            currentAmmo--;
            UpdateAmmoUI();
        }

        RaycastHit hit;
        Vector3 hitPoint = rayOrigin + rayDirection * range;
        
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, range))
        {
            hitPoint = hit.point;
            if (!hit.transform.CompareTag("Player"))
            {
                var target = hit.transform.GetComponent<TargetMultiplayer>();
                if (target != null) target.TakeDamageServerRpc(damage);
            }
            if (hitEffect != null) Instantiate(hitEffect, hitPoint, Quaternion.LookRotation(hit.normal));
        }

        Vector3 startPos = (muzzleTransform != null) ? muzzleTransform.position : transform.position;
        SpawnLocalBulletTrail(startPos, rayDirection, hitPoint);

        if (weaponManager != null)
            weaponManager.RequestFireServerRpc(startPos, rayDirection, hitPoint);
    }

    private void SpawnLocalBulletTrail(Vector3 startPos, Vector3 direction, Vector3 hitPoint)
    {
        GameObject trail = Instantiate(bulletTrailVFX, startPos, Quaternion.LookRotation(direction));
        var ps = trail.GetComponent<ParticleSystem>();
        if (ps != null) Destroy(trail, ps.main.duration + 0.2f);
        else StartCoroutine(MoveTrail(trail, startPos, hitPoint));
    }

    private IEnumerator MoveTrail(GameObject trail, Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        float duration = distance / Mathf.Max(0.0001f, trailSpeed);
        float t = 0f;
        while (t < 1f)
        {
            if (trail == null) yield break;
            t += Time.deltaTime / duration;
            trail.transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }
        if (trail != null) Destroy(trail);
    }

    void Reload()
    {
        int needed = maxMagAmmo - currentAmmo;
        if (needed <= 0) return;
        if (maxAmmo >= needed) 
        { 
            maxAmmo -= needed; 
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
        weaponManager?.ReportCurrentWeaponAmmoFromOwner(currentAmmo, maxAmmo);
        var mgr = GetComponentInParent<PlayerWeaponManager>();
        if (mgr == null || !mgr.IsOwner) return;
        InterfaceController.Instance?.UpdateAmmo(currentAmmo, maxMagAmmo, maxAmmo);
    }

    public void RefillAmmo()
    {
        maxAmmo = WeaponMaxAmmo;
        currentAmmo = maxMagAmmo;
        UpdateAmmoUI();
    }
}