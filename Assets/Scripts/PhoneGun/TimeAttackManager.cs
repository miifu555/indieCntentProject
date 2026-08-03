using System.Collections.Generic;
using TMPro;
using UnityEngine;

// フェーズごとに出題する的を切り替えるタイムアタックモード。
// インスペクタでフェーズ(制限時間 + 出現させる的のリスト)をいくつでも登録でき、
// 制限時間が経過すると次のフェーズへ、最後のフェーズが終わると最初のフェーズへループする。
public class TimeAttackManager : MonoBehaviour
{
    [System.Serializable]
    public class Phase
    {
        public string phaseName = "フェーズ";
        [Tooltip("このフェーズが続く時間(秒)")]
        public float duration = 15f;
        [Tooltip("このフェーズ中に出現させる的")]
        public List<ShootingTarget> targets = new List<ShootingTarget>();
    }

    [Header("フェーズ設定")]
    public List<Phase> phases = new List<Phase>();

    [Header("表示(任意)")]
    public TMP_Text phaseLabelText;
    public TMP_Text timerText;

    public int CurrentPhaseIndex { get; private set; } = -1;
    public float PhaseTimeRemaining { get; private set; }

    // フェーズ間で重複なく的を管理するため、登録された全ての的を先に集めておく
    private readonly List<ShootingTarget> allTargets = new List<ShootingTarget>();

    void Start()
    {
        CollectAllTargets();
        if (phases.Count > 0)
        {
            GoToPhase(0);
        }
    }

    void Update()
    {
        if (phases.Count == 0) return;

        PhaseTimeRemaining -= Time.deltaTime;
        UpdateTimerText();

        if (PhaseTimeRemaining <= 0f)
        {
            int next = (CurrentPhaseIndex + 1) % phases.Count;
            GoToPhase(next);
        }
    }

    void CollectAllTargets()
    {
        allTargets.Clear();
        foreach (var phase in phases)
        {
            foreach (var target in phase.targets)
            {
                if (target != null && !allTargets.Contains(target))
                {
                    allTargets.Add(target);
                }
            }
        }
    }

    void GoToPhase(int index)
    {
        if (phases.Count == 0) return;

        CurrentPhaseIndex = index;
        Phase phase = phases[CurrentPhaseIndex];
        PhaseTimeRemaining = phase.duration;

        // 一旦すべて隠してから、このフェーズの的だけ出現させ直す
        foreach (var target in allTargets)
        {
            if (target != null) target.gameObject.SetActive(false);
        }
        foreach (var target in phase.targets)
        {
            if (target == null) continue;
            target.gameObject.SetActive(true);
            target.ResetTarget();
        }

        if (phaseLabelText != null)
        {
            phaseLabelText.text = string.IsNullOrEmpty(phase.phaseName)
                ? $"フェーズ {CurrentPhaseIndex + 1}/{phases.Count}"
                : phase.phaseName;
        }
        UpdateTimerText();
    }

    void UpdateTimerText()
    {
        if (timerText == null) return;
        timerText.text = Mathf.CeilToInt(Mathf.Max(0f, PhaseTimeRemaining)) + "秒";
    }
}
