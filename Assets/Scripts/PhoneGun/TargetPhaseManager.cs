using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 的をフェーズ（場面）ごとに分けて順番に表示する。
// 各フェーズに表示する的はInspector上のリストで自由に選択できる。
public class TargetPhaseManager : MonoBehaviour
{
    [System.Serializable]
    public class Phase
    {
        public string name = "Phase";
        [Tooltip("このフェーズで表示する的")]
        public List<GameObject> targets = new List<GameObject>();
        [Tooltip("このフェーズを表示し続ける秒数。0以下にすると自動では次に進まない（NextPhase()を外部から呼ぶ運用向け）")]
        public float duration = 15f;
    }

    [Tooltip("フェーズの一覧。上から順番に表示される")]
    public List<Phase> phases = new List<Phase>();
    [Tooltip("最後のフェーズが終わったら最初のフェーズに戻ってループするか")]
    public bool loop = true;
    [Tooltip("開始時に自動的にフェーズ0から始める")]
    public bool playOnStart = true;

    public int CurrentPhaseIndex { get; private set; } = -1;

    private Coroutine phaseCoroutine;

    void Start()
    {
        HideAllPhases();
        if (playOnStart && phases.Count > 0)
        {
            StartPhase(0);
        }
    }

    void HideAllPhases()
    {
        foreach (var phase in phases)
        {
            foreach (var target in phase.targets)
            {
                if (target != null) target.SetActive(false);
            }
        }
    }

    // 指定したフェーズを表示する。前のフェーズの的は非表示になる
    public void StartPhase(int index)
    {
        if (phases.Count == 0) return;
        if (phaseCoroutine != null)
        {
            StopCoroutine(phaseCoroutine);
            phaseCoroutine = null;
        }

        if (CurrentPhaseIndex >= 0 && CurrentPhaseIndex < phases.Count)
        {
            foreach (var target in phases[CurrentPhaseIndex].targets)
            {
                if (target != null) target.SetActive(false);
            }
        }

        CurrentPhaseIndex = ((index % phases.Count) + phases.Count) % phases.Count;
        var phase = phases[CurrentPhaseIndex];
        foreach (var target in phase.targets)
        {
            if (target == null) continue;
            target.SetActive(true);
            // 前回このフェーズが表示されていた間に倒れたまま(isDown)だった的を、
            // 出現済み・命中可能な状態へ強制的に戻す。呼ばないと、非表示中に
            // 再出現コルーチンが止まるため次のループで永久に復活しなくなる。
            var shootingTarget = target.GetComponent<ShootingTarget>();
            if (shootingTarget != null) shootingTarget.ResetTarget();
        }

        if (phase.duration > 0f)
        {
            phaseCoroutine = StartCoroutine(AdvanceAfterDelay(phase.duration));
        }
    }

    IEnumerator AdvanceAfterDelay(float duration)
    {
        yield return new WaitForSeconds(duration);
        NextPhase();
    }

    // 次のフェーズへ進む。最後のフェーズならloop設定に従う
    public void NextPhase()
    {
        int nextIndex = CurrentPhaseIndex + 1;
        if (nextIndex >= phases.Count)
        {
            if (!loop) return;
            nextIndex = 0;
        }
        StartPhase(nextIndex);
    }
}
