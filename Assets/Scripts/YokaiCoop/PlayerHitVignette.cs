using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 妖に攻撃された瞬間、画面の周囲に赤い靄を一瞬出して知らせる演出。
// 中央は透明、外周に向かって赤くなるグラデーションの画像を全画面に敷き、
// CanvasGroup.alphaを一瞬1にしてからflashDuration秒かけて0へ戻すだけの単純な仕組み。
public class PlayerHitVignette : MonoBehaviour
{
    [Tooltip("赤い靄の画像。未設定ならこのGameObjectの子として自動生成する")]
    public Image vignetteImage;
    [Tooltip("靄の表示・非表示を制御するCanvasGroup。未設定ならvignetteImageから自動取得・追加する")]
    public CanvasGroup canvasGroup;
    [Tooltip("最大表示時の靄の濃さ（0〜1）")]
    [Range(0f, 1f)]
    public float maxAlpha = 0.55f;
    [Tooltip("フラッシュしてから消えるまでの秒数")]
    public float flashDuration = 0.3f;
    [Tooltip("靄の色")]
    public Color tintColor = new Color(0.9f, 0.05f, 0.05f, 1f);

    private Coroutine flashCoroutine;

    void Awake()
    {
        if (vignetteImage == null)
        {
            var go = new GameObject("Vignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            vignetteImage = go.GetComponent<Image>();
            vignetteImage.raycastTarget = false;
            vignetteImage.color = tintColor;
            vignetteImage.sprite = GenerateVignetteSprite();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    // 中央は透明、外周に向かって不透明な赤になる正方形のグラデーション画像を作る。
    // UV空間（0〜1）の楕円距離で計算しているため、画面の縦横比に関わらず
    // 四辺すべてにきちんと届く（引き伸ばされても偏らない）
    Sprite GenerateVignetteSprite()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        const float innerEdge = 0.55f; // ここまでは完全に透明
        const float outerEdge = 1.05f; // ここで最大濃度に達する

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                float dx = (u - 0.5f) * 2f;
                float dy = (v - 0.5f) * 2f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerEdge, outerEdge, dist));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // 一瞬フルの濃さにしてから、flashDuration秒かけてゆっくり消す
    public void Flash()
    {
        if (canvasGroup == null) return;
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        canvasGroup.alpha = maxAlpha;
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(maxAlpha, 0f, t / flashDuration);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        flashCoroutine = null;
    }
}
