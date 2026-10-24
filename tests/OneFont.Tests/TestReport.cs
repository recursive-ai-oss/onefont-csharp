namespace OneFont.Tests;

/// <summary>Appends measured numbers to the file named by ONEFONT_TEST_REPORT, when set, so a passing run can still show them.</summary>
internal static class TestReport
{
    private static readonly object Gate = new();

    public static void Line(string text)
    {
        if (Environment.GetEnvironmentVariable("ONEFONT_TEST_REPORT") is { Length: > 0 } path)
        {
            lock (Gate)
            {
                File.AppendAllText(path, text + "\n");
            }
        }
    }
}
