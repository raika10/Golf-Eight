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

    [Header("アニメーション（未設定なら子のAnimatorを自動取得。無ければ何もしない）")]
    [SerializeField] private Animator animator;             // キャラモデルのAnimator
    [SerializeField] private string speedParam = "Speed";   // 移動量 0(停止)〜1(走り)。Blend Tree で待機/走りを切り替える想定
    [SerializeField] private string groundedParam = "Grounded"; // 接地中か（bool）
    [SerializeField] private string jumpParam = "Jump";     // ジャンプの瞬間（trigger）
    [SerializeField] private float speedDamp = 0.1f;        // Speed の変化をなめらかにする時間 (s)

    [Header("スイング時の体の向き")]
    [SerializeField] private float swingTurnSpeed = 540f; // スイングの構えへ体を回す速さ（度/秒）

    private int speedHash;
    private int groundedHash;
    private int jumpHash;

    private CharacterController controller;
    private Transform cameraTransform;  // 子として付いているカメラ（自動検出）
    private float yaw;                  // 現在の狙い/移動の向き（Y軸回転, 度）※論理的な向き
    private float pitch = 20f;          // 現在のカメラ上下角（度）
    private float verticalVelocity;     // 上下方向の速度 (m/s)
    private bool skipFirstMouse = true; // 開始直後のマウスの跳ねを1フレーム無視
    private float swingVisualOffset;    // 見た目だけ足す横向きの角度（度）。今の値
    private float swingVisualTarget;    // 見た目の横向き角度の目標値（スイング中だけ非0）
    private Transform modelTransform;   // 見た目のモデル（Animatorが付いた子）。ここだけ回す
    private Vector3 modelBaseLocalPos;  // モデルの本来のローカル位置（毎フレームここへ戻してズレを防ぐ）

    /// 狙い/移動の基準になる向き（度）。見た目のスイング横向きは含まない論理的な向き。
    public float AimYaw => yaw;

    /// スイング中だけ、見た目のキャラを横向き（ゴルフの構え）にするための角度を設定する。
    /// 0 で通常向きに戻る。狙い・移動・カメラには影響しない（見た目だけ）。
    public void SetSwingBodyOffset(float degrees)
    {
        swingVisualTarget = degrees;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;  // シーン上でキャラが向いている左右の角度を初期値として保存

        // アニメーター（キャラモデルの子）を自動取得。パラメータ名はハッシュにしておく。
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        if (animator != null)
        {
            // 移動は CharacterController が担当するので、アニメ側の移動（ルートモーション）は切る。
            // これを切らないと、走りアニメがキャラを勝手に動かして「カタカタ」＋カメラとのズレの原因になる。
            animator.applyRootMotion = false;
            // 物理更新（Animate Physics）だと揺れることがあるので通常更新に固定
            animator.updateMode = AnimatorUpdateMode.Normal;
            // 見た目のモデル＝Animatorが付いているオブジェクト。スイングの横向きはここだけ回す。
            // 元のローカル位置を覚えておき、毎フレーム戻して本体からズレないようにする。
            modelTransform = animator.transform;
            modelBaseLocalPos = modelTransform.localPosition;
        }
        speedHash = Animator.StringToHash(speedParam);
        groundedHash = Animator.StringToHash(groundedParam);
        jumpHash = Animator.StringToHash(jumpParam);

        AcquireCamera();
    }

    /// 使うカメラを取得する。子にあればそれ、無ければシーンのメインカメラ（Camera.main）。
    /// カメラの位置合わせはワールド空間で行うので、子でなくても追従する。
    private void AcquireCamera()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null)
        {
            cam = Camera.main;
        }
        cameraTransform = cam != null ? cam.transform : null;
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

        Vector3 moveInput = ReadMoveInput();
        Vector3 horizontalMove = moveInput * moveSpeed;
        UpdateVerticalVelocity();

        // 水平移動と上下移動を合成して1回で動かす
        Vector3 velocity = horizontalMove;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        UpdateAnimator(moveInput.magnitude);
    }

    /// 移動量と接地状態をAnimatorに渡す（待機↔走りの切り替え用）。Animatorが無ければ何もしない。
    private void UpdateAnimator(float moveAmount)
    {
        if (animator == null)
        {
            return;
        }
        // moveAmount は 0(停止)〜1(全力移動)。damp でなめらかに変化させる。
        animator.SetFloat(speedHash, moveAmount, speedDamp, Time.deltaTime);
        animator.SetBool(groundedHash, controller.isGrounded);
    }

    // Animator は Update の後に動くので、モデルの位置合わせ・カメラは LateUpdate で行う
    private void LateUpdate()
    {
        ApplyModelPose();

        if (cameraTransform == null)
        {
            AcquireCamera(); // まだ取れていなければ探し直す
        }
        UpdateCamera();
    }

    /// 見た目のモデル（子）の位置と向きを、Animator の後に確定させる。
    /// ・localPosition を毎フレーム元に戻す → ルートモーション等でモデルが本体から前へズレるのを防ぐ
    /// ・localRotation でスイング中の横向きを適用する（本体・カメラ・当たり判定には影響しない）
    private void ApplyModelPose()
    {
        if (modelTransform == null || modelTransform == transform)
        {
            return;
        }
        modelTransform.localPosition = modelBaseLocalPos;
        modelTransform.localRotation = Quaternion.Euler(0f, swingVisualOffset, 0f);
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

        // 見た目の横向き（スイングの構え）を目標へなめらかに寄せる
        swingVisualOffset = Mathf.MoveTowardsAngle(swingVisualOffset, swingVisualTarget, swingTurnSpeed * Time.deltaTime);

        if (modelTransform != null && modelTransform != transform)
        {
            // 本体は狙いの向きのまま（カメラ・当たり判定・移動に影響させない）。
            // モデルの位置・向きは Animator の後（LateUpdate）で適用する。
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        else
        {
            // モデルが本体と同じ場合（子モデルが無い）は本体ごと回す
            transform.rotation = Quaternion.Euler(0f, yaw + swingVisualOffset, 0f);
        }
    }

    /// カメラをプレイヤーの後方＋上に、ワールド空間で配置する（親子関係に依存しない）。
    /// これにより、カメラが Player の子でなくても本体と一体で追従し、先行しない。
    private void UpdateCamera()
    {
        if (cameraTransform == null)
        {
            return;
        }

        // 注視点（プレイヤーの少し上）
        Vector3 pivot = transform.position + Vector3.up * cameraHeight;
        // yaw（左右）＋pitch（上下）でカメラの向きを決め、その後方 cameraDistance に置く
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        cameraTransform.position = pivot - (rot * Vector3.forward) * cameraDistance;
        cameraTransform.rotation = rot;
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

        // 論理向き(yaw)基準で移動する。見た目のスイング横向きオフセットは移動に影響させない。
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 move = (yawRot * Vector3.right) * strafe + (yawRot * Vector3.forward) * forward;

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
                if (animator != null)
                {
                    animator.SetTrigger(jumpHash);
                }
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime; // 空中では重力で加速
        }
    }
}
