using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 妖退治（1人プレイ）用。PhoneGunServer/PhoneGunManagerは元々複数人の同時接続を
// 前提とした無改造の共有スクリプトのため、これらには一切手を加えず、外部から公開API
// （ConnectedPlayerIds / RemovePlayer）とリフレクションで「2人目以降を即座に退出させる」
// ことで、後から来た人だけを弾いて1人プレイ化する。
public class SinglePlayerGate : MonoBehaviour
{
    public PhoneGunServer server;
    public PhoneGunManager manager;

    private string activePlayerId;
    private FieldInfo rigsField;
    private FieldInfo playerOrderField;
    private FieldInfo reticleField;

    void Awake()
    {
        var managerType = typeof(PhoneGunManager);
        rigsField = managerType.GetField("rigs", BindingFlags.Instance | BindingFlags.NonPublic);
        playerOrderField = managerType.GetField("playerOrder", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    void Update()
    {
        if (server == null) return;

        var ids = server.ConnectedPlayerIds?.ToList();
        if (ids == null || ids.Count == 0)
        {
            activePlayerId = null;
            return;
        }

        if (activePlayerId == null || !ids.Contains(activePlayerId))
        {
            activePlayerId = ids[0];
        }

        foreach (var id in ids)
        {
            if (id != activePlayerId)
            {
                RemoveRig(id);
                server.RemovePlayer(id);
            }
        }
    }

    // PhoneGunManagerが対象プレイヤー用に生成したレティクル(rig)を、既存コードを一切変更せず
    // リフレクション経由で片付ける（放置すると狙いが動かない残像レティクルが残ってしまうため）
    void RemoveRig(string id)
    {
        if (manager == null || rigsField == null) return;

        var rigs = rigsField.GetValue(manager) as IDictionary;
        if (rigs == null || !rigs.Contains(id)) return;

        var rig = rigs[id];
        if (reticleField == null && rig != null)
        {
            reticleField = rig.GetType().GetField("reticle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        var reticle = reticleField?.GetValue(rig) as RectTransform;
        if (reticle != null) Destroy(reticle.gameObject);

        rigs.Remove(id);

        var playerOrder = playerOrderField?.GetValue(manager) as IList;
        playerOrder?.Remove(id);
    }
}
