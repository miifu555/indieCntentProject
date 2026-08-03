using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// PhoneGunServerが受け取った各プレイヤーの傾き・発砲イベントを毎フレーム消費し、
// 画面上のレティクルを動かして的への命中判定を行う。
public class PhoneGunManager : MonoBehaviour
{
    [Header("参照")]
    public PhoneGunServer server;
    public Camera aimCamera; // 未指定ならCamera.mainを使用
    public RectTransform reticleParent; // レティクルを生成するCanvas配下の親
    public GameObject reticlePrefab; // Image + TMP_Text(番号)を含むプレファブ（中心アンカー推奨）
    public TMP_Text connectionInfoText; // 接続用URL表示
    public TMP_Text scoreboardText; // スコア一覧表示
    public RawImage qrCodeImage; // 接続用URLのQRコード表示（任意）

    [Header("照準設定")]
    [Tooltip("スマホの傾き1度あたりのレティクル移動量(px)")]
    public float sensitivity = 12f;
    [Tooltip("Canvas中心からのレティクル移動限界(px)")]
    public float maxOffset = 400f;
    [Tooltip("値が大きいほどレティクルの追従が速くなる")]
    public float followSpeed = 20f;

    [Header("的設定")]
    public LayerMask targetLayer = ~0;
    public float raycastMaxDistance = 200f;
    [Tooltip("命中判定の許容半径（大きいほど当てやすくなる）")]
    public float hitRadius = 0.5f;

    [Header("手裏剣プレファブ（実体を投げる）")]
    [Tooltip("実際に3D空間を飛んでいく手裏剣のプレファブ")]
    public GameObject shurikenPrefab;
    [Tooltip("手裏剣の飛行速度(m/s)")]
    public float shurikenSpeed = 40f;
    [Tooltip("手裏剣が飛びながら回転する速さ(度/秒)")]
    public float shurikenSpinSpeed = 720f;
    [Tooltip("投擲の開始位置。未設定ならカメラの位置から発射する")]
    public Transform throwOrigin;

    [Header("発砲エフェクト（レティクル側）")]
    [Tooltip("発砲時にレティクルが何倍に拡大されるか")]
    public float fireFlashScale = 2.6f;
    [Tooltip("発砲エフェクトが元の大きさに戻るまでの秒数")]
    public float fireFlashDuration = 0.15f;

    private class PlayerRig
    {
        public string id;
        public int playerNumber;
        public RectTransform reticle;
        public RectTransform visual; // 発砲時に拡大するのはこの子オブジェクトだけ（ラベルは常に等倍）
        public Image reticleImage;
        public TMP_Text label;
        public bool calibrated;
        public float centerBeta;
        public float centerAlpha;
        public Vector2 currentOffset;
        public Vector3 baseScale;
        public Coroutine flashCoroutine;
    }

    private readonly Dictionary<string, PlayerRig> rigs = new Dictionary<string, PlayerRig>();
    private readonly List<string> playerOrder = new List<string>();
    private string lastQrUrl = "";

    void Start()
    {
        if (server == null) server = PhoneGunServer.Instance;
        if (aimCamera == null) aimCamera = Camera.main;
    }

    void Update()
    {
        if (server == null) return;

        UpdateConnectionInfo();

        while (server.TryDequeueJoin(out string joinedId))
        {
            SpawnRig(joinedId);
        }

        foreach (var rig in rigs.Values)
        {
            UpdateAim(rig);
        }

        while (server.TryDequeueFire(out string fireId))
        {
            HandleFire(fireId);
        }

        UpdateScoreboard();
    }

    void UpdateConnectionInfo()
    {
        // ngrokの公開URLが確立していればそちらを優先する（正規の証明書で警告が出ないため）
        string url = !string.IsNullOrEmpty(server.PublicUrl) ? server.PublicUrl : server.ServerUrl;
        bool ready = server.IsRunning && !string.IsNullOrEmpty(url);

        if (connectionInfoText != null)
        {
            connectionInfoText.text = ready ? "接続先: " + url : "サーバー起動中...";
        }

        if (qrCodeImage != null && ready && url != lastQrUrl)
        {
            lastQrUrl = url;
            var tex = QrCodeTexture.Generate(url);
            qrCodeImage.texture = tex;
        }
    }

