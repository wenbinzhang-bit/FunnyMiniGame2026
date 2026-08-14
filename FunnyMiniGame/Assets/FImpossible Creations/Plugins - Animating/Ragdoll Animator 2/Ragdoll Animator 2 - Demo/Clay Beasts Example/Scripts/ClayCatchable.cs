using UnityEngine;

namespace FIMSpace.RagdollAnimatorDemo
{
    public class ClayCatchable : MonoBehaviour
    {
        [Tooltip( "If it is moving object, it needs to be treated differently by the catch-configurable joints (mass modifier for catch objects)" )]
        public bool Moving = false;
    }
}