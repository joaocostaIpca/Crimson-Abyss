using UnityEngine;

public class Weapon : MonoBehaviour
{
    public GameObject bulletPrefab;   // Prefab do projetil
    public Transform firePoint;       // Ponto de disparo
    public float bulletSpeed = 20f;

    public int maxMagAmmo=30;
    public int maxAmmo=120;
    private int currentAmmo=30;


    public float fireRate = 0.5f;     // Intervalo entre tiros
    public float reloadCooldown = 5f; // Intervalo entre reloads
    private float nextFireTime = 0f;
    private float nextReloadTime = 0f;

    void Start()
    {
        currentAmmo = maxMagAmmo;
        UpdateAmmoUI();
    }

    void Update()
    {
       if (Input.GetMouseButtonDown(0) && Time.time >= nextFireTime) // Botão esquerdo do rato
        {
            Shoot();
        }

         if (Input.GetKeyDown(KeyCode.R) && Time.time >= nextReloadTime) // Tecla R para recarregar
        {
            reload();
        }
    }

    void reload()
    {
        int ammoNeeded = maxMagAmmo - currentAmmo;
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
        
        // Definir cooldown do reload
        nextReloadTime = Time.time + reloadCooldown;
        UpdateAmmoUI();
    }

    void Shoot()
    {
        if (currentAmmo > 0)
        {
            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
            Rigidbody rb = bullet.GetComponent<Rigidbody>();
            rb.linearVelocity = firePoint.forward * bulletSpeed;

            // Destruir a bala após 1.5 segundos para não encher a cena
            Destroy(bullet, 1.5f);
            currentAmmo--;
            // Definir cooldown do tiro
            nextFireTime = Time.time + fireRate;
            UpdateAmmoUI();
        }
    }
    
    void UpdateAmmoUI()
    {
        GameData.InterfaceController.UpdateAmmo(currentAmmo, maxMagAmmo, maxAmmo);
    }
}
