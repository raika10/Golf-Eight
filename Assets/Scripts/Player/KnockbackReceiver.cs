using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// 吹っ飛ばされる側の本体。IKnockbackable として「方向＋威力」を受け取り、
/// ragdoll（ぐにゃぐにゃ）のまま予測線どおりの放物線で吹っ飛ぶ。
///
/// 仕組み：飛行中、毎物理ステップで腰（Hips）の速度を理想の放物線速度 v(t)=v0+g*t に矯正する。
/// 関節に引っ張られて軌道が曲がる／回転するのを毎ステップ打ち消すので、狙った方向へまっすぐ飛ぶ。
/// 手足は関節でぶら下がったまま＝体はぐにゃぐにゃして見える。
///
/// 流れ：ApplyKnockback → 飛行（操作無効） → 着地判定 → 横滑りせず崩れる → stunTime 後にその場で復活
///       → graceTime の間は無敵（連続で吹っ飛ばされない）
///
/// 使い方：Player（RagdollController と同じオブジェクト）に付けるだけ。
/// 自爆武器の反動は、自分の ApplyKnockback を呼べば同じロジックで吹っ飛ぶ。
[RequireComponent(typeof(RagdollController))]
public class KnockbackReceiver : MonoBehaviour, IKnockbackable
{
    [Header("威力・角度")]
    [SerializeField] private float powerMultiplier = 1f; // 受ける威力の倍率（この相手だけ飛びやすくする等）
    [SerializeField] private float minLaunchAngle = 0f;  // 最低の打ち上げ角（度）。0なら攻撃側の角度のまま（＝予測線と一致）
    [SerializeField] private float maxFlightTime = 6f;   // 飛行の最大時間 (s)。保険
    [SerializeField] private float groundDetectDistance = 1.0f; // 腰の下このm以内に地面が来たら着地（立ち姿勢の腰の高さ）
    [SerializeField] private float minAirTime = 0.15f;   // 発射直後この秒数は着地判定しない（即着地を防ぐ）

    [Header("ぐにゃぐにゃ具合")]
    [Tooltip("飛行中の手足のバタつき。大きいほどぐにゃぐにゃ回る（重心の軌道＝予測線は変わらない）")]
    [SerializeField] private float floppiness = 6f;      // 発射時に各骨へ与えるランダム回転の強さ (rad/s)

    [Header("着地後")]
    [SerializeField] private float stunTime = 1.0f;      // 着地してから起き上がるまで (s)
    [SerializeField] private float graceTime = 0.5f;     // 起き上がった後の無敵時間 (s)

    [Header("場外リスポーン")]
    [SerializeField] private float minY = -20f;          // これより下に落ちたらリスポーン
    [SerializeField] private Transform respawnPoint;     // リスポーン地点（未設定なら開始位置）
    [Tooltip("リスポーンした時に呼ばれる（演出やスコア処理をつなぐ）")]
    public UnityEvent onRespawned;

    private RagdollController ragdoll;
    private Rigidbody hips;
    private Vector3 initialPosition;  // 開始位置（respawnPoint 未設定時のリスポーン先）
    private Vector3 landedHipsPosition; // 着地した瞬間の腰の位置（スタン中に押されても起き上がり位置をズレさせないため）
    private float graceUntil;         // この時刻まで無敵
    private Coroutine flightRoutine;
    private static readonly RaycastHit[] groundHits = new RaycastHit[8];

    /// いま吹っ飛ばされてダウン中か。
    public bool IsKnockedDown => ragdoll != null && ragdoll.IsDown;

    /// 着地後の無敵時間中か。
    public bool IsInvulnerable => Time.time < graceUntil;

    private void Awake()
    {
        ragdoll = GetComponent<RagdollController>();
        // 起き上がりは「着地してから stunTime」でこちらが管理する（打たれた瞬間から数えると空中で復活してしまう）
        ragdoll.SuppressAutoRecover = true;
        initialPosition = transform.position;
    }

