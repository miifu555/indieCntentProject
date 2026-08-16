using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

// ロビー(QR画面) → 封印(StartGate)破壊でゲーム開始 → 全フェーズ終了で結果発表
// （1位が殿堂入りする場合はスマホで名前入力を待つ）→ 一定時間後に自動でロビーへ戻る、
// という一連の流れを管理する。シーン遷移はせず同じ画面内で完結する。
public class GameFlowController : MonoBehaviour
{
    [Header("フェード演出")]
    public CanvasGroup fadeGroup;
    public float fadeOutDuration = 0.8f;
    public float fadeInDuration = 0.8f;

    [Header("画面")]
    [Tooltip("QRコード・説明文などのロビーUI")]
    public GameObject lobbyUI;
    [Tooltip("ロビー中だけ試し打ちできる練習用の的（ゲーム開始で隠す）")]
    public GameObject practiceTargetsRoot;
    [Tooltip("右上のスコア表示。ロビー中は隠す")]
    public GameObject scoreboardUI;
    [Tooltip("全フェーズ終了時に一瞬映す「止め！」の合図画面")]
    public GameObject endCallUI;
    [Tooltip("「止め！」を表示しておく秒数")]
    public float endCallDisplayDuration = 1.5f;
    [Tooltip("結果発表を表示するUI（ランキング・ハイスコア名前入力を1画面にまとめる）")]
    public GameObject resultUI;
    public TMP_Text resultTitleText;
    public TMP_Text resultText;
    [Tooltip("結果発表画面下部の案内文（通常時／名前入力待ち中で内容を切り替える）")]
    public TMP_Text resultCaptionText;
    [Tooltip("結果発表の後に表示する「また遊んでね！」等の感謝メッセージ")]
    public TMP_Text nextRoundText;
    [Tooltip("感謝メッセージを表示しておく秒数")]
    public float nextRoundDisplayDuration = 4f;

    [Header("背景（城）の見た目切り替え")]
    [Tooltip("マテリアルを切り替える対象のRenderer（背景の城など）")]
    public Renderer stageRenderer;
    [Tooltip("ロビー画面で使うマテリアル")]
    public Material lobbyStageMaterial;
    [Tooltip("ゲーム中に使うマテリアル")]
    public Material gameStageMaterial;

    [Header("ゲーム進行")]
    public TargetPhaseManager targetPhaseManager;
    public StartGate startGate;
    public PhoneGunManager phoneGunManager;
    public PhoneGunServer server;
    [Tooltip("結果発表を表示しておく秒数（順位を下から出し終えた後の秒数）")]
    public float resultDisplayDuration = 8f;
    [Tooltip("結果発表で、順位を1つ出すごとに空ける間隔（下位から出す）")]
    public float rankingRevealInterval = 0.5f;

    [Header("歴代ハイスコア")]
    public HighScoreManager highScoreManager;
    [Tooltip("ロビー画面に表示する歴代ハイスコア一覧")]
    public TMP_Text lobbyHighScoreText;
    [Tooltip("名前入力を待つ最大秒数（超えたら???のまま記録される）")]
    public float nameEntryTimeout = 20f;

    private bool running;

    void Awake()
    {
        if (targetPhaseManager != null) targetPhaseManager.onAllPhasesComplete += HandleAllPhasesComplete;
    }

    void Start()
    {
        RefreshLobbyHighScore();
        ApplyStageMaterial(lobbyStageMaterial);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
    }

