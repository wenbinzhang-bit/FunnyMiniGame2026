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
        [SerializeField, Min(0f)] float punchVoiceStartOffsetSeconds = 0.55f;

        int lastPunchVoiceIndex = -1;
        float suppressEventVoiceUntil = -1f;

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
            // 联机控制器会在挥拳动画启动时提前播放。保留这里作为 Demo/旧场景的兜底，
            // 但同一拳已经提前播放时不要在 0.333 秒的命中事件上再响一次。
            if (Time.time >= suppressEventVoiceUntil)
                PlayPunchVoice();

            if (target != null)
                target.EPunchForward();
        }

        public void PlayPunchVoiceAtAttackStart()
        {
            if (Time.time < suppressEventVoiceUntil) return;

            suppressEventVoiceUntil = Time.time + 1f;
            PlayPunchVoice();
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
            punchVoiceSource.Stop();
            punchVoiceSource.clip = clip;
            punchVoiceSource.time = Mathf.Min(punchVoiceStartOffsetSeconds, Mathf.Max(0f, clip.length - 0.01f));
            punchVoiceSource.Play();
        }
    }
}
