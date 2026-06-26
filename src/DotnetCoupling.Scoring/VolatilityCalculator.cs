namespace DotnetCoupling.Scoring;

/// <summary>
/// 変更されやすさ（docs/scoring.md S5）。repo 相対ではなく絶対スケールで正規化し、
/// CI ゲートの安定性を確保する（レビュー C3）。
/// </summary>
public static class VolatilityCalculator
{
    /// <summary>Git 履歴が無い等で不明な場合の値。risk 計算では 0 相当に扱う。</summary>
    public const double Unknown = double.NaN;

    /// <summary>既定の「飽和」変更回数。90 日窓でこの回数以上を 1.0 とみなす。</summary>
    public const int DefaultFullScale = 10;

    /// <summary>直近 90 日窓の変更回数を固定基準で正規化する。</summary>
    public static double FromChangeCount(int changeCountInWindow, int fullScale = DefaultFullScale)
    {
        if (changeCountInWindow <= 0) return 0.0;
        if (fullScale < 1) fullScale = 1;
        var v = Math.Log(1 + changeCountInWindow) / Math.Log(1 + fullScale);
        return Math.Min(1.0, v);
    }
}
