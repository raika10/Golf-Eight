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

    [Header("テスト用（他プレイヤーに突き飛ばされた再現）")]
    [SerializeField] private bool enableTestKey = true;       // キーで吹っ飛びを再現できるようにする
    [SerializeField] private Key testFlingKey = Key.R;        // このキーで吹っ飛ぶ
    [SerializeField] private float testFlingSpeed = 9f;       // 吹っ飛ぶ勢い (m/s)

    [Header("関節の剛性（吹っ飛び時の伸び対策）")]
    [Tooltip("骨の関節(CharacterJoint)に projection を効かせ、一定以上離れた骨をスナップして繋ぎ止める。\nネットワーク越しで手足がビヨーンと伸びるのを抑える")]
    [SerializeField] private bool strengthenJoints = true;
    [Tooltip("骨がこの距離(m)以上ずれたら関節が引き戻す。小さいほど伸びに厳しい")]
    [SerializeField] private float jointProjectionDistance = 0.05f;
    [Tooltip("骨がこの角度(度)以上ずれたら関節が引き戻す")]
    [SerializeField] private float jointProjectionAngle = 20f;
    [Tooltip("各骨のソルバー反復回数。多いほど関節が剛直になり伸びにくい（重くなる）")]
    [SerializeField] private int boneSolverIterations = 20;

    private static readonly RaycastHit[] groundHits = new RaycastHit[16];

    private CharacterController cc;
    private PlayerController playerController;
    private BallHitController hitController;
    private Rigidbody[] bones;         // ragdoll の骨
    private Collider[] boneColliders;  // ragdoll 中だけ有効化する当たり判定
    private Collider[] bodyColliders;  // 骨以外の自分のコライダー（移動用カプセル・CharacterController）
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

    /// ネットワークのリモート（＝この端末が所有していない）プレイヤーか。
    /// true の間は、本体の位置と CharacterController の有効/無効を触らず NetworkTransform（owner権威）に委譲し、
    /// ラグドールは見た目（骨の物理）だけ再生する。吹っ飛び中の物理は各端末で独立に走って着地点が
    /// わずかにズレるため、位置は owner のものへ一本化しないと撃ち合うたびにズレが累積する。
    /// PlayerNetworkSync が IsOwner に応じて設定する。単体（非ネットワーク）では常に false のまま。
    public bool NetworkRemote { get; set; }

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
        // 骨のコライダーに「跳ねない・摩擦あり」の物理マテリアルを付ける。
        // これで着地時に地面で跳ね返って前後にバウンドせず、摩擦で前へ転がって止まる。
        PhysicsMaterial ragMat = new PhysicsMaterial("RagdollNoBounce")
        {
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum, // どちらか跳ねなければ跳ねない
            dynamicFriction = 0.7f,
            staticFriction = 0.7f,
            frictionCombine = PhysicsMaterialCombine.Average,
        };
        for (int i = 0; i < bones.Length; i++)
        {
            boneColliders[i] = bones[i].GetComponent<Collider>();
            if (boneColliders[i] != null)
            {
                boneColliders[i].sharedMaterial = ragMat;
            }
            // 着地後に地面を滑って壁へ速く当たった時、ボールと同じように壁を壊せるようにする
            if (bones[i].GetComponent<RagdollBoneWallBreaker>() == null)
            {
                bones[i].gameObject.AddComponent<RagdollBoneWallBreaker>();
            }

            // 関節を剛直にして「伸び」を抑える。projection は骨が一定以上離れると強制的に引き戻すので、
            // ネットワーク越しに手足がビヨーンと伸びる（関節が引き伸ばされる）のを物理側で潰せる。
            if (strengthenJoints)
            {
                CharacterJoint joint = bones[i].GetComponent<CharacterJoint>();
                if (joint != null)
                {
                    joint.enableProjection = true;
                    joint.projectionDistance = jointProjectionDistance;
                    joint.projectionAngle = jointProjectionAngle;
                    joint.enablePreprocessing = false; // プリプロセスを切ると引き伸ばし時の挙動が安定する
                }
                // ソルバー反復を増やすと関節がより硬く保たれ、伸びにくくなる
                bones[i].solverIterations = boneSolverIterations;
                bones[i].solverVelocityIterations = boneSolverIterations;
            }
        }
        // 骨の中で最上位（親を辿って最初に見つかるRigidbody）を hips とみなす
        hipsBone = FindRootBone();
        hipsBody = hipsBone != null ? hipsBone.GetComponent<Rigidbody>() : null;

        // 骨以外の自分のコライダー（移動用のカプセル・CharacterController）を集める。
        // これらは倒れている間も骨を包んだまま残るので、骨と衝突させてはいけない。
        bodyColliders = CollectBodyColliders();

        // 骨同士／骨と本体カプセルの当たり判定を切る
        ApplyCollisionIgnores();

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

    /// 骨以外の「自分のコライダー」を集める。移動用のカプセル（CapsuleCollider）や
    /// CharacterController が該当する。CharacterController も Collider の一種なので一緒に取れる。
    private Collider[] CollectBodyColliders()
    {
        var result = new System.Collections.Generic.List<Collider>();
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (System.Array.IndexOf(boneColliders, c) < 0)
            {
                result.Add(c); // 骨に属さない＝本体側のコライダー
            }
        }
        return result.ToArray();
    }

    /// 当たり判定の「無視」設定をまとめて入れる。
    ///
    /// ① 骨どうし：これがないと手足が押し合って暴れる／横へ滑る。
    ///
    /// ② 骨と本体カプセル：★これが無いと弱く打たれた時に床を突き抜ける★
    ///    移動用カプセルや CharacterController は、飛んでいる間もその場に残り続ける（飛ぶのは骨だけで、
    ///    本体の transform は動かないため）。飛行中は骨の当たり判定がOFFなので問題は表に出ないが、
    ///    着地で当たり判定を戻した瞬間に「骨がカプセルの中に深くめり込んでいる」状態になり、
    ///    PhysXがそれを解消しようと骨を高速で弾き飛ばす。床は薄いので簡単に突き抜けて落ちていく。
    ///    強く打った時は体がカプセルの外まで飛ぶので起きず、弱く打った時ほど確実に起きる。
    ///
    /// Unity の IgnoreCollision はコライダーを無効化すると失われるので、有効化するたびに呼び直すこと。
    private void ApplyCollisionIgnores()
    {
        for (int i = 0; i < boneColliders.Length; i++)
        {
            if (boneColliders[i] == null)
            {
                continue;
            }
            for (int j = i + 1; j < boneColliders.Length; j++)
            {
                if (boneColliders[j] != null)
                {
                    Physics.IgnoreCollision(boneColliders[i], boneColliders[j], true);
                }
            }
            foreach (Collider body in bodyColliders)
            {
                // 無効なコライダーに IgnoreCollision を呼ぶとUnityがエラーを出すので飛ばす。
                // 本体のCharacterControllerは倒れている間 無効なのでここに来るが、
                // 無効な以上ぶつからないので無視設定も要らない（Character側の重複CCは有効なまま＝ここで効く）。
                if (body != null && body.enabled && body.gameObject.activeInHierarchy)
                {
                    Physics.IgnoreCollision(boneColliders[i], body, true);
                }
            }
        }
    }

    private void Update()
    {
        if (!isRagdoll)
        {
            CheckTestKey(); // テスト用：キーで吹っ飛ばされた状態を再現
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

    /// ボール（GolfBall）が物理的に衝突した瞬間に呼ばれる（GolfBall.OnCollisionEnter から）。
    /// relativeVelocity は衝突の瞬間の正確な相対速度（Collision.relativeVelocity）なので、
    /// 「同フレーム内で跳ね返った後に判定して逃げ方向と誤判定される」ことがなく、
    /// 遅いボールで誤って飛ぶこともない（衝突の瞬間の実測値で閾値判定するため）。
    public void ApplyBallImpact(Vector3 relativeVelocity)
    {
        if (!flingOnBallHit || isRagdoll)
        {
            return;
        }
        float speed = relativeVelocity.magnitude;
        if (speed < ballFlingSpeed)
        {
            return; // 遅すぎるボールでは飛ばない
        }
        // relativeVelocity は「ボールの進行方向の逆」を向く（壁破壊で -relativeVelocity を進行方向に使っているのと同じ）。
        // 反転して、プレイヤーをボールの進む向きへ飛ばす。
        Vector3 dir = (-relativeVelocity.normalized + Vector3.up * 0.4f).normalized;
        Fling(dir * speed);
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
            // 補間オン：物理は50Hzだが描画は毎フレーム補間される。これが無いと手足・頭が
            // 物理レートでしか更新されず、高フレームレート時にカクカクして見える（見た目のガタつき）。
            b.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// 起き上がってアニメ状態に戻る。倒れた体（腰）が「今」止まっている場所の地面に立って復活する。
    /// 着地後スタン中に誰かに押されて位置がズレている可能性があるので、着地直後の位置を使いたい
    /// 場合は RecoverAtHips(Vector3) を使うこと（KnockbackReceiver はそちらを使う）。
    public void Recover()
    {
        Vector3 pos = transform.position;
        if (hipsBone != null)
        {
            pos = ComputeStandPosition(hipsBone.position);
        }
        RecoverAt(pos);
    }

    /// 指定した腰の位置を基準に、その真下の地面に立って復活する。
    /// 着地した瞬間の位置を KnockbackReceiver 側で記録しておき、ここに渡すことで、
    /// スタン中（起き上がるまでの間）に他プレイヤーにぶつかられて体が押されても、
    /// 起き上がり位置が「本来着地した場所」からズレないようにする。
    public void RecoverAtHips(Vector3 hipsPosition)
    {
        RecoverAt(ComputeStandPosition(hipsPosition));
    }

    /// 指定した腰の位置の真下の地面に、直立で立つときの位置を計算する。
    private Vector3 ComputeStandPosition(Vector3 hipsPosition)
    {
        float groundY = FindGroundY(hipsPosition);
        return new Vector3(hipsPosition.x, groundY + recoverYOffset, hipsPosition.z);
    }

    /// 着地後、カメラを追従から着地点固定に切り替える（KnockbackReceiver が着地時に呼ぶ）。
    public void FreezeRagdollCamera(Vector3 worldPos)
    {
        if (playerController != null)
        {
            playerController.FreezeRagdollCameraAt(worldPos);
        }
    }

    /// 指定した位置で起き上がってアニメ状態に戻る（場外リスポーンなどに使う）。
    public void RecoverAt(Vector3 position)
    {
        // リモート（非所有）プレイヤーは本体位置を NetworkTransform（owner権威）に委譲する。
        // 自前のラグドール物理で求めた着地点でワープさせると owner の着地点とズレ、累積するため触らない。
        if (!NetworkRemote)
        {
            // 先にプレイヤー本体を移す。その後で ragdoll を解除するので、
            // 骨は新しい位置で立ちポーズにスナップする（元の位置へワープして見えない）。
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); // 直立に戻す
        }

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

    /// 骨を「今のポーズのまま」固定（キネマティック）する。物理シミュレーションが止まるので、
    /// 動いているカメラから見てもカクつかない（着地後スタン中にこれを呼んで動きを止める）。
    /// ポーズは崩れた形のまま保持される。RecoverAt でアニメ状態に戻すまで動かない。
    public void FreezeBonesPose()
    {
        if (bones == null)
        {
            return;
        }
        foreach (Rigidbody b in bones)
        {
            if (b == null)
            {
                continue;
            }
            if (!b.isKinematic)
            {
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
            }
            b.isKinematic = true; // 物理停止＝完全静止＝どのカメラから見ても滑らか
        }
    }

    /// 操作・移動・打撃・アニメの有効/無効を切り替える（active=通常時）。
    /// PlayerController は無効化せず「Ragdollモード」にする（カメラを制御し続けるため）。
    private void SetControlsActive(bool active)
    {
        if (animator != null) animator.enabled = active;
        if (playerController != null) playerController.SetRagdollMode(!active, active ? null : hipsBone);
        if (hitController != null) hitController.enabled = active;
        // リモート（非所有）プレイヤーの CharacterController は NetworkTransform が管理するので触らない。
        // ここで有効化すると、非所有側で重力移動・着地計算が独自に走って owner権威の位置同期と競合し、
        // 撃ち合うたびに位置がズレて累積する（見た目のラグドール＝骨の物理は NetworkRemote でも再生される）。
        if (!NetworkRemote)
        {
            cc.enabled = active;
        }
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
        if (physics)
        {
            ApplyCollisionIgnores(); // 有効化で無視設定が失われるので入れ直す
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
        if (enabled)
        {
            // ★着地の瞬間はここを通る★ 無視設定は無効化で失われているので、必ず入れ直してから
            // 物理に戻す。これを忘れると、骨が本体カプセルにめり込んだ状態で弾き飛ばされる。
            ApplyCollisionIgnores();
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
