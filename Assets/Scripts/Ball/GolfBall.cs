using UnityEngine;

/// ゴルフボール本体。
/// ・地面や壁に当たると跳ねる（ステージ1）
/// ・転がって速度が落ちたら自然に止まる（ステージ1）
/// ・打たれたら打った強さに応じて飛ぶ（ステージ2：Hit）
/// ・自分のボールは物体に遮蔽されても透過シルエットで位置がわかる（ステージ5）
/// ・カップに入ったら静止して固定され、以降は打てなくなる（ステージ6：MarkHoled）
/// 色分けは次の段階で足していく。
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class GolfBall : MonoBehaviour
{
    [Header("物理")]
    [SerializeField] private float bounciness = 0.6f;     // 反発係数（0=跳ねない, 1=よく跳ねる）
    [SerializeField] private float friction = 0.3f;       // 接触面の摩擦
    [SerializeField] private float linearDamping = 0.4f;  // 転がりの抵抗（大きいほど早く止まる）
    [SerializeField] private float angularDamping = 0.6f; // 回転の抵抗

    [Header("停止判定")]
    [SerializeField] private float restLockSpeed = 1.0f;  // この速さ以下が続いたら固定 (m/s)。押されて微振動していても固定できるよう少し高め
    [SerializeField] private float stopDuration = 0.4f;   // restLockSpeed 以下が続くべき時間 (s)

    [Header("透過表示")]
    [SerializeField] private bool ownedByLocalPlayer = false; // このボールがこの画面の操作者のものか
    [SerializeField] private Renderer seeThroughRenderer;     // 遮蔽時に透過表示するシルエット用の子Renderer

    [Header("プレイヤーへの衝突")]
    [SerializeField] private float hitImmunityTime = 0.2f; // 打った直後、自分の体に当たって誤爆しないための猶予 (s)

    private Rigidbody body;
    private SphereCollider sphere;
    private float slowTimer; // 遅い状態が続いている時間
    private bool isResting;  // 止まって固定中か（プレイヤー等に押されてもブレないようロックしている）
    private RagdollController lastHitBy;    // 直近このボールを打ったプレイヤー（自爆防止用）
    private float lastHitTime = -999f;      // 直近打たれた時刻

    /// このボールがローカル操作者のものか。
    public bool OwnedByLocalPlayer => ownedByLocalPlayer;

    /// カップインして固定されたか。true の間は打てない。
    public bool IsHoled { get; private set; }

    /// いま動いているか（「止まっている球だけ打てる」判定などに使う）。
    public bool IsMoving => slowTimer < stopDuration;

    /// 現在の質量（打った強さから初速を見積もるときに使う）。
    public float Mass => body != null ? body.mass : 1f;

    /// このボールの半径（ワールド実寸）。予測線のシミュレーションで使う。
    public float Radius
    {
        get
        {
            if (sphere == null)
            {
                return 0.5f;
            }
            Vector3 s = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            return sphere.radius * maxScale;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();

        // 転がって自然に止まるように抵抗を設定
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;

        // 連続衝突判定にする。旗竿のような細い障害物を速い球がすり抜けず、
        // 予想線（掃引で当たり判定している）と実際の軌道が一致するようにする。
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // 反発するPhysicsMaterialを実行時に用意（アセットを手で作らなくても跳ねる）
        if (sphere.sharedMaterial == null)
        {
            sphere.material = new PhysicsMaterial("GolfBallBounce")
            {
                bounciness = bounciness,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                dynamicFriction = friction,
                staticFriction = friction,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
        }
    }

    private void Start()
    {
        ApplySeeThrough();
    }

    private void Update()
    {
        UpdateStopState();
    }

    /// このボールを自分のもの（ローカル所有）にするか設定する。透過表示も切り替わる。
    public void SetOwnedByLocalPlayer(bool local)
    {
        ownedByLocalPlayer = local;
        ApplySeeThrough();
    }

    /// 透過シルエットは自分のボールのときだけ表示する。
    private void ApplySeeThrough()
    {
        if (seeThroughRenderer != null)
        {
            seeThroughRenderer.enabled = ownedByLocalPlayer;
        }
    }

    /// このボールを打つ。direction の向きに power の強さで飛ばす。
    /// power が大きいほど初速が上がり飛距離が伸びる。誰が呼んでもよい（誰でも誰のボールを打てる）。
    /// hitBy を渡すと、打った直後にその人自身の体へ当たっても誤爆で吹っ飛ばさない（hitImmunityTime秒間）。
    public void Hit(Vector3 direction, float power, RagdollController hitBy = null)
    {
        if (IsHoled || direction.sqrMagnitude < 0.0001f || power <= 0f)
        {
            return;
        }

        // 止まって固定していたら解除（打つと動けるように）
        Unfreeze();

        // 打つ瞬間に眠っていたら起こす。今の速度は打ち消して打った向きを優先する。
        body.WakeUp();
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        // Impulse なので Δ速度 = power / 質量。強さがそのまま飛距離に効く。
        body.AddForce(direction.normalized * power, ForceMode.Impulse);

        slowTimer = 0f; // 動き出したので停止タイマーをリセット
        lastHitBy = hitBy;
        lastHitTime = Time.time;
    }

    /// プレイヤー（RagdollController）に物理的に衝突した瞬間に呼ばれる。
    /// 衝突時点の正確な相対速度（Collision.relativeVelocity）を使うので、
    /// 「同フレーム内で跳ね返った後に判定して逃げ方向と誤判定される」ことがなく、
    /// 遅いボールで誤って吹っ飛ぶこともない（速度がその瞬間の実測値だから）。
    private void OnCollisionEnter(Collision collision)
    {
        RagdollController rc = collision.collider.GetComponentInParent<RagdollController>();
        if (rc == null)
        {
            return;
        }
        // 打った直後、自分の体に触れて誤爆するのを防ぐ（同じ人・短時間だけ無視）
        if (rc == lastHitBy && Time.time - lastHitTime < hitImmunityTime)
        {
            return;
        }
        rc.ApplyBallImpact(collision.relativeVelocity);
    }

    /// カップインしたときに呼ぶ。速度を消してその場に固定し、以降は打てなくする。
    /// 位置合わせ（カップ中心へ吸い込む）は呼び出し側（GolfHole）が済ませてから呼ぶ。
    public void MarkHoled()
    {
        if (IsHoled)
        {
            return;
        }
        IsHoled = true;

        // isKinematic にする前に速度をゼロにしておく
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        // kinematic では連続衝突判定が使えず警告になるので Discrete に戻してから固定する
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        // 物理から切り離してカップの底で完全静止させる（縁に押し戻されて飛び出すのを防ぐ）
        body.isKinematic = true;

        // 地面の下に沈むので、透過シルエットが地面越しに光って見えないように消す
        if (seeThroughRenderer != null)
        {
            seeThroughRenderer.enabled = false;
        }
    }

    /// 速度が十分に落ちたら「止まった」と判定し、微振動しないよう完全に固定する。
    private void UpdateStopState()
    {
        if (IsHoled || isResting)
        {
            return; // 固定済み／カップイン済みなら何もしない
        }

        // 「明らかに動いている」ときだけタイマーをリセット。プレイヤーに押されて微振動している程度なら
        // タイマーを進めて固定に持っていく（そうしないと押され続けて永遠に固定できずカタカタする）。
        if (body.linearVelocity.magnitude <= restLockSpeed)
        {
            slowTimer += Time.deltaTime;
            if (slowTimer >= stopDuration)
            {
                Freeze(); // 止まったら固定。プレイヤーに触れられても押されず、予想線がブレない
            }
        }
        else
        {
            slowTimer = 0f;
        }
    }

    /// 止まったボールを完全に固定する。kinematic にして物理シミュレーションから外すので、
    /// 地面や連続衝突判定による微振動が一切なくなり、外から押されても動かない。
    /// コライダーは有効なまま（固体として機能）なので、当たり判定は残る。
    private void Freeze()
    {
        isResting = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        // kinematic では連続衝突判定が使えず警告になるので Discrete に戻してから固定する
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
    }

    /// 固定を解除して、また自由に動ける状態に戻す。
    private void Unfreeze()
    {
        isResting = false;
        body.isKinematic = false;
        // 動いている間は細い障害物をすり抜けないよう連続衝突判定に戻す
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }
}
