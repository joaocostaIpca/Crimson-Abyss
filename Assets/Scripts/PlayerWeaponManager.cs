using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerWeaponManager : NetworkBehaviour
{
    [Header("Assigned weapon prefabs (inspector)")]
    [SerializeField] public List<GameObject> weaponPrefabs = new List<GameObject>();
    [SerializeField] public Transform weaponHolder;

    // networked current selection (server-authoritative via ServerRpc)
    public NetworkVariable<int> CurrentWeaponIndex = new NetworkVariable<int>(0);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CurrentWeaponIndex.OnValueChanged += OnWeaponIndexChanged;

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
        Debug.Log($"{name} is changing weapon");
        if (!IsClient)
        {
            // don't instantiate visuals on a dedicated server
            return;
        }

        // clear existing children
        foreach (Transform child in weaponHolder)
        {
            Destroy(child.gameObject);
        }

        // instantiate the prefab under weaponHolder 
        Debug.Log($"Instantiating weapon index {index}");
        var prefab = weaponPrefabs[index];
        GameObject runtimeWeaponInstance = Instantiate(prefab, weaponHolder);
        runtimeWeaponInstance.transform.localPosition = Vector3.zero;
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
}