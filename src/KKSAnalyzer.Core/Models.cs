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

public sealed record SuffixGroup(
    string Suffix,
    KksSection Section,
    int Count,
    string Examples,
    IReadOnlyList<string> Codes);

public sealed record SuffixIntersection(
    string Suffix,
    int AnalogCount,
    int DiscreteCount,
    string AnalogExamples,
    string DiscreteExamples,
    IReadOnlyList<string> AnalogCodes,
    IReadOnlyList<string> DiscreteCodes);

public sealed record ComparisonRow(string Code, string Location, string FirstSection, string SecondSection);

public sealed record SectionMismatch(
    string Code,
    KksSection ExpectedSection,
    KksSection ActualSection,
    int ReferenceLineNumber,
    int CheckedLineNumber);

public sealed class SectionConsistencyResult
{
    public required IReadOnlyList<SectionMismatch> Mismatches { get; init; }
    public required int CommonSignalCount { get; init; }
}

public sealed class FileAnalysis
{
    public required KksDocument Document { get; init; }
    public required IReadOnlyList<DuplicateGroup> Duplicates { get; init; }
    public required IReadOnlyList<KksSignal> WithoutSuffix { get; init; }
    public required IReadOnlyList<SuffixGroup> AnalogSuffixes { get; init; }
    public required IReadOnlyList<SuffixGroup> DiscreteSuffixes { get; init; }
    public required IReadOnlyList<SuffixIntersection> CommonSuffixes { get; init; }
}

public sealed class ComparisonResult
{
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }
    public int CommonCount => Rows.Count(x => x.Location == "В обоих файлах");
    public int OnlyFirstCount => Rows.Count(x => x.Location == "Только в файле 1");
    public int OnlySecondCount => Rows.Count(x => x.Location == "Только в файле 2");
}
