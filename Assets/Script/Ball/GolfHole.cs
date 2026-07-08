using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// カップ（ゴールの穴）。ボールが中心付近に来て十分に遅ければ「カップイン」する（ステージ6）。
/// ・ふち（リップ）付近では縁の力でボールが中心へ引き込まれ、軌道が曲がる。遅ければ回り込んで落ち、
///   速い・外し気味だとカクッと逸れる（リップアウト）。ふちギリギリで自動で吸い込まれることはない。
/// ・入ったら、まずカップの真上に寄ってから中へストンと落ちる（ワープではなく落下する動き）。
/// ・落ち切ったら静止して固定し、以降は打てなくする。
/// ・カップインを onBallHoled で通知するので、演出や次ホール処理をそこにつなげられる。
///
/// 使い方：地面のゴール位置に空オブジェクトを置き、少し大きめの Collider（SphereやCapsule）を
/// つけて isTrigger にして、このコンポーネントを足す。カップの見た目は別の子オブジェクトでよい。
[RequireComponent(typeof(Collider))]
public class GolfHole : MonoBehaviour
{
    // カップインしたボールを渡すイベント。インスペクタに出すため具象サブクラスにする
    [System.Serializable]
    public class BallHoledEvent : UnityEvent<GolfBall> { }

    [Header("判定")]
    [SerializeField] private float captureSpeed = 3f;     // この速さ以下 かつ 中心寄りならカップイン (m/s)
    [SerializeField] private float captureRadius = 0.26f; // カップ中心からこの距離内に球の中心が来たら「入る」(m)。旗竿に触れる手前で入るよう ボール半径+竿半径 より大きく
    [SerializeField] private Transform cupCenter;         // カップ中心（未設定ならこのオブジェクトの位置）

    [Header("ふち（リップ）")]
    [SerializeField] private float lipRadius = 0.45f;     // この距離内なら縁の影響で軌道が曲がる (m)。トリガーの大きさもこれに合わせる
    [SerializeField] private float lipStrength = 8f;      // 縁がボールを中心へ引き込む強さ（加速度）。大きいほど吸い込み／リップアウトが強い
    [SerializeField] private float lipDamping = 6f;       // 縁でボールを失速させる強さ。中心付近ほど効く。これが無いと回り続けて入らない

    [Header("落ちる演出")]
    [SerializeField] private float dropDepth = 0.35f;    // カップの中でボールが落ち着く深さ（カップ中心からの下向き m）。地面の下までボールを沈めて隠す
    [SerializeField] private float alignTime = 0.12f;    // カップの真上へ横に寄せる時間 (s)
    [SerializeField] private float dropTime = 0.22f;     // そこから中へ落ちるのにかける時間 (s)

    [Header("通知")]
    [Tooltip("カップインしたボールを渡す。演出や次ホール処理につなぐ")]
    public BallHoledEvent onBallHoled;

    // 二重通知を防ぐため、入れ終わったボールを覚えておく
    private readonly HashSet<GolfBall> holed = new HashSet<GolfBall>();

    // Collider を足した直後に Trigger にして、当たり判定の大きさをリップ範囲に合わせる
    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
        SyncTriggerRadius();
    }

    private void OnValidate()
    {
        SyncTriggerRadius();
    }

    // 検出用トリガー（Sphere）の半径をリップの届く範囲に合わせる。※このオブジェクトのスケールは1想定
    private void SyncTriggerRadius()
    {
        if (GetComponent<Collider>() is SphereCollider sphere)
        {
            sphere.isTrigger = true;
            sphere.radius = lipRadius;
        }
    }

    // 縁の力を効かせ続けたいので Stay で見続ける（Enter だけだと1フレームしか効かない）
    private void OnTriggerStay(Collider other)
    {
        HandleBall(other);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleBall(other);
    }

    private void HandleBall(Collider other)
    {
        GolfBall ball = other.GetComponentInParent<GolfBall>();
        if (ball == null || ball.IsHoled || holed.Contains(ball))
        {
            return;
        }

        Rigidbody body = ball.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic)
        {
            return;
        }

        Transform center = cupCenter != null ? cupCenter : transform;
        Vector3 offset = ball.transform.position - center.position;
        offset.y = 0f; // 水平距離だけで見る
        float dist = offset.magnitude;
        float speed = body.linearVelocity.magnitude;

        // 中心付近まで来ていて十分に遅ければカップイン
        if (dist <= captureRadius && speed <= captureSpeed)
        {
            Hole(ball);
            return;
        }

        // それ以外はふち（リップ）の影響：中心へ引き込みつつ、中心付近ほど失速させて落ち着かせる。
        // 減衰があるので、寄って来た球は速度を失って「低速＋中心」の条件を満たし、ちゃんと落ちる。
        // 外周では効きが弱いので、速い・外し気味の球は軌道が少し曲がるだけで逸れる（リップアウト）。
        if (dist > 1e-4f && dist <= lipRadius)
        {
            Vector3 inward = -offset / dist;
            float influence = 1f - dist / lipRadius; // 0（外周）〜1（中心寄り）

            body.AddForce(inward * lipStrength * influence, ForceMode.Acceleration);

            // 縁での失速（中心付近ほど強く速度を食う）→ これが無いと回り続けて入らない
            float keep = Mathf.Clamp01(1f - lipDamping * influence * Time.fixedDeltaTime);
            body.linearVelocity *= keep;
        }
    }

    private void Hole(GolfBall ball)
    {
        holed.Add(ball);

        // ボール側で「以降は動かない・打てない」状態に固定してから、中へ落ちる動きを演出する。
        // MarkHoled で isKinematic になるので、以降は物理に邪魔されず transform で動かせる。
        ball.MarkHoled();
        StartCoroutine(DropIntoCup(ball));
    }

    /// カップの真上へ横に寄せてから、中へストンと落として底で止める。
    /// 中心へ一瞬でワープするのではなく「縁から入って落ちる」ように見せる。
    private IEnumerator DropIntoCup(GolfBall ball)
    {
        Transform center = cupCenter != null ? cupCenter : transform;
        Vector3 from = ball.transform.position;
        Vector3 overCup = new Vector3(center.position.x, from.y, center.position.z); // カップ真上（高さはそのまま）
        Vector3 bottom = center.position + Vector3.down * dropDepth;                 // カップの底

        // ① 横方向：カップの真上に寄せる
        for (float t = 0f; t < alignTime; t += Time.deltaTime)
        {
            if (ball == null) yield break;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.0001f, alignTime));
            ball.transform.position = Vector3.Lerp(from, overCup, k);
            yield return null;
        }

        // ② 下方向：縁から底へ落とす。落ちる感じが出るよう終盤ほど速く（加速）する
        for (float t = 0f; t < dropTime; t += Time.deltaTime)
        {
            if (ball == null) yield break;
            float n = t / Mathf.Max(0.0001f, dropTime);
            float k = n * n; // 加速して落ちる
            ball.transform.position = Vector3.Lerp(overCup, bottom, k);
            yield return null;
        }

        if (ball != null)
        {
            ball.transform.position = bottom;
        }

        onBallHoled?.Invoke(ball);
    }

    // シーンビューでカップの吸い込み範囲を確認できるように
    private void OnDrawGizmosSelected()
    {
        Transform center = cupCenter != null ? cupCenter : transform;
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(center.position, 0.2f);
    }
}
