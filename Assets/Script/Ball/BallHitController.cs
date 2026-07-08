using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// ボールを打つ操作（ステージ2 → プレイヤー合流）。
/// ・左クリック長押しで強さを溜め、離すと打つ（強さ＝飛距離）
/// ・打つ方向は「このオブジェクトが向いている水平方向」。プレイヤーに載せればプレイヤーの向きで狙える
/// ・loftAngle で打ち上げ角を調整
/// ・範囲内でいちばん近いボールを打てる（所有者は問わない＝誰でも誰のボールを打てる）
/// ・打った後の軌道を予測線で表示（跳ね返り込み）
///
/// 使い方（プレイヤーに載せる場合）：Player に付けるだけ。useMouseAim は false のまま、
/// aimSource も未設定でOK（自分の向き＝プレイヤーの向きで狙う）。
/// 単体テスト（プレイヤー無し）で使う場合だけ useMouseAim を true にすると、マウス左右／A・Dで向きを変えられる。
public class BallHitController : MonoBehaviour
{
    [Header("狙い")]
    [SerializeField] private bool useMouseAim = false;    // true=単体テスト用（マウス/ADで向き）。プレイヤーに載せるなら false
    [SerializeField] private float mouseYawSpeed = 0.2f;  // マウス感度（度/ピクセル）※useMouseAim時のみ
    [SerializeField] private float keyYawSpeed = 90f;     // A/D キーの旋回速度（度/秒）※useMouseAim時のみ
    [SerializeField] private float loftAngle = 20f;       // 打ち上げ角（度）。0=水平, 大きいほど上へ
    [SerializeField] private Transform aimSource;         // 向きの基準（未設定なら自分）。カメラ等を割り当ててもよい
    [SerializeField] private float aimSmoothTime = 0.05f; // 狙いのスムージング時間 (s)。マウスの微ブレで予想線が揺れるのを抑える

    [Header("対象さがし")]
    [SerializeField] private float hitRange = 5f;         // この距離内のボールを打てる
    [SerializeField] private LayerMask hittableBallMask = ~0; // ボール探索の対象レイヤー
    [SerializeField] private bool onlyHitRestingBall = true;  // 止まっている球だけ打てるか

    [Header("アドレス（立ち位置の制限）")]
    [SerializeField] private float maxAddressAngle = 75f; // ボールがこの角度以内で正面に見えていないと打てない（背後や真横は不可）
    [SerializeField] private bool addressFromLeft = false; // 本物のゴルフのように、ボールの左側からしか打てないようにする（打てる方向が制限される点に注意）
    [SerializeField] private float chargeCancelBackDistance = 0.25f; // チャージ中にこれ以上ボールから離れたらキャンセル (m)

    [Header("強さ")]
    [SerializeField] private float minPower = 4f;         // 最小の打つ強さ（Impulse）
    [SerializeField] private float maxPower = 22f;        // 最大の打つ強さ
    [SerializeField] private float chargeTime = 1.2f;     // 0→最大まで溜めるのにかかる時間 (s)

    [Header("予測線")]
    [SerializeField] private LineRenderer aimLine;              // 予測線（未設定なら自動生成）
    [SerializeField] private int predictionSteps = 60;         // 予測の分割数（多いほど長く・滑らか）
    [SerializeField] private float predictionTimeStep = 0.04f; // 予測1ステップの時間 (s)
    [SerializeField] private float predictionBounciness = 0.6f;// 跳ね返りで残る速度の割合（GolfBall側と合わせる）
    [SerializeField] private LayerMask predictionObstacleMask = ~0; // 予測で跳ね返る対象（壁・地面など）
    [SerializeField] private bool stopAtFirstBounce = true;    // ゴルフゲーム風に、最初の着地（最初の衝突）で線を止める
    [SerializeField] private float maxPredictionDistance = 30f; // 予測線の最大の長さ (m)。これを超えたら打ち切る

    private float currentYaw;         // 現在の水平の向き（度）※スムージング済み
    private float yawVelocity;        // SmoothDampAngle 用の速度バッファ
    private float currentCharge;      // 現在の溜め (0..1)
    private bool isCharging;          // 溜め中か
    private float chargeStartDistance; // 溜め始めたときのボールまでの距離（後退キャンセル判定用）
    private GolfBall targetBall;      // いま狙っているボール
    private readonly List<Vector3> predictedPoints = new List<Vector3>();
    private static readonly Collider[] overlapBuffer = new Collider[32];

    private Collider[] selfColliders; // 自分（プレイヤー）の当たり判定。予測線でプレイヤーを無視するのに使う

    private void Awake()
    {
        // 自分（プレイヤー）側のコライダー。予測線がプレイヤーで折れないように使う。
        // CharacterController も Collider の一種なので含まれる。
        selfColliders = GetComponentsInChildren<Collider>();
        currentYaw = AimTransform().eulerAngles.y;
        EnsureAimLine();
    }

    /// 狙いの向きの基準になる Transform（未設定なら自分＝載っているオブジェクトの向き）。
    private Transform AimTransform()
    {
        return aimSource != null ? aimSource : transform;
    }

    private void Update()
    {
        UpdateAim();
        targetBall = FindTargetBall();
        UpdateCharge();
        UpdateAimLine();
    }

    /// 狙う向き（水平のyaw）を更新する。
    private void UpdateAim()
    {
        // プレイヤー合流モード：基準（未設定なら自分）の向いている水平方向で狙う。
        // マウス視点の微ブレで予想線が揺れないよう、狙いの角度をスムージング（ローパス）する。
        if (!useMouseAim)
        {
            float targetYaw = AimTransform().eulerAngles.y;
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, aimSmoothTime);
            return;
        }

