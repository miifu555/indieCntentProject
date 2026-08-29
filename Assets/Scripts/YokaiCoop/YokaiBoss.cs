using UnityEngine;

// 妖退治（協力プレイ）の決戦ボス。既存のShootingTarget（無改造）のrespawn機能を利用し、
// 被弾のたびに一瞬消えて再出現する挙動（Collider.enabledの立ち下がり）を回数カウントする
// ことで多段HPを表現する。ShootingTarget/PhoneGunManagerには一切手を加えない。
[RequireComponent(typeof(ShootingTarget))]
public class YokaiBoss : MonoBehaviour
{
    [Tooltip("倒すのに必要な被弾回数")]
    public int maxHP = 8;
    [Tooltip("被弾してから再出現するまでの間（短いほど連続して狙いやすい）")]
    public float hitFlickerDelay = 0.35f;
    [Tooltip("完全に倒された後、演出のために消えるまでの秒数")]
    public float defeatDestroyDelay = 0.8f;
    [Tooltip("完全に撃破された瞬間に出すエフェクト（任意）")]
    public GameObject defeatEffectPrefab;
    [Tooltip("撃破エフェクトを消すまでの秒数")]
    public float defeatEffectLifetime = 1.5f;
    [Tooltip("命中音・撃破音を鳴らすBGMマネージャー（同じ場所から2D再生する）")]
    public CoopBgmManager bgmManager;
    [Tooltip("被弾するたびに鳴らす効果音（任意）")]
    public AudioClip hitSound;
    [Range(0f, 1f)]
    public float hitVolume = 1f;

    public System.Action onDefeated;

    private ShootingTarget shootingTarget;
    private Collider col;
    private int currentHP;
    private bool wasEnabled = true;
    private bool defeated;

    void Awake()
    {
        shootingTarget = GetComponent<ShootingTarget>();
        col = GetComponent<Collider>();
        currentHP = maxHP;

        // ボスのダメージはこのスクリプトのHPで管理するため、既存のスコア加算は使わない
        shootingTarget.scoreValue = 0;
        shootingTarget.respawns = true;
        shootingTarget.respawnDelay = hitFlickerDelay;
    }

    void Update()
    {
        if (defeated || col == null) return;

        bool isEnabled = col.enabled;
        if (wasEnabled && !isEnabled)
        {
            currentHP--;
            if (bgmManager != null) bgmManager.PlaySfx(hitSound, hitVolume);
            if (currentHP <= 0)
            {
                defeated = true;
                // 保留中の自動再出現(RespawnRoutine)を止め、消えたままにする
                shootingTarget.StopAllCoroutines();
                if (defeatEffectPrefab != null)
                {
                    GameObject effect = Instantiate(defeatEffectPrefab, transform.position, Quaternion.identity);
                    Destroy(effect, defeatEffectLifetime);
                }
                onDefeated?.Invoke();
                Destroy(gameObject, defeatDestroyDelay);
            }
        }
        wasEnabled = isEnabled;
    }
}
