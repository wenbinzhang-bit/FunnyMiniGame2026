using UnityEngine;

namespace FIMSpace.RagdollAnimatorDemo
{
    public class GrinderRotator : MonoBehaviour
    {
        public Rigidbody body;
        public Vector3 RotateSpeed = new Vector3( 0f, 0f, 300f );

        void FixedUpdate()
        {
            body.MoveRotation( body.rotation * Quaternion.Euler( RotateSpeed * Time.fixedDeltaTime ) );
        }
    }
}