using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkWeapon : MonoBehaviour
{
    [Header("Animation Rigging")]
    [Tooltip("Cria um objeto vazio na Pega/Gatilho da arma e arrasta para aqui.")]
    public Transform rightHandGrip; // <--- MUDADO PARA RIGHT

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

        if (Input.GetMouseButtonDown(0))
        {
            if (Time.time < nextFireTime) return;
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
        if (cam == null) return;
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
        if (maxAmmo >= needed) { maxAmmo -= needed; currentAmmo = maxMagAmmo; }
        else { currentAmmo += maxAmmo; maxAmmo = 0; }
        nextReloadTime = Time.time + reloadCooldown;
        UpdateAmmoUI();
    }

    public void UpdateAmmoUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
            canvas.GetComponent<InterfaceController>()?.UpdateAmmo(currentAmmo, maxMagAmmo, maxAmmo);
    }

    public void RefillAmmo()
    {
        maxAmmo = 120;
        currentAmmo = maxMagAmmo;
        UpdateAmmoUI();
    }
}