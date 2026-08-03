using System.Collections;
using UnityEngine;

// 射的用の的。命中でスコアを返し、一定時間後にランダムな場所へ再出現する。
public class ShootingTarget : MonoBehaviour
{
    [Header("スコア設定")]
    public int scoreValue = 10;

    [Header("再出現設定")]
    public bool respawns = true;
    public float respawnDelay = 1.5f;
    [Tooltip("設定すると倒れた後にこの中からランダムな位置へ移動して再出現する。未設定なら同じ位置で再出現")]
    public Transform[] respawnPoints;

    [Header("演出")]
    public GameObject hitEffectPrefab;
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 1f;

    private Collider col;
    private Renderer[] renderers;
    private bool isDown;

    void Awake()
    {
        col = GetComponent<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
    }

    // 命中時にPhoneGunManagerから呼ばれる。獲得スコアを返す（既に倒れている場合は0）
    public int Hit(Vector3 hitPoint)
    {
        if (isDown) return 0;
        isDown = true;

        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, hitPoint, Quaternion.identity);
        }
        if (hitSound != null)
        {
            AudioSource.PlayClipAtPoint(hitSound, hitPoint, hitVolume);
        }

        SetVisible(false);

        if (respawns)
        {
            StartCoroutine(RespawnRoutine());
        }

        return scoreValue;
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        if (respawnPoints != null && respawnPoints.Length > 0)
        {
            Transform p = respawnPoints[Random.Range(0, respawnPoints.Length)];
            transform.position = p.position;
            transform.rotation = p.rotation;
        }

        isDown = false;
        SetVisible(true);
    }

    // TimeAttackManagerがフェーズ切り替え時に呼ぶ。倒れた状態や再出現待ちを打ち切り、
    // 即座に「出現済み・命中可能」な状態へ戻す。
    public void ResetTarget()
    {
        StopAllCoroutines();
        isDown = false;
        SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        if (col != null) col.enabled = visible;
        foreach (var r in renderers) r.enabled = visible;
    }
}
