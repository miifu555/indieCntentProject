using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 参加人数ごとの最大HP設定
[System.Serializable]
public class PlayerCountHP
{
    [Tooltip("この人数以上で以下のmaxHPを適用する")]
    public int playerCount = 1;
    public int maxHP = 5;
}

// 妖退治（協力プレイ）ゲームの進行管理。
// ロビー → 封印(CoopStartGate)破壊で開始 → 町の中に妖が徐々に短い間隔で出現し、
// プレイヤー(カメラ)に接近してくる → 妖に近づかれるたびHPが減り、0になると全滅（失敗）。
// YokaiSpawnerの全フェーズを出し切ればクリア。スコアは常時表示するが、勝敗の判定には使わない。
// 既存のランキングゲーム側（GameFlowController等）には一切手を加えず、
// PhoneGunServer・PhoneGunManager・ShootingTargetは無改造のまま利用する。
public class CoopGameFlowController : MonoBehaviour
{
    [Header("フェード演出")]
    public CanvasGroup fadeGroup;
    public float fadeOutDuration = 0.8f;
    public float fadeInDuration = 0.8f;

    [Header("画面")]
    [Tooltip("QRコード・説明文などのロビーUI")]
    public GameObject lobbyUI;
    [Tooltip("右上のスコア表示。ロビー中は隠す")]
    public GameObject scoreboardUI;
    [Tooltip("結果発表を表示するUI")]
    public GameObject resultUI;
    public TMP_Text resultTitleText;
    public TMP_Text resultText;
    [Tooltip("結果発表を表示しておく秒数")]
    public float resultDisplayDuration = 6f;
    [Tooltip("ロビー中に遊べる射撃練習用のお化け。ラウンド開始で片付き、ロビーに戻ると再度出現する")]
    public CoopPracticeTargets practiceTargets;
    [Tooltip("ロビー中だけ表示し、ラウンド開始で非表示にする3Dオブジェクト（的の模型など。UIではなくシーン上のオブジェクトを指定する）")]
    public GameObject[] lobbyOnlyObjects;
    [Tooltip("最後のフェーズが始まっている間だけ表示する煽り文字（「連打！！！」等）")]
    public TMP_Text rapidFireText;

    [Tooltip("BGMの再生を担当する専用マネージャー")]
    public CoopBgmManager bgmManager;

    [Header("ゲーム進行")]
    public YokaiSpawner spawner;
    public CoopStartGate startGate;
    public PhoneGunServer server;
    [Tooltip("結果画面で1人ずつのスコアを色名付きで表示するために使う（未設定なら色名無しで1人ずつ表示する）")]
    public CoopPlayerSlotManager slotManager;
    [Tooltip("プレイヤーのHP。妖に近づかれるたび1減る。hpByPlayerCountが未設定の場合のデフォルト値としても使う")]
    public int maxHP = 5;
    [Tooltip("参加人数によってHPを変える場合のリスト。ラウンド開始時点の接続人数以下で最も人数が多い設定が使われる（どれも当てはまらなければ一番少ない人数の設定を使う）。空ならmaxHPをそのまま使う")]
    public PlayerCountHP[] hpByPlayerCount;

    [Header("被弾演出（妖に攻撃された時）")]
    [Tooltip("妖に攻撃された瞬間に出す視覚効果（任意。カメラの少し前に出す）")]
    public GameObject playerHitEffectPrefab;
    [Tooltip("被弾エフェクトを消すまでの秒数")]
    public float playerHitEffectLifetime = 1f;
    [Tooltip("妖に攻撃された瞬間に鳴らす効果音（任意。未設定なら鳴らさない）")]
    public AudioClip playerHitSound;
    [Range(0f, 1f)]
    public float playerHitVolume = 1f;

    [Header("HP UI")]
    public Slider gaugeSlider;
    public TMP_Text gaugeText;

    private bool running;
    private bool gameActive;
    private int currentHP;

    void Awake()
    {
        if (spawner != null)
        {
            spawner.onYokaiReachedTarget = OnYokaiReachedPlayer;
            spawner.onLastPhaseActiveChanged = OnLastPhaseActiveChanged;
            spawner.onAllWavesCleared = OnAllWavesCleared;
        }
        if (rapidFireText != null) rapidFireText.gameObject.SetActive(false);
    }

    void OnLastPhaseActiveChanged(bool active)
    {
        if (rapidFireText != null) rapidFireText.gameObject.SetActive(active);
    }

    // YokaiSpawnerが全フェーズの敵を出し切ったら呼ばれる。クリア扱いにする
    void OnAllWavesCleared()
    {
        if (!gameActive) return;
        gameActive = false;
        StartCoroutine(EndSequence(true));
    }

    void Start()
    {
        SetLobbyOnlyObjectsActive(true);
        if (practiceTargets != null) practiceTargets.ShowPracticeTargets();
        if (bgmManager != null) bgmManager.PlayLobbyBgm();
    }

    void SetLobbyOnlyObjectsActive(bool active)
    {
        if (lobbyOnlyObjects == null) return;
        foreach (var go in lobbyOnlyObjects)
        {
            if (go != null) go.SetActive(active);
        }
    }

    // YokaiSpawnerから、妖がプレイヤーへ到達（攻撃）した時に呼ばれる
    void OnYokaiReachedPlayer()
    {
        PlayPlayerHitReaction();
        LoseHP(1);
    }