        // 単体テスト用：マウス左右 と A/D で向きを変える（プレイヤー無しで試すとき）
        if (Mouse.current != null)
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

        // 押し始め：狙えるボールがあれば溜め開始。開始時の距離を覚えておく（後退キャンセル用）
        if (mouse.leftButton.wasPressedThisFrame && CanHit(targetBall))
        {
            isCharging = true;
            currentCharge = 0f;
            chargeStartDistance = FlatDistanceTo(targetBall);
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

        // 溜め中に少し後ろへ下がったらキャンセル（本物のゴルフのように、構えから離れたら仕切り直し）
        if (FlatDistanceTo(targetBall) > chargeStartDistance + chargeCancelBackDistance)
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

    /// 渡されたコライダーが自分（プレイヤー）のものか。予測線でプレイヤーを無視するのに使う。
    private bool IsSelfCollider(Collider c)
    {
        if (c == null || selfColliders == null)
        {
            return false;
        }
        for (int i = 0; i < selfColliders.Length; i++)
        {
            if (selfColliders[i] == c)
            {
                return true;
            }
        }
        return false;
    }

    /// そのボールをいま打てるか。
    private bool CanHit(GolfBall ball)
    {
        if (ball == null || ball.IsHoled)
        {
            return false;
        }
        if (onlyHitRestingBall && ball.IsMoving)
        {
            return false;
        }
        if (!IsAddressedFromCorrectSide(ball))
        {
            return false;
        }
        return true;
    }

    /// 打てる立ち位置か。基本は「打つ方向にボールを正面付近で捉えている」こと（向いた方向へ打てる）。
    /// addressFromLeft=true のときだけ、さらに「ボールが自分の右側にある（＝プレイヤーはボールの左）」を要求する。
    private bool IsAddressedFromCorrectSide(GolfBall ball)
    {
        Vector3 aimFlat = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;

        Vector3 toBall = ball.transform.position - transform.position;
        toBall.y = 0f;
        if (toBall.sqrMagnitude < 1e-6f)
        {
            return true; // ほぼ真上：位置の判定は省略
        }
        Vector3 toBallDir = toBall.normalized;

        // ① ボールを正面付近に捉えているか（背後や真横は不可）。打つ方向にボールがあることを要求。
        float minFrontDot = Mathf.Cos(maxAddressAngle * Mathf.Deg2Rad);
        if (Vector3.Dot(toBallDir, aimFlat) < minFrontDot)
        {
            return false;
        }

        // ② 左側アドレス限定のときだけ、ボールが右側にあること（プレイヤーがボールの左）を要求
        if (addressFromLeft)
        {
            Vector3 aimRight = Vector3.Cross(Vector3.up, aimFlat);
            return Vector3.Dot(toBallDir, aimRight) >= 0f;
        }
        return true;
    }

    /// ボールまでの水平距離（高さは無視）。
    private float FlatDistanceTo(GolfBall ball)
    {
        if (ball == null)
        {
            return float.MaxValue;
        }
        Vector3 d = ball.transform.position - transform.position;
        d.y = 0f;
        return d.magnitude;
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

    /// 予測線を更新する。重力と跳ね返りを含めて、打った後の軌道をシミュレートして描く。
    /// 溜め中はその強さでの着弾予測、狙っているだけのときは最小強さで方向の目安を出す。
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

        // Hit と同じく Impulse なので、打った直後の初速 = 強さ / 質量。
        float power = isCharging ? CurrentPower() : minPower;
        Vector3 start = targetBall.transform.position;
        Vector3 initialVelocity = GetAimDirection() * (power / Mathf.Max(0.0001f, targetBall.Mass));

        SimulateTrajectory(start, initialVelocity, targetBall.Radius, predictedPoints);
        aimLine.positionCount = predictedPoints.Count;
        aimLine.SetPositions(predictedPoints.ToArray());
    }

    /// 重力と跳ね返りを含めてボールの軌道を数値シミュレートし、点列を outPoints に入れる。
    private void SimulateTrajectory(Vector3 start, Vector3 velocity, float radius, List<Vector3> outPoints)
    {
        outPoints.Clear();
        Vector3 pos = start;
        Vector3 vel = velocity;
        float dt = predictionTimeStep;
        float castRadius = Mathf.Max(0.01f, radius * 0.95f); // 壁にめり込む前に検出できるよう少し小さめ
        float traveled = 0f; // 予測線の合計の長さ（長すぎたら打ち切る）

        outPoints.Add(pos);
        for (int i = 0; i < predictionSteps; i++)
        {
            vel += Physics.gravity * dt;
            Vector3 stepVec = vel * dt;
            float dist = stepVec.magnitude;
            if (dist < 1e-5f)
            {
                break;
            }

            Vector3 dir = stepVec / dist;
            if (Physics.SphereCast(pos, castRadius, dir, out RaycastHit hit, dist, predictionObstacleMask, QueryTriggerInteraction.Ignore)
                && !IsSelfCollider(hit.collider))
            {
                // 最初の着地点（最初の衝突）を線の終点にする＝ゴルフゲーム風の短い予測線
                pos = hit.point + hit.normal * radius;
                outPoints.Add(pos);
                if (stopAtFirstBounce)
                {
                    break;
                }

                // （跳ね返りまで見せる設定のとき）当たった面で反射して続ける
                vel = Vector3.Reflect(vel, hit.normal) * predictionBounciness;
                if (vel.magnitude < 0.5f)
                {
                    break; // ほぼ止まったら予測終了
                }
            }
            else
            {
                // 障害物なし、またはプレイヤー自身（無視）→ そのまま直進
                pos += stepVec;
                outPoints.Add(pos);
            }

            // 線が長くなりすぎたら打ち切る
            traveled += dist;
            if (traveled >= maxPredictionDistance)
            {
                break;
            }
        }
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
