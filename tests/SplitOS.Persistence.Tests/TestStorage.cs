using Microsoft.Data.Sqlite;

namespace SplitOS.Persistence.Tests;

internal sealed class TestStorage : IDisposable
{
    public TestStorage()
    {
        Root = Path.Combine(Path.GetTempPath(), "SplitOS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string PathFor(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
