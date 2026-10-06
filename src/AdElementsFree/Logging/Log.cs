namespace AdElementsFree.Logging;

public sealed class Log(string directory)
{
    private readonly object gate = new();
    public void Write(string message)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    // Only exception type is logged; messages may contain URLs or private data.
    public void Error(string operation, Exception error) => Write($"{operation}: {error.GetType().Name}");
}
