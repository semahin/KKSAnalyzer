using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace KKSAnalyzer.Core;

public sealed record CodeSearchResult(
    string Code,
    string FileName,
    string FullPath,
    int Count,
    string Snippet);

public sealed record DocumentLoadError(string FileName, string Message);

public sealed class BatchSearchResult
{
    public required IReadOnlyList<CodeSearchResult> Matches { get; init; }
    public required IReadOnlyList<DocumentLoadError> Errors { get; init; }
}

public static class DocumentSearchService
{
    public static readonly string FileDialogFilter =
        "Поддерживаемые документы|*.pdf;*.docx;*.txt;*.cfg;*.csv;*.log;*.md;*.xml;*.json;*.rtf|" +
        "PDF (*.pdf)|*.pdf|Word (*.docx)|*.docx|Текстовые файлы|*.txt;*.cfg;*.csv;*.log;*.md;*.xml;*.json;*.rtf|Все файлы (*.*)|*.*";

    public static BatchSearchResult Search(IEnumerable<string> codes, IEnumerable<string> filePaths)
    {
        var normalizedCodes = codes
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matches = new List<CodeSearchResult>();
        var errors = new List<DocumentLoadError>();

        foreach (var path in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string text;
            try { text = ExtractText(path); }
            catch (Exception ex)
            {
                errors.Add(new DocumentLoadError(Path.GetFileName(path), ex.Message));
                continue;
            }

            foreach (var code in normalizedCodes)
            {
                var textMatches = FindAll(text, code);
                if (textMatches.Count == 0) continue;
                matches.Add(new CodeSearchResult(
                    code,
                    Path.GetFileName(path),
                    path,
                    textMatches.Count,
                    CreateSnippet(text, textMatches[0].Index, textMatches[0].Length)));
            }
        }

        return new BatchSearchResult
        {
            Matches = matches.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
            Errors = errors
        };
    }

    public static string ExtractText(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => ExtractPdf(path),
        ".docx" => ExtractDocx(path),
        ".rtf" => ExtractRtf(path),
        ".txt" or ".cfg" or ".csv" or ".log" or ".md" or ".xml" or ".json" => ReadText(path),
        _ => throw new NotSupportedException($"Формат {Path.GetExtension(path)} не поддерживается.")
    };

    private static string ExtractPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    private static string ExtractDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var contentNames = new[] { "document.xml", "header", "footer", "footnotes.xml", "endnotes.xml", "comments.xml" };
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
                     contentNames.Any(name => entry.Name.Contains(name, StringComparison.OrdinalIgnoreCase))))
        {
            using var stream = entry.Open();
            var xml = XDocument.Load(stream);
            foreach (var paragraph in xml.Descendants().Where(x => x.Name.LocalName == "p"))
            {
                foreach (var textNode in paragraph.Descendants().Where(x => x.Name.LocalName == "t"))
                    builder.Append(textNode.Value);
                builder.AppendLine();
            }
        }
        return builder.ToString();
    }

    private static string ExtractRtf(string path)
    {
        var rtf = ReadText(path);
        rtf = Regex.Replace(rtf, @"\\u(-?\d+)\??", match =>
        {
            var value = int.Parse(match.Groups[1].Value);
            return char.ConvertFromUtf32(value < 0 ? value + 65536 : value);
        });
        rtf = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", " ");
        rtf = Regex.Replace(rtf, @"\\[a-zA-Z]+-?\d* ?", " ");
        return Regex.Replace(rtf, "[{}]", " ");
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return new UTF8Encoding(false, false).GetString(bytes).TrimStart('\uFEFF');
    }

    private static List<TextMatch> FindAll(string text, string value)
    {
        if (value.Contains('*'))
        {
            if (value.All(character => character == '*')) return [];
            var pattern = Regex.Escape(value).Replace("\\*", @"\S*?");
            return Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(2))
                .Where(match => match.Length > 0)
                .Select(match => new TextMatch(match.Index, match.Length))
                .ToList();
        }

        var result = new List<TextMatch>();
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            result.Add(new TextMatch(index, value.Length));
            start = index + Math.Max(value.Length, 1);
        }
        return result;
    }

    private static string CreateSnippet(string text, int index, int length)
    {
        const int context = 55;
        var start = Math.Max(0, index - context);
        var end = Math.Min(text.Length, index + length + context);
        var value = text[start..end].Replace('\r', ' ').Replace('\n', ' ');
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return $"{(start > 0 ? "…" : "")}{value}{(end < text.Length ? "…" : "")}";
    }

    private sealed record TextMatch(int Index, int Length);
}
