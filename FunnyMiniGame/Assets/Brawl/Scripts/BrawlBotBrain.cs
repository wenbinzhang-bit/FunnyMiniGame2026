using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 服务端 Bot：抢电脑关抱着就逃；甩锅关靠近就砸，看见飞来的电脑就躲。
    /// 甩锅关：背锅后追人右键甩出去；没背锅就躲开持有者。
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class BrawlBotBrain : MonoBehaviour
    {
        public const int MaxBots = 3;

        public static int AliveCount { get; private set; }

        [Header("Obstacle Avoidance")]
        [Min(0.2f)] public float ObstacleProbeDistance = 1.4f;
        [Min(0.05f)] public float ObstacleProbeRadius = 0.28f;
        [Range(20f, 70f)] public float AvoidanceAngle = 42f;
        public LayerMask ObstacleMask = ~0;

        [Header("Stuck Recovery")]
        [Min(0.2f)] public float StuckCheckSeconds = 0.35f;
        [Min(0.02f)] public float StuckMoveDistance = 0.18f;
        [Min(0.2f)] public float RecoverySeconds = 1.1f;
        [Min(0.1f)] public float RecoveryRetargetSeconds = 0.3f;

        static readonly float[] AvoidanceOffsets = { 0f, 1f, -1f, 2f, -2f };

        NetFAnnequinController self;
        float nextThink;
        readonly RaycastHit[] obstacleHits = new RaycastHit[24];

        Vector3 lastProgressPosition;
        Vector3 lastRequestedDirection;
        Vector3 recoveryDirection;
        Vector3 wallContactNormal;
        Vector3 arenaCenter;
        float nextStuckCheck;
        float recoveryUntil;
        float nextRecoveryRetarget;
        float nextArenaCenterRefresh;
        float wallContactUntil;
        int preferredRecoverySide;
        bool hasArenaCenter;

        void Awake()
        {
            self = GetComponent<NetFAnnequinController>();
            var mouse = GetComponent<FAnnequinMouseActions>();
            if (mouse != null) mouse.enabled = false;

            lastProgressPosition = transform.position;
            nextStuckCheck = Time.time + StuckCheckSeconds;
            preferredRecoverySide = (GetInstanceID() & 1) == 0 ? 1 : -1;
        }

        void OnCollisionStay(Collision collision)
        {
            if (!NetworkServer.active || collision == null || !IsNavigationObstacle(collision.collider)) return;

            Vector3 normal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 contactNormal = Flat(collision.GetContact(i).normal);
                if (contactNormal.sqrMagnitude > 0.05f)
                    normal += contactNormal.normalized;
            }

            if (normal.sqrMagnitude < 0.05f) return;
            wallContactNormal = normal.normalized;
            wallContactUntil = Time.time + 0.25f;
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
            if (!self.InputActive || self.IsDead || self.IsKnockedDown || self.IsGrabbed || self.IsCatchStunned)
            {
                StopMoving();
                return;
            }

            if (BrawlGameManager.Instance != null && !BrawlGameManager.Instance.HudIsPlaying)
            {
                StopMoving();
                return;
            }

            if (BrawlGameManager.PassTheBuckActive)
            {
                ThinkPassTheBuck();
                return;
            }

            bool passTheBuck = false;
            bool dumpPhase = false;
            bool behindLeader = self.Score < CurrentLeaderScore();
            if (self.IsHoldingComputer)
            {
                if (passTheBuck && TryDumpComputer(dumpPhase, behindLeader))
                    return;

                MoveSmart(FleeDir(), true);
                return;
            }

            KpiComputerObjective computer = NearestComputer();
            if (computer != null && computer.IsThrown)
            {
                float thrownDistance = Flat(computer.transform.position - self.transform.position).magnitude;
                if (thrownDistance < 6f)
                {
                    DodgeThrown(computer);
                    return;
                }
            }

            if (computer != null && !computer.IsHeld && !computer.IsThrown)
            {
                Vector3 to = Flat(computer.transform.position - self.transform.position);
                if (dumpPhase && !behindLeader)
                {
                    KeepAwayFromPoint(computer.transform.position, 6f);
                    return;
                }

                if (to.magnitude <= 1.7f)
                {
                    self.ServerBotFace(to);
                    StopMoving();
                    self.ServerBotTryPickup();
                }
                else
                {
                    MoveSmart(to, dumpPhase);
                }

                return;
            }

            NetFAnnequinController holder = computer != null
                ? FindByNetId(computer.HolderNetId)
                : NearestHolder();
            if (holder != null && holder != self)
            {
                if (dumpPhase)
                {
                    IsolateDumpHolder(holder, behindLeader);
                    return;
                }

                Vector3 to = Flat(holder.transform.position - self.transform.position);
                if (to.magnitude <= 2f)
                {
                    self.ServerBotFace(to);
                    StopMoving();
                    self.ServerBotPunch();
                }
                else
                {
                    MoveSmart(to, to.magnitude > 3f);
                }

                return;
            }

            if (computer != null)
                MoveSmart(computer.transform.position - self.transform.position);
            else
                StopMoving();
        }

        void ThinkPassTheBuck()
        {
            if (self.IsHoldingComputer)
            {
                if (self.IsCatchStunned)
                {
                    StopMoving();
                    return;
                }

                NetFAnnequinController target = NearestLivingOther();
                if (target == null)
                {
                    MoveSmart(FleeDir(), true);
                    return;
                }

                Vector3 to = Flat(target.transform.position - self.transform.position);
                self.ServerBotFace(to);
                if (to.magnitude <= NetFAnnequinController.PassBuckMaxDistance)
                {
                    StopMoving();
                    self.ServerBotPassBuck(target);
                    return;
                }

                MoveSmart(to, true);
                return;
            }

            NetFAnnequinController holder = NearestHolder();
            if (holder != null)
            {
                Vector3 away = Flat(self.transform.position - holder.transform.position);
                MoveSmart(away.sqrMagnitude > 0.01f ? away : FleeDir(), true);
                return;
            }

            StopMoving();
        }

        NetFAnnequinController NearestLivingOther()
        {
            NetFAnnequinController best = null;
            float bestDist = float.MaxValue;
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other == null || other == self || other.IsDead || other.IsKnockedDown) continue;
                float dist = Flat(other.transform.position - self.transform.position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = other;
                }
            }

            return best;
        }

        /// <summary>
        /// 在目标方向上做轻量转向。前方有墙时选更畅通的一侧；位移长时间过小时进入脱困状态。
        /// 这里刻意不用 NavMesh，避免每个小游戏场景都要额外烘焙导航数据。
        /// </summary>
        void MoveSmart(Vector3 desiredDirection, bool sprint = false)
        {
            Vector3 desired = Flat(desiredDirection);
            if (desired.sqrMagnitude < 0.001f)
            {
                StopMoving();
                return;
            }

            desired.Normalize();
            bool mustAvoidNow = Time.time < wallContactUntil && wallContactNormal.sqrMagnitude > 0.05f;
            if (mustAvoidNow)
            {
                Vector3 centerDirection = ArenaCenterDirection();
                desired = desired * 0.2f + wallContactNormal * 1.2f;
                if (centerDirection.sqrMagnitude > 0.1f)
                    desired += centerDirection * 0.25f;
                desired.Normalize();
            }

            UpdateStuckState(desired);

            Vector3 move;
            if (mustAvoidNow)
            {
                // 已经贴墙时物理接触法线最可靠，立即离墙，不等待“卡住”计时。
                recoveryUntil = 0f;
                move = desired;
            }
            else if (Time.time < recoveryUntil)
            {
                if (Time.time >= nextRecoveryRetarget || IsBlocked(recoveryDirection, ObstacleProbeDistance * 0.7f))
                {
                    recoveryDirection = ChooseRecoveryDirection(desired);
                    nextRecoveryRetarget = Time.time + RecoveryRetargetSeconds;
                }

                move = recoveryDirection;
            }
            else
            {
                move = SteerAroundObstacle(desired);
            }

            lastRequestedDirection = move;
            self.ServerBotSetMove(move, sprint);
        }

        void StopMoving()
        {
            self.ServerBotSetMove(Vector3.zero);
            lastRequestedDirection = Vector3.zero;
            lastProgressPosition = transform.position;
            nextStuckCheck = Time.time + StuckCheckSeconds;
            recoveryUntil = 0f;
        }

        void UpdateStuckState(Vector3 desired)
        {
            if (Time.time < nextStuckCheck) return;

            float moved = Flat(transform.position - lastProgressPosition).magnitude;
            if (lastRequestedDirection.sqrMagnitude > 0.1f && moved < StuckMoveDistance)
            {
                preferredRecoverySide = -preferredRecoverySide;
                recoveryDirection = ChooseRecoveryDirection(desired);
                recoveryUntil = Time.time + RecoverySeconds;
                nextRecoveryRetarget = Time.time + RecoveryRetargetSeconds;
            }

            lastProgressPosition = transform.position;
            nextStuckCheck = Time.time + StuckCheckSeconds;
        }

        Vector3 SteerAroundObstacle(Vector3 desired)
        {
            if (!IsBlocked(desired, ObstacleProbeDistance)) return desired;

            Vector3 centerDirection = ArenaCenterDirection();
            Vector3 best = desired;
            float bestScore = float.MinValue;

            foreach (float offset in AvoidanceOffsets)
            {
                float angle = offset * AvoidanceAngle;
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * desired;
                float clearance = Clearance(candidate, ObstacleProbeDistance * 1.45f);
                float score = clearance * 2f + Vector3.Dot(candidate, desired) * 0.5f;

                if (centerDirection.sqrMagnitude > 0.1f)
                    score += Vector3.Dot(candidate, centerDirection) * 0.35f;

                // 左右同样畅通的时候，让不同 Bot 偏向不同侧，避免挤成一团。
                if (Mathf.Sign(offset) == preferredRecoverySide)
                    score += 0.06f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best.normalized;
        }

        Vector3 ChooseRecoveryDirection(Vector3 desired)
        {
            Vector3 centerDirection = ArenaCenterDirection();
            Vector3 best = -desired;
            float bestScore = float.MinValue;

            ScoreRecoveryCandidate(Quaternion.AngleAxis(85f * preferredRecoverySide, Vector3.up) * desired,
                desired, centerDirection, 0.18f, ref best, ref bestScore);
            ScoreRecoveryCandidate(Quaternion.AngleAxis(-85f * preferredRecoverySide, Vector3.up) * desired,
                desired, centerDirection, 0f, ref best, ref bestScore);
            ScoreRecoveryCandidate(Quaternion.AngleAxis(135f * preferredRecoverySide, Vector3.up) * desired,
                desired, centerDirection, 0.08f, ref best, ref bestScore);
            ScoreRecoveryCandidate(-desired, desired, centerDirection, 0f, ref best, ref bestScore);

            if (centerDirection.sqrMagnitude > 0.1f)
            {
                ScoreRecoveryCandidate(centerDirection, desired, centerDirection, 0.35f,
                    ref best, ref bestScore);
                ScoreRecoveryCandidate(Quaternion.AngleAxis(45f * preferredRecoverySide, Vector3.up) * centerDirection,
                    desired, centerDirection, 0.2f, ref best, ref bestScore);
            }

            return best.normalized;
        }

        void ScoreRecoveryCandidate(Vector3 candidate, Vector3 desired, Vector3 centerDirection,
            float bonus, ref Vector3 best, ref float bestScore)
        {
            candidate = Flat(candidate).normalized;
            float clearance = Clearance(candidate, ObstacleProbeDistance * 1.8f);
            float score = clearance * 2f + Vector3.Dot(candidate, desired) * 0.12f + bonus;
            if (centerDirection.sqrMagnitude > 0.1f)
                score += Vector3.Dot(candidate, centerDirection) * 0.8f;

            if (score <= bestScore) return;
            bestScore = score;
            best = candidate;
        }

        bool IsBlocked(Vector3 direction, float distance)
        {
            return Clearance(direction, distance) < distance * 0.92f;
        }

        float Clearance(Vector3 direction, float distance)
        {
            direction = Flat(direction);
            if (direction.sqrMagnitude < 0.001f) return 0f;
            direction.Normalize();

            Vector3 origin = transform.position + Vector3.up * 0.72f;
            int count = Physics.SphereCastNonAlloc(origin, ObstacleProbeRadius, direction, obstacleHits,
                distance, EffectiveObstacleMask(), QueryTriggerInteraction.Ignore);

            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = obstacleHits[i].collider;
                if (!IsNavigationObstacle(hitCollider)) continue;
                nearest = Mathf.Min(nearest, obstacleHits[i].distance);
            }

            return nearest;
        }

        int EffectiveObstacleMask()
        {
            // 旧 Prefab/运行时 AddComponent 在某些 Unity 序列化情况下可能把新 LayerMask 留成 0。
            return ObstacleMask.value == 0 ? Physics.DefaultRaycastLayers : ObstacleMask.value;
        }

        bool IsNavigationObstacle(Collider hitCollider)
        {
            if (hitCollider == null || hitCollider.isTrigger) return false;

            // 玩家和争夺物不是墙；追击时不能因为目标本身而绕开。
            if (hitCollider.GetComponentInParent<NetFAnnequinController>() != null) return false;
            if (hitCollider.GetComponentInParent<KpiComputerObjective>() != null) return false;
            return true;
        }

        Vector3 ArenaCenterDirection()
        {
            if (Time.time >= nextArenaCenterRefresh)
                RefreshArenaCenter();

            return hasArenaCenter
                ? Flat(arenaCenter - transform.position).normalized
                : Vector3.zero;
        }

        void RefreshArenaCenter()
        {
            nextArenaCenterRefresh = Time.time + 5f;

            BrawlAirWall wall = BrawlAirWall.Instance;
            if (wall == null)
                wall = FindObjectOfType<BrawlAirWall>(true);
            if (wall != null)
            {
                arenaCenter = wall.transform.position;
                hasArenaCenter = true;
                return;
            }

            NetworkStartPosition[] starts = FindObjectsOfType<NetworkStartPosition>();
            if (starts.Length > 0)
            {
                Vector3 total = Vector3.zero;
                foreach (NetworkStartPosition start in starts)
                    total += start.transform.position;

                arenaCenter = total / starts.Length;
                hasArenaCenter = true;
                return;
            }

            KpiComputerObjective computer = NearestComputer();
            if (computer != null && !computer.IsHeld)
            {
                arenaCenter = computer.transform.position;
                hasArenaCenter = true;
                return;
            }

            hasArenaCenter = false;
        }

        bool TryDumpComputer(bool dumpPhase, bool behindLeader)
        {
            NetFAnnequinController target = dumpPhase && behindLeader
                ? HighestOther() ?? NearestOther()
                : HighestOtherWithin(3.6f) ?? NearestOther();
            float range = dumpPhase ? 8.5f : 3.6f;

            if (TryThrowAt(target, range, dumpPhase && !behindLeader))
                return true;

            if (dumpPhase && target != null)
            {
                Vector3 to = Flat(target.transform.position - self.transform.position);
                self.ServerBotFace(to);
                MoveSmart(to, true);
                return true;
            }

            return false;
        }

        bool TryThrowAt(NetFAnnequinController target, float maxRange, bool throwEvenIfAlone)
        {
            Vector3 throwDir = self.transform.forward;
            if (target != null)
            {
                Vector3 to = Flat(target.transform.position - self.transform.position);
                if (to.magnitude > maxRange) return false;
                if (to.sqrMagnitude > 0.01f)
                    throwDir = to.normalized;
                self.ServerBotFace(to);
            }
            else if (!throwEvenIfAlone)
            {
                return false;
            }

            StopMoving();
            self.ServerBotThrowComputer(throwDir);
            return true;
        }

        void IsolateDumpHolder(NetFAnnequinController holder, bool behindLeader)
        {
            NetFAnnequinController keepAway = holder;
            if (behindLeader && holder.Score < CurrentLeaderScore())
                keepAway = HighestOther() ?? holder;

            Vector3 away = Flat(self.transform.position - keepAway.transform.position);
            MoveSmart(away.sqrMagnitude > 0.01f ? away : FleeDir(), true);
        }

        void KeepAwayFromPoint(Vector3 point, float minDistance)
        {
            Vector3 away = Flat(self.transform.position - point);
            if (away.magnitude < minDistance)
                MoveSmart(away.sqrMagnitude > 0.01f ? away : FleeDir(), true);
            else
                StopMoving();
        }

        int CurrentLeaderScore()
        {
            int best = self.Score;
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other == null || other.IsDead) continue;
                if (other.Score > best)
                    best = other.Score;
            }

            return best;
        }

        NetFAnnequinController HighestOther()
        {
            NetFAnnequinController best = null;
            int bestScore = int.MinValue;
            float bestDist = float.MaxValue;
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other == null || other == self || other.IsDead || other.IsKnockedDown) continue;
                float dist = Flat(other.transform.position - self.transform.position).sqrMagnitude;
                if (other.Score > bestScore || (other.Score == bestScore && dist < bestDist))
                {
                    bestScore = other.Score;
                    bestDist = dist;
                    best = other;
                }
            }

            return best;
        }

        NetFAnnequinController HighestOtherWithin(float range)
        {
            NetFAnnequinController leader = HighestOther();
            if (leader == null) return null;
            float dist = Flat(leader.transform.position - self.transform.position).magnitude;
            return dist <= range ? leader : null;
        }

        void DodgeThrown(KpiComputerObjective computer)
        {
            Vector3 toComputer = Flat(computer.transform.position - self.transform.position);
            Vector3 velocity = computer.Body != null
                ? Flat(computer.Body.velocity)
                : Vector3.zero;
            Vector3 away = toComputer.sqrMagnitude > 0.01f
                ? -toComputer.normalized
                : FleeDir();
            Vector3 side = Vector3.Cross(Vector3.up, away).normalized * preferredRecoverySide;
            if (velocity.sqrMagnitude > 0.2f && Vector3.Dot(velocity.normalized, toComputer.normalized) < -0.1f)
                away += side * 1.2f;
            else
                away += side * 0.65f;

            MoveSmart(away, true);
        }

        NetFAnnequinController NearestOther()
        {
            NetFAnnequinController best = null;
            float bestDist = float.MaxValue;
            foreach (NetFAnnequinController other in FindObjectsOfType<NetFAnnequinController>())
            {
                if (other == null || other == self || other.IsDead || other.IsKnockedDown) continue;
                float dist = Flat(other.transform.position - self.transform.position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = other;
                }
            }

            return best;
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
