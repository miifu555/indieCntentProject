using UnityEngine;

// 妖退治（協力プレイ）用。出現した妖をプレイヤー（カメラ）へ向かって少しずつ接近させ、
// 一定距離まで近づくと「攻撃」としてコールバックを呼び、自身は消える。
// 討伐(ShootingTarget.Hit)されるとCollider.enabledがfalseになるので、それを監視して
// 移動を止めて消滅させる（ShootingTarget/PhoneGunManagerには一切手を加えない）。
[RequireComponent(typeof(ShootingTarget))]
public class YokaiMover : MonoBehaviour
{
    public Transform target;
    public float moveSpeed = 1.3f;
    public float attackDistance = 4f;
    public float destroyDelayAfterHit = 0.6f;
    [Tooltip("モデルの正面が進行方向とズレている場合の補正角度（X軸回転）")]
    public float pitchOffsetDegrees = 0f;
    [Tooltip("被弾したら消滅させるか。多段HPのボスなど、被弾しても消えてほしくない場合はfalseにする")]
    public bool destroyOnHit = true;
    [Tooltip("倒すのに必要な被弾回数。2以上にする場合、呼び出し元でShootingTarget.respawnsをtrueにしておく必要がある")]
    public int maxHP = 1;
    [Tooltip("到達（攻撃）したら消滅させるか。falseの場合はその場に留まり続ける（ボス向け）")]
    public bool destroyOnReachTarget = true;
    [Tooltip("撃破された瞬間（destroyOnHitでの消滅時）に出すエフェクト（任意）")]
    public GameObject defeatEffectPrefab;
    [Tooltip("撃破エフェクトを消すまでの秒数")]
    public float defeatEffectLifetime = 1.5f;
    [Tooltip("命中音・撃破音を鳴らすBGMマネージャー（同じ場所から2D再生する）")]
    public CoopBgmManager bgmManager;
    [Tooltip("命中（撃破）した時に鳴らす効果音（任意）")]
    public AudioClip hitSound;
    [Range(0f, 1f)]
    public float hitVolume = 1f;

    public System.Action onReachedTarget;
    [Tooltip("被弾により撃破された瞬間に呼ばれる（全員スコア加算などに使う）")]
    public System.Action onDefeated;

    private Collider col;
    private bool finished;
    private bool reachedTarget;
    private int currentHP;
    private bool wasColliderEnabled = true;

    void Awake()
    {
        col = GetComponent<Collider>();
    }

    void Start()
    {
        // YokaiSpawnerはInstantiate直後(Awakeが即座に走った後)にmaxHPを設定するため、
        // Awakeで初期化すると常にデフォルト値(1)のまま固定されてしまう。Startまで遅らせる
        currentHP = Mathf.Max(1, maxHP);
    }

    void Update()
    {
        if (finished) return;

        if (destroyOnHit && col != null)
        {
            bool isColliderEnabled = col.enabled;
            if (wasColliderEnabled && !isColliderEnabled)
            {
                currentHP--;
                if (currentHP <= 0)
                {
                    finished = true;
                    if (bgmManager != null) bgmManager.PlaySfx(hitSound, hitVolume);
                    if (defeatEffectPrefab != null)
                    {
                        GameObject effect = Instantiate(defeatEffectPrefab, transform.position, Quaternion.identity);
                        Destroy(effect, defeatEffectLifetime);
                    }
                    onDefeated?.Invoke();
                    Destroy(gameObject, destroyDelayAfterHit);
                    wasColliderEnabled = isColliderEnabled;
                    return;
                }
                // まだ倒れていない。ShootingTarget側の自動再出現(respawns=true)で
                // 少し後にColliderが戻るので、そのまま移動・攻撃判定を継続する
            }
            wasColliderEnabled = isColliderEnabled;
        }

        if (target == null || reachedTarget) return;

        Vector3 toTarget = target.position - transform.position;
        float dist = toTarget.magnitude;

        if (dist <= attackDistance)
        {
            reachedTarget = true;
            onReachedTarget?.Invoke();
            if (destroyOnReachTarget)
            {
                finished = true;
                Destroy(gameObject);
            }
            return;
        }

        transform.position += toTarget.normalized * moveSpeed * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(toTarget.normalized) * Quaternion.Euler(pitchOffsetDegrees, 0f, 0f);
    }
}
