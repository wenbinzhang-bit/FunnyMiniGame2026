using FIMSpace.FProceduralAnimation;
using FIMSpace.RagdollAnimatorDemo;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 按 Punch Minigame Demo 的方式重建 Catch Magnet:
    /// 挂在右手骨骼上,使用 Demo 的本地偏移和磁铁参数,抓取时播 Upper Body 的 Holding。
    /// </summary>
    public class FAnnequinGrabHelper : MonoBehaviour
    {
        static readonly Vector3 DemoHandLocalPosition = new Vector3(-0.088f, 0.114f, -0.061f);
        static readonly Vector3 DemoHandLocalEuler = new Vector3(359.646f, 173.542f, 297.349f);

        Demo_Ragd_Hero1 hero;
        Animator mecanim;

        void Awake()
        {
            hero = GetComponent<Demo_Ragd_Hero1>();
            mecanim = GetComponent<Animator>();
        }

        void Start()
        {
            if (hero == null) return;
            EnsureCatchMagnet();
        }

        public void EnsureCatchMagnet()
        {
            if (hero == null) return;
            if (hero.CatchMagnet != null) return;

            Transform hand = hero.Hand;
            if (hand == null && mecanim && mecanim.isHuman)
                hand = mecanim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) hand = transform;

            var go = new GameObject("Catch Magnet");
            go.transform.SetParent(hand, false);
            go.transform.localPosition = DemoHandLocalPosition;
            go.transform.localRotation = Quaternion.Euler(DemoHandLocalEuler);

            var magnet = go.AddComponent<RA2MagnetPoint>();
            magnet.DragPower = 2f;
            magnet.RotatePower = 3f;
            magnet.KinematicOnMax = true;
            magnet.RestoreOnDestroy = true;
            magnet.MotionInfluence = 1f;
            magnet.enabled = false;

            var boneParent = go.AddComponent<RA2DummyBoneAsParent>();
            boneParent.TargetParent = hand;
            boneParent.LocalPosition = DemoHandLocalPosition;
            boneParent.LocalRotation = DemoHandLocalEuler;

            hero.CatchMagnet = magnet;
        }

        public void PlayHoldingPose()
        {
            if (mecanim == null || mecanim.layerCount < 2) return;
            mecanim.CrossFadeInFixedTime("Holding", 0.1f, 1);
        }
    }
}
