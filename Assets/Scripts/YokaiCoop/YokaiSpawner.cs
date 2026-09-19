using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// フェーズ内で出現させる敵の種類と数の組
[System.Serializable]
public class PhaseEnemyEntry
{
    public GameObject prefab;
    [Tooltip("このフェーズで出現させる数")]
    public int count = 1;
}

// 妖の出現フェーズ。指定した敵をすべて出し切ったら次のフェーズへ進む
[System.Serializable]
public class SpawnPhase
{
    [Tooltip("Inspector上で分かりやすくするための名前（任意、動作には影響しない）")]
    public string phaseName = "Phase";
    [Tooltip("このフェーズ内で1体出現させるごとの間隔（秒）")]
    public float spawnInterval = 1f;
    [Tooltip("このフェーズで出現させる敵の種類と数")]
    public PhaseEnemyEntry[] enemies;
    [Tooltip("このフェーズで使う出現位置。空にするとYokaiSpawner本体のspawnPoints（デフォルトの出現位置）を使う")]
    public Transform[] spawnPoints;
}

// 妖退治（協力プレイ）ゲーム用。町の中のランダムな地点に、フェーズごとに指定した
// 敵の種類・数だけ出現させ続ける。既存のランキングゲーム側のスクリプトには一切手を
// 加えず、ShootingTarget（既存・無改造）を出現させた妖に動的に付与することで、
// 既存のPhoneGunManagerの命中判定をそのまま利用する。
public class YokaiSpawner : MonoBehaviour
{
    [Tooltip("設定すると出現した妖の見た目にこのマテリアルを強制適用する（未設定ならプレファブ本来のマテリアルを使う）")]
    public Material overrideMaterial;
    [Tooltip("出現位置の候補。この中からランダムに1つ選ぶ")]
    public Transform[] spawnPoints;
    [Tooltip("妖が近づいていく先（未設定ならMain Cameraを自動で使う）")]
    public Transform attackTarget;
    [Tooltip("YokaiStats.shareScoreWithAllがtrueの敵を倒した時、全プレイヤーへスコアを加算するために使う")]
    public PhoneGunServer server;

    [Header("出現フェーズ")]
    [Tooltip("フェーズごとに出現させる敵の種類と数を指定する。全フェーズの敵を出し切ったらクリア扱いになる")]
    public SpawnPhase[] phases;
    [Tooltip("現在のフェーズの敵を出し切った後、次のフェーズに進むまで待つ秒数（休憩時間）")]
    public float phaseInterval = 3f;
    [Tooltip("ゲーム開始直後、最初のフェーズの1体目が出現するまで待つ秒数")]
    public float firstWaveDelay = 2f;

    [Header("妖の接近・攻撃")]
    [Tooltip("この距離まで近づくと攻撃してくる")]
    public float attackDistance = 4f;
    [Tooltip("モデルの正面が進行方向とズレている場合の補正角度（X軸回転）。向きがまだおかしい場合はここを90刻みで調整する（90/-90/180を試す）")]
    public float yokaiPitchOffset = 90f;
    [Tooltip("命中されずに残っていられる最大秒数（保険用のタイムアウト。0以下で無効）")]
    public float maxLifetime = 25f;
    [Tooltip("命中時に加算されるスコア。プレハブにYokaiStatsが付いている場合はそちらのscoreValueが優先される")]
    public int scoreValuePerHit = 20;
    [Tooltip("命中した時に命中位置へ出すエフェクト（任意）")]
    public GameObject hitEffectPrefab;
    [Tooltip("命中した時に鳴らす効果音（任意）")]
    public AudioClip hitSound;
    [Range(0f, 1f)]
    public float hitVolume = 1f;
    [Tooltip("撃破された瞬間に出すエフェクト（任意）")]
    public GameObject defeatEffectPrefab;
    [Tooltip("命中音を鳴らすBGMマネージャー（同じ場所から2D再生する）")]
    public CoopBgmManager bgmManager;
    [Tooltip("YokaiStats.showHpBarがtrueの妖（ボス）が出現した時にHPを表示するバー")]
    public BossHpBar bossHpBar;

    [Tooltip("妖がプレイヤーへ到達（攻撃）した時に呼ばれる")]
    public System.Action onYokaiReachedTarget;
    [Tooltip("最後のフェーズが始まったらtrue、終わったらfalseで呼ばれる（「連打！！！」等の表示に使う）")]
    public System.Action<bool> onLastPhaseActiveChanged;
    [Tooltip("全フェーズの敵を出し切ったら呼ばれる（クリア判定に使う）")]
    public System.Action onAllWavesCleared;

    // プレハブにYokaiStatsが付いていない場合のフォールバック速度
    const float DefaultMoveSpeed = 1.3f;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private Coroutine spawnCoroutine;
    private bool lastPhaseActive;

    public void StartSpawning()
    {
        StopSpawning();
        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    // 出現中の妖を全て消し、出現ループを止める
    public void StopSpawning()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        foreach (var go in spawned)
        {
            if (go != null) Destroy(go);
        }
        spawned.Clear();

        SetLastPhaseActive(false);
    }

    void SetLastPhaseActive(bool active)
    {
        if (lastPhaseActive == active) return;
        lastPhaseActive = active;
        onLastPhaseActiveChanged?.Invoke(active);
    }

