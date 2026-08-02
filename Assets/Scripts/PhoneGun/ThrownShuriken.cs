using System;
using UnityEngine;

// PhoneGunManagerが発砲時に生成する、実際に飛んでいく手裏剣。
// 物理挙動には頼らず、毎フレームSphereCastで前フレーム位置からの区間をチェックすることで
// 高速移動時のすり抜け（トンネリング）を防ぎつつ、見た目と命中判定のタイミングを一致させる。
public class ThrownShuriken : MonoBehaviour
{
    private Vector3 direction = Vector3.forward;
    private float speed = 40f;
    private float maxDistance = 200f;
    private float hitRadius = 0.5f;
    private LayerMask targetLayer = ~0;
    private float spinDegreesPerSecond = 720f;
    private Action<RaycastHit> onHitTarget;

    private float traveled;
    private bool initialized;

    public void Init(Vector3 dir, float initSpeed, float initMaxDistance, float initHitRadius,
        LayerMask layer, float initSpinDegreesPerSecond, Action<RaycastHit> onHit)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        speed = initSpeed;
        maxDistance = initMaxDistance;
        hitRadius = initHitRadius;
        targetLayer = layer;
        spinDegreesPerSecond = initSpinDegreesPerSecond;
        onHitTarget = onHit;
        initialized = true;

        transform.rotation = Quaternion.LookRotation(direction);

        // 自分自身のColliderがSphereCastに引っかかって発射直後に自爆するのを防ぐ
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }
    }

    void Update()
    {
        if (!initialized) return;

        float step = speed * Time.deltaTime;
        Vector3 previousPos = transform.position;

        if (Physics.SphereCast(previousPos, hitRadius, direction, out RaycastHit hit, step, targetLayer))
        {
            transform.position = hit.point;
            onHitTarget?.Invoke(hit);
            Destroy(gameObject);
            return;
        }

        transform.position = previousPos + direction * step;
        transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
        traveled += step;

        if (traveled >= maxDistance)
        {
            Destroy(gameObject);
        }
    }
}
