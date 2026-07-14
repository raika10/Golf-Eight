using UnityEngine;
using UnityEngine.InputSystem;

/// プレイヤーの操作・移動・カメラ・スイングの見た目を担当する。
/// ・WASD で移動、マウスで視点、移動は CharacterController が行う
/// ・見た目は子の Mixamo モデル（Animator）。スイング中は見た目のモデルだけ横向きにする（本体・カメラ・当たり判定には影響しない）
/// ・isLocalPlayer=false のプレイヤー（他プレイヤー/ダミー）は入力もカメラも扱わない
/// ・倒れている間は RagdollController から SetRagdollMode で操作を止め、カメラだけ動かせるようにする
///
/// Player（CharacterController付き）に付ける。打撃時のスイングは BallHitController が制御する。
[RequireComponent(typeof(CharacterController))]             // CharacterControllerコンポーネントの自動追加
public class PlayerController : MonoBehaviour
{
    [Header("操作")]
    [SerializeField] private bool isLocalPlayer = true;     // この画面で操作するプレイヤーか。falseなら入力もカメラも扱わない（他プレイヤー/ダミー）

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
    [SerializeField] private float cameraSmoothTime = 0.05f; // カメラ位置のスムージング (s)。CharacterControllerの微振動でブレるのを抑える

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
    private Vector3 cameraVelocity;     // カメラ位置スムージング用の速度バッファ
    private bool cameraSnapped;         // 初回だけカメラを目標位置へ瞬間移動させる
    private bool ragdollMode;           // 倒れている間か（操作・アニメを止め、カメラを固定する）
    private bool actionLocked;          // 空振りの後隙などで一時的に動けないか（見回しは可）
    private float moveSpeedMultiplier = 1f; // 移動速度の倍率（チャージ中に落とす等。1=通常）
    private bool swingHold;             // スイング構えを保持中か（true の間はジャンプで構えを崩さない）
    private Transform ragdollFollow;    // 倒れている間の参照（今は未使用。将来の追従用）
    private Vector3 ragdollPivot;       // 倒れた瞬間の注視点。倒れている間はここを軸にカメラを回す

    /// 倒れている間の状態を切り替える。倒れている間は操作を受け付けないが、
    /// カメラは「倒れる直前の位置」を軸に、通常と同じ視点操作（オービット）ができる。
    public void SetRagdollMode(bool on, Transform followTarget)
    {
        ragdollMode = on;
        ragdollFollow = followTarget;

        if (on)
        {
            // 吹っ飛ばされる直前の注視点を覚えて、その場を軸にする（体は追わない）
            ragdollPivot = transform.position + Vector3.up * cameraHeight;
        }
        else
        {
            cameraVelocity = Vector3.zero; // 復帰時のスムージングをリセット
        }
    }

    /// 狙い/移動の基準になる向き（度）。見た目のスイング横向きは含まない論理的な向き。
    public float AimYaw => yaw;

    /// 地面に接しているか（空中ではチャージできない等の判定に使う）。
    public bool IsGrounded => controller != null && controller.isGrounded;

    /// この画面で操作するプレイヤーか。false のプレイヤーは入力を受け付けない。
    public bool IsLocalPlayer => isLocalPlayer;

    /// スイング中だけ、見た目のキャラを横向き（ゴルフの構え）にするための角度を設定する。
    /// 0 で通常向きに戻る。狙い・移動・カメラには影響しない（見た目だけ）。
    public void SetSwingBodyOffset(float degrees)
    {
        swingVisualTarget = degrees;
    }

    /// 空振りの後隙などで一時的に動けなくする（見回し・重力は効く）。BallHitController から呼ぶ。
    public void SetActionLocked(bool locked)
    {
        actionLocked = locked;
    }

    /// 移動速度の倍率を設定する（チャージ中に遅くする等）。1で通常。BallHitController から呼ぶ。
    public void SetMoveSpeedMultiplier(float multiplier)
    {
        moveSpeedMultiplier = multiplier;
    }

    /// スイングの構え保持を切り替える。true の間はジャンプしてもジャンプアニメを出さず、構えを崩さない。
    public void SetSwingHold(bool on)
    {
        swingHold = on;
    }

