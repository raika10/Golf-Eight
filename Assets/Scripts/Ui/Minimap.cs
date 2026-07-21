using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GolfEight.Network;

/// ミニマップ上に全プレイヤー（2〜4人）のドットを表示する。
/// ・マップの絵は minimapCamera → RenderTexture → RawImage で描かれている。
///   ドットの位置は「その同じカメラ」の投影で決めるので、マップと必ず一致する（手動キャリブレーション不要）。
/// ・プレイヤーは実行時に FishNet でスポーンするため、playerDot をテンプレートにして人数分クローンし、
///   参加/退出に追従してドットを増減させる。
/// ・色は接続順（PlayerNetworkSync.PlayerIndex）で決める。PlayerColors と同じ並びなので、
///   1人目=赤 / 2人目=青 / 3人目=緑 / 4人目=黄 となり、ボール・本体の色とも一致する。
public class Minimap : MonoBehaviour
{
    public RectTransform mapArea;     // MinimapPanel（ドットの座標基準。中心アンカー前提）
    public RectTransform playerDot;   // ドットのテンプレート（PlayerDot1）。白いスプライトなら色付けが正しく乗る
    public Camera minimapCamera;      // MinimapRenderTexture を撮っているカメラ（真上見下ろしのorthographic）

    [Header("フォールバック（minimapCamera 未設定時のみ使用）")]
    public Vector2 worldMin = new Vector2(-10f, -10f); // ステージの左下
    public Vector2 worldMax = new Vector2(10f, 10f);   // ステージの右上

    // プレイヤー探索は毎フレームだと FindObjects の割り当てが無駄なので、一定間隔でだけ更新する。
    private const float RefreshInterval = 0.25f;
    private float nextRefreshTime;

    // 各プレイヤーに対応するドット。参加/退出に合わせて増減する。
    private readonly Dictionary<PlayerNetworkSync, RectTransform> dots = new Dictionary<PlayerNetworkSync, RectTransform>();
    private readonly List<PlayerNetworkSync> toRemove = new List<PlayerNetworkSync>();

    void Awake()
    {
        // playerDot はテンプレート。直接は表示せず、人数分クローンして使う。
        if (playerDot != null) playerDot.gameObject.SetActive(false);
    }

    void Update()
    {
        RefreshPlayers();     // 参加/退出に合わせてドットを作成・破棄（間引き実行）
        UpdateDotPositions(); // 位置は毎フレーム更新（滑らかに動かすため）
    }

    /// 現在のプレイヤーに合わせてドットを作成・破棄する。
    private void RefreshPlayers()
    {
        if (Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        if (playerDot == null || mapArea == null) return;

        // 参加中プレイヤー：ドットが無ければ作る。色は毎回反映（index はスポーン後に遅れて確定することがある）。
        foreach (PlayerNetworkSync p in FindObjectsByType<PlayerNetworkSync>(FindObjectsSortMode.None))
        {
            if (p.PlayerIndex < 0) continue; // インデックス未確定の間は色が決まらないので待つ
            if (!dots.TryGetValue(p, out RectTransform dot))
            {
                dot = Instantiate(playerDot, mapArea);
                dot.gameObject.SetActive(true);
                dots.Add(p, dot);
            }
            ApplyDotColor(dot, p.PlayerIndex);
        }

        // 退出（破棄された）プレイヤー：対応するドットも消す。
        // FishNet でデスポーンされると PlayerNetworkSync は破棄され、キーが null 扱いになる。
        toRemove.Clear();
        foreach (KeyValuePair<PlayerNetworkSync, RectTransform> kv in dots)
        {
            if (kv.Key == null)
            {
                if (kv.Value != null) Destroy(kv.Value.gameObject);
                toRemove.Add(kv.Key);
            }
        }
        foreach (PlayerNetworkSync k in toRemove) dots.Remove(k);
    }

    /// 各ドットを、対応するプレイヤーの現在位置に合わせて配置する。
    private void UpdateDotPositions()
    {
        if (mapArea == null) return;
        foreach (KeyValuePair<PlayerNetworkSync, RectTransform> kv in dots)
        {
            PlayerNetworkSync p = kv.Key;
            RectTransform dot = kv.Value;
            if (p == null || dot == null) continue;

            Vector2 normalized = minimapCamera != null
                ? WorldToMinimap01(p.transform.position)   // マップを撮っているカメラの投影で合わせる
                : WorldToMinimap01Fallback(p.transform.position); // カメラ未設定時は worldMin/Max で近似

            dot.anchoredPosition = new Vector2(
                (normalized.x - 0.5f) * mapArea.rect.width,
                (normalized.y - 0.5f) * mapArea.rect.height);
        }
    }

    /// 接続順のインデックスで色を付ける（0=赤,1=青,2=緑,3=黄）。ボール・本体と共通の PlayerColors を使う。
    private void ApplyDotColor(RectTransform dot, int index)
    {
        Graphic graphic = dot.GetComponent<Graphic>();
        if (graphic != null) graphic.color = PlayerColors.Get(index);
    }

    /// ワールド座標をカメラのビューポート(0..1)へ。マップの絵と同じ投影なので位置が必ず一致する。
    /// 画角外に出たプレイヤーは端に張り付かせる（消すと見失うため）。
    private Vector2 WorldToMinimap01(Vector3 worldPos)
    {
        Vector3 vp = minimapCamera.WorldToViewportPoint(worldPos);
        return new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
    }

    private Vector2 WorldToMinimap01Fallback(Vector3 worldPos)
    {
        float nx = Mathf.InverseLerp(worldMin.x, worldMax.x, worldPos.x);
        float ny = Mathf.InverseLerp(worldMin.y, worldMax.y, worldPos.z);
        return new Vector2(nx, ny);
    }
}
