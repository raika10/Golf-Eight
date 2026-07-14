using System.Collections;
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
    [SerializeField] private float loftAngle = 35f;       // 打ち上げ角（度）。0=水平, 大きいほど山なり（上へ）
    [SerializeField] private Transform aimSource;         // 向きの基準（未設定なら自分）。カメラ等を割り当ててもよい
    [SerializeField] private float aimSmoothTime = 0.05f; // 狙いのスムージング時間 (s)。マウスの微ブレで予想線が揺れるのを抑える

    [Header("アニメーション（未設定なら子のAnimatorを自動取得。無ければ何もしない）")]
    [SerializeField] private Animator animator;                // キャラモデルのAnimator
    [SerializeField] private string isSwingingParam = "IsSwinging"; // スイング状態に入るかの Bool パラメータ名
    [SerializeField] private string swingTimeParam = "SwingTime";   // スイングクリップの再生位置(0..1)を指定する Float パラメータ名（Motion Time用）
    [SerializeField] private float backswingNormalized = 0.4f; // 振り上げの頂点（クリップ 0..1）。チャージ中はここで保持
    [SerializeField] private float downswingTime = 0.25f;     // 離してから振り下ろし切るまでの時間 (s)
    [SerializeField] private float swingHitDelay = 0.12f;     // 離してからボールが飛ぶまでの時間 (s)。当たりの瞬間に合わせる
    [SerializeField] private float swingBodyAngle = 120f;     // スイング中に見た目のキャラを横向きにする角度。+90〜120=体が右向き / -で左

    [Header("どこでもスイング / 空振り")]
    [SerializeField] private Key cancelKey = Key.E;           // このキーでチャージ解除
    [SerializeField] private float minWhiffRecoverTime = 0.2f; // 空振り後隙の最小（溜め0での後隙）(s)
    [SerializeField] private float whiffRecoverTime = 0.6f;    // 空振り後隙の最大（フルチャージでの後隙）(s)
    [SerializeField] private float stumbleBodyAngle = 40f;    // よろけで体が反対へ泳ぐ角度（見た目だけ）
    [SerializeField] private float chargeMoveSpeedScale = 0.5f;// チャージ中の歩き速度の倍率（0.5=半分）

    [Header("相手プレイヤーを打つ")]
    [SerializeField] private bool canHitPlayers = true;        // ボールと同じように相手プレイヤーも打てる（吹っ飛ばす）
    [SerializeField] private float playerFlingPower = 0.4f;    // 打つ強さ×この倍率＝吹っ飛ぶ勢い。まず弱めにして少しずつ上げる
    [SerializeField] private float playerHitRange = 2.5f;      // 相手を打てる距離 (m)
    [SerializeField] private float playerPredictRadius = 0.3f; // 予測線で相手の体を見立てる半径 (m)
    [SerializeField] private float playerAimHeight = 1.0f;     // 予測線の開始高さ（相手の胸あたり m）

    [Header("対象さがし")]
    [SerializeField] private float hitRange = 5f;         // この距離内のボールを打てる
    [SerializeField] private LayerMask hittableBallMask = ~0; // ボール探索の対象レイヤー
    [SerializeField] private bool onlyHitRestingBall = true;  // 止まっている球だけ打てるか

    [Header("アドレス（ボールは左側の近くだけ打てる）")]
    [SerializeField] private bool addressFromLeft = true;  // ボールの左側に立った時だけ打てる（オフなら右側）
    [SerializeField] private float minSideOffset = 0.3f;   // ボールが横方向にこれ以上離れていること (m)。真後ろからは打てない
    [SerializeField] private float maxSideOffset = 1.4f;   // ボールが横方向にこれ以内であること (m)。離れすぎは打てない
    [SerializeField] private float maxForwardOfBall = 0.3f;// 自分がボールより「前」に出られる距離 (m)。小さいほど“ボールの前”から打てない
    [SerializeField] private float maxBehindBall = 1.1f;   // 自分がボールより「後ろ」に下がれる距離 (m)

    [Header("強さ")]
    [SerializeField] private float minPower = 4f;         // 最小の打つ強さ（Impulse）
    [SerializeField] private float maxPower = 22f;        // 最大の打つ強さ
    [SerializeField] private float chargeTime = 2f;     // 0→最大まで溜めるのにかかる時間 (s)

    [Header("予測線")]
    [SerializeField] private LineRenderer aimLine;              // 予測線（未設定なら自動生成）
    [SerializeField] private int predictionSteps = 60;         // 予測の分割数（多いほど長く・滑らか）
    [SerializeField] private float predictionTimeStep = 0.04f; // 予測1ステップの時間 (s)
    [SerializeField] private float predictionBounciness = 0.6f;// 跳ね返りで残る速度の割合（GolfBall側と合わせる）
    [SerializeField] private LayerMask predictionObstacleMask = ~0; // 予測で跳ね返る対象（壁・地面など）
    [SerializeField] private bool stopAtFirstBounce = true;    // ゴルフゲーム風に、最初の着地（最初の衝突）で線を止める
    [SerializeField] private float maxPredictionDistance = 30f; // 予測線の最大の長さ (m)。これを超えたら打ち切る
    [SerializeField] private float aimLineWidth = 0.16f;       // 予測線の太さ（手前側 m）。大きいほど見やすい
    [SerializeField] private float aimLineTipWidthScale = 0.1f; // 先端の太さ＝手前の何倍か（0.1=1/10で先細り）
    [SerializeField] private Color aimLineStartColor = new Color(1f, 0.9f, 0.2f, 1f);   // 手前側の色（明るい黄）
    [SerializeField] private Color aimLineEndColor = new Color(1f, 0.45f, 0.1f, 0.85f); // 着地側の色（橙・少し薄め）
    [SerializeField] private float aimLineHeightOffset = 0.05f; // 線を少し浮かせて地面との重なり（チラつき）を防ぐ (m)
    [SerializeField] private float idleAimLineLength = 4f;      // 非チャージ時の目安線の長さ (m)。方向が分かる程度に短く切る

    private float currentYaw;         // 現在の水平の向き（度）※スムージング済み
    private float yawVelocity;        // SmoothDampAngle 用の速度バッファ
    private float currentCharge;      // 現在の溜め (0..1)
    private bool isCharging;          // 溜め中か
    private bool isRecovering;        // 空振り後の後隙中か（この間は新しいスイング不可・動けない）
    private GolfBall targetBall;             // いま狙っている（構えられている）ボール
    private RagdollController targetPlayer;  // いま狙っている相手プレイヤー
    private RagdollController selfRagdoll;   // 自分（対象から除外する）
    private readonly List<Vector3> predictedPoints = new List<Vector3>();
    private static readonly Collider[] overlapBuffer = new Collider[32];

    private Collider[] selfColliders; // 自分（プレイヤー）の当たり判定。予測線でプレイヤーを無視するのに使う
    private int isSwingingHash;
    private int swingTimeHash;
    private PlayerRig rig;                       // 自作リグ（あればスイングを再生）
    private PlayerController playerController;    // スイング中に体を横向きにする指示先

    private void Awake()
    {
        // 自分（プレイヤー）側のコライダー。予測線がプレイヤーで折れないように使う。
        // CharacterController も Collider の一種なので含まれる。
        selfColliders = GetComponentsInChildren<Collider>();
        currentYaw = AimTransform().eulerAngles.y;
        EnsureAimLine();
        StyleAimLine();

        // アニメーター（キャラモデルの子）を自動取得
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        isSwingingHash = Animator.StringToHash(isSwingingParam);
        swingTimeHash = Animator.StringToHash(swingTimeParam);

        // 自作リグ（プリミティブの体）があれば取得
        rig = GetComponentInParent<PlayerRig>();
        // 自分の Ragdoll（対象から除外するため）
        selfRagdoll = GetComponentInParent<RagdollController>();

        // プレイヤー（スイング中の体の横向き用）。自分/親→子→シーン全体の順で探す。
        playerController = GetComponentInParent<PlayerController>();
        if (playerController == null)
        {
            playerController = GetComponentInChildren<PlayerController>();
        }
        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }
        if (playerController == null)
        {
            Debug.LogWarning("[BallHitController] PlayerController が見つかりません。スイング中の体の向き変更が効きません。", this);
        }
    }

    /// 狙いの向きの基準になる Transform（未設定なら自分＝載っているオブジェクトの向き）。
    private Transform AimTransform()
    {
        return aimSource != null ? aimSource : transform;
    }

    private void Update()
    {
        // 他プレイヤー／ダミーは入力を受け付けない（狙い・チャージ・予測線を出さない）
        if (playerController != null && !playerController.IsLocalPlayer)
        {
            if (aimLine != null)
            {
                aimLine.enabled = false;
            }
            return;
        }

        UpdateAim();
        targetBall = FindTargetBall();
        targetPlayer = FindTargetPlayer();
        ResolveTarget(); // ボールと相手が両方近いときは、近い方だけ狙う
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
            // 論理向き(AimYaw)を狙いに使う。スイング中に見た目を横向きにしても狙いはズレない。
            float targetYaw = playerController != null ? playerController.AimYaw : AimTransform().eulerAngles.y;
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

        // 押し始め：どこでも溜め開始できる（SBG風）。対象の有無は「離した瞬間」に判定する。
        // 空振りの後隙中（isRecovering）や空中では始められない。
        bool grounded = playerController == null || playerController.IsGrounded;
        if (mouse.leftButton.wasPressedThisFrame && !isRecovering && grounded)
        {
            isCharging = true;
            currentCharge = 0f;

            // スイング状態に入る（IsSwinging=true）。再生位置は SwingTime で直接指定する。
            if (animator != null)
            {
                animator.SetBool(isSwingingHash, true);
                animator.SetFloat(swingTimeHash, 0f);
            }
            // 見た目のキャラを横向き（ゴルフの構え）にする＋歩き減速＋ジャンプで構えを崩さない
            if (playerController != null)
            {
                playerController.SetSwingBodyOffset(swingBodyAngle);
                playerController.SetMoveSpeedMultiplier(chargeMoveSpeedScale);
                playerController.SetSwingHold(true);
            }
        }

        if (!isCharging)
        {
            return;
        }

        // 解除は E キー、またはジャンプで。対象の有無ではキャンセルしない
        // ＝対象が無くても構え続けられる（離すと空振りになる）。
        if (CancelPressedThisFrame() || JumpPressedThisFrame())
        {
            CancelCharge();
            return;
        }

        // 押している間、強さが溜まっていく（最大で頭打ち）
        currentCharge = Mathf.Clamp01(currentCharge + Time.deltaTime / Mathf.Max(0.01f, chargeTime));

        // チャージ中：クラブを振り上げた位置（backswingNormalized）まで上げて、そこで保持する。
        // Motion Time で再生位置を直接指定しているので、指定した所でピタッと止まる（＝振り上げキープ）。
        if (animator != null)
        {
            float raise = Mathf.SmoothStep(0f, backswingNormalized, Mathf.Clamp01(currentCharge * 2.5f));
            animator.SetFloat(swingTimeHash, raise);
        }

        // 離した瞬間：振り上げ位置から振り下ろし（SwingTimeを1へ動かす）。当たる瞬間にボール発射。
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (rig != null)
            {
                rig.Swing();
            }

            // 離した瞬間に対象を判定：居れば当てる、居なければ空振り（よろけ＋後隙）
            if (targetPlayer != null)
            {
                StartCoroutine(DownSwing());
                StartCoroutine(FlingAfterSwing(targetPlayer, GetAimDirection(), CurrentPower() * playerFlingPower));
            }
            else if (targetBall != null)
            {
                StartCoroutine(DownSwing());
                StartCoroutine(HitAfterSwing(targetBall, GetAimDirection(), CurrentPower()));
            }
            else
            {
                // 範囲内に対象なし → 空振り。溜めが大きいほど後隙が長い（振り切りすぎて隙だらけ）
                StartCoroutine(WhiffSwing(currentCharge));
            }
            // 溜め終了：歩き速度と構え保持を通常へ戻す（スイングのモーションは各コルーチンが進める）
            if (playerController != null)
            {
                playerController.SetMoveSpeedMultiplier(1f);
                playerController.SetSwingHold(false);
            }
            isCharging = false;
            currentCharge = 0f;
        }
    }

    /// スイングの当たる瞬間（swingHitDelay秒後）に相手プレイヤーを吹っ飛ばす。
    /// IKnockbackable（方向＋威力を受け取る共通口）があればそちらへ渡す。無ければ従来の Fling。
    private IEnumerator FlingAfterSwing(RagdollController other, Vector3 direction, float power)
    {
        if (swingHitDelay > 0f)
        {
            yield return new WaitForSeconds(swingHitDelay);
        }
        if (other == null || other.IsDown)
        {
            yield break;
        }

        IKnockbackable knockable = other.GetComponent<IKnockbackable>();
        if (knockable != null)
        {
            knockable.ApplyKnockback(direction.normalized, power);
        }
        else
        {
            other.Fling(direction.normalized * power);
        }
    }

    /// 振り下ろし：SwingTime を1まで動かしつつ、体の横向きも同時にほどいて正面へ戻す。
    /// （本物のゴルフのように、振り下ろしと一緒に体が正面を向く。最後に一気に戻ると不自然になるため）
    private IEnumerator DownSwing()
    {
        float start = animator != null ? animator.GetFloat(swingTimeHash) : 0f;
        for (float t = 0f; t < downswingTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, downswingTime));
            if (animator != null)
            {
                animator.SetFloat(swingTimeHash, Mathf.Lerp(start, 1f, k));
            }
            // 振り下ろしの進み具合に合わせて、体の横向きを 0（正面）へほどく
            if (playerController != null)
            {
                playerController.SetSwingBodyOffset(Mathf.Lerp(swingBodyAngle, 0f, k));
            }
            yield return null;
        }
        if (animator != null)
        {
            animator.SetFloat(swingTimeHash, 1f);
            animator.SetBool(isSwingingHash, false); // 通常（歩き/待機）へ戻る
        }
        if (playerController != null)
        {
            playerController.SetSwingBodyOffset(0f);
        }
    }

    /// 溜めを中断して通常（歩き/待機）に戻す。
    private void CancelCharge()
    {
        isCharging = false;
        currentCharge = 0f;
        if (animator != null)
        {
            animator.SetBool(isSwingingHash, false);
            animator.SetFloat(swingTimeHash, 0f);
        }
        if (playerController != null)
        {
            playerController.SetSwingBodyOffset(0f); // 見た目の横向きを戻す
            playerController.SetMoveSpeedMultiplier(1f); // 歩き速度を戻す
            playerController.SetSwingHold(false);        // 構え保持を解除
        }
    }

    /// このフレームで解除キー（既定E）が押されたか。
    private bool CancelPressedThisFrame()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[cancelKey].wasPressedThisFrame;
    }

    /// このフレームでジャンプ入力があったか（チャージ解除の判定に使う）。
    private bool JumpPressedThisFrame()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
    }

    /// 範囲内に対象がいないのにスイングした時：空を切って、よろけ＋後隙（少しの間動けない）をさらす。
    /// 後隙中は動けず新しいスイングもできないので、相手に打ち返される「スキ」になる。
    private IEnumerator WhiffSwing(float charge)
    {
        // 溜めが大きいほど後隙が長い（min〜max を charge で補間）
        float recoverTime = Mathf.Lerp(minWhiffRecoverTime, whiffRecoverTime, Mathf.Clamp01(charge));

        isRecovering = true;
        if (playerController != null)
        {
            playerController.SetActionLocked(true); // 後隙：この間は動けない
        }

        // まず普通に振り切る（空を切る）。DownSwing と同じ動きで体の横向きも正面へ戻す。
        float start = animator != null ? animator.GetFloat(swingTimeHash) : 0f;
        for (float t = 0f; t < downswingTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, downswingTime));
            if (animator != null)
            {
                animator.SetFloat(swingTimeHash, Mathf.Lerp(start, 1f, k));
            }
            if (playerController != null)
            {
                playerController.SetSwingBodyOffset(Mathf.Lerp(swingBodyAngle, 0f, k));
            }
            yield return null;
        }
        if (animator != null)
        {
            animator.SetFloat(swingTimeHash, 1f);
            animator.SetBool(isSwingingHash, false); // 通常（歩き/待機）へ戻す
        }

        // よろけ：勢い余って反対側へ体が泳ぎ、ゆっくり戻る（後隙の見た目）。時間は溜めに比例。
        for (float t = 0f; t < recoverTime; t += Time.deltaTime)
        {
            float k = t / Mathf.Max(0.01f, recoverTime);
            float wobble = Mathf.Sin(k * Mathf.PI) * -stumbleBodyAngle; // 0 → -角度 → 0 の山なり
            if (playerController != null)
            {
                playerController.SetSwingBodyOffset(wobble);
            }
            yield return null;
        }

        if (playerController != null)
        {
            playerController.SetSwingBodyOffset(0f);
            playerController.SetActionLocked(false); // 後隙おわり：また動ける
        }
        isRecovering = false;
    }

    /// スイングの当たる瞬間（swingHitDelay秒後）にボールを発射する。向き・強さは離した瞬間の値。
    private IEnumerator HitAfterSwing(GolfBall ball, Vector3 direction, float power)
    {
        if (swingHitDelay > 0f)
        {
            yield return new WaitForSeconds(swingHitDelay);
        }
        if (ball != null && !ball.IsHoled)
        {
            ball.Hit(direction, power, selfRagdoll);
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
        if (!IsAddressed(ball.transform.position))
        {
            return false;
        }
        return true;
    }

    /// 打てる立ち位置か。「対象（ボール／相手プレイヤー）の左側（既定）のすぐ近く」に居るときだけ true。
    /// 打つ方向を基準に、横方向（minSideOffset〜maxSideOffset）と
    /// 前後方向（前は maxForwardOfBall まで、後ろは maxBehindBall まで）の“箱”の中に対象がある必要がある。
    /// ボールと相手プレイヤーで**同じ判定**を使う。
    private bool IsAddressed(Vector3 targetPos)
    {
        Vector3 aimFlat = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        Vector3 aimRight = Vector3.Cross(Vector3.up, aimFlat);

        Vector3 to = targetPos - transform.position;
        to.y = 0f;

        float side = Vector3.Dot(to, aimRight);   // +なら自分の右にボール（＝自分はボールの左）
        float forward = Vector3.Dot(to, aimFlat); // +ならボールが自分より前（＝自分はボールの後ろ）

        // 左側アドレスなら「ボールは自分の右」。右側アドレスなら符号を反転して同じ判定にする。
        float sideOnAddressSide = addressFromLeft ? side : -side;
        if (sideOnAddressSide < minSideOffset || sideOnAddressSide > maxSideOffset)
        {
            return false; // 横に近すぎ／遠すぎ（＝真後ろや離れた位置からは打てない）
        }

        // 前後は非対称。自分がボールより前に出すぎると打てない（＝“ボールの左前”を弾く）
        if (forward < -maxForwardOfBall)
        {
            return false; // 自分がボールより前に出すぎ
        }
        if (forward > maxBehindBall)
        {
            return false; // 自分がボールより後ろに下がりすぎ
        }
        return true;
    }

    /// その相手プレイヤーをいま打てるか。
    /// ボールのような厳密な“箱”は不要だが、**相手の左側（既定）に立っている**必要がある。
    /// 距離は playerHitRange 内、前後の位置は自由。
    private bool CanHit(RagdollController other)
    {
        if (other == null || other == selfRagdoll || other.IsDown)
        {
            return false;
        }
        if (FlatDistanceTo(other.transform.position) > playerHitRange)
        {
            return false;
        }
        return IsPlayerAddressed(other.transform.position);
    }

    /// 相手を打てる立ち位置か。「相手の左後ろ（既定）」に居るときだけ true（ボールと同じ考え方）。
    /// ・左側：対象が自分の右側にある（addressFromLeft=true のとき）
    /// ・後ろ側：対象が自分より前にある（＝自分が相手より前に出ていない）
    private bool IsPlayerAddressed(Vector3 targetPos)
    {
        Vector3 aimFlat = Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward;
        Vector3 aimRight = Vector3.Cross(Vector3.up, aimFlat);

        Vector3 to = targetPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-6f)
        {
            return true;
        }

        float side = Vector3.Dot(to, aimRight);   // +なら対象が自分の右（＝自分は対象の左）
        float forward = Vector3.Dot(to, aimFlat); // +なら対象が自分より前（＝自分は対象の後ろ）

        float sideOnAddressSide = addressFromLeft ? side : -side;
        if (sideOnAddressSide < 0f)
        {
            return false; // 対象の左側（既定）に居ない
        }
        if (forward < 0f)
        {
            return false; // 自分が対象より前＝「左前」なので打てない
        }
        return true;
    }

    /// 範囲内でいちばん近い、打てる相手プレイヤーを探す。
    private RagdollController FindTargetPlayer()
    {
        if (!canHitPlayers)
        {
            return null;
        }
        int count = Physics.OverlapSphereNonAlloc(transform.position, playerHitRange, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        RagdollController nearest = null;
        float nearestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RagdollController other = overlapBuffer[i].GetComponentInParent<RagdollController>();
            if (!CanHit(other))
            {
                continue;
            }
            float sqr = (other.transform.position - transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = other;
            }
        }
        return nearest;
    }

    /// 狙う対象を1つに決める。ボールをきちんと構えられているならボール優先（ゴルフ優先）、
    /// そうでなければ範囲内の相手プレイヤーを狙う。決定後は有効な対象だけが残る。
    private void ResolveTarget()
    {
        if (targetBall != null && CanHit(targetBall))
        {
            targetPlayer = null; // ボールを構えている → ボールを打つ
            return;
        }

        targetBall = null; // ボールは打てる構えではない
        if (!CanHit(targetPlayer))
        {
            targetPlayer = null;
        }
    }

    /// いま狙えている対象があるか（ResolveTarget 後は有効な対象だけが入っている）。
    private bool HasTarget()
    {
        return targetBall != null || targetPlayer != null;
    }

    /// 狙っている対象の位置（無ければ自分の位置）。
    private Vector3 TargetPosition()
    {
        if (targetBall != null) return targetBall.transform.position;
        if (targetPlayer != null) return targetPlayer.transform.position;
        return transform.position;
    }

    /// ボールまでの水平距離（高さは無視）。
    private float FlatDistanceTo(GolfBall ball)
    {
        return ball == null ? float.MaxValue : FlatDistanceTo(ball.transform.position);
    }

    /// 指定位置までの水平距離（高さは無視）。
    private float FlatDistanceTo(Vector3 pos)
    {
        Vector3 d = pos - transform.position;
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

        bool showBall = targetBall != null;
        bool showPlayer = targetPlayer != null;

        // ボール／相手を「打てる位置」で構えている時だけ線を出す。
        // チャージはどこでもできるが、打てない場所（対象なし）では線を出さない。
        aimLine.enabled = showBall || showPlayer;
        if (!aimLine.enabled)
        {
            return;
        }

        // 溜め中は今の強さで着弾予測。溜めていない時は最大強さの弧を「狙いの目安線」として出す
        //（minPower だと弧が短すぎてほぼ見えないため。打てる位置に入ったことがはっきり分かる）。
        float power = isCharging ? CurrentPower() : maxPower;
        Vector3 start;
        Vector3 initialVelocity;
        float radius;

        if (showBall)
        {
            // Hit と同じく Impulse なので、打った直後の初速 = 強さ / 質量。
            start = targetBall.transform.position;
            initialVelocity = GetAimDirection() * (power / Mathf.Max(0.0001f, targetBall.Mass));
            radius = targetBall.Radius;
        }
        else // showPlayer
        {
            // 体は胸あたりから飛ぶ想定。KnockbackReceiver があれば、実際の初速計算
            //（最低打ち上げ角 minLaunchAngle・powerMultiplier 込み）で予測線を描いて軌道と一致させる。
            start = targetPlayer.transform.position + Vector3.up * playerAimHeight;
            KnockbackReceiver receiver = targetPlayer.GetComponent<KnockbackReceiver>();
            if (receiver != null)
            {
                initialVelocity = receiver.GetLaunchVelocity(GetAimDirection(), power * playerFlingPower);
            }
            else
            {
                initialVelocity = GetAimDirection() * (power * playerFlingPower);
            }
            radius = playerPredictRadius;
        }

        // プレイヤーの吹っ飛び線は、途中で打ち切られないよう長め（着地まで描き切る）。
        // ボール用の制限（predictionSteps / maxPredictionDistance）が短いと、強い吹っ飛ばしで
        // 線が上昇の途中で切れて「実際の軌道が線とかけ離れて見える」原因になる。
        int steps = showBall ? predictionSteps : 300;
        float maxDist = showBall ? maxPredictionDistance : 500f;

        // 非チャージ時は弧の最初だけ描いて「方向の目安」に留める（長い弧は見にくいため）
        if (!isCharging)
        {
            maxDist = idleAimLineLength;
        }

        SimulateTrajectory(start, initialVelocity, radius, predictedPoints, steps, maxDist);

        // 少し上に浮かせて地面と重ならないようにする（Zファイト＝チラつきを防ぎ、見やすく）
        if (aimLineHeightOffset != 0f)
        {
            for (int i = 0; i < predictedPoints.Count; i++)
            {
                predictedPoints[i] += Vector3.up * aimLineHeightOffset;
            }
        }

        aimLine.positionCount = predictedPoints.Count;
        aimLine.SetPositions(predictedPoints.ToArray());
    }

    /// 重力と跳ね返りを含めてボールの軌道を数値シミュレートし、点列を outPoints に入れる。
    private void SimulateTrajectory(Vector3 start, Vector3 velocity, float radius, List<Vector3> outPoints, int steps, float maxDistance)
    {
        outPoints.Clear();
        Vector3 pos = start;
        Vector3 vel = velocity;
        float dt = predictionTimeStep;
        float castRadius = Mathf.Max(0.01f, radius * 0.95f); // 壁にめり込む前に検出できるよう少し小さめ
        float traveled = 0f; // 予測線の合計の長さ（長すぎたら打ち切る）

        outPoints.Add(pos);
        for (int i = 0; i < steps; i++)
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
            if (traveled >= maxDistance)
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
        aimLine.useWorldSpace = true;
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.receiveShadows = false;
        // 影を受けず常に一定の明るさで見えるよう、Unlit系のスプライトシェーダを使う
        aimLine.material = new Material(Shader.Find("Sprites/Default"));
        aimLine.enabled = false;
    }

    /// 予測線の見た目（太さ・色・角の滑らかさ）を適用する。作成時も、Inspectorで割り当てた線にも効く。
    private void StyleAimLine()
    {
        if (aimLine == null)
        {
            return;
        }
        // 手前を太く、先を細く（先細り）。widthCurve は 0..1 の割合、実寸は widthMultiplier 倍。
        aimLine.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, Mathf.Clamp01(aimLineTipWidthScale));
        aimLine.widthMultiplier = aimLineWidth;
        aimLine.numCapVertices = 6;             // 端を丸める
        aimLine.numCornerVertices = 6;          // 角を丸めて滑らかに
        aimLine.textureMode = LineTextureMode.Stretch;
        aimLine.alignment = LineAlignment.View; // 常にカメラ正面を向く＝どの角度でも太さが見える
        aimLine.startColor = aimLineStartColor;
        aimLine.endColor = aimLineEndColor;
    }

    // シーンビューで打てる範囲を確認できるように
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hitRange);
    }
}
