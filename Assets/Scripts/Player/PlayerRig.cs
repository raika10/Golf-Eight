using UnityEngine;

/// プレイヤーの見た目と動きを Unity 内だけで作る（外部アセット不要）。
/// ・プリミティブで人型（頭・胴・上腕/前腕・腿/すね・足）＋ゴルフクラブを自動生成（クラブは右手に持つ）
/// ・待機／走り（膝・肘も曲がる）／スイングをコードでアニメーション
/// ・他プレイヤーに飛ばされた時だけ Ragdoll（物理でぐにゃっと倒れる）に切り替わる
///
/// Player（CharacterController）に付ける。走る速さは CharacterController の速度から自動で読む。
/// 打撃時のスイングは BallHitController から Swing() を呼ぶ。倒すときは Fling() を呼ぶ。
[RequireComponent(typeof(CharacterController))]
public class PlayerRig : MonoBehaviour
{
    [Header("体の大きさ")]
    [SerializeField] private float hipHeight = 1.0f;   // 腰の高さ（Player原点＝足元からの高さ m）
    [SerializeField] private Color bodyColor = new Color(0.2f, 0.5f, 0.9f);
    [SerializeField] private Color skinColor = new Color(0.95f, 0.8f, 0.7f);
    [SerializeField] private Color clubColor = new Color(0.85f, 0.85f, 0.85f);

    [Header("走りアニメ")]
    [SerializeField] private float runCycleSpeed = 9f;         // 手足を振る速さ
    [SerializeField] private float runSwingAngle = 42f;        // 腿・腕の振り幅（度）
    [SerializeField] private float fullRunSpeed = 6f;          // この移動速度で振り幅が最大になる (m/s)

    [Header("スイングアニメ")]
    [SerializeField] private float swingDuration = 0.5f;       // スイング1回の時間 (s)

    [Header("Ragdoll（他プレイヤーに飛ばされた時だけ）")]
    [SerializeField] private float recoverTime = 3f;           // 倒れてから起き上がるまで (s)。0以下で起き上がらない
    [SerializeField] private string otherPlayerTag = "Player"; // このタグの相手に当てられたら倒れる
    [SerializeField] private float flingHitSpeed = 3f;         // 相手がこの速さ以上でぶつかってきたら倒れる (m/s)

    private CharacterController cc;
    private PlayerController playerController;
    private Transform rigRoot;

    // 動かすパーツ（ピボット＝関節位置）
    private Transform hips, torso, head;
    private Transform upperArmL, forearmL, upperArmR, forearmR;
    private Transform thighL, shinL, thighR, shinR;

    private Rigidbody[] bones;         // ragdoll 用の全ボーン
    private Collider[] boneColliders;  // ragdoll 中だけ有効化する当たり判定
    private Vector3[] boneLocalPos;    // 生成時の各ボーンのローカル位置（復帰時に戻す）
    private Quaternion[] boneLocalRot; // 生成時の各ボーンのローカル回転
    private Vector3 torsoBase;

    private float runPhase;
    private float swingTimer = -1f;    // 0以上ならスイング中
    private bool isRagdoll;
    private float ragdollTimer;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();

        // 元のカプセルの見た目は隠す（自作の体を出すため）
        MeshRenderer capsule = GetComponent<MeshRenderer>();
        if (capsule != null)
        {
            capsule.enabled = false;
        }

