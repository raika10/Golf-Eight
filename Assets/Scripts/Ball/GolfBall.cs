using UnityEngine;

/// ゴルフボール本体。
/// ・地面や壁に当たると跳ねる（ステージ1）
/// ・転がって速度が落ちたら自然に止まる（ステージ1）
/// ・打たれたら打った強さに応じて飛ぶ（ステージ2：Hit）
/// 色分け／透過表示／ゴール判定は次の段階で足していく。
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

    private Rigidbody body;
    private SphereCollider sphere;
    private float slowTimer; // 遅い状態が続いている時間

    /// いま動いているか（「止まっている球だけ打てる」判定などに使う）。
    public bool IsMoving => slowTimer < stopDuration;

    /// 現在の質量（打った強さから初速を見積もるときに使う）。
    public float Mass => body != null ? body.mass : 1f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();

        // 転がって自然に止まるように抵抗を設定
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;

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

    private void Update()
    {
        UpdateStopState();
    }

    /// このボールを打つ。direction の向きに power の強さで飛ばす。
    /// power が大きいほど初速が上がり飛距離が伸びる。誰が呼んでもよい（誰でも誰のボールを打てる）。
    public void Hit(Vector3 direction, float power)
    {
        if (direction.sqrMagnitude < 0.0001f || power <= 0f)
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

    /// 速度が十分に落ちたら「止まった」と判定し、微振動しないよう完全停止させる。
    private void UpdateStopState()
    {
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
