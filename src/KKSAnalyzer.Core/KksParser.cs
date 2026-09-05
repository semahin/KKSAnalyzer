using System.Text;

namespace KKSAnalyzer.Core;

public static class KksParser
{
    public static KksDocument Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var signals = new List<KksSignal>();
        var section = KksSection.Unknown;
        var hasSystemLines = lines.Any(line => line.TrimStart().StartsWith('#'));

        for (var index = 0; index < lines.Length; index++)
        {
            var value = lines[index].Trim();
            if (TryParseSectionHeader(value, out var parsedSection))
            {
                section = parsedSection;
                continue;
            }

            if (value.StartsWith('#'))
            {
                section = KksSection.Unknown;
                continue;
            }

            if ((hasSystemLines && section == KksSection.Unknown) || string.IsNullOrWhiteSpace(value))
                continue;

            var separator = value.LastIndexOf('_');
            var hasSuffix = separator > 0 && separator < value.Length - 1;
            signals.Add(new KksSignal(
                value,
                hasSuffix ? value[..separator] : value,
                hasSuffix ? value[(separator + 1)..] : null,
                section,
                index + 1));
        }

        return new KksDocument { Lines = lines, Signals = signals };
    }

    public static bool TryParseSectionHeader(string value, out KksSection section)
    {
        var header = value.Trim();
        section = KksSection.Unknown;
        if (header.Length < 4 || header[0] != '#') return false;

        var prefix = header[..3];
        if (prefix.Equals("#IA", StringComparison.OrdinalIgnoreCase))
            section = KksSection.Analog;
        else if (prefix.Equals("#ID", StringComparison.OrdinalIgnoreCase))
            section = KksSection.Discrete;
        else
            return false;

        if (header[3..].All(char.IsDigit)) return true;
        section = KksSection.Unknown;
        return false;
    }

    public static KksDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
            text = new UTF8Encoding(false, false).GetString(bytes).TrimStart('\uFEFF');
        return Parse(text);
    }
}