    private void Update()
    {
        // 場外チェック：マップ外や水場の下に落ちたらリスポーン
        Vector3 pos = (IsKnockedDown && ragdoll.HipsBone != null) ? ragdoll.HipsBone.position : transform.position;
        if (pos.y < minY)
        {
            TriggerRespawn();
        }
    }

    /// この方向・威力で吹っ飛ばした時の「実際の初速 v0」を返す。
    /// minLaunchAngle（最低打ち上げ角）と powerMultiplier を反映するので、
    /// 予測線をこの v0 で描けば、実際に飛ぶ軌道と完全に一致する。
    public Vector3 GetLaunchVelocity(Vector3 direction, float power)
    {
        if (direction.sqrMagnitude < 1e-6f || power <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 dir = direction.normalized;

        // 最低の打ち上げ角を保証（0なら攻撃側の角度のまま）
        if (minLaunchAngle > 0f)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                float elevation = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (elevation < minLaunchAngle)
                {
                    flat.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, flat);
                    dir = (Quaternion.AngleAxis(-minLaunchAngle, right) * flat).normalized;
                }
            }
        }

        return dir * (power * powerMultiplier);
    }

    /// 「方向＋威力」で吹っ飛ばす（IKnockbackable）。ダウン中・無敵中は無視。
    public void ApplyKnockback(Vector3 direction, float power)
    {
        if (IsKnockedDown || IsInvulnerable)
        {
            return;
        }

        // 実際の初速（打ち上げ角・倍率を反映）。予測線もこれと同じ計算で描いている。
        Vector3 v0 = GetLaunchVelocity(direction, power);
        if (v0.sqrMagnitude < 1e-6f)
        {
            return;
        }

        // ragdoll化（操作無効＋ぐにゃぐにゃ）して全身に初速。以降の軌道は FlightRoutine が管理
        ragdoll.EnterRagdollWithVelocity(v0);
        // 飛行中は骨の当たり判定を切る（足が地面に引っかかって体がHipsの軌道から外れるのを防ぐ）
        ragdoll.SetBoneCollidersEnabled(false);
        if (hips == null)
        {
            hips = ragdoll.HipsBody;
        }
        if (flightRoutine != null)
        {
            StopCoroutine(flightRoutine);
            flightRoutine = null;
        }
        if (hips != null)
        {
            flightRoutine = StartCoroutine(FlightRoutine(v0));
        }
    }

    /// 飛行：腰（Hips）の速度を毎物理ステップ、理想の放物線速度 v(t)=v0+g*t に矯正し続ける。
    /// 関節に引っ張られようが、何が起きようが、腰は常に「正しい方向・正しい速さ」へ押し戻されるので、
    /// 軌道が予測線からズレることがない（パルス的に毎ステップ同じ計算で力＝速度を与え直すイメージ）。
    /// 手足は関節でぶら下がったまま自由に動く＝ぐにゃぐにゃは維持される（腰だけを矯正し、他は矯正しない）。
    ///
    /// 重要：骨をキネマティック→ダイナミックに切り替えた「最初の1物理ステップ」で、関節（CharacterJoint）が
    /// 　現在ポーズへスナップする勢いに v0 が食われて速度がほぼ0になることがある（＝打った所から飛ばない）。
    /// 　そこで最初のFixedUpdateを1回待ってから、全骨へ v0 を与える。
    private IEnumerator FlightRoutine(Vector3 v0)
    {
        // 1ステップ待ってから全骨へ初速を与える（関節スナップに食われた分を取り戻す）。
        // 手足はこの v0 と重力で腰と同じ放物線を飛ぶので、腰から離れず一緒に飛ぶ。
        yield return new WaitForFixedUpdate();
        ragdoll.SetAllBonesVelocity(v0);
        // 各骨にランダムな回転を加えて手足をバタつかせる（ぐにゃぐにゃ）
        if (floppiness > 0f)
        {
            ragdoll.AddRandomSpin(floppiness);
        }

        Vector3 startPos = hips.position; // 基準点（この時刻を t=0 とする放物線を描く）

        // 腰をキネマティック化し、補間はオフにする。
        // ★カクつきの根本原因対策★：カメラ(LateUpdate)が追う位置と、実際に描画される位置がズレると、
        //   視点を動かした時に体がカクついて見える（物理は50Hz、補間はLateUpdateの後に適用されるため）。
        //   そこで腰を「物理レート」ではなく「描画フレームごと(yield return null)」に自前で動かす。
        //   こうするとカメラが読む位置＝描画される位置になり、視点を動かしても一切カクつかない。
        //   補間はこの自前更新と衝突するのでオフにする（毎フレーム正確な位置を入れるので補間は不要）。
        hips.isKinematic = true;
        hips.interpolation = RigidbodyInterpolation.None;

        float t = 0f;
        while (t < maxFlightTime)
        {
            yield return null;          // 描画フレームごと＝カメラと同じタイミングで位置を更新
            t += Time.deltaTime;

            // 腰を式どおりの位置へ（キネマティックなので何にも邪魔されない・描画フレーム精度で滑らか）
            hips.transform.position = startPos + v0 * t + 0.5f * Physics.gravity * (t * t);

            // 着地判定は式から計算した鉛直速度で行う
            float vy = v0.y + Physics.gravity.y * t;

            // 発射直後（minAirTime秒）は着地判定しない。足元に地面がある状態で始まるので即着地を防ぐ
            if (t < minAirTime)
            {
                continue;
            }
            // 落下に転じてから足元（腰の下）に地面が来たら着地
            if (vy < 0f && IsGroundBelow(hips.transform.position, groundDetectDistance))
            {
                break;
            }
        }

        hips.interpolation = RigidbodyInterpolation.Interpolate; // 着地後の物理描画用に補間を戻す

        hips.isKinematic = false;         // 着地：腰を物理に戻して崩れられるように
        flightRoutine = null;
        // 着地した瞬間の位置を記録する。この後スタンで倒れている間に他プレイヤーへ押されて
        // 位置がズレても、起き上がりは「本来着地した場所」を使う（StunThenRecover参照）。
        landedHipsPosition = hips.position;
        Land();
    }

    /// 着地：当たり判定を戻し、横滑りしないよう勢いを消して、その場で崩れる。stunTime 後に起き上がる。
    private void Land()
    {
        ragdoll.SetBoneCollidersEnabled(true); // 着地：当たり判定を戻して地面の上で崩れる
        Rigidbody[] bones = ragdoll.Bones;
        if (bones != null)
        {
            foreach (Rigidbody b in bones)
            {
                if (b == null || b.isKinematic) continue; // kinematic には速度を代入できない
                b.linearDamping = 1.5f;                 // すぐ止まる（滑らない）
                b.linearVelocity *= 0.1f;               // 勢いはほぼ残さない
            }
        }
        StartCoroutine(StunThenRecover());
    }

    private IEnumerator StunThenRecover()
    {
        yield return new WaitForSeconds(stunTime);
        // 着地した瞬間の位置で復活する（スタン中に押されて体が動いていても、その影響を受けない）
        ragdoll.RecoverAtHips(landedHipsPosition);
        graceUntil = Time.time + graceTime;   // 起き上がり直後は無敵
    }

    /// 場外・水場用のリスポーン（フック）。RespawnZone や外部からも呼べる。
    public void TriggerRespawn()
    {
        StopAllCoroutines();
        flightRoutine = null;

        Vector3 pos = respawnPoint != null ? respawnPoint.position : initialPosition;
        ragdoll.RecoverAt(pos);
        graceUntil = Time.time + graceTime;
        onRespawned?.Invoke();
    }

    /// 指定位置の下に地面があるか（自分の骨は無視する）。
    private bool IsGroundBelow(Vector3 from, float distance)
    {
        int count = Physics.RaycastNonAlloc(from + Vector3.up * 0.1f, Vector3.down, groundHits, distance + 0.1f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = groundHits[i].collider;
            if (col != null && !col.transform.IsChildOf(transform))
            {
                return true;
            }
        }
        return false;
    }
}
