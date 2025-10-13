using UnityEngine;

public class EnemyDeathNotifier : MonoBehaviour
{
    public WavControllerScript spawner;

    private void OnDestroy()
    {
        if (spawner != null)
            spawner.EnemyDied();
    }
}
