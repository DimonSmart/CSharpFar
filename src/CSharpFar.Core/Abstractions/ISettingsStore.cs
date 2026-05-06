namespace CSharpFar.Core.Abstractions;

public interface ISettingsStore
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    void Save();
}
