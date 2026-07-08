using System.Collections.Generic;
using UnityEngine;

/// カップ（ゴール）の見た目を手続き的に作る（穴・リム・旗）。
/// GolfHole と同じオブジェクトに付ける想定。当たり判定は GolfHole 側が持つので、
/// ここで作る見た目パーツはコライダーを外した「飾り」だけ。
///
/// 地面はソリッド（穴が空いていない）前提。地面の下に何か置いても地面に隠れて見えないので、
/// 「地面のすぐ上に、明るい輪（リム）＋ボールより大きい暗い円盤（穴の口）」を重ねて穴に見せる。
/// ボールが入るとその暗い円盤と地面に隠れて沈むので「穴に落ちた」ように見える。
///
/// ・穴の半径はボールより少し大きくする（既定はボール半径0.2mより大きい0.28m）。
/// ・旗竿と旗を立てて遠くからでも位置が分かるようにする。
/// ・エディタでもPlayでもプレビューできるよう [ExecuteAlways]。値を変えたら右クリック →
///   「カップの見た目を作り直す」で反映する。生成物はシーンに保存しない（DontSave）。
[ExecuteAlways]
public class GolfHoleVisual : MonoBehaviour
{
    [Header("穴")]
    [SerializeField] private float holeRadius = 0.34f;                       // 穴（黒い円盤）の半径 (m)。ボール半径より大きくする
    [SerializeField] private float rimWidth = 0.05f;                         // 穴のまわりの明るいリムの幅 (m)
    [SerializeField] private Color holeColor = new Color(0.02f, 0.02f, 0.02f); // 穴の色（ほぼ黒）
    [SerializeField] private Color rimColor = new Color(0.92f, 0.92f, 0.9f);   // リムの色

    [Header("旗")]
    [SerializeField] private bool showFlag = true;
    [SerializeField] private float poleHeight = 1.4f;                       // 旗竿の高さ (m)
    [SerializeField] private float poleRadius = 0.012f;                     // 旗竿の太さ（半径 m）
    [SerializeField] private float poleColliderClearance = 0.45f;           // 竿の当たり判定を地面からこの高さまで空ける (m)。転がる球が入れるように
    [SerializeField] private Color poleColor = new Color(0.85f, 0.85f, 0.85f);
    [SerializeField] private Vector2 flagSize = new Vector2(0.35f, 0.22f);  // 旗の 横×縦 (m)
    [SerializeField] private Color flagColor = new Color(0.85f, 0.1f, 0.1f);

    // 生成したパーツとマテリアル（作り直し時に片付ける）
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

    /// 見た目を作り直す。インスペクタで値を変えたら右クリックからこれを実行する。
    [ContextMenu("カップの見た目を作り直す")]
    public void Rebuild()
    {
        Clear();

        // ① 明るい輪（リム）：少し大きい円盤を地面のすぐ上に置く。外周が輪として見える。
        BuildDisc("HoleRim", holeRadius + rimWidth, 0.004f, rimColor, false);

        // ② 穴の口：ボールより大きい暗い円盤をリムの少し上に重ねる。中央を覆い、外周だけリムが輪に残る。
        //    地面はソリッドなので、ボールが下へ沈むとこの暗い円盤と地面に隠れて「穴に落ちた」ように見える。
        BuildDisc("HoleFace", holeRadius, 0.008f, holeColor, true); // Unlit で光に関係なく常に暗く

        if (showFlag)
        {
            BuildFlag();
        }
    }

    /// 薄い円盤（地面に置く穴やリム）。Quadではなく平たいCylinderで円形にする。
    private GameObject BuildDisc(string name, float radius, float y, Color color, bool unlit)
    {
        GameObject disc = BuildCylinder(name, radius, 0.008f, color, unlit);
        disc.transform.localPosition = new Vector3(0f, y, 0f);
        return disc;
    }

    /// 円柱を作る。radius=半径, height=高さ。既定のUnity円柱は直径1m・高さ2mなのでスケールで合わせる。
    private GameObject BuildCylinder(string name, float radius, float height, Color color, bool unlit)
    {
        GameObject go = MakePart(name, PrimitiveType.Cylinder);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        Paint(go, color, unlit);
        return go;
    }

    private void BuildFlag()
    {
        // 旗竿：細い円柱を地面から立てる（中心が原点なので高さの半分だけ持ち上げる）
        GameObject pole = MakePart("FlagPole", PrimitiveType.Cylinder);
        pole.transform.localScale = new Vector3(poleRadius * 2f, poleHeight * 0.5f, poleRadius * 2f);
        pole.transform.localPosition = new Vector3(0f, poleHeight * 0.5f, 0f);
        Paint(pole, poleColor, false);

        // 旗竿の当たり判定（見た目とは別の、スケール1の専用オブジェクト）。
        // 地面付近（poleColliderClearance より下）は空けておく。こうしないと中心の竿に阻まれて
        // 転がってくる球がカップインできない。空中の球や高い位置では竿に当たって跳ね返る。
        float colBottom = Mathf.Clamp(poleColliderClearance, 0f, poleHeight - 0.02f);
        float colHeight = Mathf.Max(0.02f, poleHeight - colBottom);
        GameObject poleCol = new GameObject("FlagPoleCollider");
        poleCol.transform.SetParent(transform, false);
        poleCol.transform.localPosition = new Vector3(0f, colBottom + colHeight * 0.5f, 0f);
        poleCol.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        generated.Add(poleCol);
        CapsuleCollider cc = poleCol.AddComponent<CapsuleCollider>();
        cc.direction = 1;           // Y軸方向
        cc.radius = poleRadius;
        cc.height = colHeight;
        cc.isTrigger = false;       // 物理的に当たる（跳ね返す）

        // 旗：Quadを竿の上のほうに付ける。裏からも見えるよう両面表示にする
        GameObject flag = MakePart("Flag", PrimitiveType.Quad);
        flag.transform.localScale = new Vector3(flagSize.x, flagSize.y, 1f);
        flag.transform.localPosition = new Vector3(poleRadius + flagSize.x * 0.5f, poleHeight - flagSize.y * 0.5f, 0f);
        Material flagMat = Paint(flag, flagColor, false);
        flagMat.SetFloat("_Cull", 0f); // 両面表示（裏面カリングを切る）
    }

    /// プリミティブを1つ作り、コライダーを外し、このオブジェクトの子にする（飾り専用）。
    private GameObject MakePart(string name, PrimitiveType type)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            DestroyObject(col); // 当たり判定は GolfHole 側だけが持つ
        }

        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable; // シーンに保存しない一時プレビュー
        generated.Add(go);
        return go;
    }

    /// マテリアルを作って色を塗る。unlit=true なら光の影響を受けず常に同じ色（穴を暗く保つ用）。
    private Material Paint(GameObject go, Color color, bool unlit)
    {
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard"); // URPが見つからないときの保険
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
        return mat;
    }

    /// 生成したパーツとマテリアルを片付ける。
    private void Clear()
    {
        for (int i = 0; i < generated.Count; i++)
        {
            if (generated[i] != null)
            {
                DestroyObject(generated[i]);
            }
        }
        generated.Clear();

        for (int i = 0; i < materials.Count; i++)
        {
            if (materials[i] != null)
            {
                DestroyObject(materials[i]);
            }
        }
        materials.Clear();
    }

    // Play中は Destroy、エディタ編集中は DestroyImmediate を使い分ける。
    private void DestroyObject(Object obj)
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
