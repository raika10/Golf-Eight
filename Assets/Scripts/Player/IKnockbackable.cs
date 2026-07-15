using UnityEngine;

/// 「方向＋威力」を受け取って吹っ飛ぶことができるもの。
/// 攻撃側（クラブ・ボール・武器・爆風など）は相手の実装を知らずに ApplyKnockback を呼ぶだけでよい。
/// 自爆型武器（反動が自分に来る武器）も、自分自身の IKnockbackable を呼べば同じロジックで吹っ飛べる。
public interface IKnockbackable
{
    /// direction の向きへ power の勢い (m/s) で吹っ飛ばす（direction は正規化されていなくてもよい）。
    /// ダウン中・着地後の無敵時間中は無視される。
    void ApplyKnockback(Vector3 direction, float power);

    /// いま吹っ飛ばされてダウン中か。
    bool IsKnockedDown { get; }
}
