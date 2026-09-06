using UnityEngine;

// 妖のプレハブに付けて、見た目のモデル(子オブジェクト)だけを上下左右にずらしたり、
// 向きを補正したりする。当たり判定(Collider)やYokaiMoverの移動・出現位置・
// 進行方向への回転の基準はこのGameObject自身のTransformのまま変わらないので、
// モデル本来のピボットが体の中心などにあり地面に浮いて見える/埋まって見える時や、
// モデルの正面・上向きが他の妖とズレている時に、この値だけ調整すれば直せる。
public class YokaiPivotOffset : MonoBehaviour
{
    [Tooltip("見た目のモデルを入れている子オブジェクト")]
    public Transform model;
    [Tooltip("モデルをこの分だけずらす（Yをマイナスにすると下がる）")]
    public Vector3 offset = Vector3.zero;
    [Tooltip("モデルの向きをこの角度だけ補正する（他の妖と向きが揃っていない時に使う）")]
    public Vector3 rotationOffset = Vector3.zero;

    void Awake()
    {
        Apply();
    }

    void OnValidate()
    {
        Apply();
    }

    void Apply()
    {
        if (model == null) return;
        model.localPosition = offset;
        model.localRotation = Quaternion.Euler(rotationOffset);
    }
}
