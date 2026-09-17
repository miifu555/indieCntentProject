using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 妖退治（協力プレイ）用。参加者を「赤・青・黄・緑・紫・水・桃・白」の8色の枠に
// 空いている色から順番に割り当てる。枠がすべて埋まっていたら新規参加を拒否し、
// 割り当て済みのプレイヤーが抜けたらその枠を解放する（次の参加者に再利用される）。
//
// 色の割り当ては PhoneGunServer.ColorAssignerOverride フック経由で「/join」処理の
// 中で同期的に行われるため、スマホの背景色（/joinの応答で即決まる）とレティクル・
// スコアボードの色が常に一致する。このフックはPhoneGunServer.cs側にごく小さく
// 追加されたもので、未設定時は元の8色ローテーションのまま動くため、他シーン
// （ランキング用のShootingGallery等）の挙動には影響しない。
// PhoneGunManagerには一切手を加えず、リフレクション（レティクル・ラベルの上書き）で
// 表示を差し替えている。
public class CoopPlayerSlotManager : MonoBehaviour
{
    [System.Serializable]
    public class ColorSlot
    {
        public string colorName;
        public string colorHex;
    }

    public PhoneGunServer server;
    public PhoneGunManager manager;

    [Tooltip("赤から順に、空いている枠に割り当てる。要素数が最大参加人数になる")]
    public ColorSlot[] slots = new ColorSlot[]
    {
        new ColorSlot { colorName = "赤", colorHex = "#ff4757" },
        new ColorSlot { colorName = "青", colorHex = "#1e90ff" },
        new ColorSlot { colorName = "黄", colorHex = "#ffd32a" },
        new ColorSlot { colorName = "緑", colorHex = "#2ed573" },
        new ColorSlot { colorName = "紫", colorHex = "#a55eea" },
        new ColorSlot { colorName = "水", colorHex = "#00d2d3" },
        new ColorSlot { colorName = "桃", colorHex = "#ff6b81" },
        new ColorSlot { colorName = "白", colorHex = "#ffffff" },
    };

    // playerId -> slots配列のindex。/joinのバックグラウンドスレッドとUnityのメイン
    // スレッドの両方からアクセスされるため、assignLockで保護する
    private readonly Dictionary<string, int> assignments = new Dictionary<string, int>();
    // 参加時点で空き枠が無く、次のUpdate()で弾く必要があるプレイヤー
    private readonly HashSet<string> overCapacity = new HashSet<string>();
    private readonly object assignLock = new object();

    private FieldInfo rigsField;
    private FieldInfo playerOrderField;
    private FieldInfo reticleField;
    private FieldInfo reticleImageField;
    private FieldInfo labelField;
    private FieldInfo calibratedField;

    // 枠(色)が新しい持ち主に切り替わった際、前の持ち主のキャリブレーション(基準向き)が
    // 引き継がれないように、レティクルが初めて現れたタイミングで一度だけ強制的に
    // 未キャリブレーション状態に戻す。メインスレッドのみで触るのでlock不要
    private readonly HashSet<string> calibrationResetDone = new HashSet<string>();

    void Awake()
    {
        var managerType = typeof(PhoneGunManager);
        rigsField = managerType.GetField("rigs", BindingFlags.Instance | BindingFlags.NonPublic);
        playerOrderField = managerType.GetField("playerOrder", BindingFlags.Instance | BindingFlags.NonPublic);

        if (server != null)
        {
            server.ColorAssignerOverride = AssignColorForNewJoin;
        }
    }

    void OnDestroy()
    {
        if (server != null && server.ColorAssignerOverride == (System.Func<string, string>)AssignColorForNewJoin)
        {
            server.ColorAssignerOverride = null;
        }
    }

    // PhoneGunServerの/join処理(バックグラウンドスレッド)から同期的に呼ばれる。
    // ここで枠を確定させることで、応答に載る色とレティクルの色が食い違わないようにする
    string AssignColorForNewJoin(string id)
    {
        lock (assignLock)
        {
            int freeSlot = FindFreeSlotLocked();
            if (freeSlot < 0)
            {
                // 満枠。次のUpdate()で弾かれるまでの一瞬だけ見える色なので適当でよい
                overCapacity.Add(id);
                return slots[0].colorHex;
            }

            assignments[id] = freeSlot;
            return slots[freeSlot].colorHex;
        }
    }

