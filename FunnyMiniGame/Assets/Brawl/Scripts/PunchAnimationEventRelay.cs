using FIMSpace.RagdollAnimatorDemo;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// Receives the hero Animation Events on a skinned character whose Animator
    /// lives below the gameplay root, then forwards them to the demo controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PunchAnimationEventRelay : MonoBehaviour
    {
        [SerializeField] Demo_Ragd_Hero1 target;
        [SerializeField] AudioSource punchVoiceSource;
        [SerializeField] AudioClip[] punchVoiceClips;
        [SerializeField] Vector2 punchVoicePitchRange = new Vector2(0.96f, 1.04f);

        int lastPunchVoiceIndex = -1;

        public Demo_Ragd_Hero1 Target
        {
            get => target;
            set => target = value;
        }

        public AudioSource PunchVoiceSource => punchVoiceSource;
        public AudioClip[] PunchVoiceClips => punchVoiceClips;

        public void ConfigurePunchVoice(AudioSource source, AudioClip[] clips)
        {
            punchVoiceSource = source;
            punchVoiceClips = clips;
        }

        void Awake()
        {
            if (target == null)
                target = GetComponentInParent<Demo_Ragd_Hero1>(true);
        }

        public void EPunchForward()
        {
            PlayPunchVoice();

            if (target != null)
                target.EPunchForward();
        }

        public void EPunchUp()
        {
            if (target != null)
                target.EPunchUp();
        }

        public void EThrow()
        {
            if (target != null)
                target.EThrow();
        }

        public void EPushForce()
        {
            if (target != null)
                target.EPushForce();
        }

        void PlayPunchVoice()
        {
            if (punchVoiceSource == null || punchVoiceClips == null || punchVoiceClips.Length == 0)
                return;

            int index = Random.Range(0, punchVoiceClips.Length);
            if (punchVoiceClips.Length > 1 && index == lastPunchVoiceIndex)
                index = (index + 1) % punchVoiceClips.Length;

            AudioClip clip = punchVoiceClips[index];
            if (clip == null) return;

            lastPunchVoiceIndex = index;
            punchVoiceSource.pitch = Random.Range(punchVoicePitchRange.x, punchVoicePitchRange.y);
            punchVoiceSource.PlayOneShot(clip);
        }
    }
}