    // 主催者用のショートカット。Cキーで歴代ハイスコアを全消去する
    // このプロジェクトはActive Input HandlingがInput System側のみのため、
    // 旧UnityEngine.Inputではなく新Input SystemのKeyboardを使う
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame && highScoreManager != null)
        {
            highScoreManager.ClearAll();
            RefreshLobbyHighScore();
            Debug.Log("[GameFlowController] Cキーが押されたため、歴代ハイスコアをクリアしました。");
        }
    }

    void OnDestroy()
    {
        if (targetPhaseManager != null) targetPhaseManager.onAllPhasesComplete -= HandleAllPhasesComplete;
    }

    // StartGateが壊された時に呼ばれる
    public void StartGame()
    {
        if (running) return;
        running = true;
        StartCoroutine(GameStartSequence());
    }

    IEnumerator GameStartSequence()
    {
        yield return Fade(1f, fadeOutDuration);

        if (lobbyUI != null) lobbyUI.SetActive(false);
        if (practiceTargetsRoot != null) practiceTargetsRoot.SetActive(false);
        if (scoreboardUI != null) scoreboardUI.SetActive(true);
        if (targetPhaseManager != null) targetPhaseManager.StartPhase(0);
        ApplyStageMaterial(gameStageMaterial);

        yield return Fade(0f, fadeInDuration);
    }

    // 画面が真っ暗な間に城のマテリアルを切り替える（切り替わる瞬間が見えないように）
    void ApplyStageMaterial(Material material)
    {
        if (stageRenderer != null && material != null)
        {
            stageRenderer.material = material;
        }
    }

    void HandleAllPhasesComplete()
    {
        StartCoroutine(GameEndSequence());
    }

    // 全フェーズ終了時に「止め！」を一瞬映してから結果発表へつなげる
    IEnumerator GameEndSequence()
    {
        yield return Fade(1f, fadeOutDuration);

        if (endCallUI != null) endCallUI.SetActive(true);
        yield return Fade(0f, fadeInDuration);

        yield return new WaitForSeconds(endCallDisplayDuration);

        yield return Fade(1f, fadeOutDuration);
        if (endCallUI != null) endCallUI.SetActive(false);

        yield return ResultSequence();
    }

    // 呼び出された時点で画面は既に真っ暗（alpha=1）である前提
    IEnumerator ResultSequence()
    {
        // 殿堂入りするかどうかを先に判定し、結果発表画面自体にも「ハイスコア！」を出す
        string topId = null;
        int topPlayerNumber = 0;
        int topScore = 0;
        bool hasTop = phoneGunManager != null &&
            phoneGunManager.GetTopPlayer(out topId, out topPlayerNumber, out topScore);
        bool qualifiesForHighScore = hasTop && highScoreManager != null && server != null &&
            highScoreManager.WouldQualify(topScore);

        if (resultTitleText != null)
        {
            resultTitleText.text = qualifiesForHighScore ? "結果発表！\nハイスコア！" : "結果発表！";
        }
        if (resultText != null) resultText.text = "";
        if (resultCaptionText != null) resultCaptionText.text = "まもなくロビーに戻ります…";
        SetResultContentVisible(true);
        if (resultUI != null) resultUI.SetActive(true);

        yield return Fade(0f, fadeInDuration);

        // 下位から順に1つずつ表示していき、最後に1位が現れる
        if (resultText != null && phoneGunManager != null)
        {
            yield return RevealRankingSequence(resultText, phoneGunManager.GetRankingEntries());
        }

        // 殿堂入りする場合は同じ画面のまま案内文をハイスコア名前入力に切り替える。
        // しない場合はそのままランキングを見せておく
        if (qualifiesForHighScore)
        {
            yield return NameEntryInline(topId, topPlayerNumber, topScore);
        }
        else
        {
            yield return new WaitForSeconds(resultDisplayDuration);
        }

        // 結果・入力画面を消して、同じ結果発表UI内で感謝メッセージへ切り替える
        SetResultContentVisible(false);
        if (nextRoundText != null)
        {
            nextRoundText.gameObject.SetActive(true);
            nextRoundText.text = "ご参加ありがとうございました！\nまた遊んでね！";
        }

        yield return new WaitForSeconds(nextRoundDisplayDuration);

        if (nextRoundText != null) nextRoundText.gameObject.SetActive(false);

        yield return Fade(1f, fadeOutDuration);

        if (resultUI != null) resultUI.SetActive(false);

        ResetForNextRound();
        RefreshLobbyHighScore();
        ApplyStageMaterial(lobbyStageMaterial);
        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (practiceTargetsRoot != null) practiceTargetsRoot.SetActive(true);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);

        yield return Fade(0f, fadeInDuration);

        running = false;
    }

    // 順位を下位から1つずつ表示していく（最後に1位が現れる演出）
    IEnumerator RevealRankingSequence(TMP_Text targetText, System.Collections.Generic.List<(string label, int score)> entries)
    {
        targetText.text = "";

        if (entries.Count == 0)
        {
            targetText.text = "参加者がいませんでした";
            yield break;
        }

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            string line = RankingColor.FormatLine(i + 1, entries[i].label, entries[i].score);
            targetText.text = string.IsNullOrEmpty(targetText.text) ? line : line + "\n" + targetText.text;
            yield return new WaitForSeconds(rankingRevealInterval);
        }
    }

    // 結果発表画面はそのままに、案内文だけをハイスコア名前入力に切り替えて待つ
    IEnumerator NameEntryInline(string playerId, int playerNumber, int score)
    {
        server.RequestNameEntry(playerId);

        if (resultCaptionText != null)
        {
            resultCaptionText.text = $"P{playerNumber}のスマホで名前を入力してください…";
        }

        float elapsed = 0f;
        string submittedName = null;
        while (elapsed < nameEntryTimeout)
        {
            if (server.TryDequeueNameSubmission(out submittedName)) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        server.CancelNameEntry();
        if (string.IsNullOrEmpty(submittedName)) submittedName = "???";
        highScoreManager.AddEntry(submittedName, score);

        if (resultCaptionText != null)
        {
            resultCaptionText.text = $"{submittedName} さん、殿堂入りです！";
        }
        yield return new WaitForSeconds(1.5f);
    }

    void SetResultContentVisible(bool visible)
    {
        if (resultTitleText != null) resultTitleText.gameObject.SetActive(visible);
        if (resultText != null) resultText.gameObject.SetActive(visible);
        if (resultCaptionText != null) resultCaptionText.gameObject.SetActive(visible);
    }

    void ResetForNextRound()
    {
        if (server != null) server.ResetScores();
        if (targetPhaseManager != null) targetPhaseManager.ResetForNextRound();
        if (startGate != null) startGate.ResetGate();
    }

    void RefreshLobbyHighScore()
    {
        if (lobbyHighScoreText != null && highScoreManager != null)
        {
            lobbyHighScoreText.text = highScoreManager.BuildRankingText();
        }
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
