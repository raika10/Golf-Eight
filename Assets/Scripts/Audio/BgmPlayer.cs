using UnityEngine;
using UnityEngine.SceneManagement;

/// GameScene の BGM。ループ再生する。
///
/// 設計方針：シーンにもプレハブにも一切手を入れない（他の人の作業とコンフリクトさせないため）。
/// [RuntimeInitializeOnLoadMethod] でゲーム起動時に自分で GameObject を作り、DontDestroyOnLoad で
/// 生き残る。クリップは Assets/Resources/BGM.mp3 を Resources.Load で読む。
/// シーンを跨いで存在し続けるが、鳴るのは TargetScene の間だけ（他のシーンでは止める）。
///
/// ネットワーク：BGM は各端末のローカル再生なので同期不要。NetworkBehaviour ではない。
public class BgmPlayer : MonoBehaviour
{
    /// この名前のシーンにいる間だけ鳴らす。
    private const string TargetScene = "GameScene";

    /// Assets/Resources/ からの相対パス（拡張子なし）。
    private const string ClipResourcePath = "BGM";

    /// BGM の音量。効果音より控えめにする（かきーん等をかき消さないため）。
    private const float DefaultVolume = 0.35f;

    /// シーン切り替えでフェードする秒数。0 なら即座に切り替わる。
    private const float FadeTime = 1.0f;

    private static BgmPlayer instance;

    private AudioSource source;
    private float targetVolume;

    /// 音量（0〜1）。設定画面などから変えられるように公開しておく。
    public static float Volume { get; set; } = DefaultVolume;

    /// ゲーム起動時（最初のシーンが読み込まれた後）に一度だけ呼ばれる。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return; // ドメインリロード無しの再生などで二重に作らない
        }
        GameObject go = new GameObject("BgmPlayer");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BgmPlayer>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        AudioClip clip = Resources.Load<AudioClip>(ClipResourcePath);
        if (clip == null)
        {
            Debug.LogWarning($"[BgmPlayer] Resources/{ClipResourcePath} が見つかりません。BGM は鳴りません。", this);
            enabled = false;
            return;
        }

        source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // BGM は 2D＝プレイヤーの位置や向きで聞こえ方が変わらない
        source.volume = 0f;       // フェードインさせるので 0 から始める
        targetVolume = 0f;

        SceneManager.sceneLoaded += HandleSceneLoaded;
        ApplyForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForScene(scene.name);
    }

    /// 対象シーンなら鳴らす、それ以外なら止める。
    private void ApplyForScene(string sceneName)
    {
        if (source == null)
        {
            return;
        }

        if (sceneName == TargetScene)
        {
            targetVolume = Volume;
            if (!source.isPlaying)
            {
                source.Play();
            }
        }
        else
        {
            targetVolume = 0f; // 音量が 0 になった時点で Update が止める
        }
    }

    private void Update()
    {
        if (source == null)
        {
            return;
        }

        // 音量を targetVolume へ寄せる（切り替わりでブツ切りにならないように）
        if (FadeTime > 0f)
        {
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, Time.unscaledDeltaTime / FadeTime);
        }
        else
        {
            source.volume = targetVolume;
        }

        // 完全に消えたら再生自体を止める（無音を鳴らし続けない）
        if (targetVolume <= 0f && source.volume <= 0f && source.isPlaying)
        {
            source.Stop();
        }
    }
}
