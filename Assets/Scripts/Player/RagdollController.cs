using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// リグ付きモデル（Mixamo等）用の Ragdoll 切り替え。
/// 普段は Animator でアニメ、他プレイヤーに飛ばされた時だけ物理でぐにゃっと倒れる。
///
/// 使い方：
/// 1) Player の下にモデル（Animator付き）を置く。
/// 2) モデルの骨に対して Unity の Ragdoll Wizard（GameObject > 3D Object > Ragdoll…）で
///    Rigidbody / Collider / CharacterJoint を作る。
/// 3) この RagdollController を Player に付ける（骨のRigidbodyは子から自動収集）。
/// 倒すときは Fling() を呼ぶ（他プレイヤーに当てられた時など）。
[RequireComponent(typeof(CharacterController))]
public class RagdollController : MonoBehaviour
{
    [Header("参照（未設定なら自動取得）")]
    [SerializeField] private Animator animator;               // モデルのAnimator（子から自動取得）

    [Header("Ragdoll（吹っ飛ばされた時だけ）")]
    [SerializeField] private float recoverTime = 3f;          // 倒れてから起き上がるまで (s)。0以下で起き上がらない
    [SerializeField] private float recoverYOffset = 2f;       // 起き上がる時、地面からこの高さに置く (m)。低く沈むのを防ぐ
    [SerializeField] private string otherPlayerTag = "Player";// このタグの相手に当てられたら倒れる
    [SerializeField] private float flingHitSpeed = 3f;        // 相手がこの速さ以上でぶつかってきたら倒れる (m/s)

    [Header("ボールに当たったら倒れる")]
    [SerializeField] private bool flingOnBallHit = true;      // 速いボールに当たったら倒れる
    [SerializeField] private float ballFlingSpeed = 8f;       // この速さ以上のボールに当たったら倒れる (m/s)
    [SerializeField] private LayerMask ballMask = ~0;         // ボール探索の対象レイヤー

    [Header("テスト用（他プレイヤーに突き飛ばされた再現）")]
    [SerializeField] private bool enableTestKey = true;       // キーで吹っ飛びを再現できるようにする
    [SerializeField] private Key testFlingKey = Key.R;        // このキーで吹っ飛ぶ
    [SerializeField] private float testFlingSpeed = 9f;       // 吹っ飛ぶ勢い (m/s)

    private static readonly Collider[] overlapBuffer = new Collider[16];
    private static readonly RaycastHit[] groundHits = new RaycastHit[16];

    private CharacterController cc;
    private PlayerController playerController;
    private BallHitController hitController;
    private Rigidbody[] bones;         // ragdoll の骨
    private Collider[] boneColliders;  // ragdoll 中だけ有効化する当たり判定
    private Transform hipsBone;        // いちばん根元の骨（復帰時に位置合わせ）
    private Rigidbody hipsBody;        // 腰のRigidbody（KnockbackReceiverが軌道を矯正するのに使う）
    private bool isRagdoll;
    private float ragdollTimer;

    /// いま倒れている（Ragdoll中）か。すでに倒れている相手は打てない等の判定に使う。
    public bool IsDown => isRagdoll;

    /// 腰（根元）の骨。飛行中の位置参照や着地判定に使う。
    public Transform HipsBone => hipsBone;

    /// 腰のRigidbody。KnockbackReceiver が飛行中の速度を矯正するのに使う。
    public Rigidbody HipsBody => hipsBody;

    /// ragdoll の全骨。着地時の減速などに使う。
    public Rigidbody[] Bones => bones;

