using System.Collections.Generic;
using UnityEngine;

/// キャラの右手にゴルフクラブを持たせる（プリミティブで自動生成。外部モデル不要）。
/// 手のボーンの子に作るので、走行中もスイング中も自然に追従する。
///
/// 使い方：Player か Character に付けるだけ（手のボーンは名前で自動検索）。
/// 位置がずれていたら Grip Position / Grip Euler を調整し、
/// コンポーネントの「⋮」→「クラブを作り直す」で反映する（Play不要）。
[ExecuteAlways]
public class GolfClub : MonoBehaviour
{
    [Header("持たせる手（未設定なら名前で自動検索）")]
    [SerializeField] private Transform handBone;                  // 直接指定してもよい
    [SerializeField] private string handBoneSuffix = "RightHand"; // 名前がこれで終わるボーンを探す（mixamorig6:RightHand 等）

    [Header("クラブの形")]
    [SerializeField] private float shaftLength = 0.9f;    // シャフトの長さ (m)
    [SerializeField] private float shaftRadius = 0.008f;  // シャフトの太さ（半径 m）
    [SerializeField] private Vector3 headSize = new Vector3(0.09f, 0.045f, 0.03f); // ヘッドの大きさ
    [SerializeField] private Color shaftColor = new Color(0.75f, 0.76f, 0.8f);
    [SerializeField] private Color headColor = new Color(0.25f, 0.26f, 0.3f);

    [Header("持ち位置の調整")]
    [SerializeField] private Vector3 gripPosition = Vector3.zero;  // 手のボーンからのローカル位置
    [SerializeField] private Vector3 gripEuler = Vector3.zero;     // 手のボーンからのローカル回転（度）

    private readonly List<GameObject> generated = new List<GameObject>();
    private readonly List<Material> materials = new List<Material>();

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        Clear();
    }

    /// クラブを作り直す。形や持ち位置を変えたら実行する。
    [ContextMenu("クラブを作り直す")]
    public void Rebuild()
    {
        Clear();

        Transform hand = FindHandBone();
        if (hand == null)
        {
            Debug.LogWarning($"[GolfClub] 手のボーン（名前が '{handBoneSuffix}' で終わる）が見つかりません。", this);
            return;
        }

        // クラブの根元（グリップ）。手のボーンの子に置く。
        GameObject root = new GameObject("GolfClub");
        root.transform.SetParent(hand, false);
        root.transform.localPosition = gripPosition;
        root.transform.localEulerAngles = gripEuler;
        root.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        generated.Add(root);

        // シャフト：グリップから下に伸ばす（Unityの円柱は高さ2mなので half を掛ける）
        GameObject shaft = MakePart("Shaft", root.transform, PrimitiveType.Cylinder, shaftColor);
        shaft.transform.localScale = new Vector3(shaftRadius * 2f, shaftLength * 0.5f, shaftRadius * 2f);
        shaft.transform.localPosition = new Vector3(0f, -shaftLength * 0.5f, 0f);

        // ヘッド：シャフトの先端に付ける（少し前方へ出す）
        GameObject head = MakePart("Head", root.transform, PrimitiveType.Cube, headColor);
        head.transform.localScale = headSize;
        head.transform.localPosition = new Vector3(0f, -shaftLength, headSize.z * 0.5f);
    }

    /// 名前が handBoneSuffix で終わるボーンを探す（"mixamorig6:RightHand" などに一致。指のボーンは除外される）。
    private Transform FindHandBone()
    {
        if (handBone != null)
        {
            return handBone;
        }
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.EndsWith(handBoneSuffix, System.StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        return null;
    }

    /// 見た目だけのパーツを作る（当たり判定は不要なので外す）。
    private GameObject MakePart(string name, Transform parent, PrimitiveType type, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            DestroySafely(col);
        }
        Paint(go, color);

        go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        generated.Add(go);
        return go;
    }

    private void Paint(GameObject go, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        Material mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
        }
        materials.Add(mat);
    }

    private void Clear()
    {
        for (int i = 0; i < generated.Count; i++)
        {
            if (generated[i] != null)
            {
                DestroySafely(generated[i]);
            }
        }
        generated.Clear();

        for (int i = 0; i < materials.Count; i++)
        {
            if (materials[i] != null)
            {
                DestroySafely(materials[i]);
            }
        }
        materials.Clear();
    }

    // Play中は Destroy、エディタ編集中は DestroyImmediate を使い分ける。
    private void DestroySafely(Object obj)
    {
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}
