namespace Automated_FTP.Services.Processors;

public class FileProcessorRegistry
{
    private readonly Dictionary<string, IFileProcessor> _map;

    public FileProcessorRegistry(IEnumerable<IFileProcessor> processors)
    {
        _map = processors.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public IFileProcessor Resolve(string name)
    {
        if (_map.TryGetValue(name, out var p)) return p;
        throw new InvalidOperationException(
            $"未找到名为 '{name}' 的文件处理方法。已注册：[{string.Join(", ", _map.Keys)}]");
    }

    public IReadOnlyCollection<string> RegisteredNames => _map.Keys;
}
