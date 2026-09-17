using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 主催者がマウスで操作する、参加中プレイヤーの手動切断メニュー。
// 通常は60秒の非アクティブタイムアウトで自動的に切断されるが、
// 稀にすんなり消えない場合があるため、手動ですぐに追い出せるようにする。
// CoopPlayerSlotManagerの8色スロットに合わせて8行を常に用意しておき、
// 埋まっている枠だけ表示する（行を動的生成する必要がないので構成がシンプルになる）。
public class CoopPlayerAdminPanel : MonoBehaviour
{
    [System.Serializable]
    public class Row
    {
        [Tooltip("この行全体（空き枠の時はこれごと非表示にする）")]
        public GameObject root;
        [Tooltip("色名・スコアを表示するテキスト")]
        public TMP_Text label;
        [Tooltip("押すとそのプレイヤーを切断するボタン")]
        public Button kickButton;
    }

    [Tooltip("切断に使う")]
    public PhoneGunServer server;
    [Tooltip("枠番号→プレイヤーIDの対応を取得するために使う")]
    public CoopPlayerSlotManager slotManager;
    [Tooltip("プレイヤー一覧パネル本体（開いている間だけ表示する）")]
    public GameObject panel;
    [Tooltip("パネルの開閉に使うボタン")]
    public Button toggleButton;
    [Tooltip("8色スロット分の行。slotManager.slotsと同じ順番で並べる")]
    public Row[] rows;

    void Awake()
    {
        if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);
        if (panel != null) panel.SetActive(false);
    }

    void TogglePanel()
    {
        if (panel != null) panel.SetActive(!panel.activeSelf);
    }

    void Update()
    {
        if (panel == null || !panel.activeSelf) return;
        if (server == null || slotManager == null || rows == null) return;

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row == null || row.root == null) continue;

            if (slotManager.TryGetPlayerIdForSlot(i, out string id) && server.TryGetPlayer(id, out var info))
            {
                row.root.SetActive(true);

                string colorName = (slotManager.slots != null && i < slotManager.slots.Length)
                    ? slotManager.slots[i].colorName
                    : ("枠" + (i + 1));
                if (row.label != null) row.label.text = colorName + "　" + info.score + "点";

                if (row.kickButton != null)
                {
                    row.kickButton.interactable = true;
                    row.kickButton.onClick.RemoveAllListeners();
                    string capturedId = id;
                    row.kickButton.onClick.AddListener(() => server.RemovePlayer(capturedId));
                }
            }
            else
            {
                row.root.SetActive(false);
            }
        }
    }
}
