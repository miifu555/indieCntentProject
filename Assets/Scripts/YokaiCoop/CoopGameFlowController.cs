using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 妖退治（1人プレイ）ゲームの進行管理。
// ロビー → 封印(CoopStartGate)破壊で開始 → 町の中に妖が徐々に短い間隔で出現し、
// プレイヤー(カメラ)に接近してくる → 妖に近づかれるたびHPが減り、0になると全滅（失敗）。
// それとは別に、体力の多いボスが4〜6秒おきにランダムな間隔で出現し続ける（倒しても
// ラウンドは終了しない、あくまで難易度要素）。制限時間まで生き延びれば生存成功。
// スコアは常時表示するが、勝敗の判定には使わない。
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

    [Header("ゲーム進行")]
    public YokaiSpawner spawner;
    public CoopStartGate startGate;
    public PhoneGunServer server;
    [Tooltip("プレイヤーのHP。妖に近づかれるたび1減る")]
    public int maxHP = 5;
    [Tooltip("生き延びる必要がある制限時間（秒）。0以下にすると無制限（HPが0になるまで終わらない）")]
    public float timeLimit = 30f;

    [Header("HP UI")]
    public Slider gaugeSlider;
    public TMP_Text gaugeText;
    public TMP_Text timerText;

    [Header("ボス（体力の多い妖）")]
    [Tooltip("出現させるボスのプレファブ（複数指定するとランダムに選ばれる。GitHub等から取り込んだGLBモデルのプレファブをここに設定する）")]
    public GameObject[] bossPrefabs;
    [Tooltip("設定すると出現したボスの見た目にこのマテリアルを強制適用する（未設定ならプレファブ本来のマテリアルを使う）")]
    public Material bossOverrideMaterial;
    [Tooltip("ボスの出現位置。未設定ならspawnerの出現地点の中からランダムに選ぶ")]
    public Transform bossSpawnPoint;
    [Tooltip("ボスが出現する間隔（秒）の最小値")]
    public float bossSpawnIntervalMin = 4f;
    [Tooltip("ボスが出現する間隔（秒）の最大値")]
    public float bossSpawnIntervalMax = 6f;
    [Tooltip("倒すのに必要な被弾回数")]
    public int bossMaxHP = 8;
    [Tooltip("ボスがプレイヤーへ近づく速さ")]
    public float bossMoveSpeed = 0.6f;
    [Tooltip("この距離まで近づくと攻撃してくる")]
    public float bossAttackDistance = 5f;
    [Tooltip("ボスの攻撃が命中した時に減るHP")]
    public int bossAttackDamage = 2;
    [Tooltip("倒した時に加算されるスコア")]
    public int bossScoreValue = 100;

    private bool running;
    private bool gameActive;
    private float remainingTime;
    private int currentHP;
    private readonly List<YokaiBoss> activeBosses = new List<YokaiBoss>();
    private Coroutine bossSpawnCoroutine;

    void Awake()
    {
        if (spawner != null) spawner.onYokaiReachedTarget = OnYokaiReachedPlayer;
    }

    void Start()
    {
        if (practiceTargets != null) practiceTargets.ShowPracticeTargets();
    }

    void Update()
    {
        if (!gameActive) return;

        if (timeLimit > 0f)
        {
            remainingTime -= Time.deltaTime;
            if (timerText != null)
            {
                timerText.text = Mathf.Max(0, Mathf.CeilToInt(remainingTime)) + "秒";
            }
            if (remainingTime <= 0f)
            {
                gameActive = false;
                StartCoroutine(EndSequence(true));
                return;
            }
        }
    }

    IEnumerator BossSpawnLoop()
    {
        while (true)
        {
            float wait = Random.Range(bossSpawnIntervalMin, bossSpawnIntervalMax);
            yield return new WaitForSeconds(wait);
            SpawnBoss();
        }
    }

    void SpawnBoss()
    {
        activeBosses.RemoveAll(b => b == null);

        if (bossPrefabs == null || bossPrefabs.Length == 0) return;
        GameObject prefab = bossPrefabs[Random.Range(0, bossPrefabs.Length)];
        if (prefab == null) return;

        Transform point = bossSpawnPoint;
        if (point == null && spawner != null && spawner.spawnPoints != null && spawner.spawnPoints.Length > 0)
        {
            point = spawner.spawnPoints[Random.Range(0, spawner.spawnPoints.Length)];
        }
        Vector3 spawnPos = point != null ? point.position : transform.position;
        Quaternion spawnRot = point != null ? point.rotation : Quaternion.identity;

        GameObject instance = Instantiate(prefab, spawnPos, spawnRot);

        if (bossOverrideMaterial != null)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = bossOverrideMaterial;
                r.sharedMaterials = mats;
            }
        }

        var shootingTarget = instance.GetComponent<ShootingTarget>();
        if (shootingTarget == null) shootingTarget = instance.AddComponent<ShootingTarget>();

        if (instance.GetComponent<Collider>() == null)
        {
            instance.AddComponent<BoxCollider>();
        }

        var boss = instance.AddComponent<YokaiBoss>();
        boss.maxHP = bossMaxHP;
        boss.onDefeated = () => OnBossDefeated(boss, instance);
        activeBosses.Add(boss);

        Transform moveTarget = spawner != null && spawner.attackTarget != null
            ? spawner.attackTarget
            : (Camera.main != null ? Camera.main.transform : null);
        var mover = instance.AddComponent<YokaiMover>();
        mover.target = moveTarget;
        mover.moveSpeed = bossMoveSpeed;
        mover.attackDistance = bossAttackDistance;
        mover.pitchOffsetDegrees = spawner != null ? spawner.yokaiPitchOffset : 0f;
        mover.destroyOnHit = false;
        mover.destroyOnReachTarget = false;
        mover.onReachedTarget = OnBossReachedPlayer;
    }

    // ボスを倒してもラウンドは終了しない。スコア加算のみ行う（HPと勝敗には影響しない）
    void OnBossDefeated(YokaiBoss boss, GameObject instance)
    {
        activeBosses.Remove(boss);
        if (server != null && server.ConnectedPlayerIds != null)
        {
            foreach (var id in server.ConnectedPlayerIds)
            {
                server.AddScore(id, bossScoreValue);
                break; // 1人プレイ想定。最初の1人にのみ加算する
            }
        }
    }

    void OnBossReachedPlayer()
    {
        LoseHP(bossAttackDamage);
    }

    // YokaiSpawnerから、妖がプレイヤーへ到達（攻撃）した時に呼ばれる
    void OnYokaiReachedPlayer()
    {
        LoseHP(1);
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

        if (lobbyUI != null) lobbyUI.SetActive(false);
        if (scoreboardUI != null) scoreboardUI.SetActive(true);
        if (practiceTargets != null) practiceTargets.ClearPracticeTargets();
        if (server != null) server.ResetScores();
        currentHP = maxHP;
        UpdateHPUI();
        remainingTime = timeLimit;
        activeBosses.Clear();
        gameActive = true;
        if (spawner != null) spawner.StartSpawning();
        bossSpawnCoroutine = StartCoroutine(BossSpawnLoop());

        yield return Fade(0f, fadeInDuration);
    }

    IEnumerator EndSequence(bool cleared)
    {
        if (spawner != null) spawner.StopSpawning();
        if (bossSpawnCoroutine != null)
        {
            StopCoroutine(bossSpawnCoroutine);
            bossSpawnCoroutine = null;
        }
        foreach (var boss in activeBosses)
        {
            if (boss != null) Destroy(boss.gameObject);
        }
        activeBosses.Clear();

        yield return Fade(1f, fadeOutDuration);

        int finalScore = GetPlayerScore();

        if (resultTitleText != null)
        {
            resultTitleText.text = cleared ? "生存成功！" : "全滅…";
        }
        if (resultText != null)
        {
            resultText.text = (cleared ? "残りHP　" + currentHP + " / " + maxHP : "妖にやられてしまった…") +
                "\nスコア　" + finalScore;
        }
        if (resultUI != null) resultUI.SetActive(true);

        yield return Fade(0f, fadeInDuration);

        yield return new WaitForSeconds(resultDisplayDuration);

        yield return Fade(1f, fadeOutDuration);

        if (resultUI != null) resultUI.SetActive(false);

        ResetForNextRound();

        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
        if (practiceTargets != null) practiceTargets.ShowPracticeTargets();

        yield return Fade(0f, fadeInDuration);

        running = false;
    }

    int GetPlayerScore()
    {
        if (server == null || server.ConnectedPlayerIds == null) return 0;
        foreach (var id in server.ConnectedPlayerIds)
        {
            if (server.TryGetPlayer(id, out var info)) return info.score;
        }
        return 0;
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
