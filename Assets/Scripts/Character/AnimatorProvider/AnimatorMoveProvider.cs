using System;
using RPG.Character;
using UnityEngine;
using WS_Modules.Extensions;

public class AnimatorMoveProvider : MonoBehaviour
{
    private PlayerController playerController;
    private Animator animator;
    private CharacterController cc;

    private void Awake()
    {
        playerController = GetComponentInParent<PlayerController>();
        animator = GetComponent<Animator>();
        cc = gameObject.GetOrAddComponent<CharacterController>();
    }

    private void Start()
    {
        animator.applyRootMotion = true;
    }

    private void OnAnimatorMove()
    {
        animator.applyRootMotion = true;
        Debug.Log(animator.applyRootMotion);
        Debug.Log("AnimatorMoveProvider.OnAnimatorMove called: " + animator.deltaPosition);
        Debug.Log("Rotation: " + animator.deltaRotation);
        playerController?.OnAnimatorMove();
        cc.Move(animator.deltaPosition);
        transform.rotation *= animator.deltaRotation;
    }
}