    void SpawnRig(string id)
    {
        if (rigs.ContainsKey(id) || reticlePrefab == null || reticleParent == null) return;

        GameObject go = Instantiate(reticlePrefab, reticleParent);
        var rt = go.GetComponent<RectTransform>();

        Image reticleImage = go.GetComponentInChildren<Image>();
        Transform visualTransform = reticleImage != null ? reticleImage.transform : rt;

        var rig = new PlayerRig
        {
            id = id,
            playerNumber = playerOrder.Count + 1,
            reticle = rt,
            visual = visualTransform as RectTransform,
            reticleImage = reticleImage,
            label = go.GetComponentInChildren<TMP_Text>(),
            baseScale = visualTransform.localScale,
        };

        if (server.TryGetPlayer(id, out var info))
        {
            if (rig.reticleImage != null && ColorUtility.TryParseHtmlString(info.colorHex, out Color c))
            {
                rig.reticleImage.color = c;
            }
        }
        if (rig.label != null)
        {
            rig.label.text = "P" + rig.playerNumber;
        }

        rigs[id] = rig;
        playerOrder.Add(id);
    }

    void UpdateAim(PlayerRig rig)
    {
        if (rig.reticle == null || !server.TryGetAim(rig.id, out var aim)) return;

        if (!rig.calibrated || aim.recenter)
        {
            rig.centerBeta = aim.beta;
            rig.centerAlpha = aim.alpha;
            rig.calibrated = true;
        }

        float dBeta = aim.beta - rig.centerBeta; // 前後の傾き -> 上下移動
        // 体を横に振る(コンパス回転)動きで左右を狙う。alphaは0-360で一周するため
        // Mathf.DeltaAngleで最短差分に正規化してから使う（360度付近での跳ねを防ぐ）
        float dAlpha = Mathf.DeltaAngle(rig.centerAlpha, aim.alpha);

        // 上下・左右とも体感と逆だったため反転
        Vector2 target = new Vector2(-dAlpha * sensitivity, dBeta * sensitivity);
        target = Vector2.ClampMagnitude(target, maxOffset);

        rig.currentOffset = Vector2.Lerp(rig.currentOffset, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        rig.reticle.anchoredPosition = rig.currentOffset;
    }

    void HandleFire(string id)
    {
        if (!rigs.TryGetValue(id, out var rig) || rig.reticle == null || aimCamera == null) return;

        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, rig.reticle.position);
        Ray ray = aimCamera.ScreenPointToRay(screenPos);

        SpawnShuriken(id, ray);

        // 連打で複数のフラッシュ演出が重なると拡大率が積み上がってしまうため、
        // 直前の演出を止めてから常に基準スケールを起点にやり直す
        if (rig.flashCoroutine != null)
        {
            StopCoroutine(rig.flashCoroutine);
        }
        rig.flashCoroutine = StartCoroutine(FlashReticle(rig));
    }

    // レティクルが狙っている方向へ実際に手裏剣を飛ばす。命中判定は手裏剣自身が飛行中に行い、
    // 命中した瞬間にコールバック経由でスコアを加算する（見た目と判定のタイミングを一致させるため）。
    void SpawnShuriken(string id, Ray aimRay)
    {
        if (shurikenPrefab == null) return;

        Vector3 spawnPos = throwOrigin != null ? throwOrigin.position : aimRay.origin;
        Vector3 aimPoint = aimRay.origin + aimRay.direction * Mathf.Max(raycastMaxDistance, 1f);
        Vector3 direction = aimPoint - spawnPos;

        GameObject go = Instantiate(shurikenPrefab, spawnPos, Quaternion.identity);
        ThrownShuriken thrown = go.GetComponent<ThrownShuriken>();
        if (thrown == null) thrown = go.AddComponent<ThrownShuriken>();

        thrown.Init(direction, shurikenSpeed, raycastMaxDistance, hitRadius, targetLayer, shurikenSpinSpeed, hit =>
        {
            var target = hit.collider.GetComponentInParent<ShootingTarget>();
            if (target != null)
            {
                int gained = target.Hit(hit.point);
                if (gained > 0)
                {
                    server.AddScore(id, gained);
                }
            }
        });
    }

    IEnumerator FlashReticle(PlayerRig rig)
    {
        if (rig.visual == null) yield break;

        float elapsed = 0f;

        while (elapsed < fireFlashDuration)
        {
            if (rig.visual == null) yield break;
            float t = elapsed / fireFlashDuration;

            // 前半で拡大、後半で元のサイズへ戻すことで「飛び出す」感を出す
            float scaleT = t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f;
            rig.visual.localScale = Vector3.Lerp(rig.baseScale, rig.baseScale * fireFlashScale, scaleT);

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (rig.visual != null)
        {
            rig.visual.localScale = rig.baseScale;
        }
        rig.flashCoroutine = null;
    }

    void UpdateScoreboard()
    {
        if (scoreboardText == null) return;

        var sb = new StringBuilder();
        foreach (var id in playerOrder)
        {
            if (server.TryGetPlayer(id, out var info))
            {
                sb.AppendLine($"P{rigs[id].playerNumber}: {info.score}点");
            }
        }
        scoreboardText.text = sb.ToString();
    }
}
