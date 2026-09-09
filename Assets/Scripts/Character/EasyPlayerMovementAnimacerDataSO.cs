using Animancer;
using UnityEngine;

[CreateAssetMenu(fileName = "EasyPlayerMovementAnimacerDataSO", menuName = "ScriptableObjects/EasyPlayer/EasyPlayerMovementAnimacerDataSO", order = 2)]
public class EasyPlayerMovementAnimacerDataSO : ScriptableObject
{
    public TransitionAsset Idle;
    public TransitionAsset  moveMixer;
    public StringAsset moveMixerName_X;
    public StringAsset moveMixerName_Rotator;
}
