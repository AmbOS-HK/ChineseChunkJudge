using System.Globalization;
using System.Text.RegularExpressions;
using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.RuleEngine;

/// <summary>CCJ-018：策略名必须与选项一致，且 targetSize 落在 1..1500。</summary>
public sealed class StrategyConsistencyRule : IChunkRule
{
    private static readonly Regex NamePattern =
        new(ContractConstants.StrategyNamePattern, RegexOptions.CultureInvariant);

    private static readonly Regex PartsPattern =
        new(@"^(?<type>[a-z][a-z0-9_-]*)@(?<target>[1-9][0-9]*)", RegexOptions.CultureInvariant);

    public string RuleId => ChunkRuleIds.StrategyConsistency;

    public string Description => "切片策略名中的 {type} 必须等于 options.strategyType，{targetSize} 必须等于 options.targetSize 且落在 1..1500。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var chunk in context.Chunks)
        {
            // 前置：策略名整体非法时只由 CCJ-002 报告，这里不重复报。
            if (!NamePattern.IsMatch(chunk.Strategy))
            {
                continue;
            }

            var match = PartsPattern.Match(chunk.Strategy);
            if (!match.Success)
            {
                continue;
            }

            string type = match.Groups["type"].Value;
            int targetSize = int.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);

            if (targetSize < 1 || targetSize > ContractConstants.MaxChunkChars)
            {
                yield return Issue(
                    chunk.ChunkId,
                    $"切片 {chunk.ChunkId} 的策略名 {chunk.Strategy} 中 targetSize={targetSize} 越界，合法区间 1..{ContractConstants.MaxChunkChars}。",
                    "targetSize",
                    targetSize.ToString(CultureInfo.InvariantCulture),
                    $"1..{ContractConstants.MaxChunkChars}");
                continue;
            }

            if (context.Options is not { } options)
            {
                continue;
            }

            if (!string.Equals(type, options.StrategyType, StringComparison.Ordinal))
            {
                yield return Issue(
                    chunk.ChunkId,
                    $"切片 {chunk.ChunkId} 的策略类型 {type} 与选项 strategyType={options.StrategyType} 不一致。",
                    "strategyType",
                    type,
                    options.StrategyType);
            }

