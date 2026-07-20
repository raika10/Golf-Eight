using UnityEngine;

/// プレイヤーのボイス再生。Player プレハブのルート（RagdollController と同じオブジェクト）に付ける。
///
/// 設計方針：既存スクリプトには一切手を入れない（他の人の作業とコンフリクトさせないため）。
/// 再生のきっかけは RagdollController.IsDown の「立ち上がり」を自分で監視して取る。
/// IsDown が false→true になる ＝ 吹っ飛ばされて ragdoll 化した瞬間＝「打たれた時」。
///
/// ネットワーク：KnockbackReceiver.ApplyKnockback は全端末で実行される（PlayerNetworkSync が
/// ObserversRpc で配信している）ので、IsDown も全端末で立つ。つまりこのスクリプトは
/// 追加のRPCなしで全員の耳に届く。AudioListener は所有者のカメラだけが有効なので、
/// 3Dサウンドとして「自分から見た距離・方向」で正しく鳴る。
[RequireComponent(typeof(RagdollController))]
public class PlayerVoice : MonoBehaviour
{
    [Header("ボイス")]
    [Tooltip("他プレイヤーに打たれて吹っ飛ばされた瞬間に鳴らす")]
    [SerializeField] private AudioClip hitVoice;

    [Tooltip("場外へ落ちた（fallVoiceY を下回った）瞬間に鳴らす")]
    [SerializeField] private AudioClip fallVoice;

    [Tooltip("スイングがボール／相手プレイヤーに当たった瞬間に鳴らす（かきーん）")]
    [SerializeField] private AudioClip swingHitVoice;

    [Header("場外判定")]
    [Tooltip("このYを下回ったら場外ボイスを鳴らす。KnockbackReceiver の minY（リスポーン）より上にしておくと、落下中に鳴ってから戻される")]
    [SerializeField] private float fallVoiceY = -10f;
    [Tooltip("鳴らした後、このYより上に戻ったら再び鳴らせるようにする（境界を行き来した時の連続再生を防ぐ）")]
    [SerializeField] private float fallVoiceRearmY = -8f;

    [Header("音の設定")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [Tooltip("1 = 完全な3D（距離と方向で聞こえ方が変わる）、0 = 2D（どこにいても同じ）")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
    [Tooltip("この距離までは減衰しない (m)")]
    [SerializeField] private float minDistance = 3f;
    [Tooltip("この距離で聞こえなくなる (m)")]
    [SerializeField] private float maxDistance = 40f;
    [Tooltip("連続で鳴らさない最小間隔 (s)。多重再生で音が割れるのを防ぐ")]
    [SerializeField] private float minInterval = 0.2f;

    private RagdollController ragdoll;
    private AudioSource source;
    private bool wasDown;      // 前フレームの IsDown（立ち上がり検出用）
    private bool fallVoiceArmed = true; // 場外ボイスを鳴らせる状態か（鳴らしたら false、上に戻ったら true）
    private float lastPlayTime = -999f;

    private void Awake()
    {
        ragdoll = GetComponent<RagdollController>();

        // AudioSource は自前で用意する（プレハブに手で足すコンポーネントを1つに減らすため）
        source = GetComponent<AudioSource>();
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }
        source.playOnAwake = false;
        source.loop = false;
        source.volume = volume;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;

        wasDown = ragdoll.IsDown;
    }

    private void Update()
    {
        bool isDown = ragdoll.IsDown;
        if (isDown && !wasDown)
        {
            PlayHitVoice(); // false→true の瞬間だけ＝打たれた瞬間だけ鳴らす
        }
        wasDown = isDown;

        CheckFall();
    }

    /// 場外（fallVoiceY より下）へ落ちたら1回だけ鳴らす。
    /// 高さの取り方は KnockbackReceiver の場外チェックに合わせる：ragdoll 中は体（腰）が本体から
    /// 離れて落ちていくので、腰の位置を見ないと落下を検出できない。
    private void CheckFall()
    {
        Vector3 pos = (ragdoll.IsDown && ragdoll.HipsBone != null) ? ragdoll.HipsBone.position : transform.position;

        if (fallVoiceArmed)
        {
            if (pos.y < fallVoiceY)
            {
                Play(fallVoice);
                fallVoiceArmed = false; // 落ちている間に鳴り続けないよう、ここで止める
            }
        }
        else if (pos.y > fallVoiceRearmY)
        {
            fallVoiceArmed = true; // リスポーンなどで上に戻った＝次の場外でまた鳴らせる
        }
    }

    /// 「打たれた時」のボイスを鳴らす。外部（演出やテスト）からも呼べる。
    public void PlayHitVoice()
    {
        Play(hitVoice);
    }

    /// 「場外」のボイスを鳴らす。外部（演出やテスト）からも呼べる。
    public void PlayFallVoice()
    {
        Play(fallVoice);
    }

    /// 「かきーん」（スイングが当たった音）を鳴らす。
    /// 打った本人のオブジェクトから鳴る＝クラブの位置で鳴るので、これでよい。
    /// 全端末で鳴らすのは PlayerNetworkSync.RequestPlaySwingHitVoice が担当する。
    public void PlaySwingHitVoice()
    {
        Play(swingHitVoice);
    }

    private void Play(AudioClip clip)
    {
        if (clip == null || source == null)
        {
            return;
        }
        if (Time.time - lastPlayTime < minInterval)
        {
            return; // 直前に鳴らしたばかり＝多重再生しない
        }
        lastPlayTime = Time.time;
        source.PlayOneShot(clip, volume);
    }
}
