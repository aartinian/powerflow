namespace PowerFlow.Tests;

internal static class TestData
{
    public static string Path(string fileName) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Data", fileName);
}