    /// 操作・カメラの担当をこのプレイヤーに切り替える／外す（デバッグ用の視点切替などに使う）。
    /// on にする時はカメラを取り直す（複数プレイヤーで同じ Camera.main を使い回す前提）。
    public void SetLocalPlayer(bool on)
    {
        isLocalPlayer = on;
        if (on)
        {
            AcquireCamera();
            cameraSnapped = false; // 切り替え直後はワープしてよい（新しい位置へスムージングさせない）
        }
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

        if (isLocalPlayer)
        {
            AcquireCamera();
        }
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
        if (isLocalPlayer && lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (ragdollMode)
        {
            // 倒れている間：移動・アニメは止めるが、マウスで周りを見渡せるようにする
            if (isLocalPlayer)
            {
                ReadLookInput();
            }
            return;
        }

        // 他プレイヤー／ダミー：入力は受け付けず、重力で地面に立っているだけ
        if (!isLocalPlayer)
        {
            UpdateVerticalVelocity();
            controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
            UpdateAnimator(0f); // 待機アニメ
            return;
        }

        UpdateLook();

        // 空振りの後隙：移動できない（重力だけ効かせる）。見回しは UpdateLook で可能。
        if (actionLocked)
        {
            UpdateVerticalVelocity();
            controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
            UpdateAnimator(0f);
            return;
        }

        Vector3 moveInput = ReadMoveInput();
        Vector3 horizontalMove = moveInput * (moveSpeed * moveSpeedMultiplier); // チャージ中は倍率で減速
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
        if (!ragdollMode)
        {
            ApplyModelPose(); // 倒れている間は骨を物理に任せるので触らない
        }

        if (!isLocalPlayer)
        {
            return; // 他プレイヤーはカメラを触らない（ローカルのカメラを奪わないように）
        }

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

    /// マウス入力で yaw / pitch（見る向き）だけ更新する。体の回転はしない。
    /// 倒れている間も、これだけ呼べば周りを見渡せる。
    private void ReadLookInput()
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
            return;
        }
        yaw += delta.x * mouseSensitivity;                                           // 左右の向き
        pitch = Mathf.Clamp(pitch - delta.y * pitchSensitivity, minPitch, maxPitch); // 上下でカメラ
    }

    /// マウスで向きとカメラ上下を更新し、体の向きを反映する。
    private void UpdateLook()
    {
        ReadLookInput();

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

        // 注視点。通常はプレイヤーの少し上。倒れている間は「飛んでいる体（腰）」を追う。
        // 視点操作（オービット）は通常と同じで、腰の位置を軸に回る。
        // 体の回転はカメラに伝わらない（yaw/pitchはマウス由来）ので、一緒にぐるぐる回らない。
        Vector3 pivot;
        if (ragdollMode)
        {
            // 吹っ飛んでいる体（腰）に追従。取れなければ倒れた瞬間の位置で固定。
            pivot = ragdollFollow != null ? ragdollFollow.position : ragdollPivot;
        }
        else
        {
            pivot = transform.position + Vector3.up * cameraHeight;
        }
        // yaw（左右）＋pitch（上下）でカメラの向きを決め、その後方 cameraDistance に置く
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 targetPos = pivot - (rot * Vector3.forward) * cameraDistance;

        if (!cameraSnapped)
        {
            // 開始時は目標位置へ即座に合わせる（遠くから滑って来ないように）
            cameraTransform.position = targetPos;
            cameraVelocity = Vector3.zero;
            cameraSnapped = true;
        }
        else
        {
            // CharacterController の微振動でカメラがブレるので、位置だけなめらかに追従させる
            cameraTransform.position = Vector3.SmoothDamp(cameraTransform.position, targetPos, ref cameraVelocity, cameraSmoothTime);
        }
        cameraTransform.rotation = rot; // 向きはマウスに直結（遅れさせない）
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
                // スイング構え中はジャンプのアニメを出さない（クラブを振り上げたまま跳ぶ）。物理的なジャンプはする。
                if (animator != null && !swingHold)
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