            if (targetSize != options.TargetSize)
            {
                yield return Issue(
                    chunk.ChunkId,
                    $"切片 {chunk.ChunkId} 的 targetSize={targetSize} 与选项 targetSize={options.TargetSize} 不一致。",
                    "targetSize",
                    targetSize.ToString(CultureInfo.InvariantCulture),
                    options.TargetSize.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private ChunkIssue Issue(string? chunkId, string message, string field, string actual, string expected) => new()
    {
        RuleId = RuleId,
        Severity = Severity,
        ChunkId = chunkId,
        Message = message,
        Detail = new Dictionary<string, string>
        {
            ["field"] = field,
            ["actual"] = actual,
            ["expected"] = expected
        }
    };
}

/// <summary>CCJ-019：切片字符数超过 maxChunkSize（且未超 1500）告警。</summary>
public sealed class OversizedAgainstMaxRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.OversizedAgainstMax;

    public string Description => "切片字符数超过 options.maxChunkSize 且未超过 1500 时告警；charCount > 1500 由 CCJ-004 独占。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Options is not { } options)
        {
            yield break;
        }

        // maxChunkSize 等于 1500（契约阈值上限）时不触发：此时任何超限都由 CCJ-004 负责。
        if (options.MaxChunkSize >= ContractConstants.MaxChunkChars)
        {
            yield break;
        }

        foreach (var chunk in context.Chunks)
        {
            int charCount = ChunkTextMetrics.CountCharacters(chunk.Text);
            if (charCount > options.MaxChunkSize && charCount <= ContractConstants.MaxChunkChars)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"切片 {chunk.ChunkId} 字符数 {charCount} 超过配置阈值 maxChunkSize={options.MaxChunkSize}。",
                    Detail = new Dictionary<string, string>
                    {
                        ["charCount"] = charCount.ToString(CultureInfo.InvariantCulture),
                        ["maxChunkSize"] = options.MaxChunkSize.ToString(CultureInfo.InvariantCulture)
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-020：元信息必须与文档等值。</summary>
public sealed class MetadataEqualityRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.MetadataEquality;

    public string Description => "切片与父块的元信息五字段必须与文档元信息等值。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var expected = context.Document.Metadata;
        if (expected is null)
        {
            yield break;
        }

        foreach (var chunk in context.Chunks)
        {
            foreach (var field in DocumentMetadata.RequiredFields)
            {
                if (!IsSameValue(expected, chunk.Metadata, field))
                {
                    yield return Issue(chunk.ChunkId, "子块", chunk.Metadata, field, expected);
                }
            }
        }

        foreach (var parent in context.ParentChunks)
        {
            foreach (var field in DocumentMetadata.RequiredFields)
            {
                if (!IsSameValue(expected, parent.Metadata, field))
                {
                    yield return Issue(parent.ParentChunkId, "父块", parent.Metadata, field, expected);
                }
            }
        }
    }

    private static bool IsSameValue(DocumentMetadata expected, DocumentMetadata? actual, string field)
    {
        if (actual is null)
        {
            return false;
        }

        return field switch
        {
            DocumentMetadata.FieldTitle => string.Equals(expected.Title, actual.Title, StringComparison.Ordinal),
            DocumentMetadata.FieldSourcePath => string.Equals(expected.SourcePath, actual.SourcePath, StringComparison.Ordinal),
            DocumentMetadata.FieldDocumentType => string.Equals(expected.DocumentType, actual.DocumentType, StringComparison.Ordinal),
            DocumentMetadata.FieldPageInfo => string.Equals(expected.PageInfo, actual.PageInfo, StringComparison.Ordinal),
            DocumentMetadata.FieldCreateTime => expected.CreateTime == actual.CreateTime,
            _ => false
        };
    }

    private ChunkIssue Issue(string? ownerId, string kind, DocumentMetadata? actual, string field, DocumentMetadata expected)
    {
        string actualValue = actual is null ? "<null>" : Field(actual, field);
        return new ChunkIssue
        {
            RuleId = RuleId,
            Severity = Severity,
            ChunkId = ownerId,
            Message = $"{kind} {ownerId} 的元信息 {field}={actualValue} 与文档 {Field(expected, field)} 不一致。",
            Detail = new Dictionary<string, string>
            {
                ["owner"] = kind,
                ["field"] = field,
                ["actual"] = actualValue,
                ["expected"] = Field(expected, field)
            }
        };
    }

    private static string Field(DocumentMetadata metadata, string field) => field switch
    {
        DocumentMetadata.FieldTitle => metadata.Title,
        DocumentMetadata.FieldSourcePath => metadata.SourcePath,
        DocumentMetadata.FieldDocumentType => metadata.DocumentType,
        DocumentMetadata.FieldPageInfo => metadata.PageInfo,
        DocumentMetadata.FieldCreateTime => metadata.CreateTime.ToString("O", CultureInfo.InvariantCulture),
        _ => string.Empty
    };
}

/// <summary>CCJ-021：父块结构完整性（序号连续、字符数、偏移区间）。</summary>
public sealed class ParentStructureRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentStructure;

    public string Description => "父块 index 必须从 0 起连续且不重复；charCount 必须等于 text 的码位数；偏移区间必须落在正文内且长度为正。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ParentChunks.Count == 0)
        {
            yield break;
        }

        var parents = context.ParentChunks;
        string content = context.Document.Content;

        var indexCounts = new Dictionary<int, int>();
        foreach (var parent in parents)
        {
            indexCounts.TryGetValue(parent.Index, out int count);
            indexCounts[parent.Index] = count + 1;
        }

        foreach (var (index, count) in indexCounts.Where(pair => pair.Value > 1))
        {
            yield return Issue(
                null,
                $"父块序号 index={index} 重复出现 {count} 次，index 必须从 0 起连续且不重复。",
                new Dictionary<string, string>
                {
                    ["field"] = "index",
                    ["value"] = index.ToString(CultureInfo.InvariantCulture),
                    ["occurrences"] = count.ToString(CultureInfo.InvariantCulture)
                });
        }

        var distinctIndexes = indexCounts.Keys.OrderBy(value => value).ToList();
        int expectedCount = parents.Count;
        bool continuous = distinctIndexes.Count == expectedCount
            && distinctIndexes[0] == 0
            && distinctIndexes[^1] == expectedCount - 1
            && distinctIndexes[^1] - distinctIndexes[0] + 1 == expectedCount;

        if (!continuous)
        {
            yield return Issue(
                null,
                $"父块序号不连续：共 {expectedCount} 个父块，实际序号 {distinctIndexes[0]}..{distinctIndexes[^1]}（去重后 {distinctIndexes.Count} 个），期望 0..{expectedCount - 1}。",
                new Dictionary<string, string>
                {
                    ["field"] = "index",
                    ["expected"] = $"0..{expectedCount - 1}",
                    ["distinctCount"] = distinctIndexes.Count.ToString(CultureInfo.InvariantCulture),
                    ["min"] = distinctIndexes[0].ToString(CultureInfo.InvariantCulture),
                    ["max"] = distinctIndexes[^1].ToString(CultureInfo.InvariantCulture)
                });
        }

        foreach (var parent in parents)
        {
            if (parent.StartOffset < 0
                || parent.EndOffset <= parent.StartOffset
                || parent.EndOffset > content.Length)
            {
                yield return Issue(
                    parent.ParentChunkId,
                    $"父块 {parent.ParentChunkId} 偏移区间 [{parent.StartOffset}, {parent.EndOffset}) 越界或长度非正，正文长度 {content.Length}。",
                    new Dictionary<string, string>
                    {
                        ["field"] = "range",
                        ["startOffset"] = parent.StartOffset.ToString(CultureInfo.InvariantCulture),
                        ["endOffset"] = parent.EndOffset.ToString(CultureInfo.InvariantCulture),
                        ["contentLength"] = content.Length.ToString(CultureInfo.InvariantCulture)
                    });
            }
        }

        foreach (var parent in parents)
        {
            int actual = ChunkTextMetrics.CountCharacters(parent.Text);
            if (parent.CharCount != actual)
            {
                yield return Issue(
                    parent.ParentChunkId,
                    $"父块 {parent.ParentChunkId} 的 charCount={parent.CharCount} 与正文码位数 {actual} 不一致。",
                    new Dictionary<string, string>
                    {
                        ["field"] = "charCount",
                        ["expected"] = actual.ToString(CultureInfo.InvariantCulture),
                        ["value"] = parent.CharCount.ToString(CultureInfo.InvariantCulture)
                    });
            }
        }
    }

    private ChunkIssue Issue(string? chunkId, string message, IReadOnlyDictionary<string, string> detail) => new()
    {
        RuleId = RuleId,
        Severity = Severity,
        ChunkId = chunkId,
        Message = message,
        Detail = detail
    };
}
