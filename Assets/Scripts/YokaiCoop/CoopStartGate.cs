using UnityEngine;

// 妖退治（協力プレイ）ロビーに置く「封印」オブジェクト。
// PhoneGunManager（既存・無改造）の命中判定は既存のShootingTarget/StartGateしか
// 認識しないため、封印にもShootingTarget（既存・無改造）を付与し、その被弾状態
// （命中でColliderが無効化される）を監視することで「壊れた」を検知する。
[RequireComponent(typeof(ShootingTarget))]
public class CoopStartGate : MonoBehaviour
{
    [Tooltip("命中してからゲーム開始演出が始まるまでの間")]
    public float breakDelay = 0.5f;
    public CoopGameFlowController flowController;

    private ShootingTarget shootingTarget;
    private Collider col;
    private bool broken;

    void Awake()
    {
        shootingTarget = GetComponent<ShootingTarget>();
        col = GetComponent<Collider>();
        // 封印はダメージ(討伐ゲージ)の対象ではないので0点にし、
        // 壊れたらResetGate()を呼ぶまで自動では戻らないようにする
        shootingTarget.scoreValue = 0;
        shootingTarget.respawns = false;
    }

    void Update()
    {
        if (broken) return;
        if (col != null && !col.enabled)
        {
            broken = true;
            if (flowController != null)
            {
                Invoke(nameof(StartGame), breakDelay);
            }
        }
    }

    void StartGame()
    {
        flowController.StartGame();
    }

    // 次の回のために封印を元の状態へ戻す
    public void ResetGate()
    {
        broken = false;
        if (shootingTarget != null) shootingTarget.ResetTarget();
    }
}
