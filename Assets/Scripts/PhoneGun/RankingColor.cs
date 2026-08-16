// ランキング表示（結果発表・歴代ハイスコア）で共通して使う、1〜3位のメダルカラー。
public static class RankingColor
{
    public const string Gold = "#FFD700";
    public const string Silver = "#C0C0C0";
    public const string Bronze = "#CD7F32";

    public static string GetColor(int rank)
    {
        switch (rank)
        {
            case 1: return Gold;
            case 2: return Silver;
            case 3: return Bronze;
            default: return null;
        }
    }

    public static string FormatLine(int rank, string label, int score)
    {
        string line = $"{rank}位　{label}　{score}点";
        string color = GetColor(rank);
        return color != null ? $"<color={color}>{line}</color>" : line;
    }
}