        BuildBody();
        SetRagdoll(false); // 最初はアニメ（キネマティック）状態
    }

    private void Update()
    {
        if (isRagdoll)
        {
            if (recoverTime > 0f)
            {
                ragdollTimer += Time.deltaTime;
                if (ragdollTimer >= recoverTime)
                {
                    Recover();
                }
            }
            return;
        }

        Vector3 v = cc.velocity;
        float speed = new Vector2(v.x, v.z).magnitude;
        Animate(speed);
    }

    // ============ アニメーション ============

    private void Animate(float speed)
    {
        float run01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, fullRunSpeed));
        runPhase += Time.deltaTime * runCycleSpeed * run01;

        float swing = Mathf.Sin(runPhase) * runSwingAngle * run01; // 腿・腕の前後振り
        float idle = Mathf.Sin(Time.time * 2f) * 2f;               // 待機の軽い揺れ

        // 脚：左右逆位相で前後に振る
        SetLocalPitch(thighL, swing);
        SetLocalPitch(thighR, -swing);
        // 膝：後ろに引いた脚を曲げる（走りらしく）
        SetLocalPitch(shinL, Mathf.Max(0f, -Mathf.Sin(runPhase)) * 60f * run01);
        SetLocalPitch(shinR, Mathf.Max(0f, -Mathf.Sin(runPhase + Mathf.PI)) * 60f * run01);

        // 左腕：脚と逆に振る＋肘を軽く曲げる
        SetLocalPitch(upperArmL, -swing + idle);
        SetLocalPitch(forearmL, 22f * run01 + 8f);

        // 右腕：スイング中はスイングの弧、普段は走りに合わせて振る（クラブを持ったまま）
        if (swingTimer >= 0f)
        {
            swingTimer += Time.deltaTime;
            float t = Mathf.Clamp01(swingTimer / Mathf.Max(0.01f, swingDuration));
            float e = Mathf.SmoothStep(0f, 1f, t);
            SetLocalPitch(upperArmR, Mathf.Lerp(-135f, 55f, e)); // 振り上げ→振り下ろし
            SetLocalPitch(forearmR, Mathf.Lerp(25f, 5f, e));     // 肘は少しだけ
            if (t >= 1f)
            {
                swingTimer = -1f;
            }
        }
        else
        {
            SetLocalPitch(upperArmR, swing + idle);
            SetLocalPitch(forearmR, 22f * run01 + 8f);
        }

        // 胴の上下（走りの躍動）
        if (torso != null)
        {
            float bob = Mathf.Abs(Mathf.Cos(runPhase)) * 0.05f * run01;
            torso.localPosition = torsoBase + Vector3.up * bob;
        }
    }

    private void SetLocalPitch(Transform t, float degrees)
    {
        if (t != null)
        {
            t.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }
    }

    /// 打撃時に呼ぶ：スイングのアニメを再生する。
    public void Swing()
    {
        if (isRagdoll)
        {
            return;
        }
        swingTimer = 0f;
    }

    // ============ Ragdoll ============

    /// 他プレイヤーなどに飛ばされて倒れる。velocity は吹き飛ぶ勢い。
    public void Fling(Vector3 velocity)
    {
        if (isRagdoll)
        {
            return;
        }
        SetRagdoll(true);
        ragdollTimer = 0f;
        if (bones != null)
        {
            foreach (Rigidbody b in bones)
            {
                if (b != null)
                {
                    b.linearVelocity = velocity;
                }
            }
        }
    }

    /// 起き上がってアニメ状態に戻る。
    public void Recover()
    {
        if (hips != null)
        {
            Vector3 pos = hips.position;
            transform.position = new Vector3(pos.x, transform.position.y, pos.z);
        }
        SetRagdoll(false);
    }

    private void SetRagdoll(bool on)
    {
        isRagdoll = on;

        if (playerController != null) playerController.enabled = !on;
        cc.enabled = !on;

        if (bones != null)
        {
            foreach (Rigidbody b in bones)
            {
                if (b == null) continue;
                b.isKinematic = !on;
                if (!on)
                {
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }
            }
        }
        if (boneColliders != null)
        {
            foreach (Collider c in boneColliders)
            {
                if (c != null) c.enabled = on;
            }
        }

        if (!on)
        {
            ResetPose();
        }
    }

    private void ResetPose()
    {
        if (rigRoot != null)
        {
            rigRoot.localPosition = new Vector3(0f, hipHeight, 0f);
            rigRoot.localRotation = Quaternion.identity;
        }
        if (bones != null && boneLocalPos != null)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].transform.localPosition = boneLocalPos[i];
                bones[i].transform.localRotation = boneLocalRot[i];
            }
        }
        swingTimer = -1f;
    }

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

    // ============ 体の生成 ============

    private void BuildBody()
    {
        rigRoot = new GameObject("Rig").transform;
        rigRoot.SetParent(transform, false);
        rigRoot.localPosition = new Vector3(0f, hipHeight, 0f);

        // 胴体（腰→胴→頭）。torso は上に伸ばすので visualDrop を負に。
        hips = MakePart("Hips", rigRoot, Vector3.zero, new Vector3(0.28f, 0.18f, 0.2f), 0f, true, PrimitiveType.Capsule, bodyColor);
        torso = MakePart("Torso", hips, new Vector3(0f, 0.06f, 0f), new Vector3(0.34f, 0.5f, 0.24f), -0.25f, true, PrimitiveType.Capsule, bodyColor);
        torsoBase = torso.localPosition;
        MakeVisual("Neck", torso, new Vector3(0f, 0.48f, 0f), new Vector3(0.1f, 0.12f, 0.1f), Vector3.zero, PrimitiveType.Capsule, skinColor);
        head = MakePart("Head", torso, new Vector3(0f, 0.62f, 0f), new Vector3(0.23f, 0.25f, 0.23f), 0f, true, PrimitiveType.Sphere, skinColor);

        // 腕（肩→上腕→前腕→手）。腕は下に垂らすので visualDrop は正。
        BuildArm(-1f, out upperArmL, out forearmL);
        BuildArm(1f, out upperArmR, out forearmR);
        BuildClub(forearmR); // クラブは右の前腕（手）の先に

        // 脚（腰→腿→すね→足）
        BuildLeg(-1f, out thighL, out shinL);
        BuildLeg(1f, out thighR, out shinR);

        CollectBones();
    }

    private void BuildArm(float side, out Transform upperArm, out Transform forearm)
    {
        upperArm = MakePart("UpperArm", torso, new Vector3(0.2f * side, 0.46f, 0f), new Vector3(0.1f, 0.28f, 0.1f), 0.14f, true, PrimitiveType.Capsule, bodyColor);
        forearm = MakePart("Forearm", upperArm, new Vector3(0f, -0.28f, 0f), new Vector3(0.09f, 0.26f, 0.09f), 0.13f, true, PrimitiveType.Capsule, skinColor);
        MakeVisual("Hand", forearm, new Vector3(0f, -0.28f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Vector3.zero, PrimitiveType.Sphere, skinColor);
    }

    private void BuildLeg(float side, out Transform thigh, out Transform shin)
    {
        thigh = MakePart("Thigh", hips, new Vector3(0.1f * side, 0f, 0f), new Vector3(0.15f, 0.45f, 0.15f), 0.22f, true, PrimitiveType.Capsule, bodyColor);
        shin = MakePart("Shin", thigh, new Vector3(0f, -0.45f, 0f), new Vector3(0.13f, 0.42f, 0.13f), 0.21f, true, PrimitiveType.Capsule, bodyColor);
        MakeVisual("Foot", shin, new Vector3(0f, -0.42f, 0.07f), new Vector3(0.13f, 0.08f, 0.24f), Vector3.zero, PrimitiveType.Cube, bodyColor);
    }

    /// 関節ピボットを作り、見た目メッシュを offset して付ける（物理ボーンはコライダー＋Rigidbody付き）。
    private Transform MakePart(string name, Transform parent, Vector3 localPos, Vector3 size, float visualDrop, bool physics, PrimitiveType type, Color color)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = localPos;

        GameObject mesh = GameObject.CreatePrimitive(type);
        mesh.name = name + "_Mesh";
        mesh.transform.SetParent(pivot.transform, false);
        mesh.transform.localPosition = new Vector3(0f, -visualDrop, 0f);
        mesh.transform.localScale = size;
        Paint(mesh, color);
        Destroy(mesh.GetComponent<Collider>()); // 見た目メッシュの当たり判定は不要

        if (physics)
        {
            CapsuleCollider col = pivot.AddComponent<CapsuleCollider>();
            col.direction = 1; // Y軸
            col.height = Mathf.Max(size.y, size.x);
            col.radius = Mathf.Max(size.x, size.z) * 0.5f;
            col.center = new Vector3(0f, -visualDrop, 0f);
            col.enabled = false;

            Rigidbody rb = pivot.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.isKinematic = true;
        }
        return pivot.transform;
    }

    /// 見た目だけのパーツ（首・手・足など。物理ボーンではない）。
    private void MakeVisual(string name, Transform parent, Vector3 localPos, Vector3 size, Vector3 euler, PrimitiveType type, Color color)
    {
        GameObject mesh = GameObject.CreatePrimitive(type);
        mesh.name = name;
        mesh.transform.SetParent(parent, false);
        mesh.transform.localPosition = localPos;
        mesh.transform.localEulerAngles = euler;
        mesh.transform.localScale = size;
        Paint(mesh, color);
        Destroy(mesh.GetComponent<Collider>());
    }

    private void BuildClub(Transform hand)
    {
        GameObject club = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        club.name = "Club";
        club.transform.SetParent(hand, false);
        club.transform.localPosition = new Vector3(0f, -0.28f, 0.05f); // 手のあたり
        club.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        club.transform.localScale = new Vector3(0.04f, 0.45f, 0.04f);
        Paint(club, clubColor);
        Destroy(club.GetComponent<Collider>());
    }

    private void CollectBones()
    {
        bones = rigRoot.GetComponentsInChildren<Rigidbody>();
        boneColliders = rigRoot.GetComponentsInChildren<Collider>();

        boneLocalPos = new Vector3[bones.Length];
        boneLocalRot = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            boneLocalPos[i] = bones[i].transform.localPosition;
            boneLocalRot[i] = bones[i].transform.localRotation;
        }

        foreach (Rigidbody rb in bones)
        {
            Rigidbody parentBone = FindParentBone(rb.transform);
            if (parentBone != null)
            {
                CharacterJoint joint = rb.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parentBone;
            }
        }
    }

    private Rigidbody FindParentBone(Transform t)
    {
        Transform p = t.parent;
        while (p != null && p != rigRoot.parent)
        {
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null) return rb;
            p = p.parent;
        }
        return null;
    }

    private void Paint(GameObject go, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;
    }

    // シーンビューでテストしやすいように、右クリックから倒す/起こすを実行できる
    [ContextMenu("テスト：倒す(Ragdoll)")]
    private void TestFling() { Fling(transform.forward * -4f + Vector3.up * 3f); }
    [ContextMenu("テスト：起き上がる")]
    private void TestRecover() { Recover(); }
}
