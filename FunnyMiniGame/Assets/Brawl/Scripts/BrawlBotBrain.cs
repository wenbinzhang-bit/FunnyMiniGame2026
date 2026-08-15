using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 服务端 Bot：没人抱电脑就去捡，有人抱就追着打，自己抱着就躲开。
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class BrawlBotBrain : MonoBehaviour
    {
        public const int MaxBots = 3;

        public static int AliveCount { get; private set; }

        NetFAnnequinController self;
        float nextThink;

        void Awake()
        {
            self = GetComponent<NetFAnnequinController>();
            var mouse = GetComponent<FAnnequinMouseActions>();
            if (mouse != null) mouse.enabled = false;
        }

        void OnEnable()
        {
            AliveCount++;
        }

        void OnDisable()
        {
            AliveCount = Mathf.Max(0, AliveCount - 1);
        }

        void Update()
        {
            if (!NetworkServer.active || self == null) return;
            if (Time.time < nextThink) return;
            nextThink = Time.time + 0.12f;
            Think();
        }

        void Think()
        {
            if (!self.InputActive || self.IsDead || self.IsKnockedDown || self.IsGrabbed)
            {
                self.ServerBotSetMove(Vector3.zero);
                return;
            }

            if (self.IsHoldingComputer)
            {
                self.ServerBotSetMove(FleeDir(), true);
                return;
            }

            KpiComputerObjective computer = NearestComputer();
            if (computer != null && !computer.IsHeld)
            {
                Vector3 to = Flat(computer.transform.position - self.transform.position);
                if (to.magnitude <= 1.7f)
                {
                    self.ServerBotFace(to);
                    self.ServerBotSetMove(Vector3.zero);
                    self.ServerBotTryPickup();
                }
                else
                {
                    self.ServerBotSetMove(to.normalized);
                }

                return;
            }

            NetFAnnequinController holder = computer != null
                ? FindByNetId(computer.HolderNetId)
                : NearestHolder();
            if (holder != null && holder != self)
            {
                Vector3 to = Flat(holder.transform.position - self.transform.position);
                if (to.magnitude <= 2.4f)
                {
                    self.ServerBotFace(to);
                    self.ServerBotSetMove(Vector3.zero);
                    self.ServerBotPunch();
                }
                else
                {
                    self.ServerBotSetMove(to.normalized, to.magnitude > 3f);
                }

                return;
            }

            if (computer != null)
                self.ServerBotSetMove(Flat(computer.transform.position - self.transform.position).normalized);
        }

        Vector3 FleeDir()
        {
            Vector3 away = Vector3.zero;
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other == null || other == self) continue;
                Vector3 delta = Flat(self.transform.position - other.transform.position);
                float dist = Mathf.Max(0.4f, delta.magnitude);
                away += delta.normalized / dist;
            }

            if (away.sqrMagnitude < 0.01f)
                away = Flat(self.transform.right + self.transform.forward);
            return away.normalized;
        }

        KpiComputerObjective NearestComputer()
        {
            KpiComputerObjective best = null;
            float bestDist = float.MaxValue;
            foreach (KpiComputerObjective computer in FindObjectsOfType<KpiComputerObjective>())
            {
                if (computer == null) continue;
                float dist = (computer.transform.position - self.transform.position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = computer;
                }
            }

            return best;
        }

        NetFAnnequinController NearestHolder()
        {
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other != null && other != self && other.IsHoldingComputer)
                    return other;
            }

            return null;
        }

        static NetFAnnequinController FindByNetId(uint netId)
        {
            if (netId == 0u) return null;
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other != null && other.NetId == netId)
                    return other;
            }

            return null;
        }

        static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
