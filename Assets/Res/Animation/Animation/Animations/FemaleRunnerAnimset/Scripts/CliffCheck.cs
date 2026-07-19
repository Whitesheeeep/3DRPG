using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CliffCheck : GroundCheck
{
    private Vector3 _initialGroundPos;

    protected override void Start()
    {
        base.Start();
        _initialGroundPos = Vector3.zero;
    }

    protected override void OnTriggerEnter(Collider other)
    {
        base.OnTriggerEnter(other);
        if (other.CompareTag("Ground"))
        {
            if(_initialGroundPos == Vector3.zero)
            {
                _initialGroundPos = _parent.localPosition;
            }
            else
            {
                _parent.localPosition = _initialGroundPos;
            }
        }
    }
}
