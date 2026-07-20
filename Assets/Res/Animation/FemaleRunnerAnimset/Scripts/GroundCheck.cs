using System.ComponentModel;
using UnityEngine;

public class GroundCheck : MonoBehaviour
{
    public Animator animator;
    public bool isGoingBack = true;
    public float startY;
    public float startX;
    private Vector3 _startPosition;

    [SerializeField] protected Transform _parent;

    protected virtual void Start()
    {
        _parent = this.transform.parent;
        _startPosition = _parent.localPosition;
        if (_startPosition.y != 0f)
        {
            _startPosition = new Vector3(_startPosition.x, 0f, _startPosition.z);
        }

    }
    


    protected virtual void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ground"))
        {
            animator.SetBool("isLanding", true);
            if (isGoingBack)
            {
                _parent.localPosition = new Vector3(_parent.localPosition.x, startY, _parent.localPosition.z);
                Debug.Log(_parent.localPosition);
            }
        }
    }


    protected virtual void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Ground"))
        {
            animator.SetBool("isLanding", false);
        }

    }
}