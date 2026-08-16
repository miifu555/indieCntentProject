using System.Collections.Generic;
using UnityEngine;

// ロビー画面（開始前）で遊べる射撃練習用のお化け。命中してもスコアやゲーム進行には
// 影響せず、既存のShootingTarget（無改造）のrespawn機能でその場に何度でも出現し直す。
// ラウンド開始時に片付け、結果発表からロビーへ戻った時に再度出す。
public class CoopPracticeTargets : MonoBehaviour
{
    [Tooltip("練習用に出現させるプレファブ（複数指定するとランダムに選ばれる）")]
    public GameObject[] practicePrefabs;
    [Tooltip("設定すると出現した練習用お化けの見た目にこのマテリアルを強制適用する")]
    public Material overrideMaterial;
    [Tooltip("出現位置。ロビーの見える範囲に置くこと")]
    public Transform[] practicePoints;
    [Tooltip("同時に出す体数")]
    public int count = 3;
    [Tooltip("命中してから再出現するまでの秒数")]
    public float respawnDelay = 1.2f;

    private readonly List<GameObject> spawned = new List<GameObject>();

    public void ShowPracticeTargets()
    {
        ClearPracticeTargets();

        if (practicePrefabs == null || practicePrefabs.Length == 0) return;
        if (practicePoints == null || practicePoints.Length == 0) return;

        var indices = new List<int>();
        for (int i = 0; i < practicePoints.Length; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        int n = Mathf.Min(count, practicePoints.Length);
        for (int i = 0; i < n; i++)
        {
            Transform point = practicePoints[indices[i]];
            GameObject prefab = practicePrefabs[Random.Range(0, practicePrefabs.Length)];
            GameObject instance = Instantiate(prefab, point.position, point.rotation);

            if (overrideMaterial != null)
            {
                foreach (var r in instance.GetComponentsInChildren<Renderer>())
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = overrideMaterial;
                    r.sharedMaterials = mats;
                }
            }

            var shootingTarget = instance.GetComponent<ShootingTarget>();
            if (shootingTarget == null) shootingTarget = instance.AddComponent<ShootingTarget>();
            shootingTarget.scoreValue = 0;
            shootingTarget.respawns = true;
            shootingTarget.respawnDelay = respawnDelay;

            if (instance.GetComponent<Collider>() == null)
            {
                instance.AddComponent<BoxCollider>();
            }

            spawned.Add(instance);
        }
    }

    public void ClearPracticeTargets()
    {
        foreach (var go in spawned)
        {
            if (go != null) Destroy(go);
        }
        spawned.Clear();
    }
}
