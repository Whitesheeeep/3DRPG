using System;
using Animancer;
using UnityEngine;
using WS_Modules.FSM;

public class EasyPlayer : MonoBehaviour
{
    private AnimancerComponent animancer;
    private CharacterController cc;
    private StateMachine<int, EasyPlayer> playerLocoMotionFsm;
    [SerializeField] private Transform cameraTransform;
    private SmoothedFloatParameter rotatorParameter;


    public EasyPlayerDataSO playerData;

    private void Awake()
    {
        // 找组件
        animancer = GetComponent<AnimancerComponent>();
        cc = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        rotatorParameter = new SmoothedFloatParameter(
            animancer,
            playerData.movementAnimacerData.moveMixerName_Rotator,
            0.1f);

        BuildPlayerLocoMotionFsm();
    }

    private void Start()
    {
        playerLocoMotionFsm.OnEnter();
    }

    private void Update()
    {
        UpdateRotation();
        playerLocoMotionFsm.OnUpdate();
    }

    private void UpdateRotation()
    {
        Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        if (input.sqrMagnitude < playerData.IdleMoveDeadZone * playerData.IdleMoveDeadZone)
        {
            rotatorParameter.TargetValue = 0f;
            return;
        }

        Vector2 moveDirection = input.normalized;
        Vector3 cameraForward = cameraTransform != null
            ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized
            : Vector3.forward;
        Vector3 cameraRight = cameraTransform != null
            ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized
            : Vector3.right;
        Vector3 targetDirection = cameraRight * moveDirection.x + cameraForward * moveDirection.y;
        if (targetDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        targetDirection.Normalize();
        float targetAngle = Vector3.SignedAngle(transform.forward, targetDirection, Vector3.up);
        rotatorParameter.TargetValue = targetAngle * Mathf.Deg2Rad;

        Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * playerData.turnSpeed);
    }

    private void OnDestroy()
    {
        playerLocoMotionFsm?.OnExit();
        rotatorParameter?.Dispose();
    }

    private void BuildPlayerLocoMotionFsm()
    {
        playerLocoMotionFsm = new StateMachine<int, EasyPlayer>(3, this);
        playerLocoMotionFsm.State(EasyPlayerState.Idle)
            .OnEnter(state =>
            {
                state.Owner.animancer.Play(state.Owner.playerData.movementAnimacerData.Idle);
            })
            .OnUpdate(state =>
            {
                var inputMoveDir = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
                if (inputMoveDir.magnitude > state.Owner.playerData.IdleMoveDeadZone)
                {
                    state.Machine.ChangeState(EasyPlayerState.Move);
                }
            });

        playerLocoMotionFsm.State(EasyPlayerState.Move)
            .OnEnter(state =>
            {
                state.Owner.animancer.Play(state.Owner.playerData.movementAnimacerData.moveMixer);
            })
            .OnUpdate(state =>
            {
                Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
                if (input.magnitude < state.Owner.playerData.IdleMoveDeadZone)
                {
                    state.Machine.ChangeState(EasyPlayerState.Idle);
                    return;
                }
                // 与参考项目一致：角色朝向先平滑过渡，位移再沿当前 forward 前进。
                Vector3 move = state.Owner.transform.forward *
                               (state.Owner.playerData.walkSpeed * Time.deltaTime);
                state.Owner.cc.Move(move);
                state.Owner.animancer.Parameters.SetValue(
                    state.Owner.playerData.movementAnimacerData.moveMixerName_X,
                    1f);
            });

        playerLocoMotionFsm.SetDefaultState(EasyPlayerState.Idle);
    }

}

public static class EasyPlayerState
{
    public const int Idle = 0;
    public const int Move = 1;
}