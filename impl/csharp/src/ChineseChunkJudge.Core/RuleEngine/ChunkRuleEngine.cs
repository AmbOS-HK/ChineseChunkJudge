using System.Globalization;
using System.Text.RegularExpressions;
using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.RuleEngine;

/// <summary>规则编号常量。基础规则编号不可变更。</summary>
public static class ChunkRuleIds
{
    public const string MetadataComplete = "CCJ-001";
    public const string StrategyName = "CCJ-002";
    public const string EmptyChunk = "CCJ-003";
    public const string OversizedChunk = "CCJ-004";
    public const string OffsetConsistency = "CCJ-005";
    public const string Overlap = "CCJ-006";
    public const string InvalidOptions = "CCJ-007";
    public const string ParentChildPairStructure = "CCJ-008";
    public const string ParentOversized = "CCJ-009";
    public const string ParentCoverage = "CCJ-010";
    public const string ParentReferenceCycle = "CCJ-011";
    public const string ChildWithinParent = "CCJ-012";
    public const string ParentChildDisabled = "CCJ-013";
    public const string ParentAliasConsistency = "CCJ-014";
}

/// <summary>规则求值上下文。</summary>
public sealed record ChunkValidationContext
{
    public required DocumentFeature Document { get; init; }

    public required IReadOnlyList<ContractChunk> Chunks { get; init; }

    public ChunkStrategyOptions? Options { get; init; }

    /// <summary>双库上下文父块集合，无双库产物时为空。</summary>
    public IReadOnlyList<ContextParentChunk> ParentChunks { get; init; } = [];

    /// <summary>父子配对集合，无双库产物时为空。</summary>
    public IReadOnlyList<ContextParentChildPair> Pairs { get; init; } = [];
}

/// <summary>扩展点：一条判定规则。</summary>
public interface IChunkRule
{
    string RuleId { get; }

    string Description { get; }

    ChunkIssueSeverity Severity { get; }

    bool IsBaseRule { get; }

    IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context);
}

/// <summary>CCJ-001：元信息五字段必须完整。</summary>
public sealed class MetadataCompletenessRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.MetadataComplete;

    public string Description => "元信息五字段（title、sourcePath、documentType、pageInfo、createTime）必须完整，任一缺失即判不通过。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var targets = new List<(string? ChunkId, DocumentMetadata Metadata)>
        {
            (null, context.Document.Metadata)
        };

        foreach (var chunk in context.Chunks)
        {
            targets.Add((chunk.ChunkId, chunk.Metadata));
        }

        foreach (var field in DocumentMetadata.RequiredFields)
        {
            var hits = targets.Where(t => t.Metadata.IsFieldMissing(field)).ToList();
            if (hits.Count == 0)
            {
                continue;
            }

            yield return new ChunkIssue
            {
                RuleId = RuleId,
                Severity = Severity,
                ChunkId = hits.Count == 1 ? hits[0].ChunkId : null,
                Message = $"元信息字段 {field} 缺失或为空，命中 {hits.Count} 处。",
                Detail = new Dictionary<string, string>
                {
                    ["field"] = field,
                    ["occurrences"] = hits.Count.ToString(CultureInfo.InvariantCulture)
                }
            };
        }
    }
}

/// <summary>CCJ-002：策略名必须符合 {type}@{targetSize}。</summary>
public sealed class StrategyNameRule : IChunkRule
{
    private static readonly Regex NamePattern =
        new(ContractConstants.StrategyNamePattern, RegexOptions.CultureInvariant);

    public string RuleId => ChunkRuleIds.StrategyName;

