using UnityEngine;
using TMPro;
using Unity.Netcode;

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
        if (cam == null) return;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        Debug.DrawRay(ray.origin, ray.direction * range, Color.blue, 2.0f);
        ShootServerRpc(ray.origin, ray.direction);
    }

    [ServerRpc]
    void ShootServerRpc(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (currentAmmo.Value <= 0) return;
        currentAmmo.Value--;
        
        Debug.DrawRay(rayOrigin, rayDirection * range, Color.red, 2.0f);

        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, range))
        {
           
            if (!hit.transform.CompareTag("Player"))
            {
                
                TargetMultiplayer target = hit.transform.GetComponent<TargetMultiplayer>();
                if (target != null)
                {
                    target.TakeDamageServerRpc(damage);
                }
            }
            

            if (hitEffect != null)
            {
                ShowHitEffectClientRpc(hit.point, hit.normal);
            }
        }
    }
    [ClientRpc]
    void ShowHitEffectClientRpc(Vector3 point, Vector3 normal)
    {
        GameObject impact = Instantiate(hitEffect, point, Quaternion.LookRotation(normal));
        Destroy(impact, 1f);
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
        if(IsOwner)
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
}