using System.Collections.Generic;
using FIMSpace.FProceduralAnimation;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 姿态流同步:服务端在 RA2 完成骨骼混合后采样整套可见骨骼,
    /// 以非可靠通道广播;客户端(非主机)完全禁用本地物理/动画,按快照插值回放。
    ///
    /// 骨骼列表在 Awake 中采集(此时 RA2 还没生成物理假体、抓取触发器等运行时对象),
    /// 因此服务端与客户端的骨骼顺序天然一致,无需额外映射。
    /// </summary>
    [DefaultExecutionOrder(31000)]
    public class RagdollNetworkSync : NetworkBehaviour
    {
        [Tooltip("姿态快照每秒发送次数")]
        public float SendsPerSecond = 25f;

        [Tooltip("客户端插值缓冲(相对发送间隔的倍数),越大越平滑但延迟越高")]
        public float BufferTimeMultiplier = 2f;

        Transform[] bones;
        double lastSendTime;

        /// <summary>诊断:服务端已发送/客户端已应用的快照计数(冒烟测试用)。</summary>
        public static long PoseSendCount;
        public static long PoseApplyCount;
        bool loggedFirstApply;

        // ---- 客户端插值状态 ----
        struct Snapshot
        {
            public double time;
            public Vector3 rootPos;
            public Quaternion rootRot;
            public Vector3[] bonePos;
            public Quaternion[] boneRot;
        }

        readonly List<Snapshot> snapshots = new List<Snapshot>();
        double clientTimeline;
        bool timelineInitialized;

        float SendInterval => 1f / Mathf.Max(1f, SendsPerSecond);
        float BufferTime => SendInterval * BufferTimeMultiplier;

        void Awake()
        {
            // 在 RA2 初始化之前采集骨骼层级,保证两端顺序一致
            var all = GetComponentsInChildren<Transform>(true);
            var list = new List<Transform>(all.Length);
            foreach (var t in all)
                if (t != transform)
                    list.Add(t);
            bones = list.ToArray();
        }

        public override void OnStartClient()
        {
            if (isServer) return;

            // 纯客户端:关闭一切本地模拟,只做姿态回放
            var ragdoll = GetComponent<RagdollAnimator2>();
            if (ragdoll) ragdoll.enabled = false;

            foreach (var animator in GetComponentsInChildren<Animator>(true))
                animator.enabled = false;

            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
                rb.isKinematic = true;

            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                // 根节点胶囊体留给出拳判定,不要和远程姿态回放一起关掉
                if (col.transform == transform && col is CapsuleCollider)
                    continue;
                col.enabled = false;
            }
        }

        void LateUpdate()
        {
            if (isServer)
            {
                if (NetworkTime.localTime - lastSendTime < SendInterval) return;
                // 固定步进推进发送时钟,避免快照间距漂移造成客户端插值速度波动
                lastSendTime = System.Math.Max(lastSendTime + SendInterval, NetworkTime.localTime - SendInterval);
                ServerSendPose();
            }
            else
            {
                ClientApplyPose();
            }
        }

        [Server]
        void ServerSendPose()
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteVector3(transform.position);
                writer.WriteUInt(Compression.CompressQuaternion(transform.rotation));

                for (int i = 0; i < bones.Length; i++)
                {
                    Vector3 lp = bones[i].localPosition;
                    writer.WriteUShort(Mathf.FloatToHalf(lp.x));
                    writer.WriteUShort(Mathf.FloatToHalf(lp.y));
                    writer.WriteUShort(Mathf.FloatToHalf(lp.z));
                    writer.WriteUInt(Compression.CompressQuaternion(bones[i].localRotation));
                }

                RpcPose(NetworkTime.localTime, writer.ToArray());
                PoseSendCount++;
            }
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcPose(double serverTime, byte[] data)
        {
            if (isServer) return; // 主机本地已是权威姿态

            using (NetworkReaderPooled reader = NetworkReaderPool.Get(data))
            {
                Snapshot snap = new Snapshot
                {
                    time = serverTime,
                    rootPos = reader.ReadVector3(),
                    rootRot = Compression.DecompressQuaternion(reader.ReadUInt()),
                    bonePos = new Vector3[bones.Length],
                    boneRot = new Quaternion[bones.Length]
                };

                for (int i = 0; i < bones.Length; i++)
                {
                    snap.bonePos[i] = new Vector3(
                        Mathf.HalfToFloat(reader.ReadUShort()),
                        Mathf.HalfToFloat(reader.ReadUShort()),
                        Mathf.HalfToFloat(reader.ReadUShort()));
                    snap.boneRot[i] = Compression.DecompressQuaternion(reader.ReadUInt());
                }

                // 按时间插入(乱序到达时丢弃过旧的)
                if (snapshots.Count > 0 && snap.time <= snapshots[snapshots.Count - 1].time) return;
                snapshots.Add(snap);
                if (snapshots.Count > 10) snapshots.RemoveAt(0);

                if (!timelineInitialized)
                {
                    clientTimeline = snap.time - BufferTime;
                    timelineInitialized = true;
                }
            }
        }

        void ClientApplyPose()
        {
            if (!timelineInitialized || snapshots.Count == 0) return;

            // 时间线推进 + 向 (最新快照 - 缓冲) 轻微校正,防止漂移
            double target = snapshots[snapshots.Count - 1].time - BufferTime;
            double drift = target - clientTimeline;
            double timescale = 1.0 + System.Math.Max(-0.2, System.Math.Min(0.2, drift * 0.5));
            clientTimeline += Time.deltaTime * timescale;

            // 找到包夹时间线的两个快照
            Snapshot from = snapshots[0];
            Snapshot to = snapshots[snapshots.Count - 1];
            for (int i = 0; i < snapshots.Count - 1; i++)
            {
                if (snapshots[i].time <= clientTimeline && snapshots[i + 1].time >= clientTimeline)
                {
                    from = snapshots[i];
                    to = snapshots[i + 1];
                    break;
                }
            }

            float t = 0f;
            double span = to.time - from.time;
            if (span > 0.0001) t = Mathf.Clamp01((float)((clientTimeline - from.time) / span));

            transform.SetPositionAndRotation(
                Vector3.Lerp(from.rootPos, to.rootPos, t),
                Quaternion.Slerp(from.rootRot, to.rootRot, t));

            int count = Mathf.Min(bones.Length, from.bonePos.Length);
            for (int i = 0; i < count; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localPosition = Vector3.Lerp(from.bonePos[i], to.bonePos[i], t);
                bones[i].localRotation = Quaternion.Slerp(from.boneRot[i], to.boneRot[i], t);
            }

            PoseApplyCount++;
            if (!loggedFirstApply)
            {
                loggedFirstApply = true;
                Debug.Log($"BRAWL_SMOKE: CLIENT_POSE_APPLIED netId={netId}");
            }
        }
    }
}