    void PlayPlayerHitReaction()
    {
        if (bgmManager != null) bgmManager.PlaySfx(playerHitSound, playerHitVolume);
        if (playerHitEffectPrefab != null)
        {
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 pos = cam != null ? cam.position + cam.forward * 0.5f : transform.position;
            Quaternion rot = cam != null ? cam.rotation : Quaternion.identity;
            GameObject effect = Instantiate(playerHitEffectPrefab, pos, rot);
            if (cam != null) effect.transform.SetParent(cam, true);
            Destroy(effect, playerHitEffectLifetime);
        }
    }

    void LoseHP(int amount)
    {
        if (!gameActive) return;

        currentHP = Mathf.Max(0, currentHP - amount);
        UpdateHPUI();

        if (currentHP <= 0)
        {
            gameActive = false;
            StartCoroutine(EndSequence(false));
        }
    }

    int CountConnectedPlayers()
    {
        int count = 0;
        if (server != null && server.ConnectedPlayerIds != null)
        {
            foreach (var id in server.ConnectedPlayerIds) count++;
        }
        return count;
    }

    // hpByPlayerCountから、connectedCount以下で最も人数が多い設定を選ぶ。
    // どれも当てはまらない（全設定がconnectedCountより多い人数を要求している）場合は
    // 一番少ない人数の設定を使う
    int ResolveMaxHP(int connectedCount)
    {
        PlayerCountHP best = null;
        PlayerCountHP smallest = null;
        foreach (var entry in hpByPlayerCount)
        {
            if (entry == null) continue;
            if (smallest == null || entry.playerCount < smallest.playerCount) smallest = entry;
            if (entry.playerCount <= connectedCount && (best == null || entry.playerCount > best.playerCount))
            {
                best = entry;
            }
        }
        PlayerCountHP chosen = best ?? smallest;
        return chosen != null ? chosen.maxHP : maxHP;
    }

    void UpdateHPUI()
    {
        if (gaugeSlider != null)
        {
            gaugeSlider.value = maxHP > 0 ? Mathf.Clamp01((float)currentHP / maxHP) : 0f;
        }
        if (gaugeText != null)
        {
            gaugeText.text = "HP " + currentHP + " / " + maxHP;
        }
    }

    // CoopStartGateが壊された時に呼ばれる
    public void StartGame()
    {
        if (running) return;
        running = true;
        StartCoroutine(GameStartSequence());
    }

    IEnumerator GameStartSequence()
    {
        yield return Fade(1f, fadeOutDuration);

        if (bgmManager != null) bgmManager.PlayGameplayBgm();

        if (lobbyUI != null) lobbyUI.SetActive(false);
        if (scoreboardUI != null) scoreboardUI.SetActive(true);
        SetLobbyOnlyObjectsActive(false);
        if (practiceTargets != null) practiceTargets.ClearPracticeTargets();
        if (server != null) server.ResetScores();
        if (hpByPlayerCount != null && hpByPlayerCount.Length > 0)
        {
            maxHP = ResolveMaxHP(CountConnectedPlayers());
        }
        currentHP = maxHP;
        UpdateHPUI();
        gameActive = true;
        if (spawner != null) spawner.StartSpawning();

        yield return Fade(0f, fadeInDuration);
    }

    IEnumerator EndSequence(bool cleared)
    {
        if (spawner != null) spawner.StopSpawning();

        yield return Fade(1f, fadeOutDuration);

        if (bgmManager != null) bgmManager.PlayResultBgm(cleared);

        string scoreList = GetScoreListText();

        if (resultTitleText != null)
        {
            resultTitleText.text = cleared ? "討伐成功！" : "全滅…";
        }
        if (resultText != null)
        {
            resultText.text = (cleared ? "残りHP　" + currentHP + " / " + maxHP : "妖にやられてしまった…") +
                "\n" + scoreList;
        }
        if (resultUI != null) resultUI.SetActive(true);

        yield return Fade(0f, fadeInDuration);

        yield return new WaitForSeconds(resultDisplayDuration);

        yield return Fade(1f, fadeOutDuration);

        if (resultUI != null) resultUI.SetActive(false);

        ResetForNextRound();

        if (bgmManager != null) bgmManager.PlayLobbyBgm();

        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
        SetLobbyOnlyObjectsActive(true);
        if (practiceTargets != null) practiceTargets.ShowPracticeTargets();

        yield return Fade(0f, fadeInDuration);

        running = false;
    }

    // 結果画面に表示する、参加者1人ずつのスコア一覧を組み立てる
    string GetScoreListText()
    {
        // slotManagerがあれば、ゲーム中のスコアボードと同じ色名付き表記をそのまま使う
        if (slotManager != null) return slotManager.GetFormattedScoreList();

        if (server == null || server.ConnectedPlayerIds == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var id in server.ConnectedPlayerIds)
        {
            if (server.TryGetPlayer(id, out var info)) sb.AppendLine(info.score + "点");
        }
        return sb.ToString();
    }

    void ResetForNextRound()
    {
        if (server != null) server.ResetScores();
        if (startGate != null) startGate.ResetGate();
        currentHP = maxHP;
        UpdateHPUI();
    }

    IEnumerator Fade(float target, float duration)
    {
        if (fadeGroup == null) yield break;

        fadeGroup.blocksRaycasts = true;
        float start = fadeGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fadeGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        fadeGroup.alpha = target;

        if (Mathf.Approximately(target, 0f)) fadeGroup.blocksRaycasts = false;
    }
}
