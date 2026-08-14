using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 仅本地玩家:采集键鼠输入,折算成世界空间移动意图后通过 Command 上报服务端。
    /// WASD/方向键移动(相对相机朝向),空格跳跃,Q 上举,E 前伸抓取,R 投掷。
    /// </summary>
    public class NetPlayerInput : NetworkBehaviour
    {
        [Tooltip("输入心跳发送间隔(秒),输入变化时立即发送")]
        public float HeartbeatInterval = 0.1f;

        NetPlayerMotor motor;
        Vector3 lastSentDir;
        byte lastSentReach;
        float lastSendTime;

        void Awake()
        {
            motor = GetComponent<NetPlayerMotor>();
        }

        void Update()
        {
            if (!isLocalPlayer) return;

            Vector2 inputValue = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) inputValue.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) inputValue.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) inputValue.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) inputValue.x += 1f;
            inputValue.Normalize();

            Vector3 worldDir = Vector3.zero;
            if (inputValue != Vector2.zero)
            {
                float camYaw = Camera.main ? Camera.main.transform.eulerAngles.y : 0f;
                worldDir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(inputValue.x, 0f, inputValue.y);
            }

            byte reach = 0;
            if (Input.GetKey(KeyCode.Q)) reach = (byte)NetPlayerGrab.EReachingAction.Up;
            else if (Input.GetKey(KeyCode.E)) reach = (byte)NetPlayerGrab.EReachingAction.Forward;

            bool changed = (worldDir - lastSentDir).sqrMagnitude > 0.0001f || reach != lastSentReach;
            if (changed || Time.time - lastSendTime > HeartbeatInterval)
            {
                motor.CmdSetInput(worldDir, reach);
                lastSentDir = worldDir;
                lastSentReach = reach;
                lastSendTime = Time.time;
            }

            if (Input.GetKeyDown(KeyCode.Space)) motor.CmdJump();
            if (Input.GetKeyDown(KeyCode.R)) motor.CmdThrow();
        }
    }
}
