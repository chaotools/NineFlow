using System.Text;

namespace NineFlow.Core;

/// <summary>INI 文件中的一个节。</summary>
public sealed class IniSection
{
    private readonly List<string> _order = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    internal IniSection(string name) => Name = name;

    public string Name { get; }

    public IReadOnlyList<string> Keys => _order;

    public string? Get(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    public bool Has(string key) => _values.ContainsKey(key);

    public void Set(string key, string value)
    {
        if (!_values.ContainsKey(key)) _order.Add(key);
        _values[key] = value;
    }

    internal IEnumerable<KeyValuePair<string, string>> Pairs()
    {
        foreach (string key in _order)
        {
            yield return new KeyValuePair<string, string>(key, _values[key]);
        }
    }
}

/// <summary>简单、稳健的 INI 读写实现（保留未知的节和键）。</summary>
public sealed class IniDocument
{
    private readonly List<IniSection> _sections = new();
    private readonly Dictionary<string, IniSection> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IniSection> Sections => _sections;

    public string? Get(string section, string key) =>
        _byName.TryGetValue(section, out IniSection? found) ? found.Get(key) : null;

    public void Set(string section, string key, string value) => GetOrAddSection(section).Set(key, value);

    public IniSection GetOrAddSection(string name)
    {
        if (_byName.TryGetValue(name, out IniSection? existing)) return existing;

        var section = new IniSection(name);
        _sections.Add(section);
        _byName[name] = section;
        return section;
    }

    public static IniDocument Load(string path) => Parse(ReadAllText(path));

    public static IniDocument Parse(string text)
    {
        var document = new IniDocument();
        IniSection? current = null;

        foreach (string rawLine in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line[0] == ';' || line[0] == '#') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                string name = line[1..^1].Trim();
                if (name.Length == 0) continue;
                current = document.GetOrAddSection(name);
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0) continue;

            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (key.Length == 0) continue;

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            current ??= document.GetOrAddSection("General");
            current.Set(key, value);
        }

        return document;
    }

    public string Serialize()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < _sections.Count; i++)
        {
            if (i > 0) builder.AppendLine();
            builder.Append('[').Append(_sections[i].Name).Append(']').AppendLine();
            foreach (var pair in _sections[i].Pairs())
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
            }
        }

        return builder.ToString();
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>读取文本，自动识别 UTF-8 / UTF-16 的 BOM；无 BOM 时优先按 UTF-8，失败退回 Latin-1。</summary>
    public static string ReadAllText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
