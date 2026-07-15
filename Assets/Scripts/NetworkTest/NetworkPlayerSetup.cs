using FishNet.Object;
using UnityEngine;

/// ネットワーク上の Player をオーナー専用にセットアップする。
///
/// プレハブ側のコンポーネントは「有効」のままにしておくこと。
/// （単体プレイ用シーンではこのスクリプトの OnStart～ が呼ばれないため、そのまま従来通り動く）
///
/// ネットワークでスポーンされたときだけ以下を行う:
///   - オーナーのクライアント : そのまま操作・カメラ・音声が有効。カーソルをロックする。
///   - サーバー / 他クライアント : 入力・カメラ・音声を無効化する。位置は NetworkTransform が同期する。
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Tooltip("オーナー以外では無効化するコンポーネント（PlayerController / Camera / AudioListener など）")]
    [SerializeField] private Behaviour[] ownerOnlyComponents;

    [Tooltip("オーナー以外ではキネマティックにする Rigidbody（任意）。物理で落下・干渉するのを防ぐ。")]
    [SerializeField] private Rigidbody bodyToFreeze;

    [Tooltip("オーナー以外では無効化する CharacterController（任意）。NetworkTransform との位置競合（ブルブル）を防ぐ。")]
    [SerializeField] private CharacterController characterController;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            DisableForNonOwner();
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // クライアントを兼ねないヘッドレスサーバーでは、入力・カメラ・音声はどのPlayerでも不要。
        // （OnStartServer 内では IsOwner を参照できない ─ FN0007。
        //   Host の場合は下の OnStartClient 側で IsOwner を見て制御する）
        if (IsServerOnlyStarted)
            DisableForNonOwner();
    }

    private void DisableForNonOwner()
    {
        if (ownerOnlyComponents != null)
        {
            foreach (Behaviour c in ownerOnlyComponents)
                if (c != null) c.enabled = false;
        }

        if (bodyToFreeze != null)
            bodyToFreeze.isKinematic = true;

        // CharacterController が有効だと NetworkTransform の位置書き込みと衝突解決が競合し振動する。
        if (characterController != null)
            characterController.enabled = false;
    }
}
