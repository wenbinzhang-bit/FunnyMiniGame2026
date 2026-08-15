using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 远程玩家位移插值。RPC 由 NetFAnnequinController 接收后喂进来。
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class BrawlMoveSync : MonoBehaviour
    {
        public float BufferSeconds = 0.1f;

        struct Snap
        {
            public double time;
            public Vector3 pos;
            public Quaternion rot;
        }

        readonly List<Snap> snaps = new List<Snap>();
        double timeline;
        bool timelineReady;
        Vector3 serverPos;
        bool hasServerPose;

        public Vector3 ServerPosition => serverPos;
        public bool HasServerPose => hasServerPose;

        public void Receive(double serverTime, Vector3 pos, Quaternion rot)
        {
            serverPos = pos;
            hasServerPose = true;

            if (snaps.Count > 0 && serverTime <= snaps[snaps.Count - 1].time)
                return;

            snaps.Add(new Snap { time = serverTime, pos = pos, rot = rot });
            if (snaps.Count > 12)
                snaps.RemoveAt(0);

            if (!timelineReady)
            {
                timeline = serverTime - BufferSeconds;
                timelineReady = true;
            }
        }

        public void ApplyRemoteInterpolation()
        {
            if (!timelineReady || snaps.Count == 0) return;
            if (snaps.Count == 1)
            {
                transform.SetPositionAndRotation(snaps[0].pos, snaps[0].rot);
                return;
            }

            double newest = snaps[snaps.Count - 1].time;
            double target = newest - BufferSeconds;
            double drift = target - timeline;
            timeline += Time.deltaTime * (1.0 + System.Math.Max(-0.15, System.Math.Min(0.15, drift * 0.8)));

            Snap from = snaps[0];
            Snap to = snaps[snaps.Count - 1];
            for (int i = 0; i < snaps.Count - 1; i++)
            {
                if (snaps[i].time <= timeline && snaps[i + 1].time >= timeline)
                {
                    from = snaps[i];
                    to = snaps[i + 1];
                    break;
                }
            }

            float t = 0f;
            double span = to.time - from.time;
            if (span > 0.0001)
                t = Mathf.Clamp01((float)((timeline - from.time) / span));

            transform.SetPositionAndRotation(
                Vector3.Lerp(from.pos, to.pos, t),
                Quaternion.Slerp(from.rot, to.rot, t));
        }
    }
}
