using UnityEngine;

namespace Brawl
{
    /// <summary>对局管理器操作的玩家接口,粘土怪和 FAnnequin 都实现它。</summary>
    public interface IBrawlPlayer
    {
        uint NetId { get; }
        Transform Transform { get; }
        int Score { get; set; }
        bool InputActive { get; set; }
        Vector3 SpawnPosition { get; set; }
        PlayerAttributes Attributes { get; }
        bool IsDead { get; }
        int CharacterIndex { get; }
        void ServerTeleport(Vector3 position);
    }
}