    public string Description => "策略名必须符合 {type}@{targetSize}，type 仅含小写字母、数字、下划线、连字符。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in context.Chunks)
        {
            if (!seen.Add(chunk.Strategy))
            {
                continue;
            }

            if (!NamePattern.IsMatch(chunk.Strategy))
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"策略名 {chunk.Strategy} 不符合 {{type}}@{{targetSize}} 命名规范。",
                    Detail = new Dictionary<string, string>
                    {
                        ["strategy"] = chunk.Strategy,
                        ["pattern"] = ContractConstants.StrategyNamePattern
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-003：空切片告警。</summary>
public sealed class EmptyChunkRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.EmptyChunk;

    public string Description => "切片正文为空或仅空白时告警。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var chunk in context.Chunks)
        {
            if (ChunkTextMetrics.IsBlank(chunk.Text))
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"切片 {chunk.ChunkId} 正文为空或仅空白。",
                    Detail = new Dictionary<string, string>
                    {
                        ["charCount"] = "0"
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-004：单块字符数超过 1500 告警。</summary>
public sealed class OversizedChunkRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.OversizedChunk;

    public string Description => $"单块字符数超过契约上限 {ContractConstants.MaxChunkChars} 时告警。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var chunk in context.Chunks)
        {
            int charCount = ChunkTextMetrics.CountCharacters(chunk.Text);
            if (charCount > ContractConstants.MaxChunkChars)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"切片 {chunk.ChunkId} 字符数 {charCount} 超过上限 {ContractConstants.MaxChunkChars}。",
                    Detail = new Dictionary<string, string>
                    {
                        ["charCount"] = charCount.ToString(CultureInfo.InvariantCulture),
                        ["limit"] = ContractConstants.MaxChunkChars.ToString(CultureInfo.InvariantCulture)
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-005：偏移与原文一致性。</summary>
public sealed class OffsetConsistencyRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.OffsetConsistency;

    public string Description => "偏移必须落在正文档内，且 text 与正文区间逐字一致。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string content = context.Document.Content;
        foreach (var chunk in context.Chunks)
        {
            if (chunk.StartOffset < 0 || chunk.EndOffset <= chunk.StartOffset || chunk.EndOffset > content.Length)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"切片 {chunk.ChunkId} 偏移区间 [{chunk.StartOffset}, {chunk.EndOffset}) 越界或长度非正，正文长度 {content.Length}。",
                    Detail = new Dictionary<string, string>
                    {
                        ["reason"] = "range",
                        ["startOffset"] = chunk.StartOffset.ToString(CultureInfo.InvariantCulture),
                        ["endOffset"] = chunk.EndOffset.ToString(CultureInfo.InvariantCulture),
                        ["contentLength"] = content.Length.ToString(CultureInfo.InvariantCulture)
                    }
                };
                continue;
            }

            string expected = content.Substring(chunk.StartOffset, chunk.EndOffset - chunk.StartOffset);
            if (!string.Equals(expected, chunk.Text, StringComparison.Ordinal))
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"切片 {chunk.ChunkId} 正文与原文区间 [{chunk.StartOffset}, {chunk.EndOffset}) 不一致。",
                    Detail = new Dictionary<string, string>
                    {
                        ["reason"] = "text_mismatch",
                        ["startOffset"] = chunk.StartOffset.ToString(CultureInfo.InvariantCulture),
                        ["endOffset"] = chunk.EndOffset.ToString(CultureInfo.InvariantCulture)
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-006：相邻切片区间重叠。</summary>
public sealed class OverlapRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.Overlap;

    public string Description => "相邻切片区间不得重叠。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ordered = context.Chunks.OrderBy(c => c.Index).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            if (current.StartOffset < previous.EndOffset)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = current.ChunkId,
                    Message = $"切片 {previous.ChunkId} 与 {current.ChunkId} 区间重叠。",
                    Detail = new Dictionary<string, string>
                    {
                        ["previousChunkId"] = previous.ChunkId,
                        ["previousEndOffset"] = previous.EndOffset.ToString(CultureInfo.InvariantCulture),
                        ["startOffset"] = current.StartOffset.ToString(CultureInfo.InvariantCulture)
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-007：策略选项非法。</summary>
public sealed class InvalidOptionsRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.InvalidOptions;

    public string Description => "策略选项必须落在契约规定区间内，且不得放宽单块上限。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Options is not { } options)
        {
            yield break;
        }

        if (options.TargetSize < 1 || options.TargetSize > ContractConstants.MaxChunkChars)
        {
            yield return CreateIssue("targetSize", options.TargetSize, "1..1500");
        }

        if (options.MaxChunkSize > ContractConstants.MaxChunkChars)
        {
            yield return CreateIssue("maxChunkSize", options.MaxChunkSize, $"1..{ContractConstants.MaxChunkChars}");
        }

        if (options.MaxChunkSize < options.TargetSize)
        {
            yield return CreateIssue("maxChunkSize", options.MaxChunkSize, $"targetSize..{ContractConstants.MaxChunkChars}");
        }

        if (options.MinChunkSize < 1 || options.MinChunkSize > options.TargetSize)
        {
            yield return CreateIssue("minChunkSize", options.MinChunkSize, $"1..targetSize({options.TargetSize})");
        }

        if (options.Overlap < 0 || options.Overlap >= options.TargetSize)
        {
            yield return CreateIssue("overlap", options.Overlap, $"0..{Math.Max(options.TargetSize - 1, 0)}");
        }

        if (options.ParentChild is { } parentChild && parentChild.ParentMaxChars < 1)
        {
            yield return CreateIssue("parentMaxChars", parentChild.ParentMaxChars, ">= 1");
        }
    }

    private ChunkIssue CreateIssue(string name, int value, string expected)
    {
        return new ChunkIssue
        {
            RuleId = RuleId,
            Severity = Severity,
            Message = $"策略选项 {name}={value} 非法，合法区间 {expected}。",
            Detail = new Dictionary<string, string>
            {
                ["option"] = name,
                ["value"] = value.ToString(CultureInfo.InvariantCulture),
                ["expected"] = expected
            }
        };
    }
}

/// <summary>
/// 规则引擎：按注册顺序求值。
/// 基础规则可以扩展、可以注册新规则，但不可删除、不可降级。
/// </summary>
public sealed class ChunkRuleEngine
{
    private static readonly Regex RuleIdPattern =
        new(@"^CCJ-(?:[0-9]{3}|X[0-9]{3})$", RegexOptions.CultureInvariant);

    private readonly List<IChunkRule> _rules;

    public ChunkRuleEngine()
        : this(CreateDefaultRules())
    {
    }

    public ChunkRuleEngine(IEnumerable<IChunkRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = [];
        foreach (var rule in rules)
        {
            _rules.Add(EnsureValid(rule));
        }
    }

    /// <summary>基础规则编号集合，任何实现不得删除。</summary>
    public static IReadOnlySet<string> DefaultBaseRuleIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ChunkRuleIds.MetadataComplete,
        ChunkRuleIds.StrategyName,
        ChunkRuleIds.EmptyChunk,
        ChunkRuleIds.OversizedChunk,
        ChunkRuleIds.OffsetConsistency,
        ChunkRuleIds.Overlap,
        ChunkRuleIds.InvalidOptions,
        ChunkRuleIds.ParentChildPairStructure,
        ChunkRuleIds.ParentOversized,
        ChunkRuleIds.ParentCoverage,
        ChunkRuleIds.ParentReferenceCycle,
        ChunkRuleIds.ChildWithinParent,
        ChunkRuleIds.ParentChildDisabled,
        ChunkRuleIds.ParentAliasConsistency
    };

    public IReadOnlyList<IChunkRule> Rules => _rules;

    /// <summary>构造契约规定的十四条基础规则。</summary>
    public static IReadOnlyList<IChunkRule> CreateDefaultRules() =>
    [
        new MetadataCompletenessRule(),
        new StrategyNameRule(),
        new EmptyChunkRule(),
        new OversizedChunkRule(),
        new OffsetConsistencyRule(),
        new OverlapRule(),
        new InvalidOptionsRule(),
        new ParentChildPairStructureRule(),
        new ParentOversizedRule(),
        new ParentCoverageRule(),
        new ParentReferenceCycleRule(),
        new ChildWithinParentRule(),
        new ParentChildDisabledRule(),
        new ParentAliasConsistencyRule()
    ];

    /// <summary>注册扩展规则。编号必须唯一，扩展规则不得声明为基础规则。</summary>
    public ChunkRuleEngine Register(IChunkRule rule)
    {
        var validated = EnsureValid(rule);

        if (_rules.Any(r => string.Equals(r.RuleId, validated.RuleId, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"规则编号 {validated.RuleId} 已存在，编号必须唯一。", nameof(rule));
        }

        if (validated.IsBaseRule && !DefaultBaseRuleIds.Contains(validated.RuleId))
        {
            throw new ArgumentException($"扩展规则 {validated.RuleId} 不得声明 IsBaseRule=true。", nameof(rule));
        }

        _rules.Add(validated);
        return this;
    }

    /// <summary>注销扩展规则。基础规则不可删除。</summary>
    public ChunkRuleEngine Unregister(string ruleId)
    {
        var rule = _rules.FirstOrDefault(r => string.Equals(r.RuleId, ruleId, StringComparison.Ordinal))
                   ?? throw new ArgumentException($"规则 {ruleId} 未注册。", nameof(ruleId));

        if (rule.IsBaseRule)
        {
            throw new InvalidOperationException($"基础规则 {ruleId} 不可删除。");
        }

        _rules.Remove(rule);
        return this;
    }

    /// <summary>按注册顺序求值，返回全部问题。</summary>
    public IReadOnlyList<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ChunkIssue>();
        foreach (var rule in _rules)
        {
            foreach (var issue in rule.Evaluate(context))
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    private static IChunkRule EnsureValid(IChunkRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.RuleId) || !RuleIdPattern.IsMatch(rule.RuleId))
        {
            throw new ArgumentException($"规则编号 {rule.RuleId} 不符合 CCJ-### 或 CCJ-X### 格式。", nameof(rule));
        }

        if (string.IsNullOrWhiteSpace(rule.Description))
        {
            throw new ArgumentException($"规则 {rule.RuleId} 必须提供 Description。", nameof(rule));
        }

        return rule;
    }
}
