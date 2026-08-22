namespace KKSAnalyzer.Core;

public enum KksSection
{
    Unknown,
    Analog,
    Discrete
}

public sealed record KksSignal(
    string Code,
    string MainPart,
    string? Suffix,
    KksSection Section,
    int LineNumber);

public sealed class KksDocument
{
    public required string[] Lines { get; init; }
    public required IReadOnlyList<KksSignal> Signals { get; init; }
}

public sealed record DuplicateGroup(string Code, KksSection Section, int Count, string Lines);

public sealed record SuffixGroup(string Suffix, KksSection Section, int Count, string Examples);

public sealed record ComparisonRow(string Code, string Location, string FirstSection, string SecondSection);

public sealed class FileAnalysis
{
    public required KksDocument Document { get; init; }
    public required IReadOnlyList<DuplicateGroup> Duplicates { get; init; }
    public required IReadOnlyList<KksSignal> WithoutSuffix { get; init; }
    public required IReadOnlyList<SuffixGroup> AnalogSuffixes { get; init; }
    public required IReadOnlyList<SuffixGroup> DiscreteSuffixes { get; init; }
}

public sealed class ComparisonResult
{
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }
    public int CommonCount => Rows.Count(x => x.Location == "В обоих файлах");
    public int OnlyFirstCount => Rows.Count(x => x.Location == "Только в файле 1");
    public int OnlySecondCount => Rows.Count(x => x.Location == "Только в файле 2");
}
