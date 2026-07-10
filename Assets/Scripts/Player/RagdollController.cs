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

    private CharacterController cc;
    private PlayerController playerController;
    private BallHitController hitController;
    private Rigidbody[] bones;         // ragdoll の骨
    private Collider[] boneColliders;  // ragdoll 中だけ有効化する当たり判定
    private Transform hipsBone;        // いちばん根元の骨（復帰時に位置合わせ）
    private bool isRagdoll;
    private float ragdollTimer;

    /// いま倒れている（Ragdoll中）か。すでに倒れている相手は打てない等の判定に使う。
    public bool IsDown => isRagdoll;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        hitController = GetComponentInChildren<BallHitController>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // 子の骨（Rigidbody）を集める。Player本体にRigidbodyは無い前提。
        bones = GetComponentsInChildren<Rigidbody>();
        boneColliders = new Collider[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            boneColliders[i] = bones[i].GetComponent<Collider>();
        }
        // 骨の中で最上位（親を辿って最初に見つかるRigidbody）を hips とみなす
        hipsBone = FindRootBone();

        SetRagdoll(false); // 最初はアニメ状態
    }

    private void Update()
    {
        if (!isRagdoll)
        {
            CheckTestKey(); // テスト用：キーで吹っ飛ばされた状態を再現
            CheckBallHit(); // 速いボールが飛んで来たら倒れる
            return;
        }
        if (recoverTime > 0f)
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
    /// 全ての骨に同じ速度を与え、回転と空気抵抗を消すことで、ボールのようにまっすぐ飛ぶ。
    public void Fling(Vector3 velocity)
    {
        if (isRagdoll)
        {
            return;
        }
        SetRagdoll(true);
        ragdollTimer = 0f;
        foreach (Rigidbody b in bones)
        {
            if (b == null)
            {
                continue;
            }
            b.linearDamping = 0f;          // 空気抵抗を消して予測線どおりに飛ぶ
            b.angularDamping = 0.05f;
            b.angularVelocity = Vector3.zero; // 変な回転で軌道が乱れないように
            b.linearVelocity = velocity;      // 全身に同じ初速 → 重心が放物線を描く
        }
    }

    /// 起き上がってアニメ状態に戻る。
    public void Recover()
    {
        if (hipsBone != null)
        {
            Vector3 pos = hipsBone.position;
            transform.position = new Vector3(pos.x, transform.position.y, pos.z);
        }
        SetRagdoll(false);
    }

    private void SetRagdoll(bool on)
    {
        isRagdoll = on;

        // 倒れている間は入力・移動・打撃・アニメを止める。
        // PlayerController は無効化せず「Ragdollモード」にする（カメラを固定して制御し続けるため）。
        if (animator != null) animator.enabled = !on;
        if (playerController != null) playerController.SetRagdollMode(on, on ? hipsBone : null);
        if (hitController != null) hitController.enabled = !on;
        cc.enabled = !on;

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null)
            {
                if (!on)
                {
                    // アニメ状態へ：先に速度を消してから kinematic にする（kinematic には速度をセットできない）
                    if (!bones[i].isKinematic)
                    {
                        bones[i].linearVelocity = Vector3.zero;
                        bones[i].angularVelocity = Vector3.zero;
                    }
                    bones[i].isKinematic = true; // Animatorが骨を動かす
                }
                else
                {
                    bones[i].isKinematic = false; // ragdoll中は物理
                }
            }
            if (boneColliders[i] != null)
            {
                boneColliders[i].enabled = on; // 骨の当たり判定は ragdoll 中だけ（普段は CharacterController）
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
