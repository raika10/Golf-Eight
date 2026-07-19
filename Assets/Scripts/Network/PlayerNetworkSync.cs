using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace GolfEight.Network
{
    /// PlayerController の isLocalPlayer を FishNet の所有権（IsOwner）に合わせて切り替えるだけの薄いアダプタ。
    /// ONLINE_DESIGN.md の「IsOwnerに置き換えればほぼ流用可」に対応する最小実装で、PlayerController.cs 自体は変更しない。
    /// 併せて、Player プレハブが各インスタンスに専用カメラ／AudioListenerを持つ構成のため、
    /// 所有者以外のインスタンスではそれらを無効化する（有効なままだと全クライアントで複数カメラが競合し、
    /// 誰か1人の視点だけが全員に表示されてしまう）。
    [RequireComponent(typeof(PlayerController))]
    public class PlayerNetworkSync : NetworkBehaviour
    {
        private PlayerController playerController;
        private Camera playerCamera;
        private AudioListener playerAudioListener;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerCamera = GetComponentInChildren<Camera>(includeInactive: true);
            if (playerCamera != null)
            {
                playerAudioListener = playerCamera.GetComponent<AudioListener>();
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyOwnership();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            playerController.SetLocalPlayer(IsOwner);
            if (playerCamera != null)
            {
                playerCamera.enabled = IsOwner;
            }
            if (playerAudioListener != null)
            {
                playerAudioListener.enabled = IsOwner;
            }
        }
    }
}
