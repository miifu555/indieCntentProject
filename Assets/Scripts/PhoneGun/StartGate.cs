using UnityEngine;

// ロビー画面に置く「封印」オブジェクト。
// 手裏剣が命中すると割れて、少し間を置いてからGameFlowControllerでゲームを開始する。
public class StartGate : MonoBehaviour
{
    [Tooltip("命中時に非表示にする見た目。未設定ならこのGameObject自身のRendererを非表示にする")]
    public GameObject visual;
    [Tooltip("命中してからゲーム開始演出が始まるまでの間（割れた演出を見せるための間）")]
    public float breakDelay = 0.5f;
    public GameFlowController flowController;

    private bool broken;

    public void Hit(Vector3 hitPoint)
    {
        if (broken) return;
        broken = true;

        // GameObject自体を非表示にするとInvokeで予約した処理も止まってしまうため、
        // 見た目(Renderer)とコリジョンだけを消してGameObjectとスクリプトは生かしておく
        if (visual != null)
        {
            visual.SetActive(false);
        }
        else
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
        }

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (flowController != null)
        {
            Invoke(nameof(StartGame), breakDelay);
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

        if (visual != null)
        {
            visual.SetActive(true);
        }
        else
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = true;
        }

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
    }
}
