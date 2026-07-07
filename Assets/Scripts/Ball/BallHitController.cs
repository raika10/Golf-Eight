using System.Collections.Generic;
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

    [Header("予測線")]
    [SerializeField] private LineRenderer aimLine;              // 予測線（未設定なら自動生成）
    [SerializeField] private int predictionSteps = 60;         // 予測の分割数（多いほど長く・滑らか）
    [SerializeField] private float predictionTimeStep = 0.04f; // 予測1ステップの時間 (s)
    [SerializeField] private float predictionBounciness = 0.6f;// 跳ね返りで残る速度の割合（GolfBall側と合わせる）
    [SerializeField] private LayerMask predictionObstacleMask = ~0; // 予測で跳ね返る対象（壁・地面など）

    private float currentYaw;      // 現在の水平の向き（度）
    private float currentCharge;   // 現在の溜め (0..1)
    private bool isCharging;       // 溜め中か
    private GolfBall targetBall;   // いま狙っているボール
    private readonly List<Vector3> predictedPoints = new List<Vector3>();
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
        if (ball == null || ball.IsHoled)
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
            if (Physics.SphereCast(pos, castRadius, dir, out RaycastHit hit, dist, predictionObstacleMask, QueryTriggerInteraction.Ignore))
            {
                // 当たった面で反射（速度は反発ぶん減らす）
                pos = hit.point + hit.normal * radius;
                vel = Vector3.Reflect(vel, hit.normal) * predictionBounciness;
                outPoints.Add(pos);

                if (vel.magnitude < 0.5f)
                {
                    break; // ほぼ止まったら予測終了
                }
            }
            else
            {
                pos += stepVec;
                outPoints.Add(pos);
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
