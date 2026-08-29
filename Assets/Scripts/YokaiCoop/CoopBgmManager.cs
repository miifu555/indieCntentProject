using UnityEngine;

// 妖退治（1人プレイ）用のBGM専用マネージャー。ロビー・プレイ中・結果発表それぞれの
// BGMを保持し、切り替えを行う。CoopGameFlowControllerから呼び出される。
public class CoopBgmManager : MonoBehaviour
{
    [Tooltip("BGMを鳴らすAudioSource。未設定なら自動でこのGameObjectに追加する")]
    public AudioSource bgmSource;
    [Tooltip("ロビー中に流すBGM")]
    public AudioClip lobbyBgm;
    [Tooltip("ラウンド中（妖・ボス出現中）に流すBGM")]
    public AudioClip gameplayBgm;
    [Tooltip("結果発表中（生存成功）に流すBGM（ループしない）")]
    public AudioClip resultWinBgm;
    [Tooltip("結果発表中（全滅）に流すBGM（ループしない）")]
    public AudioClip resultLoseBgm;
    [Range(0f, 1f)]
    public float bgmVolume = 0.1f;

    [Tooltip("ヒット音・撃破音・封印破壊音などのSEを鳴らすAudioSource。未設定なら自動でこのGameObjectに追加する（BGMとは別のAudioSourceだが同じ場所＝非3D・距離減衰なしで鳴らすため）")]
    public AudioSource sfxSource;

    void Awake()
    {
        if (bgmSource == null) bgmSource = gameObject.AddComponent<AudioSource>();
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f; // 2D再生。距離による減衰を受けず必ず聞こえるようにする
    }

    public void PlayLobbyBgm() => PlayBgm(lobbyBgm, true);
    public void PlayGameplayBgm() => PlayBgm(gameplayBgm, true);
    public void PlayResultBgm(bool cleared) => PlayBgm(cleared ? resultWinBgm : resultLoseBgm, false);

    public void PlayBgm(AudioClip clip, bool loop)
    {
        if (bgmSource == null || clip == null) return;
        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.Stop();
        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    // ヒット音・撃破音などのSEをBGMと同じ場所（このGameObject）から単発で鳴らす
    public void PlaySfx(AudioClip clip, float volume)
    {
        if (sfxSource == null || clip == null) return;
        sfxSource.PlayOneShot(clip, volume);
    }

    public void Stop()
    {
        if (bgmSource != null) bgmSource.Stop();
    }
}
