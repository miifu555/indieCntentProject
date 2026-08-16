using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 妖退治（協力プレイ）ゲーム用。町の中のランダムな地点に、一定間隔でお化けを出現させ続ける。
// 既存のランキングゲーム側のスクリプトには一切手を加えず、ShootingTarget（既存・無改造）を
// 出現させた妖に動的に付与することで、既存のPhoneGunManagerの命中判定をそのまま利用する。
public class YokaiSpawner : MonoBehaviour
{
    [Tooltip("出現させる妖のプレファブ（複数指定するとランダムに選ばれる。GitHub等から取り込んだGLBモデルのプレファブをここに設定する）")]
    public GameObject[] yokaiPrefabs;
    [Tooltip("設定すると出現した妖の見た目にこのマテリアルを強制適用する（未設定ならプレファブ本来のマテリアルを使う）")]
    public Material overrideMaterial;
    [Tooltip("出現位置の候補。この中からランダムに1つ選ぶ")]
    public Transform[] spawnPoints;
    [Tooltip("妖が近づいていく先（未設定ならMain Cameraを自動で使う）")]
    public Transform attackTarget;

    [Header("出現間隔（時間経過で短くなる）")]
    [Tooltip("出現開始直後の間隔（秒）")]
    public float initialSpawnInterval = 1f;
    [Tooltip("最も短くなったときの間隔（秒）")]
    public float minSpawnInterval = 0.3f;
    [Tooltip("この秒数かけてinitialSpawnIntervalからminSpawnIntervalまで短くなる")]
    public float difficultyRampDuration = 30f;

    [Header("妖の接近・攻撃")]
    [Tooltip("プレイヤーへ近づく速さ")]
    public float moveSpeed = 1.3f;
    [Tooltip("この距離まで近づくと攻撃してくる")]
    public float attackDistance = 4f;
    [Tooltip("モデルの正面が進行方向とズレている場合の補正角度（X軸回転）。向きがまだおかしい場合はここを90刻みで調整する（90/-90/180を試す）")]
    public float yokaiPitchOffset = 90f;
    [Tooltip("命中されずに残っていられる最大秒数（保険用のタイムアウト。0以下で無効）")]
    public float maxLifetime = 25f;
    [Tooltip("命中時に加算されるダメージ（スコア）")]
    public int scoreValuePerHit = 20;

    [Tooltip("妖がプレイヤーへ到達（攻撃）した時に呼ばれる")]
    public System.Action onYokaiReachedTarget;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private Coroutine spawnCoroutine;
    private float spawnStartTime;

    public void StartSpawning()
    {
        StopSpawning();
        spawnStartTime = Time.time;
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
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            SpawnOne();

            float elapsed = Time.time - spawnStartTime;
            float t = difficultyRampDuration > 0f ? Mathf.Clamp01(elapsed / difficultyRampDuration) : 1f;
            float interval = Mathf.Lerp(initialSpawnInterval, minSpawnInterval, t);
            yield return new WaitForSeconds(interval);
        }
    }

    void SpawnOne()
    {
        spawned.RemoveAll(go => go == null);

        if (yokaiPrefabs == null || yokaiPrefabs.Length == 0) return;
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        GameObject prefab = yokaiPrefabs[Random.Range(0, yokaiPrefabs.Length)];
        Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];

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

        var shootingTarget = instance.GetComponent<ShootingTarget>();
        if (shootingTarget == null) shootingTarget = instance.AddComponent<ShootingTarget>();
        shootingTarget.scoreValue = scoreValuePerHit;
        shootingTarget.respawns = false;

        if (instance.GetComponent<Collider>() == null)
        {
            instance.AddComponent<BoxCollider>();
        }

        Transform moveTarget = attackTarget != null ? attackTarget : (Camera.main != null ? Camera.main.transform : null);
        var mover = instance.AddComponent<YokaiMover>();
        mover.target = moveTarget;
        mover.moveSpeed = moveSpeed;
        mover.attackDistance = attackDistance;
        mover.pitchOffsetDegrees = yokaiPitchOffset;
        mover.onReachedTarget = () => onYokaiReachedTarget?.Invoke();

        spawned.Add(instance);

        if (maxLifetime > 0f)
        {
            Destroy(instance, maxLifetime);
        }
    }
}
