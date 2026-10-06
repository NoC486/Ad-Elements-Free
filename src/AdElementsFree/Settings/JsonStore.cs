using System.Text.Json;

namespace AdElementsFree.Settings;

public sealed class JsonStore<T> where T : new()
{
    private readonly string path;
    public JsonStore(string path) => this.path = path;
    public T Load()
    {
        if (!File.Exists(path)) return new T();
        // Fail closed: never overwrite a damaged shortcut ownership journal.
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path))
            ?? throw new InvalidDataException("设置文件为空或无效。");
    }
    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
}

public sealed class AppSettings
{
    public Dictionary<string, bool> Providers { get; set; } = new();
}
