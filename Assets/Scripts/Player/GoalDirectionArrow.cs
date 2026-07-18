using UnityEngine;

/// プレイヤーの少し上に浮かぶ矢印で、ゴール（GolfHole）の方向を指し示す。
/// Player に付けるだけ。ゴールは自動で一番近い GolfHole を探す（手動指定も可）。
/// 矢印はプリミティブで自動生成し、毎フレーム水平方向にゴールへ向ける。
public class GoalDirectionArrow : MonoBehaviour
{
    [Header("ゴール")]
    [SerializeField] private Transform goal;              // 未設定なら一番近い GolfHole を自動で探す

    [Header("矢印の見た目")]
    [SerializeField] private float height = 2.4f;         // プレイヤーからの高さ (m)
    [SerializeField] private float size = 1.0f;           // 矢印の大きさ倍率
    [SerializeField] private Color color = new Color(1f, 0.85f, 0.2f, 1f); // 矢印の色
    [SerializeField] private bool localPlayerOnly = true; // 操作中プレイヤーだけに出す

    private Transform arrowRoot;
    private PlayerController player;
    private float refindTimer;

    private void Start()
    {
        player = GetComponent<PlayerController>();
        BuildArrow();
        FindGoal();
    }

    private void Update()
    {
        // 操作していないプレイヤー（ダミー）には出さない
        if (localPlayerOnly && player != null && !player.IsLocalPlayer)
        {
            if (arrowRoot != null && arrowRoot.gameObject.activeSelf) arrowRoot.gameObject.SetActive(false);
            return;
        }
        if (arrowRoot != null && !arrowRoot.gameObject.activeSelf) arrowRoot.gameObject.SetActive(true);

        // ゴールが未設定/消えたら探し直す（次ホールへ進んだ時など）
        if (goal == null)
        {
            refindTimer -= Time.deltaTime;
            if (refindTimer <= 0f)
            {
                FindGoal();
                refindTimer = 0.5f;
            }
        }
        if (arrowRoot == null) return;

        // 位置はプレイヤーの少し上に固定
        arrowRoot.position = transform.position + Vector3.up * height;

        // ゴールへ水平方向に向ける（高さ差は無視して方向だけ示す）
        if (goal != null)
        {
            Vector3 dir = goal.position - arrowRoot.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
            {
                arrowRoot.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }
    }

    /// 一番近い GolfHole をゴールとして探す。
    private void FindGoal()
    {
        GolfHole[] holes = FindObjectsByType<GolfHole>(FindObjectsSortMode.None);
        float best = float.MaxValue;
        foreach (GolfHole h in holes)
        {
            if (h == null) continue;
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                goal = h.transform;
            }
        }
    }

    /// 矢印本体（シャフト＋矢じり）をプリミティブで作る。ローカル +Z 方向を指す形。
    private void BuildArrow()
    {
        arrowRoot = new GameObject("GoalArrow").transform;
        arrowRoot.SetParent(transform, false);

        // シャフト（細い箱、+Z方向へ伸びる）
        GameObject shaft = MakePart("Shaft", PrimitiveType.Cube);
        shaft.transform.localScale = new Vector3(0.09f, 0.09f, 0.5f) * size;
        shaft.transform.localPosition = new Vector3(0f, 0f, -0.05f) * size;

        // 矢じり（箱をY45°回した菱形。前の角が先端になり「指している」向きに見える）
        GameObject head = MakePart("Head", PrimitiveType.Cube);
        head.transform.localScale = new Vector3(0.24f, 0.09f, 0.24f) * size;
        head.transform.localPosition = new Vector3(0f, 0f, 0.28f) * size;
        head.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
    }

    /// プリミティブを作り、コライダーを外し、明るい色を塗って矢印の子にする。
    private GameObject MakePart(string name, PrimitiveType type)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col); // 見た目だけ（当たり判定不要）
        go.transform.SetParent(arrowRoot, false);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return go;
    }
}
