using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// テスト用のフリーカメラ（新 Input System 対応）。
/// カメラを自由に飛ばしてシーンを確認できる。
///
/// 操作:
///   右ドラッグ      : 視点を回す（マウスルック）
///   W / A / S / D  : 前後左右へ移動
///   Q / E          : 下降 / 上昇
///   Shift          : 加速（ダッシュ）
///   マウスホイール   : 移動速度を増減
///
/// カメラ（Main Camera など）にアタッチして使う。
/// </summary>
public class FreeCameraController : MonoBehaviour
{
    [Header("移動")]
    [Tooltip("通常の移動速度 (units/sec)")]
    public float moveSpeed = 8f;

    [Tooltip("Shift 押下時の速度倍率")]
    public float sprintMultiplier = 3f;

    [Tooltip("ホイールで変化する速度の最小/最大")]
    public float minSpeed = 1f;
    public float maxSpeed = 50f;

    [Header("視点")]
    [Tooltip("マウス感度")]
    public float lookSensitivity = 0.1f;

    [Tooltip("上下の視点角度の制限（度）")]
    public float pitchLimit = 89f;

    [Tooltip("右ボタンを押している間だけ視点を回す")]
    public bool requireRightMouseToLook = true;

    float yaw;
    float pitch;

    void Start()
    {
        // 現在の向きから初期角度を取得
        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x;
    }

    void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null || keyboard == null) return;

        HandleLook(mouse);
        HandleSpeedScroll(mouse);
        HandleMovement(keyboard);
    }

    void HandleLook(Mouse mouse)
    {
        bool looking = !requireRightMouseToLook || mouse.rightButton.isPressed;
        if (!looking) return;

        Vector2 delta = mouse.delta.ReadValue();
        yaw += delta.x * lookSensitivity;
        pitch -= delta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void HandleSpeedScroll(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            moveSpeed = Mathf.Clamp(moveSpeed + scroll * 0.01f, minSpeed, maxSpeed);
        }
    }

    void HandleMovement(Keyboard keyboard)
    {
        Vector3 dir = Vector3.zero;
        if (keyboard.wKey.isPressed) dir += Vector3.forward;
        if (keyboard.sKey.isPressed) dir += Vector3.back;
        if (keyboard.dKey.isPressed) dir += Vector3.right;
        if (keyboard.aKey.isPressed) dir += Vector3.left;
        if (keyboard.eKey.isPressed) dir += Vector3.up;
        if (keyboard.qKey.isPressed) dir += Vector3.down;

        if (dir == Vector3.zero) return;

        float speed = moveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
            speed *= sprintMultiplier;

        // ローカル方向（カメラの向き基準）で移動
        transform.Translate(dir.normalized * speed * Time.deltaTime, Space.Self);
    }
}
