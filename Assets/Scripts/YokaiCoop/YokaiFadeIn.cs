using System.Collections;
using UnityEngine;

// 出現した妖を、パッと表示するのではなく少しずつはっきり見えるようにする。
// この妖のマテリアル(URP/Lit想定)は普段は不透明(Opaque)で運用されているため、
// フェード中だけ一時的に半透明(Transparent)モードへ切り替えてアルファ値を0→1に
// アニメーションし、フェードが終わったら元の不透明設定に戻す
// （見た目・描画順・パフォーマンスを維持するため）。
// URP/Lit以外のシェーダーが付いている場合は該当プロパティが無いため何もせず、
// 今まで通り即座に表示される。
public class YokaiFadeIn : MonoBehaviour
{
    public float duration = 0.3f;

    void Start()
    {
        if (duration <= 0f) return;

        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        // Renderer.materialsへのアクセスでインスタンス化されるため、共有マテリアル資産には影響しない
        var instancedMats = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            instancedMats[i] = renderers[i].materials;
            foreach (var m in instancedMats[i]) SetTransparent(m, true);
        }

        StartCoroutine(FadeRoutine(instancedMats));
    }

    IEnumerator FadeRoutine(Material[][] mats)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            ApplyAlpha(mats, Mathf.Clamp01(t / duration));
            yield return null;
        }
        ApplyAlpha(mats, 1f);

        foreach (var group in mats)
        {
            foreach (var m in group) SetTransparent(m, false);
        }
    }

    void ApplyAlpha(Material[][] mats, float alpha)
    {
        foreach (var group in mats)
        {
            foreach (var m in group)
            {
                if (m.HasProperty("_BaseColor"))
                {
                    Color c = m.GetColor("_BaseColor");
                    c.a = alpha;
                    m.SetColor("_BaseColor", c);
                }
                else if (m.HasProperty("_Color"))
                {
                    Color c = m.GetColor("_Color");
                    c.a = alpha;
                    m.SetColor("_Color", c);
                }
            }
        }
    }

    // URP/Litのマテリアルを、不透明(Opaque)と半透明(Transparent, アルファブレンド)の間で切り替える
    void SetTransparent(Material m, bool transparent)
    {
        if (!m.HasProperty("_Surface")) return; // URP系以外のシェーダーは対象外

        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            m.SetFloat("_Surface", 0f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
