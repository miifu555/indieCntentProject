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

    private class PlayerRig
    {
        public string id;
        public int playerNumber;
        public RectTransform reticle;
        public Image reticleImage;
        public TMP_Text label;
        public bool calibrated;
        public float centerBeta;
        public float centerGamma;
        public Vector2 currentOffset;
    }

    private readonly Dictionary<string, PlayerRig> rigs = new Dictionary<string, PlayerRig>();
    private readonly List<string> playerOrder = new List<string>();

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
        if (connectionInfoText == null) return;

        connectionInfoText.text = server.IsRunning && !string.IsNullOrEmpty(server.ServerUrl)
            ? "接続先: " + server.ServerUrl
            : "サーバー起動中...";
    }

    void SpawnRig(string id)
    {
        if (rigs.ContainsKey(id) || reticlePrefab == null || reticleParent == null) return;

        GameObject go = Instantiate(reticlePrefab, reticleParent);
        var rt = go.GetComponent<RectTransform>();

        var rig = new PlayerRig
        {
            id = id,
            playerNumber = playerOrder.Count + 1,
            reticle = rt,
            reticleImage = go.GetComponentInChildren<Image>(),
            label = go.GetComponentInChildren<TMP_Text>(),
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
            rig.centerGamma = aim.gamma;
            rig.calibrated = true;
        }

        float dBeta = aim.beta - rig.centerBeta;   // 前後の傾き -> 上下移動
        float dGamma = aim.gamma - rig.centerGamma; // 左右の傾き -> 左右移動

        Vector2 target = new Vector2(dGamma * sensitivity, -dBeta * sensitivity);
        target = Vector2.ClampMagnitude(target, maxOffset);

        rig.currentOffset = Vector2.Lerp(rig.currentOffset, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        rig.reticle.anchoredPosition = rig.currentOffset;
    }

    void HandleFire(string id)
    {
        if (!rigs.TryGetValue(id, out var rig) || rig.reticle == null || aimCamera == null) return;

        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, rig.reticle.position);
        Ray ray = aimCamera.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, targetLayer))
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
        }

        StartCoroutine(FlashReticle(rig));
    }

    IEnumerator FlashReticle(PlayerRig rig)
    {
        if (rig.reticle == null) yield break;
        Vector3 original = rig.reticle.localScale;
        rig.reticle.localScale = original * 1.6f;
        yield return new WaitForSeconds(0.08f);
        if (rig.reticle != null) rig.reticle.localScale = original;
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
