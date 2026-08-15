using FIMSpace.RagdollAnimatorDemo;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// Receives punch Animation Events on a skinned character whose Animator lives
    /// below the gameplay root, then forwards them to the original demo controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PunchAnimationEventRelay : MonoBehaviour
    {
        [SerializeField] Demo_Ragd_Hero1 target;

        public Demo_Ragd_Hero1 Target
        {
            get => target;
            set => target = value;
        }

        void Awake()
        {
            if (target == null)
                target = GetComponentInParent<Demo_Ragd_Hero1>(true);
        }

        public void EPunchForward()
        {
            if (target != null)
                target.EPunchForward();
        }

        public void EPunchUp()
        {
            if (target != null)
                target.EPunchUp();
        }
    }
}
