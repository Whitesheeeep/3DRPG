using UnityEngine;

[CreateAssetMenu(fileName = "EasyPlayerDataSO", menuName = "ScriptableObjects/EasyPlayer/EasyPlayerDataSO", order = 1)]
public class EasyPlayerDataSO : ScriptableObject
{
    public float walkSpeed = 2f;
    public float runSpeed = 4f;
    [Min(0f), Tooltip("Quaternion.Slerp 的转向插值速度，参考项目默认使用 1.4")]
    public float turnSpeed = 1.4f;
    public float jumpSpeed = 8f;
    public float gravity = 2f;

    [Header("二维移动的 Mixer")]
    public EasyPlayerMovementAnimacerDataSO movementAnimacerData;
    [Tooltip("当输入的移动向量长度小于该值时，角色将被认为处于静止状态")]
    public float IdleMoveDeadZone = 0.1f;
}