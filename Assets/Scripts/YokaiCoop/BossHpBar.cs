using UnityEngine;
using TMPro;

// 画面上部に出すボスのHPバー。YokaiSpawnerが、YokaiStats.showHpBarがtrueの妖を出現させた時に
// Bind()で対象を教えてもらい、その妖のHP割合に合わせてバーを伸縮させる。
// 妖が消えたら（撃破・タイムアウト・ラウンド終了）自動で隠れる。
public class BossHpBar : MonoBehaviour
{
    [Tooltip("バー全体（対象がいない間は非表示にする）")]
    public GameObject root;
    [Tooltip("残りHPの分だけ左から伸びる部分。親いっぱいにストレッチしたRectTransformのImageを指定する")]
    public RectTransform fill;
    [Tooltip("ボスの名前を表示するテキスト（任意）")]
    public TMP_Text nameText;
    [Tooltip("バーが減る速さ（大きいほど速い）")]
    public float smoothSpeed = 6f;

    private YokaiMover target;
    private float displayRatio = 1f;

    void Awake()
    {
        if (root != null) root.SetActive(false);
    }

    public void Bind(YokaiMover mover, string displayName)
    {
        target = mover;
        displayRatio = 1f;
        if (nameText != null) nameText.text = displayName;
        ApplyFill(1f);
        if (root != null) root.SetActive(true);
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        if (target == null)
        {
            root.SetActive(false);
            return;
        }

        displayRatio = Mathf.MoveTowards(displayRatio, target.HpRatio, smoothSpeed * Time.deltaTime);
        ApplyFill(displayRatio);
    }

    void ApplyFill(float ratio)
    {
        if (fill == null) return;
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
    }
}
