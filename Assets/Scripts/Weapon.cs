using UnityEngine;
using TMPro;

public class Weapon : MonoBehaviour
{
    [Header("Weapon Settings")]
    public Transform firePoint;           // Ponto de disparo
    public float range = 100f;            // Alcance do tiro
    public float damage = 20f;            // Dano por tiro
    public float fireRate = 0.5f;         // Intervalo entre tiros
    public float reloadCooldown = 5f;     // Cooldown do reload

    [Header("Ammo Settings")]
    public int maxMagAmmo = 30;
    public int maxAmmo = 120;
    private int currentAmmo;

    [Header("Effects")]
    public ParticleSystem muzzleFlash;    // efeito de disparo
    public GameObject hitEffect;          // prefab do impacto (opcional)
    public TextMeshProUGUI ammoText;

    private float nextFireTime = 0f;
    private float nextReloadTime = 0f;

    void Start()
    {
        currentAmmo = maxMagAmmo;
        UpdateAmmoUI();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && Time.time >= nextFireTime)
        {
            Shoot();
        }

        if (Input.GetKeyDown(KeyCode.R) && Time.time >= nextReloadTime)
        {
            Reload();
        }
    }

   void Shoot()
{
    if (currentAmmo <= 0)
        return;

    currentAmmo--;
    nextFireTime = Time.time + fireRate;
    UpdateAmmoUI();

    // Efeito visual (muzzle flash)
    if (muzzleFlash != null)
        muzzleFlash.Play();

    // --- RAYCAST A PARTIR DO CENTRO DA CÂMARA ---
    Camera cam = Camera.main;
    Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)); // centro do ecrã
    RaycastHit hit;
    Vector3 targetPoint;

    // 1️⃣ Primeiro raycast a partir da câmara (para saber onde o jogador está a olhar)
    if (Physics.Raycast(ray, out hit, range))
    {
        targetPoint = hit.point;

        // Se o alvo tiver o script Target (para levar dano)
        Target target = hit.transform.GetComponent<Target>();
        if (target != null)
        {
            target.TakeDamage(damage);
        }

        // Efeito de impacto visual
        if (hitEffect != null)
        {
            GameObject impact = Instantiate(hitEffect, hit.point, Quaternion.LookRotation(hit.normal));
            Destroy(impact, 1f);
        }
    }
    else
    {
        // Caso não acerte nada, dispara em linha reta até ao limite do alcance
        targetPoint = ray.origin + ray.direction * range;
    }

    // 2️⃣ Corrigir a direção real do disparo para sair do cano da arma (firePoint)
    Vector3 direction = (targetPoint - firePoint.position).normalized;

    // --- EFEITO VISUAL DO TRAJECTO (se tiveres um bullet trail configurado) ---
   

    // Linha de debug no editor (ajuda a ver para onde o tiro vai)
    Debug.DrawRay(firePoint.position, direction * range, Color.red, 1f);
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

    void UpdateAmmoUI()
    {
        GameData.InterfaceController.UpdateAmmo(currentAmmo, maxMagAmmo, maxAmmo);
        if (ammoText != null)
            ammoText.text = $"Ammo: {currentAmmo} / {maxMagAmmo} | Total: {maxAmmo}";
    }
}
