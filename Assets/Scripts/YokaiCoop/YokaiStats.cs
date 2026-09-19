using UnityEngine;

// 妖のプレハブに直接付けておく「種類ごとの基本ステータス」。
// YokaiSpawnerが出現時にこれを読み取り、移動速度・HP・大きさ・出現時のフェードインへ反映する。
// プレハブ単位で完結するので、同じプレハブをどのフェーズで何回使っても同じ性能になる。
public class YokaiStats : MonoBehaviour
{
    [Tooltip("この種類の移動速度")]
    public float moveSpeed = 1.3f;
    [Tooltip("倒すのに必要な被弾回数。1なら1発で倒れる（従来通り）")]
    public int maxHP = 1;
    [Tooltip("被弾してから次に狙えるようになるまでの間隔（maxHPが2以上の時のみ使う）")]
    public float hitFlickerDelay = 0.25f;
    [Tooltip("プレハブ本来の大きさに対する倍率")]
    public float scale = 1f;
    [Tooltip("出現時にこの秒数かけてフェードインする。0以下にすると今まで通り即座に表示される")]
    public float fadeInDuration = 0.3f;
    [Tooltip("命中時に加算されるスコア。shareScoreWithAllがtrueの場合は「倒した時に全員へ加算される量」になる")]
    public int scoreValue = 20;
    [Tooltip("trueにすると、倒した時にscoreValueが参加中の全プレイヤーへ加算される（撃破した本人だけに入る通常の命中スコアは無効になる）。falseなら今まで通り撃破に貢献した本人だけに入る")]
    public bool shareScoreWithAll = false;

    [Header("ボス表示")]
    [Tooltip("trueにすると、出現している間、画面上部にこの妖のHPバーを表示する（ラストのボス用）")]
    public bool showHpBar = false;
    [Tooltip("HPバーに表示する名前")]
    public string bossName = "";

    [Header("演出")]
    [Tooltip("この妖専用の命中エフェクト。設定するとYokaiSpawner側の共通の命中エフェクトの代わりに使う（未設定なら共通のものを使う）")]
    public GameObject hitEffectPrefab;
}
