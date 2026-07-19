using Animancer;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "EasyPlayerMovementAnimacerDataSO", menuName = "ScriptableObjects/EasyPlayer/EasyPlayerMovementAnimacerDataSO", order = 2)]
public class EasyPlayerMovementAnimacerDataSO : ScriptableObject
{
    public TransitionAsset Idle;
    public TransitionAsset  moveMixer;
    public StringAsset moveMixerName_X;
    [FormerlySerializedAs("moveMixerName_Y")]
    public StringAsset moveMixerName_Rotator;
}