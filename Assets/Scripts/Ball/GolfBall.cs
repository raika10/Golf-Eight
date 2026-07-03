using UnityEngine;

/// ゴルフボール本体（ステージ1：物理的な振る舞いだけ）。
/// ・地面や壁に当たると跳ねる
/// ・転がって速度が落ちたら自然に止まる
/// 打つ／色分け／透過表示／ゴール判定は次の段階で足していく。
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

    /// いま動いているか（次の段階で「止まっている球だけ打てる」等に使う）。
    public bool IsMoving => slowTimer < stopDuration;

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
