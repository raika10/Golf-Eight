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
    [SerializeField] private float stopSpeed = 0.25f;     // この速さ以下がしばらく続いたら停止扱い (m/s)
    [SerializeField] private float stopDuration = 0.4f;   // stopSpeed 以下が続くべき時間 (s)

    [Header("透過表示")]
    [SerializeField] private bool ownedByLocalPlayer = false; // このボールがこの画面の操作者のものか
    [SerializeField] private Renderer seeThroughRenderer;     // 遮蔽時に透過表示するシルエット用の子Renderer

    private Rigidbody body;
    private SphereCollider sphere;
    private float slowTimer; // 遅い状態が続いている時間

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
    public void Hit(Vector3 direction, float power)
    {
        if (IsHoled || direction.sqrMagnitude < 0.0001f || power <= 0f)
        {
            return;
        }

        // 打つ瞬間に眠っていたら起こす。今の速度は打ち消して打った向きを優先する。
        body.WakeUp();
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        // Impulse なので Δ速度 = power / 質量。強さがそのまま飛距離に効く。
        body.AddForce(direction.normalized * power, ForceMode.Impulse);

        slowTimer = 0f; // 動き出したので停止タイマーをリセット
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

    /// 速度が十分に落ちたら「止まった」と判定し、微振動しないよう完全停止させる。
    private void UpdateStopState()
    {
        if (IsHoled)
        {
            return;
        }

        if (body.linearVelocity.magnitude <= stopSpeed)
        {
            slowTimer += Time.deltaTime;
            if (slowTimer >= stopDuration && !body.IsSleeping())
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep(); // これ以上の微小な滑りを止めて静止させる
            }
        }
        else
        {
            slowTimer = 0f;
        }
    }
}
