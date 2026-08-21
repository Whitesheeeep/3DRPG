using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WallrunCheck : GroundCheck
{
    public string stateName;
    protected bool isStart = true;

    protected void Update()
    {
        StartCoroutine(stateName);
    }
    protected override void OnTriggerEnter(Collider other)
    {
        if (isStart)
        {
            isStart = false;
            return;
        }
        base.OnTriggerEnter(other);

    }
    protected IEnumerator wallrunJumpCheck()
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_Jog_Stop_Rfoot"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(6.9f, _parent.localPosition.y, _parent.localPosition.z), Time.deltaTime * 1f);
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Walk_Stop_Rfoot"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(6.9f, _parent.localPosition.y, _parent.localPosition.z), Time.deltaTime * 1f);
        }
        yield return null;
    }
    protected IEnumerator wallrun1FCheck()
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("WallL_Run_Exit") || animator.GetCurrentAnimatorStateInfo(0).IsName("WallR_Run_Exit"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(startX, _parent.localPosition.y, _parent.localPosition.z), Time.deltaTime * 10f);
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_TurnIP_R180"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(_parent.localPosition.x, _parent.localPosition.y, 1f), Time.deltaTime * 2f);
        }
        yield return null;
    }
    protected IEnumerator wallrun2FCheck()
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_Sprint_Start_R0"))
        {
            _parent.rotation = Quaternion.Lerp(_parent.rotation, Quaternion.Euler(0, 180, 0), Time.deltaTime * 10f);
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_Sprint_Start_L0"))
        {
            _parent.rotation = Quaternion.Lerp(_parent.rotation, Quaternion.Euler(0, 0, 0), Time.deltaTime * 10f);
        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_TurnIP_R180 0"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(_parent.localPosition.x, _parent.localPosition.y, 0f), Time.deltaTime * 2f);
        }
        yield return null;
    }
    protected IEnumerator wallrunHold()
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_Run_Stop_Rfoot") || animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_Run_Stop_Lfoot"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(startX, _parent.localPosition.y, _parent.localPosition.z), Time.deltaTime * 10f);

        }
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Mvm_TurnIP_R180"))
        {
            _parent.localPosition = Vector3.Lerp(_parent.localPosition, new Vector3(_parent.localPosition.x, _parent.localPosition.y, 0f), Time.deltaTime * 2f);
        }


        yield return null;
    }
}
