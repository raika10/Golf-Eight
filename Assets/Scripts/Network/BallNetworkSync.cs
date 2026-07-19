using FishNet.Object;
using UnityEngine;

namespace GolfEight.Network
{
    /// GolfBall にサーバー権威の物理を持たせるための薄いアダプタ。
    /// ONLINE_DESIGN.md の「ボール＝サーバー権威、NetworkTransformで補間」に対応する。
    /// サーバー（ホスト含む）だけが Rigidbody をシミュレートし、サーバーではないクライアントは
    /// isKinematic にして自前の物理シミュレーションを止め、NetworkTransform（Inspectorで別途追加）
    /// から受け取る位置・回転をそのまま反映するだけにする（二重シミュレーションによるズレを防ぐ）。
    [RequireComponent(typeof(Rigidbody))]
    public class BallNetworkSync : NetworkBehaviour
    {
        private Rigidbody body;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerStarted)
            {
                body.isKinematic = true;
            }
        }
    }
}
