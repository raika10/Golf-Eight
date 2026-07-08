using System.Collections;
using UnityEngine;

/// <summary>
/// 破片グループに付き、一定時間後に縮小しながら消える。
/// 縮小方式はマテリアル設定（Opaque/Transparent）に依存せず確実に動く。
/// </summary>
public class FragmentCleanup : MonoBehaviour
{
    [Tooltip("この秒数だけ表示してから消え始める")]
    public float lifetime = 3f;

    [Tooltip("縮小して消えるまでの秒数")]
    public float shrinkDuration = 0.6f;

    void Start()
    {
        StartCoroutine(LifeRoutine());
    }

    IEnumerator LifeRoutine()
    {
        yield return new WaitForSeconds(lifetime);

        var children = new Transform[transform.childCount];
        var startScales = new Vector3[transform.childCount];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = transform.GetChild(i);
            startScales[i] = children[i].localScale;
        }

        float t = 0f;
        while (t < shrinkDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - t / shrinkDuration);
            for (int i = 0; i < children.Length; i++)
                if (children[i] != null)
                    children[i].localScale = startScales[i] * k;
            yield return null;
        }

        Destroy(gameObject);
    }
}
