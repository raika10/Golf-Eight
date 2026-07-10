using UnityEngine;
using UnityEngine.UI;

public class Minimap : MonoBehaviour
{
    public RectTransform mapArea;     // MinimapPanel
    public RectTransform playerDot;   // PlayerDot1
    public Transform playerTransform; // Player1のTransform

    public Vector2 worldMin = new Vector2(-10f, -10f); // ステージの左下
    public Vector2 worldMax = new Vector2(10f, 10f);   // ステージの右上

    void Update()
    {
        if (playerTransform == null) return;

        float nx = Mathf.InverseLerp(worldMin.x, worldMax.x, playerTransform.position.x);
        float ny = Mathf.InverseLerp(worldMin.y, worldMax.y, playerTransform.position.z);

        playerDot.anchoredPosition = new Vector2(
            (nx - 0.5f) * mapArea.rect.width,
            (ny - 0.5f) * mapArea.rect.height);
    }
}