    int FindFreeSlotLocked()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            bool used = false;
            foreach (var kv in assignments)
            {
                if (kv.Value == i) { used = true; break; }
            }
            if (!used) return i;
        }
        return -1;
    }

    void Update()
    {
        if (server == null) return;

        var connected = new HashSet<string>(server.ConnectedPlayerIds);
        List<string> toEvict = null;

        lock (assignLock)
        {
            // 切断されたプレイヤーの枠を解放する
            var toRemove = new List<string>();
            foreach (var kv in assignments)
            {
                if (!connected.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            foreach (var id in toRemove) assignments.Remove(id);
            overCapacity.RemoveWhere(id => !connected.Contains(id));
            foreach (var id in toRemove) calibrationResetDone.Remove(id);

            if (overCapacity.Count > 0)
            {
                toEvict = new List<string>(overCapacity);
                overCapacity.Clear();
            }
        }

        // 満枠で受け入れられなかったプレイヤーを弾く(Unity APIを呼ぶのでlockの外で行う)
        if (toEvict != null)
        {
            foreach (var id in toEvict)
            {
                RemoveRig(id);
                server.RemovePlayer(id);
            }
        }
    }

    void LateUpdate()
    {
        if (manager == null || rigsField == null) return;

        var rigs = rigsField.GetValue(manager) as System.Collections.IDictionary;
        if (rigs == null) return;

        Dictionary<string, int> assignmentsSnapshot;
        lock (assignLock)
        {
            assignmentsSnapshot = new Dictionary<string, int>(assignments);
        }

        // 満枠で弾いた直後は、PhoneGunManager側のUpdate()がまだそのプレイヤーの
        // join通知を処理しておらず、RemoveRig()の時点でrigが存在しないことがある。
        // その場合ここでPhoneGunManagerがrigを作ってしまうため、毎フレーム
        // 「枠に割り当てられていないrig」を掃除して残像が残らないようにする
        var orphanIds = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in rigs)
        {
            string id = entry.Key as string;
            if (id != null && !assignmentsSnapshot.ContainsKey(id)) orphanIds.Add(id);
        }
        foreach (var id in orphanIds) RemoveRig(id);

        foreach (var kv in assignmentsSnapshot)
        {
            string id = kv.Key;
            ColorSlot slot = slots[kv.Value];

            if (!rigs.Contains(id)) continue;
            var rig = rigs[id];
            if (rig == null) continue;

            if (reticleImageField == null) reticleImageField = rig.GetType().GetField("reticleImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (labelField == null) labelField = rig.GetType().GetField("label", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (calibratedField == null) calibratedField = rig.GetType().GetField("calibrated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // このレティクルを初めて見た＝枠の新しい持ち主。前の持ち主の基準向きを
            // 引き継がないよう、一度だけ強制的に再キャリブレーションさせる
            if (calibrationResetDone.Add(id))
            {
                calibratedField?.SetValue(rig, false);
            }

            bool hasColor = ColorUtility.TryParseHtmlString(slot.colorHex, out Color c);

            var reticleImage = reticleImageField?.GetValue(rig) as Image;
            if (reticleImage != null && hasColor)
            {
                reticleImage.color = c;
            }

            var label = labelField?.GetValue(rig) as TMP_Text;
            if (label != null)
            {
                label.text = slot.colorName;
                if (hasColor) label.color = c;
            }
        }

        UpdateScoreboard(assignmentsSnapshot);
    }

    // PhoneGunManager自身のUpdateScoreboard()（"P1"表記）を、色名表記で上書きする
    void UpdateScoreboard(Dictionary<string, int> assignmentsSnapshot)
    {
        if (manager.scoreboardText == null || server == null) return;
        manager.scoreboardText.text = BuildScoreText(assignmentsSnapshot);
    }

    // 枠番号(slots配列のindex)から、現在その枠を使っているプレイヤーIDを取得する。
    // 手動切断メニュー(CoopPlayerAdminPanel)など、他のスクリプトから参照する用
    public bool TryGetPlayerIdForSlot(int slotIndex, out string id)
    {
        lock (assignLock)
        {
            foreach (var kv in assignments)
            {
                if (kv.Value == slotIndex)
                {
                    id = kv.Key;
                    return true;
                }
            }
        }
        id = null;
        return false;
    }

    // 結果画面など、他のスクリプトから「色名: N点」を1人ずつ並べた文字列を取得するための公開API
    public string GetFormattedScoreList()
    {
        Dictionary<string, int> assignmentsSnapshot;
        lock (assignLock)
        {
            assignmentsSnapshot = new Dictionary<string, int>(assignments);
        }
        return BuildScoreText(assignmentsSnapshot);
    }

    string BuildScoreText(Dictionary<string, int> assignmentsSnapshot)
    {
        if (server == null) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var kv in assignmentsSnapshot)
        {
            if (server.TryGetPlayer(kv.Key, out var info))
            {
                ColorSlot slot = slots[kv.Value];
                sb.AppendLine($"<color={slot.colorHex}>{slot.colorName}: {info.score}点</color>");
            }
        }
        return sb.ToString();
    }

    // PhoneGunManagerが対象プレイヤー用に生成したレティクル(rig)を、既存コードを一切変更せず
    // リフレクション経由で片付ける（満枠で弾く時に、残像レティクルが残らないようにする）
    void RemoveRig(string id)
    {
        if (manager == null || rigsField == null) return;

        var rigs = rigsField.GetValue(manager) as System.Collections.IDictionary;
        if (rigs == null || !rigs.Contains(id)) return;

        var rig = rigs[id];
        if (reticleField == null && rig != null)
        {
            reticleField = rig.GetType().GetField("reticle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        var reticle = reticleField?.GetValue(rig) as RectTransform;
        if (reticle != null) Destroy(reticle.gameObject);

        rigs.Remove(id);

        var playerOrder = playerOrderField?.GetValue(manager) as System.Collections.IList;
        playerOrder?.Remove(id);
    }
}
