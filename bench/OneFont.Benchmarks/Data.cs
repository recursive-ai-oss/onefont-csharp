using System.Text;

namespace OneFont.Benchmarks;

/// <summary>Texts and faces shared by the shaping benchmarks.</summary>
internal static class Data
{
    public static readonly FontFace Latin = Load("NotoSans-Subset-Regular.ttf");
    public static readonly FontFace Arabic = Load("NotoSansArabic-Subset-Regular.ttf");
    public static readonly FontFace Devanagari = Load("NotoSansDevanagari-Subset-Regular.ttf");

    private static readonly string[] LatinWords = ["the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "office", "affine", "waffle", "Table", "AVATAR", "typography", "of", "and", "ledger", "total"];
    private static readonly string[] ArabicWords = ["\u0628\u0633\u0645", "\u0627\u0644\u0644\u0647", "\u0627\u0644\u0631\u062D\u0645\u0646", "\u0643\u062A\u0627\u0628", "\u0645\u062F\u0631\u0633\u0629", "\u0628\u064A\u062A", "\u0644\u0644\u0646\u0627\u0633", "\u0639\u0644\u0649"];
    private static readonly string[] DevanagariWords = ["\u0915\u093F\u0924\u093E\u092C", "\u0915\u094D\u0937\u0924\u094D\u0930", "\u0930\u094D\u0915", "\u0938\u0902\u0938\u094D\u0915\u0943\u0924", "\u0915\u0940", "\u0915\u094B", "\u0924\u094D\u0930\u093F", "\u091C\u094D\u091E"];

    public static readonly string LatinParagraph = Paragraph(LatinWords, 120);
    public static readonly string ArabicParagraph = Paragraph(ArabicWords, 120);
    public static readonly string DevanagariParagraph = Paragraph(DevanagariWords, 120);

    /// <summary>A run that changes script every few words, for the fallback chain.</summary>
    public static readonly string Mixed = string.Join(' ', Enumerable.Range(0, 60).Select(i => (i % 3) switch { 0 => LatinWords[i % LatinWords.Length], 1 => ArabicWords[i % ArabicWords.Length], _ => DevanagariWords[i % DevanagariWords.Length] }));

    /// <summary>A few hundred distinct one-line table cells; the benchmark cycles through them.</summary>
    public static readonly string[] Cells = [.. Enumerable.Range(0, 256).Select(i => $"{LatinWords[i % LatinWords.Length]} {LatinWords[(i * 7) % LatinWords.Length]} {i * 37 % 1000}.{i % 100:00}")];

    public static string Paragraph(string[] words, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(words[((i * 5) + (i / 3)) % words.Length]);
        }

        return sb.ToString();
    }

    private static FontFace Load(string file) => FontFace.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", file)));
}