    IEnumerator SpawnLoop()
    {
        if (phases == null || phases.Length == 0) yield break;

        if (firstWaveDelay > 0f)
        {
            yield return new WaitForSeconds(firstWaveDelay);
        }

        for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
        {
            var phase = phases[phaseIndex];
            bool isLastPhase = phaseIndex == phases.Length - 1;
            SetLastPhaseActive(isLastPhase);

            var queue = BuildSpawnQueue(phase);
            float interval = Mathf.Max(0.01f, phase.spawnInterval);
            Transform[] pointsForPhase = (phase.spawnPoints != null && phase.spawnPoints.Length > 0) ? phase.spawnPoints : spawnPoints;

            foreach (var entry in queue)
            {
                SpawnOne(entry, pointsForPhase);
                yield return new WaitForSeconds(interval);
            }

            // このフェーズで出した敵が全員倒れる（またはいなくなる）まで、次のフェーズへ進まない
            yield return WaitForCurrentWaveCleared();

            if (isLastPhase) SetLastPhaseActive(false);

            // 最後のフェーズの後は休憩を挟まずそのままクリア扱いにする
            if (!isLastPhase && phaseInterval > 0f)
            {
                yield return new WaitForSeconds(phaseInterval);
            }
        }

        onAllWavesCleared?.Invoke();
    }

    // spawned内の妖が全員いなくなるまで待つ（倒された・目的地に到達した・保険タイムアウトのいずれか）
    IEnumerator WaitForCurrentWaveCleared()
    {
        while (true)
        {
            spawned.RemoveAll(go => go == null);
            if (spawned.Count == 0) yield break;
            yield return null;
        }
    }

    // フェーズ内の(敵, 数)指定から、出現順をシャッフルしたキューを組み立てる
    List<PhaseEnemyEntry> BuildSpawnQueue(SpawnPhase phase)
    {
        var queue = new List<PhaseEnemyEntry>();
        if (phase.enemies != null)
        {
            foreach (var entry in phase.enemies)
            {
                if (entry.prefab == null) continue;
                for (int i = 0; i < entry.count; i++)
                {
                    queue.Add(entry);
                }
            }
        }

        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }
        return queue;
    }

    void SpawnOne(PhaseEnemyEntry entry, Transform[] pointsForPhase)
    {
        spawned.RemoveAll(go => go == null);

        GameObject prefab = entry.prefab;
        if (prefab == null) return;
        if (pointsForPhase == null || pointsForPhase.Length == 0) return;

        Transform point = pointsForPhase[Random.Range(0, pointsForPhase.Length)];

        GameObject instance = Instantiate(prefab, point.position, point.rotation);

        if (overrideMaterial != null)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = overrideMaterial;
                r.sharedMaterials = mats;
            }
        }

        // プレハブに付けておいた種類ごとの基本ステータス（速度・HP・大きさ・フェードイン時間）。
        // 無ければ今まで通り1発で倒れる・デフォルト速度・フェードなしで出現する
        var stats = instance.GetComponent<YokaiStats>();
        int hp = stats != null ? Mathf.Max(1, stats.maxHP) : 1;
        float speed = stats != null ? stats.moveSpeed : DefaultMoveSpeed;

        if (stats != null && stats.scale != 1f)
        {
            instance.transform.localScale *= stats.scale;
        }

        if (stats != null && stats.fadeInDuration > 0f)
        {
            var fadeIn = instance.AddComponent<YokaiFadeIn>();
            fadeIn.duration = stats.fadeInDuration;
        }

        bool shareScoreWithAll = stats != null && stats.shareScoreWithAll;

        var shootingTarget = instance.GetComponent<ShootingTarget>();
        if (shootingTarget == null) shootingTarget = instance.AddComponent<ShootingTarget>();
        // 全員へ加算する敵は、撃破に貢献した本人だけへの自動加算(PhoneGunManager側)を無効にしておく
        shootingTarget.scoreValue = shareScoreWithAll ? 0 : (stats != null ? stats.scoreValue : scoreValuePerHit);
        shootingTarget.respawns = hp > 1;
        if (hp > 1 && stats != null) shootingTarget.respawnDelay = stats.hitFlickerDelay;
        shootingTarget.hitEffectPrefab = (stats != null && stats.hitEffectPrefab != null) ? stats.hitEffectPrefab : hitEffectPrefab;

        if (instance.GetComponent<Collider>() == null)
        {
            instance.AddComponent<BoxCollider>();
        }

        Transform moveTarget = attackTarget != null ? attackTarget : (Camera.main != null ? Camera.main.transform : null);
        var mover = instance.AddComponent<YokaiMover>();
        mover.target = moveTarget;
        mover.moveSpeed = speed;
        mover.maxHP = hp;
        mover.attackDistance = attackDistance;
        mover.pitchOffsetDegrees = yokaiPitchOffset;
        mover.defeatEffectPrefab = defeatEffectPrefab;
        mover.bgmManager = bgmManager;
        mover.hitSound = hitSound;
        mover.hitVolume = hitVolume;
        mover.onReachedTarget = () => onYokaiReachedTarget?.Invoke();
        if (shareScoreWithAll)
        {
            int shareAmount = stats.scoreValue;
            mover.onDefeated = () => AwardScoreToAllPlayers(shareAmount);
        }

        if (stats != null && stats.showHpBar && bossHpBar != null)
        {
            bossHpBar.Bind(mover, string.IsNullOrEmpty(stats.bossName) ? instance.name.Replace("(Clone)", "").Trim() : stats.bossName);
        }

        spawned.Add(instance);

        if (maxLifetime > 0f)
        {
            Destroy(instance, maxLifetime);
        }
    }

    // 現在参加中の全プレイヤーへ同じ量のスコアを加算する（YokaiStats.shareScoreWithAll用）
    void AwardScoreToAllPlayers(int amount)
    {
        if (server == null || amount <= 0) return;
        foreach (var id in server.ConnectedPlayerIds)
        {
            server.AddScore(id, amount);
        }
    }
}
