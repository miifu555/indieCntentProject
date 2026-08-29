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

    private Collider col;
    private bool finished;
    private bool reachedTarget;

    void Awake()
    {
        col = GetComponent<Collider>();
    }

    void Update()
    {
        if (finished) return;

        if (destroyOnHit && col != null && !col.enabled)
        {
            finished = true;
            if (bgmManager != null) bgmManager.PlaySfx(hitSound, hitVolume);
            if (defeatEffectPrefab != null)
            {
                GameObject effect = Instantiate(defeatEffectPrefab, transform.position, Quaternion.identity);
                Destroy(effect, defeatEffectLifetime);
            }
            Destroy(gameObject, destroyDelayAfterHit);
            return;
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
