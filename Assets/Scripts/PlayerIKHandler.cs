using UnityEngine;
using Unity.Netcode;
using UnityEngine.Animations.Rigging;

public class PlayerRigHandler : NetworkBehaviour
{
    [Header("Animation Rigging")]
    public Rig handRig; 
    
    [Tooltip("Arrasta o 'RightArm (Two Bone IK Constraint)' para aqui")]
    public TwoBoneIKConstraint rightHandConstraint; // <--- MUDADO PARA RIGHT

    [Header("Settings")]
    public float ikFadeSpeed = 10f;
    private float targetWeight = 0f;

    private void Update()
    {
        if (handRig != null)
        {
            handRig.weight = Mathf.Lerp(handRig.weight, targetWeight, Time.deltaTime * ikFadeSpeed);
        }
    }

    public void SetWeaponTarget(NetworkWeapon weapon)
    {
        // Verifica se a arma tem o ponto de pega da mão DIREITA
        if (weapon != null && weapon.rightHandGrip != null && rightHandConstraint != null)
        {
            var data = rightHandConstraint.data;
            
            // Define o alvo como a pega da arma
            data.target = weapon.rightHandGrip;
            
            rightHandConstraint.data = data;

            var rigBuilder = GetComponent<RigBuilder>();
            if (rigBuilder != null) rigBuilder.Build();

            targetWeight = 1f; // Liga o IK
        }
        else
        {
            targetWeight = 0f; // Desliga o IK se não houver arma
        }
    }
}