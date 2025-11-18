using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerWeaponManager : NetworkBehaviour
{
    [Header("Assigned weapon prefabs (inspector)")]
    [SerializeField] public List<GameObject> weaponPrefabs = new List<GameObject>();

    // networked current selection (server-authoritative via ServerRpc)
    public NetworkVariable<int> CurrentWeaponIndex = new NetworkVariable<int>(0);

    private GameObject runtimeWeaponInstance;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CurrentWeaponIndex.OnValueChanged += OnWeaponIndexChanged;

        // Ensure owner has a visual on connect (local-only)
        if (IsOwner)
        {
            UpdateLocalWeaponVisual(CurrentWeaponIndex.Value);
            string name = (CurrentWeaponIndex.Value >= 0 && CurrentWeaponIndex.Value < weaponPrefabs.Count) ? weaponPrefabs[CurrentWeaponIndex.Value].name : null;
            InterfaceController.Instance?.UpdateWeapon(name);
        }
    }

    private void OnWeaponIndexChanged(int previous, int current)
    {
        if (IsOwner)
        {
            UpdateLocalWeaponVisual(current);
            // update HUD
            string name = (current >= 0 && current < weaponPrefabs.Count) ? weaponPrefabs[current].name : null;
            InterfaceController.Instance?.UpdateWeapon(name);
        }
    }

    private void UpdateLocalWeaponVisual(int index)
    {
        //if (runtimeWeaponInstance != null) Destroy(runtimeWeaponInstance);
        if (index < 0 || index >= weaponPrefabs.Count) return;

        // update the visual representation of the weapon for the local player (put the weapon prefab in the right position so that it is visible to the player)

        //var prefab = weaponPrefabs[index];
        //runtimeWeaponInstance = Instantiate(prefab, transform);
        //runtimeWeaponInstance.transform.localPosition = Vector3.zero;
        //runtimeWeaponInstance.transform.localRotation = Quaternion.identity;
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