    /// true の間は内蔵の自動起き上がり（recoverTime）を止める。
    /// KnockbackReceiver が「着地してから」の正しいタイミングで起き上がりを管理するときに使う。
    public bool SuppressAutoRecover { get; set; }

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        hitController = GetComponentInChildren<BallHitController>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // 子の骨（Rigidbody）を集める。ただし Player本体（このオブジェクト自身）に紛れている
        // Rigidbody は ragdoll の骨ではないので除外する。これを含めると FindRootBone が
        // 本体を「腰」と誤認し、本体だけが飛んで見えるモデル（mixamorig6:Hips）とズレる。
        Rigidbody[] allBodies = GetComponentsInChildren<Rigidbody>();
        var boneList = new System.Collections.Generic.List<Rigidbody>();
        Rigidbody selfBody = GetComponent<Rigidbody>();
        foreach (Rigidbody rb in allBodies)
        {
            if (rb == selfBody)
            {
                // 本体に紛れた Rigidbody は静かにして（キネマティック）誤作動を防ぐ
                rb.isKinematic = true;
                continue;
            }
            boneList.Add(rb);
        }
        bones = boneList.ToArray();
        boneColliders = new Collider[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            boneColliders[i] = bones[i].GetComponent<Collider>();
        }
        // 骨の中で最上位（親を辿って最初に見つかるRigidbody）を hips とみなす
        hipsBone = FindRootBone();
        hipsBody = hipsBone != null ? hipsBone.GetComponent<Rigidbody>() : null;

        // 骨同士の当たり判定を切る（手足がぶつかり合って暴れる／横へ滑るのを防ぐ）
        IgnoreSelfCollisions();

