using UnityEngine;
using TMPro;

public class WorldLabel : MonoBehaviour
{
    public Transform target;      // 追従するプレイヤー
    public Vector3 offset = new Vector3(0, 2f, 0);  // 頭の上に出す高さ
    public TMP_Text label;
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
    }

    void Update()
    {
        if (target == null || label == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(target.position + offset);

        // カメラの後ろに回ったら隠す
        if (screenPos.z < 0)
        {
            label.enabled = false;
            return;
        }

        label.enabled = true;
        label.transform.position = screenPos;
    }
}