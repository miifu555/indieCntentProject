using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// 妖退治（協力プレイ）ロビーに置く「封印」オブジェクト。
// PhoneGunManager（既存・無改造）の命中判定は既存のShootingTarget/StartGateしか
// 認識しないため、封印にもShootingTarget（既存・無改造）を付与し、その被弾状態
// （命中でColliderが無効化される）を監視することで「壊れた」を検知する。
//
// 全員集中判定: 命中した瞬間に、参加中の全プレイヤーのレティクルがalignRingRect
// （レティクルと同じCanvas上に置いた輪のRectTransform）の中に入っているかを確認し、
// 入っていなければその命中は「無かったこと」にして封印をすぐ元に戻す。輪の位置・
// 大きさはエディター上で自由に配置してよい（実行時に自動追従はしない）。
// PhoneGunManagerの命中判定自体は変更できないため、命中は一旦通してから
// 事後的に判定・取り消す方式にしている。
[RequireComponent(typeof(ShootingTarget))]
public class CoopStartGate : MonoBehaviour
{
    [Tooltip("命中してからゲーム開始演出が始まるまでの間")]
    public float breakDelay = 0.5f;
    public CoopGameFlowController flowController;
    [Tooltip("封印を破壊した時に命中位置へ出すエフェクト（任意）")]
    public GameObject breakEffectPrefab;
    [Tooltip("封印を破壊した時に鳴らす効果音（任意）")]
    public AudioClip breakSound;
    [Range(0f, 1f)]
    public float breakVolume = 1f;
    [Tooltip("破壊音を鳴らすBGMマネージャー（同じ場所から2D再生する）")]
    public CoopBgmManager bgmManager;

    [Header("全員集中判定")]
    [Tooltip("falseにすると輪の判定を無効にする（輪も非表示になり、誰が命中させても封印が破壊される。テスト・撮影用）")]
    public bool requireAllAligned = true;
    [Tooltip("参加中の全プレイヤーのレティクルを見るために使う")]
    public PhoneGunManager manager;
    [Tooltip("判定・表示に使う輪。レティクルと同じCanvas(reticleParent)の子として、位置・大きさをエディターで自由に配置する")]
    public RectTransform alignRingRect;
    public Color ringColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    [Tooltip("輪の太さ(半径に対する割合)")]
    [Range(0.02f, 0.3f)]
    public float ringThickness = 0.1f;

    private ShootingTarget shootingTarget;
    private Collider col;
    private bool broken;
    private FieldInfo rigsField;
    private FieldInfo reticleField;

    void Awake()
    {
        shootingTarget = GetComponent<ShootingTarget>();
        col = GetComponent<Collider>();
        // 封印はダメージ(討伐ゲージ)の対象ではないので0点にし、
        // 壊れたらResetGate()を呼ぶまで自動では戻らないようにする
        shootingTarget.scoreValue = 0;
        shootingTarget.respawns = false;
        shootingTarget.hitEffectPrefab = breakEffectPrefab;

        if (manager != null)
        {
            rigsField = typeof(PhoneGunManager).GetField("rigs", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        // 輪の見た目(色・太さ)だけコードで設定する。位置・大きさ(RectTransform)には触れない
        if (alignRingRect != null)
        {
            var image = alignRingRect.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = false;
                image.color = ringColor;
                image.sprite = GenerateRingSprite();
            }
            if (!requireAllAligned) alignRingRect.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (broken) return;
        if (col != null && !col.enabled)
        {
            if (AllReticlesAligned())
            {
                broken = true;
                if (alignRingRect != null) alignRingRect.gameObject.SetActive(false);
                if (bgmManager != null) bgmManager.PlaySfx(breakSound, breakVolume);
                if (flowController != null)
                {
                    Invoke(nameof(StartGame), breakDelay);
                }
            }
            else
            {
                // 全員が輪の中に入っていない状態での命中は無効。封印をすぐ元に戻し、やり直させる
                shootingTarget.ResetTarget();
            }
        }
    }

    Sprite GenerateRingSprite()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerR = size / 2f - 2f;
        float innerR = outerR * (1f - ringThickness);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float edge = Mathf.Min(outerR - d, d - innerR);
                float a = Mathf.Clamp01(edge);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // 参加中の全プレイヤーのレティクルが、alignRingRectの中に入っているか判定する。
    // レティクルもalignRingRectも同じreticleParentの子であるため、anchoredPositionを
    // そのまま比較すればよい（カメラ・スクリーン座標変換は不要）
    bool AllReticlesAligned()
    {
        if (!requireAllAligned) return true;
        if (manager == null || rigsField == null || alignRingRect == null) return true; // 判定できない設定の場合は従来通り即破壊

        var rigs = rigsField.GetValue(manager) as System.Collections.IDictionary;
        if (rigs == null) return false;

        // PhoneGunManagerは切断されたプレイヤーのrigを最大60秒(既定値)残したままにするため、
        // rigsをそのまま見ると「抜けた人の古いレティクル」が判定に混ざり、残った人だけでは
        // 絶対に条件を満たせなくなることがある。現在実際に接続中のidだけに絞り込む
        HashSet<string> connected = manager.server != null
            ? new HashSet<string>(manager.server.ConnectedPlayerIds)
            : null;

        Vector2 center = alignRingRect.anchoredPosition;
        float radius = Mathf.Max(alignRingRect.sizeDelta.x, alignRingRect.sizeDelta.y) / 2f;

        int checkedCount = 0;
        foreach (System.Collections.DictionaryEntry entry in rigs)
        {
            string id = entry.Key as string;
            if (connected != null && id != null && !connected.Contains(id)) continue;

            var rig = entry.Value;
            if (rig == null) continue;

            if (reticleField == null)
            {
                reticleField = rig.GetType().GetField("reticle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            var reticle = reticleField?.GetValue(rig) as RectTransform;
            if (reticle == null) continue;

            checkedCount++;
            if (Vector2.Distance(reticle.anchoredPosition, center) > radius)
            {
                return false;
            }
        }

        return checkedCount > 0; // 誰も参加していない状態では成立させない
    }

    void StartGame()
    {
        flowController.StartGame();
    }

    // 次の回のために封印を元の状態へ戻す
    public void ResetGate()
    {
        broken = false;
        if (shootingTarget != null) shootingTarget.ResetTarget();
        if (alignRingRect != null) alignRingRect.gameObject.SetActive(requireAllAligned);
    }
}
