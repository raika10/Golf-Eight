using UnityEngine;
using UnityEngine.InputSystem;

/// ボールを打つ操作（ステージ2）。
/// ・左クリック長押しで強さを溜め、離すと打つ（強さ＝飛距離）
/// ・マウス左右 または A/D キーで打つ方向（水平の向き）を変える
/// ・loftAngle で打ち上げ角を調整
/// ・範囲内でいちばん近いボールを打てる（所有者は問わない＝誰でも誰のボールを打てる）
/// ・打つ方向が分かるよう「まっすぐの目安線」を出す（跳ね返りまで予測する線はステージ3で置き換える）
///
/// プレイヤー未マージのため、この単体コンポーネントで方向も入力できるようにしてある。
/// プレイヤー合流後は useMouseAim を切って aimCamera を割り当てれば、カメラの向きで狙える。
public class BallHitController : MonoBehaviour
{
    [Header("狙い")]
    [SerializeField] private bool useMouseAim = true;     // マウス左右で向きを変えるか
    [SerializeField] private float mouseYawSpeed = 0.2f;  // マウス感度（度/ピクセル）
    [SerializeField] private float keyYawSpeed = 90f;     // A/D キーの旋回速度（度/秒）
    [SerializeField] private float loftAngle = 20f;       // 打ち上げ角（度）。0=水平, 大きいほど上へ
    [SerializeField] private Camera aimCamera;            // 合流後にカメラ基準で狙う場合に割り当てる

    [Header("対象さがし")]
    [SerializeField] private float hitRange = 5f;         // この距離内のボールを打てる
    [SerializeField] private LayerMask hittableBallMask = ~0; // ボール探索の対象レイヤー
    [SerializeField] private bool onlyHitRestingBall = true;  // 止まっている球だけ打てるか

    [Header("強さ")]
    [SerializeField] private float minPower = 4f;         // 最小の打つ強さ（Impulse）
    [SerializeField] private float maxPower = 22f;        // 最大の打つ強さ
    [SerializeField] private float chargeTime = 1.2f;     // 0→最大まで溜めるのにかかる時間 (s)

    [Header("目安線")]
    [SerializeField] private LineRenderer aimLine;        // 方向の目安線（未設定なら自動生成）
    [SerializeField] private float aimLineMinLength = 1.5f; // 未チャージ時の線の長さ
    [SerializeField] private float aimLineMaxLength = 6f;   // 最大チャージ時の線の長さ

    private float currentYaw;      // 現在の水平の向き（度）
    private float currentCharge;   // 現在の溜め (0..1)
    private bool isCharging;       // 溜め中か
    private GolfBall targetBall;   // いま狙っているボール
    private static readonly Collider[] overlapBuffer = new Collider[32];

    private void Awake()
    {
        if (aimCamera != null)
        {
            currentYaw = aimCamera.transform.eulerAngles.y;
        }
        EnsureAimLine();
    }

    private void Update()
    {
        UpdateAim();
        targetBall = FindTargetBall();
        UpdateCharge();
        UpdateAimLine();
    }

    /// 入力で狙う向き（水平のyaw）を更新する。
    private void UpdateAim()
    {
        // カメラ基準が指定されていればそちらを優先（合流後の使い方）
        if (aimCamera != null && !useMouseAim)
        {
            currentYaw = aimCamera.transform.eulerAngles.y;
            return;
        }

        if (useMouseAim && Mouse.current != null)
        {
            currentYaw += Mouse.current.delta.ReadValue().x * mouseYawSpeed;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            float dir = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            currentYaw += dir * keyYawSpeed * Time.deltaTime;
        }
    }

    /// 左クリックの状態で溜め→発射を進める。
    private void UpdateCharge()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        // 押し始め：狙えるボールがあれば溜め開始
        if (mouse.leftButton.wasPressedThisFrame && CanHit(targetBall))
        {
            isCharging = true;
            currentCharge = 0f;
        }

        if (!isCharging)
        {
            return;
        }

        // 狙っていたボールが打てなくなったら溜めをキャンセル
        if (!CanHit(targetBall))
        {
            isCharging = false;
            currentCharge = 0f;
            return;
        }

        // 押している間、強さが溜まっていく（最大で頭打ち）
        currentCharge = Mathf.Clamp01(currentCharge + Time.deltaTime / Mathf.Max(0.01f, chargeTime));

        // 離した瞬間に打つ
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            targetBall.Hit(GetAimDirection(), CurrentPower());
            isCharging = false;
            currentCharge = 0f;
        }
    }

    /// 範囲内でいちばん近い（打てる）ボールを探す。所有者は問わない。
    private GolfBall FindTargetBall()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, hitRange, overlapBuffer, hittableBallMask, QueryTriggerInteraction.Ignore);
        GolfBall nearest = null;
        float nearestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            GolfBall ball = overlapBuffer[i].GetComponentInParent<GolfBall>();
            if (ball == null)
            {
                continue;
            }

            float sqr = (ball.transform.position - transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = ball;
            }
        }
        return nearest;
    }

    /// そのボールをいま打てるか。
    private bool CanHit(GolfBall ball)
    {
        if (ball == null)
        {
            return false;
        }
        if (onlyHitRestingBall && ball.IsMoving)
        {
            return false;
        }
        return true;
    }

    /// 狙いの向き。水平のyawに loftAngle だけ上向きを足す。
    private Vector3 GetAimDirection()
    {
        Vector3 horizontal = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, horizontal);
        return (Quaternion.AngleAxis(-loftAngle, right) * horizontal).normalized;
    }

    /// 現在の溜めに対応する打つ強さ。
    private float CurrentPower()
    {
        return Mathf.Lerp(minPower, maxPower, currentCharge);
    }

    /// 方向の目安線（まっすぐ）を更新する。溜めるほど長くなる。
    private void UpdateAimLine()
    {
        if (aimLine == null)
        {
            return;
        }

        bool show = CanHit(targetBall);
        aimLine.enabled = show;
        if (!show)
        {
            return;
        }

        float length = isCharging
            ? Mathf.Lerp(aimLineMinLength, aimLineMaxLength, currentCharge)
            : aimLineMinLength;

        Vector3 start = targetBall.transform.position;
        Vector3 end = start + GetAimDirection() * length;
        aimLine.positionCount = 2;
        aimLine.SetPosition(0, start);
        aimLine.SetPosition(1, end);
    }

    /// 目安線用の LineRenderer を用意する（未設定なら子オブジェクトに作る）。
    private void EnsureAimLine()
    {
        if (aimLine != null)
        {
            return;
        }

        GameObject lineObject = new GameObject("AimLine");
        lineObject.transform.SetParent(transform, false);
        aimLine = lineObject.AddComponent<LineRenderer>();
        aimLine.widthMultiplier = 0.08f;
        aimLine.numCapVertices = 4;
        aimLine.useWorldSpace = true;
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.receiveShadows = false;
        aimLine.material = new Material(Shader.Find("Sprites/Default"));
        aimLine.startColor = Color.white;
        aimLine.endColor = new Color(1f, 1f, 1f, 0.2f);
        aimLine.enabled = false;
    }

    // シーンビューで打てる範囲を確認できるように
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hitRange);
    }
}
