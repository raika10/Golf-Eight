using UnityEngine;

namespace GolfEight.Network
{
    /// TitleScene と GameScene が PlayerPrefs 経由で受け渡す値の共有定義。
    /// キー名・型・デフォルト値をここ一箇所に集約し、保存側（Title）と読込側（GameScene）で
    /// 綴りや初期値がずれないようにする。キーの意味は設計ドキュメントに準拠：
    ///   IP_address … ゲストが入力するホストのIP（string / 既定 "localhost"）
    ///   Name       … 自分のプレイヤー名（string / 既定 "guest"）
    ///   Port       … 接続ポート（int / 既定 7770）
    ///   GameState  … 状態（int / 0=通常, -1=接続失敗。Title のエラー表示に使う）
    ///   IsHost     … 役割（int / 1=ホスト, 0=ゲスト）
    public static class TitlePrefs
    {
        // --- キー名（Title 側の保存と GameScene 側の読込で必ず一致させる）---
        public const string IpAddressKey = "IP_address";
        public const string NameKey = "Name";
        public const string PortKey = "Port";
        public const string GameStateKey = "GameState";
        public const string IsHostKey = "IsHost";

        // --- デフォルト値（Title 側が未保存でも GameScene が破綻しないための保険）---
        public const string DefaultIpAddress = "localhost";
        public const string DefaultName = "guest";
        public const int DefaultPort = 7770;

        // GameState の取り決め：0=通常 / -1=接続失敗（Title 側が読んでエラー表示に使う）。
        public const int GameStateNormal = 0;
        public const int GameStateConnectionFailed = -1;

        /// ゲストが入力したホストのIP（未設定なら localhost）。
        public static string GetIpAddress()
        {
            return PlayerPrefs.GetString(IpAddressKey, DefaultIpAddress);
        }

        /// 自分のプレイヤー名（未設定なら guest）。空白のみは既定名に丸める。
        public static string GetPlayerName()
        {
            string name = PlayerPrefs.GetString(NameKey, DefaultName);
            return string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        }

        /// 接続ポート。Tugboat は ushort を要求するので、範囲外の値は既定値に丸める。
        public static ushort GetPort()
        {
            int port = PlayerPrefs.GetInt(PortKey, DefaultPort);
            if (port < 1 || port > 65535)
            {
                return DefaultPort;
            }
            return (ushort)port;
        }

        /// この端末がホストか（1=ホスト / 0=ゲスト、未設定はゲスト扱い）。
        public static bool IsHost()
        {
            return PlayerPrefs.GetInt(IsHostKey, 0) == 1;
        }

        /// 接続状態を書き込む（Title 側が読んでエラー表示を出し分ける）。即座に永続化する。
        public static void SetGameState(int state)
        {
            PlayerPrefs.SetInt(GameStateKey, state);
            PlayerPrefs.Save();
        }

        // --- 保存側（Title が入力値を書き込むときに使う。書き込み後は Save() を呼ぶ）---

        /// ゲストが入力したホストのIP を保存する。
        public static void SetIpAddress(string ipAddress)
        {
            PlayerPrefs.SetString(IpAddressKey, string.IsNullOrWhiteSpace(ipAddress) ? DefaultIpAddress : ipAddress.Trim());
        }

        /// 自分のプレイヤー名を保存する。空白のみは既定名に丸める。
        public static void SetPlayerName(string name)
        {
            PlayerPrefs.SetString(NameKey, string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim());
        }

        /// 接続ポートを保存する。範囲外は既定値に丸める。
        public static void SetPort(int port)
        {
            PlayerPrefs.SetInt(PortKey, (port < 1 || port > 65535) ? DefaultPort : port);
        }

        /// 役割（ホスト/ゲスト）を保存する。
        public static void SetIsHost(bool isHost)
        {
            PlayerPrefs.SetInt(IsHostKey, isHost ? 1 : 0);
        }

        /// 上記セッターでの変更をディスクへ永続化する。
        public static void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
