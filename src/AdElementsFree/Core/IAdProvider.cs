namespace AdElementsFree.Core;

public interface IAdProvider : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    string Status { get; }
    bool Enabled { get; }
    event Action? Changed;
    Task SetEnabledAsync(bool enabled);
}