        // ragdollで骨が大きく動くと、メッシュの表示範囲の計算が狂って
        // 「画面内なのに描画されない（透明に見える）」ことがあるので、常に再計算させる
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.updateWhenOffscreen = true;
        }

        // 最初はアニメ状態（操作オン・骨は固定）
        SetControlsActive(true);
        SetBonesPhysics(false);
    }

    /// ragdoll の骨同士の衝突を全ペア無効化する。これがないと手足が押し合って軌道が乱れる。
    private void IgnoreSelfCollisions()
    {
        for (int i = 0; i < boneColliders.Length; i++)
        {
            for (int j = i + 1; j < boneColliders.Length; j++)
            {
                if (boneColliders[i] != null && boneColliders[j] != null)
                {
                    Physics.IgnoreCollision(boneColliders[i], boneColliders[j], true);
                }
            }
        }
    }

    private void Update()
    {
        if (!isRagdoll)
        {
            CheckTestKey(); // テスト用：キーで吹っ飛ばされた状態を再現
            CheckBallHit(); // 速いボールが飛んで来たら倒れる
            return;
        }
        // 内蔵の自動起き上がり。KnockbackReceiver が管理しているとき（Suppress中）は使わない
        // （打たれた瞬間から数えると空中で起き上がってしまうため。着地基準は Receiver 側で行う）
        if (recoverTime > 0f && !SuppressAutoRecover)
        {
            ragdollTimer += Time.deltaTime;
            if (ragdollTimer >= recoverTime)
            {
                Recover();
            }
        }
    }

    /// テスト用：キーを押すと「正面から突き飛ばされた」ように後ろ斜め上へ吹っ飛ぶ。
    private void CheckTestKey()
    {
        if (!enableTestKey || Keyboard.current == null)
        {
            return;
        }
        if (Keyboard.current[testFlingKey].wasPressedThisFrame)
        {
            Vector3 dir = (-transform.forward + Vector3.up * 0.6f).normalized;
            Fling(dir * testFlingSpeed);
        }
    }

    /// 自分に向かって飛んで来る速いボールに当たったら倒れる。
    /// 「向かってくる」ボールだけ見るので、自分が打った直後（遠ざかる）ボールでは倒れない。
    private void CheckBallHit()
    {
        if (!flingOnBallHit || cc == null)
        {
            return;
        }

        // CharacterController のカプセルの少し外側を調べる
        Vector3 center = transform.TransformPoint(cc.center);
        float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        float radius = cc.radius + 0.15f;

        int count = Physics.OverlapCapsuleNonAlloc(top, bottom, radius, overlapBuffer, ballMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            GolfBall ball = overlapBuffer[i].GetComponentInParent<GolfBall>();
            if (ball == null)
            {
                continue;
            }
            Rigidbody rb = ball.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
            {
                continue; // 止まって固定されたボールは無視
            }

            Vector3 v = rb.linearVelocity;
            float speed = v.magnitude;
            if (speed < ballFlingSpeed)
            {
                continue;
            }

            // 自分の方へ向かって来ているか（遠ざかるボールでは倒れない）
            Vector3 toMe = (transform.position - ball.transform.position);
            toMe.y = 0f;
            if (toMe.sqrMagnitude > 1e-6f && Vector3.Dot(v.normalized, toMe.normalized) < 0.2f)
            {
                continue;
            }

            Vector3 dir = (v.normalized + Vector3.up * 0.4f).normalized;
            Fling(dir * speed);
            return;
        }
    }

    /// 他プレイヤーなどに飛ばされて倒れる。velocity は吹き飛ぶ勢い。
    /// KnockbackReceiver が付いていればそちらに委譲する（軌道の矯正・着地判定・スタン・無敵を管理）。
    /// 無ければ従来どおり単純に ragdoll 化して初速を与える。
    public void Fling(Vector3 velocity)
    {
        if (isRagdoll)
        {
            return;
        }
        KnockbackReceiver receiver = GetComponent<KnockbackReceiver>();
        if (receiver == null)
        {
            // 付け忘れていても必ず新しい吹っ飛び（予測線どおりの軌道・着地基準の復活）が使われるよう、
            // その場で自動追加する（古い自由物理のフォールバックには落とさない）
            receiver = gameObject.AddComponent<KnockbackReceiver>();
        }
        receiver.ApplyKnockback(velocity.normalized, velocity.magnitude);
    }

    /// ragdoll（ぐにゃぐにゃ）に切り替え、全身に同じ初速を与える。
    /// 操作・移動・打撃・アニメは止まる。起き上がりは Recover()/RecoverAt() で。
    public void EnterRagdollWithVelocity(Vector3 velocity)
    {
        isRagdoll = true;
        ragdollTimer = 0f;

        SetControlsActive(false);  // 操作・移動・打撃・アニメを止める
        SetBonesPhysics(true);     // 物理オン（ぐにゃぐにゃ）

        foreach (Rigidbody b in bones)
        {
            if (b == null) continue;
            b.linearDamping = 0f;             // 空気抵抗なし＝予測線どおりに飛ぶ
            b.angularDamping = 0.05f;
            b.angularVelocity = Vector3.zero;
            b.linearVelocity = velocity;      // 全身に同じ初速 → 重心が放物線（弧）を描く
        }
    }

    /// 起き上がってアニメ状態に戻る。倒れた体（腰）が止まった場所の地面に立って復活する。
    public void Recover()
    {
        Vector3 pos = transform.position;
        if (hipsBone != null)
        {
            Vector3 hips = hipsBone.position;
            float groundY = FindGroundY(hips);
            pos = new Vector3(hips.x, groundY + recoverYOffset, hips.z);
        }
        RecoverAt(pos);
    }

    /// 指定した位置で起き上がってアニメ状態に戻る（場外リスポーンなどに使う）。
    public void RecoverAt(Vector3 position)
    {
        // 先にプレイヤー本体を移す。その後で ragdoll を解除するので、
        // 骨は新しい位置で立ちポーズにスナップする（元の位置へワープして見えない）。
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); // 直立に戻す

        isRagdoll = false;
        SetControlsActive(true);   // 操作を戻す
        SetBonesPhysics(false);    // 骨を固定（Animatorが動かす）
    }

    /// 指定位置の真下にある地面の高さを返す（自分の骨のコライダーは無視する）。
    /// 倒れた体が床に少し沈んでいても床の上面を見つけられるよう、高い位置からレイを打つ。
    private float FindGroundY(Vector3 from)
    {
        int hitCount = Physics.RaycastNonAlloc(from + Vector3.up * 3f, Vector3.down, groundHits, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = from.y;
        float bestDist = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            // 自分（倒れている体）の骨に当たったものは無視
            if (groundHits[i].collider.GetComponentInParent<RagdollController>() == this)
            {
                continue;
            }
            if (groundHits[i].distance < bestDist)
            {
                bestDist = groundHits[i].distance;
                best = groundHits[i].point.y;
            }
        }
        return best;
    }

    /// 操作・移動・打撃・アニメの有効/無効を切り替える（active=通常時）。
    /// PlayerController は無効化せず「Ragdollモード」にする（カメラを制御し続けるため）。
    private void SetControlsActive(bool active)
    {
        if (animator != null) animator.enabled = active;
        if (playerController != null) playerController.SetRagdollMode(!active, active ? null : hipsBone);
        if (hitController != null) hitController.enabled = active;
        cc.enabled = active;
    }

    /// 骨を物理（floppy ragdoll）にするか、固まったポーズ（キネマティック）にするか。
    private void SetBonesPhysics(bool physics)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null)
            {
                if (!physics)
                {
                    // 固定へ：先に速度を消してから kinematic にする（kinematic には速度をセットできない）
                    if (!bones[i].isKinematic)
                    {
                        bones[i].linearVelocity = Vector3.zero;
                        bones[i].angularVelocity = Vector3.zero;
                    }
                    bones[i].isKinematic = true;
                }
                else
                {
                    bones[i].isKinematic = false;
                }
            }
            if (boneColliders[i] != null)
            {
                boneColliders[i].enabled = physics; // 骨の当たり判定は物理中だけ（普段は CharacterController）
            }
        }
    }

    /// 全骨に同じ速度を与え直す（飛行の初速を関節スナップに食われた後、もう一度与えるのに使う）。
    public void SetAllBonesVelocity(Vector3 velocity)
    {
        if (bones == null)
        {
            return;
        }
        foreach (Rigidbody b in bones)
        {
            if (b == null || b.isKinematic)
            {
                continue; // kinematic には速度を代入できない
            }
            b.angularVelocity = Vector3.zero;
            b.linearVelocity = velocity;
        }
    }

    /// 各骨にランダムな角速度を与えて手足をバタつかせる（floppy な見た目にする）。
    /// 角速度は重心の直線運動量を変えないので、飛行の軌道（予測線）はそのまま。
    public void AddRandomSpin(float maxAngularSpeed)
    {
        if (bones == null || maxAngularSpeed <= 0f)
        {
            return;
        }
        foreach (Rigidbody b in bones)
        {
            if (b == null || b.isKinematic)
            {
                continue;
            }
            b.angularVelocity = Random.insideUnitSphere * maxAngularSpeed;
        }
    }

    /// 骨の当たり判定だけを切り替える（飛行中は切って地面・物に引っかからないようにする）。
    public void SetBoneCollidersEnabled(bool enabled)
    {
        if (boneColliders == null)
        {
            return;
        }
        foreach (Collider c in boneColliders)
        {
            if (c != null)
            {
                c.enabled = enabled;
            }
        }
    }

    // 他プレイヤーにぶつかられたら倒れる（CharacterController が当たった相手を教えてくれる）
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (isRagdoll || hit.collider == null || !hit.collider.CompareTag(otherPlayerTag))
        {
            return;
        }
        Rigidbody otherBody = hit.collider.attachedRigidbody;
        float otherSpeed = otherBody != null ? otherBody.linearVelocity.magnitude : 0f;
        if (otherSpeed >= flingHitSpeed)
        {
            Vector3 dir = (transform.position - hit.point).normalized + Vector3.up * 0.5f;
            Fling(dir.normalized * otherSpeed);
        }
    }

    // いちばん根元の骨（親側で最初に出てくる Rigidbody）の Transform を返す
    private Transform FindRootBone()
    {
        Rigidbody root = null;
        int bestDepth = int.MaxValue;
        foreach (Rigidbody b in bones)
        {
            int depth = 0;
            Transform t = b.transform;
            while (t != null && t != transform) { depth++; t = t.parent; }
            if (depth < bestDepth) { bestDepth = depth; root = b; }
        }
        return root != null ? root.transform : null;
    }

    [ContextMenu("テスト：倒す(Ragdoll)")]
    private void TestFling() { Fling(transform.forward * -4f + Vector3.up * 3f); }
    [ContextMenu("テスト：起き上がる")]
    private void TestRecover() { Recover(); }
}
