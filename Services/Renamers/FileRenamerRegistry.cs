namespace Automated_FTP.Services.Renamers;

public class FileRenamerRegistry
{
    private readonly Dictionary<string, IFileRenamer> _map;

    public FileRenamerRegistry(IEnumerable<IFileRenamer> renamers)
    {
        _map = renamers.ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
    }

    public IFileRenamer Resolve(string name)
    {
        if (_map.TryGetValue(name, out var r)) return r;
        throw new InvalidOperationException(
            $"未找到名为 '{name}' 的改名规则。已注册：[{string.Join(", ", _map.Keys)}]");
    }

    public IReadOnlyCollection<string> RegisteredNames => _map.Keys;
}
