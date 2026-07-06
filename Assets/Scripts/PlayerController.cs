using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]             // CharacterControllerコンポーネントの自動追加
public class PlayerController : MonoBehaviour
{
    [Header("移動")]
    [SerializeField] private float moveSpeed = 6f;          // 水平方向の速度 (m/s)

    [Header("マウス視点")]
    [SerializeField] private float mouseSensitivity = 0.1f; // 左右の感度
    [SerializeField] private float pitchSensitivity = 0.1f; // 上下の感度
    [SerializeField] private bool lockCursor = true;        // 実行中カーソルを中央に固定

    [Header("ジャンプ / 重力")]
    [SerializeField] private float jumpHeight = 1.5f;       // ジャンプの最高到達高さ (m)
    [SerializeField] private float gravity = -20f;          // 重力加速度 (m/s^2, 下向きなので負)

    [Header("三人称カメラ（Player の子カメラを自動制御）")]
    [SerializeField] private float cameraDistance = 6f;     // カメラの後方距離
    [SerializeField] private float cameraHeight = 2f;       // 注視点の高さ
    [SerializeField] private float minPitch = -30f;         // 見上げの限界（負=見上げ）
    [SerializeField] private float maxPitch = 70f;          // 見下ろしの限界

    private CharacterController controller;
    private Transform cameraTransform;  // 子として付いているカメラ（自動検出）
    private float yaw;                  // 現在の向き（Y軸回転, 度）
    private float pitch = 20f;          // 現在のカメラ上下角（度）
    private float verticalVelocity;     // 上下方向の速度 (m/s)
    private bool skipFirstMouse = true; // 開始直後のマウスの跳ねを1フレーム無視

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;  // シーン上でキャラが向いている左右の角度を初期値として保存

        Camera childCamera = GetComponentInChildren<Camera>();
        if (childCamera != null)
        {
            cameraTransform = childCamera.transform;
        }
        else
        {
            Debug.LogWarning("PlayerController: 子にカメラが見つかりません。Main Camera を Player の子にしてください。", this);
        }
    }

    private void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        UpdateLook();

        Vector3 horizontalMove = ReadMoveInput() * moveSpeed;
        UpdateVerticalVelocity();

        // 水平移動と上下移動を合成して1回で動かす
        Vector3 velocity = horizontalMove;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    // 移動が終わった後にカメラを合わせたいのでLateUpdateを使う
    private void LateUpdate()
    {
        ApplyCameraPitch();
    }

    /// マウスで向きとカメラ上下を更新する。
    private void UpdateLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 delta = mouse.delta.ReadValue();
        if (skipFirstMouse) // 最初のフレームのマウス入力を無視するため(無視しないとカメラがブレる)
        {
            skipFirstMouse = false;
        }
        else
        {
            yaw += delta.x * mouseSensitivity;                                           // 左右の向き
            pitch = Mathf.Clamp(pitch - delta.y * pitchSensitivity, minPitch, maxPitch); // 上下でカメラ
        }

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    /// 子カメラを Pitch に合わせて後方＋上下に配置する（向きと位置は親から継承）。
    private void ApplyCameraPitch()
    {
        if (cameraTransform == null)
        {
            return;
        }

        Quaternion localRot = Quaternion.Euler(pitch, 0f, 0f);
        cameraTransform.localPosition = Vector3.up * cameraHeight - localRot * Vector3.forward * cameraDistance;
        cameraTransform.localRotation = localRot;
    }

    /// WASD を向いている方向基準の水平移動ベクトル（大きさ 0〜1）に変換する。
    private Vector3 ReadMoveInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector3.zero;
        }

        float strafe = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

        // transform.right / forward は水平（ピッチしていない）なので平面移動になる
        Vector3 move = transform.right * strafe + transform.forward * forward;

        // 斜め移動が速くならないよう、大きさが1を超えたら正規化する
        return move.sqrMagnitude > 1f ? move.normalized : move;
    }

    // 接地判定に応じてジャンプ・重力を上下速度に反映する。
    private void UpdateVerticalVelocity()
    {
        if (controller.isGrounded)
        {
            verticalVelocity = -1f; // 接地中は地面に軽く押し付けて張り付かせる

            bool jumpPressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (jumpPressed)
            {
                // v = sqrt(2 * h * g) で目標の高さに届く初速を求める
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime; // 空中では重力で加速
        }
    }
}
