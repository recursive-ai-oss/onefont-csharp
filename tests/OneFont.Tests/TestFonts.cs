using OneFont.Fonts;

namespace OneFont.Tests;

/// <summary>The committed test fonts.</summary>
internal static class TestFonts
{
    public const string Regular = "NotoSans-Subset-Regular.ttf";
    public const string BoldKern = "NotoSans-Subset-Bold-kern.ttf";
    public const string Collection = "NotoSans-Subset.ttc";
    public const string Cff = "NotoSansCJK-Subset.otf";
    public const string Shaping = "NotoSans-Shaping-Subset.ttf";

    public static string PathOf(string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "testdata", "fonts", file);

    public static byte[] Bytes(string file) => File.ReadAllBytes(PathOf(file));

    /// <summary>The internal face data, for the engine tests.</summary>
    public static FontData Face(string file, int index = 0) => FontFile.Parse(Bytes(file))[index];

    /// <summary>The public face.</summary>
    public static FontFace Public(string file, int index = 0) => FontFace.Load(Bytes(file), index);

    public static FontFace Noto => Public(Regular);

    public static FontFace NotoCjk => Public(Cff);
}
