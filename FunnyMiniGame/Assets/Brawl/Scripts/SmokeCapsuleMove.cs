using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 联机冒烟测试用的胶囊体移动脚本(客户端权威 + NetworkTransform 同步)。
    /// 仅用于验证 Host/Client 连通与对象同步,不用于正式玩法。
    /// </summary>
    public class SmokeCapsuleMove : NetworkBehaviour
    {
        public float Speed = 6f;

        void Update()
        {
            if (!isLocalPlayer) return;

            Vector3 dir = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            transform.position += dir.normalized * Speed * Time.deltaTime;
        }
    }
}
