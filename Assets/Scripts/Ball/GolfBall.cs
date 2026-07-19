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

    [Header("壁の破壊")]
    [SerializeField] private bool breakWalls = true;         // 当たった壁（MazeWall）を壊すか
    [SerializeField] private float wallBreakMinSpeed = 3f;    // この相対速度以上で当たった時だけ壊す (m/s)。ゆっくり転がる球では壊れない
    [SerializeField] private int wallDamage = 1;              // 1回の衝突で壁に与えるダメージ
    [Tooltip("壁を壊した時に貫通して残る速度の割合（0=止まる, 1=減速なし）。跳ね返りを打ち消して進行方向へ通り抜けさせる")]
    [SerializeField] private float wallPunchThroughRetain = 0.75f;
    [Tooltip("壁1枚を壊すたびに失う速さ (m/s)。勢いが弱いと壁の手前で止まり、強いほど貫通する")]
    [SerializeField] private float wallPunchSpeedLoss = 1.5f;

    [Header("場外リスポーン")]
    [SerializeField] private float outOfBoundsY = -20f;       // これより下に落ちたら場外＝リスポーン
    [SerializeField] private float respawnHeightOffset = 0.5f;// リスポーン時、地面より少し上に出す高さ (m)

    private Rigidbody body;
    private SphereCollider sphere;
    private Vector3 lastVelocity; // 物理ステップ前（＝衝突直前）の速度。壁貫通で跳ね返りを打ち消すのに使う
    private float slowTimer; // 遅い状態が続いている時間
    private bool isResting;  // 止まって固定中か（プレイヤー等に押されてもブレないようロックしている）
    private RagdollController lastHitBy;    // 直近このボールを打ったプレイヤー（自爆防止用）
    private float lastHitTime = -999f;      // 直近打たれた時刻
    private Vector3 lastHitPosition;        // 直近「打球した場所」（場外リスポーン先）
    private bool hasHitPosition;            // 一度でも打たれたか
    private Vector3 initialPosition;        // 初期位置（まだ打たれていない時のリスポーン先）

    /// 壁を壊した瞬間に発火する（壁, ダメージ, 衝撃点, 衝撃速度）。ネットワーク同期用のフックで、
    /// GolfBall自体はFishNetを一切参照しない（BallNetworkSyncが購読して同期する）。
    public event System.Action<MazeWall, int, Vector3, Vector3> OnWallDamaged;

    /// プレイヤーに衝突した瞬間に発火する（衝突相手, 相対速度）。ネットワーク同期用のフック。
    public event System.Action<RagdollController, Vector3> OnPlayerImpact;

    /// この端末がこのボールの物理・状態を決める権威を持つか（サーバー）。BallNetworkSync が設定する。
    /// false の端末は停止判定・場外リスポーン・衝突による効果を一切行わず、
    /// NetworkTransform が運んでくる位置に従うだけにする。
    /// これを守らないと、たとえば場外リスポーンの Unfreeze() がクライアント側で kinematic を解除してしまい、
    /// クライアントが独自に物理を回してサーバーとズレる。単体（非ネットワーク）では true のままなので従来どおり動く。
    public bool StateAuthority { get; set; } = true;

    /// このボールがローカル操作者のものか。
    public bool OwnedByLocalPlayer => ownedByLocalPlayer;

    /// カップインして固定されたか。true の間は打てない。
    public bool IsHoled { get; private set; }

    /// いま動いているか（「止まっている球だけ打てる」判定などに使う）。
    /// 権威のある端末（サーバー・単体）は自前の物理から判定する。
    /// 権威の無い端末はボールが kinematic で物理が回らず slowTimer が動かないため、
    /// 自前判定が当てにならない。そこで権威側から同期された値を使う。
    public bool IsMoving => StateAuthority ? slowTimer < stopDuration : networkIsMoving;

    // 権威側が判定した「動いているか」。BallNetworkSync が同期して設定する。
    private bool networkIsMoving;

    /// 権威側の「動いているか」を反映する（BallNetworkSync から呼ぶ）。
    public void SetNetworkIsMoving(bool moving)
    {
        networkIsMoving = moving;
    }

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
        initialPosition = transform.position; // まだ打たれていない時のリスポーン先
        ApplySeeThrough();
    }

    private void FixedUpdate()
    {
        // 物理ステップの前に、このステップに入る速度（＝衝突直前の速度）を控えておく。
        // OnCollisionEnter 時点の body.linearVelocity は既に跳ね返り後なので使えない。
        if (!body.isKinematic)
        {
            lastVelocity = body.linearVelocity;
        }
    }

    private void Update()
    {
        // 停止判定も場外リスポーンも「ボールの状態を書き換える」処理なので、権威を持つ端末だけが行う。
        // 権威の無い端末（クライアント）では位置は NetworkTransform が運ぶので、ここで触ってはいけない。
        if (!StateAuthority)
        {
            return;
        }

        UpdateStopState();

        // 場外チェック：下に落ちたら「打球した場所」へリスポーン（未打球なら初期位置）
        if (!IsHoled && transform.position.y < outOfBoundsY)
        {
            RespawnOutOfBounds();
        }
    }

    /// 場外に落ちたボールを、直近の打球地点（無ければ初期位置）へ戻す。
    private void RespawnOutOfBounds()
    {
        Vector3 target = hasHitPosition ? lastHitPosition : initialPosition;

        Unfreeze();               // 固定されていたら解除して動けるように
        body.WakeUp();
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = target + Vector3.up * respawnHeightOffset; // 少し上に出して落として着地させる
        transform.position = body.position;
        slowTimer = 0f;
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

        // 「打球した場所」を記録（場外に落ちたらここへ戻す）
        lastHitPosition = transform.position;
        hasHitPosition = true;

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
        // 壁（MazeWall）に当たったら壊す。速い当たりだけ壊れるよう相対速度でしきい値判定し、
        // 衝突直前の速度（lastVelocity）を使って破片を飛ばし＆ボールを貫通させる。
        if (breakWalls)
        {
            MazeWall wall = collision.collider.GetComponentInParent<MazeWall>();
            if (wall != null && collision.relativeVelocity.magnitude >= wallBreakMinSpeed)
            {
                ContactPoint contact = collision.GetContact(0);
                // 壁破壊は権威を持つ端末（サーバー）だけが実行する。クライアント側でこの衝突が起きても何もしない
                // （BallNetworkSyncがサーバーからのブロードキャストを受けて各クライアントでも同じ破壊を再生する）。
                if (StateAuthority)
                {
                    // 衝突直前の速度で破片を進行方向へ飛ばす（跳ね返り後の relativeVelocity ではなく実測の入射速度）。
                    wall.TakeDamage(wallDamage, contact.point, lastVelocity);
                    OnWallDamaged?.Invoke(wall, wallDamage, contact.point, lastVelocity);
                }

                // 跳ね返りを打ち消して貫通させる。物理ソルバが既に反発の速度を入れているので、
                // 衝突直前の速度を元に「進行方向へ通り抜ける速度」で上書きする。
                // 勢いが強いほど多く残り（貫通）、弱いと wallPunchSpeedLoss で失速して壁の手前で止まる。
                float throughSpeed = lastVelocity.magnitude * wallPunchThroughRetain - wallPunchSpeedLoss;
                if (throughSpeed > 0f && lastVelocity.sqrMagnitude > 1e-4f)
                {
                    body.linearVelocity = lastVelocity.normalized * throughSpeed;
                }
                else
                {
                    // 貫通しきれなかった：跳ね返らせず、勢いを殺してその場に留める
                    body.linearVelocity = Vector3.zero;
                }
                return; // このフレームは壁破壊を優先（プレイヤー衝突判定へは進まない）
            }
        }

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
        // 吹っ飛ばしも権威を持つ端末だけが実行し、他クライアントへはBallNetworkSyncが同期する。
        if (StateAuthority)
        {
            rc.ApplyBallImpact(collision.relativeVelocity);
            OnPlayerImpact?.Invoke(rc, collision.relativeVelocity);
        }
    }

    /// 次の試合に向けて、指定位置で初期状態に戻す（再戦時にサーバーが呼ぶ）。
    /// カップイン・停止固定・打球履歴をすべて解除するので、また打てる状態になる。
    /// 権威の無い端末で呼ぶと kinematic を解除してサーバーとズレるため、権威側だけが実行する。
    public void ResetForNewMatch(Vector3 position)
    {
        if (!StateAuthority)
        {
            return;
        }

        IsHoled = false;
        isResting = false;
        slowTimer = 0f;
        hasHitPosition = false;
        lastHitBy = null;
        lastHitTime = -999f;
        initialPosition = position;

        // 物理を動ける状態に戻してから配置する
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = position;
        transform.position = position;

        ApplySeeThrough(); // カップインで消したシルエットを戻す
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
