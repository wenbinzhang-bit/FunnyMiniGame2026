using FIMSpace.FProceduralAnimation;
using UnityEngine;

namespace FIMSpace.RagdollAnimatorDemo
{
    public class ClaymanFallRespawner : MonoBehaviour
    {
        private void OnTriggerEnter( Collider other )
        {
            RagdollAnimator2BoneIndicator rBone = other.GetComponent<RagdollAnimator2BoneIndicator>();

            if( rBone )
            {
                var parent = rBone.ParentRagdollAnimator;
                var claymanContr = parent.GetBaseTransform.GetComponent<ClaymanController>();
                if( claymanContr )
                {
                    var anchor = parent.Handler.GetAnchorBoneController;
                    anchor.GameRigidbody.velocity = Vector3.zero;

                    claymanContr.OnRespawned();

                    Vector3 respawnPos = claymanContr.SpawnPosition + Vector3.up * 5f;
                    anchor.GameRigidbody.position = respawnPos;
                    anchor.GameRigidbody.velocity = Vector3.up;
                    parent.Handler.User_Teleport( respawnPos );
                    parent.Handler.User_WarpRefresh();
                }
            }
        }
    }
}