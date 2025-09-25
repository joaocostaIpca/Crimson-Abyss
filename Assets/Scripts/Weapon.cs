using UnityEngine;

public class Weapon : MonoBehaviour
{
    public GameObject bulletPrefab;   // Prefab do projetil
    public Transform firePoint;       // Ponto de disparo
    public float bulletSpeed = 20f;

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // Botão esquerdo do rato
        {
            Shoot();
        }
    }

    void Shoot()
    {
        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        rb.linearVelocity = firePoint.forward * bulletSpeed;

        // Destruir a bala após 3 segundos para não encher a cena
        Destroy(bullet, 3f);
    }
}
