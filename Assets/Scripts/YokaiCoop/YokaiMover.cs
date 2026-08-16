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
