using UnityEngine;

public class DontDestroy : MonoBehaviour
{
    void Awake()
    {
        // Garante que este objeto sobrevive à mudança de cena
        DontDestroyOnLoad(this.gameObject);
    }
}