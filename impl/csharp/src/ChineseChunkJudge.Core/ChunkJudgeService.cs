using System.Globalization;
using ChineseChunkJudge.Core.Abstractions;
using ChineseChunkJudge.Core.Builders;
using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using ChineseChunkJudge.Core.Validators;

namespace ChineseChunkJudge.Core;

/// <summary>
/// 判定服务：归一化 + 校验 + 汇总。
/// 是契约中唯一定义「原始切片 → 契约切片 → 判定报告」流转的入口。
/// </summary>
public sealed class ChunkJudgeService
{
    private readonly ChunkValidator _validator;
    private readonly ChunkRelationBuilder _relationBuilder;

    public ChunkJudgeService(ChunkRuleEngine? ruleEngine = null, ChunkRelationBuilder? relationBuilder = null)
    {
        _validator = new ChunkValidator(ruleEngine);
        _relationBuilder = relationBuilder ?? new ChunkRelationBuilder();
    }

    public ChunkValidator Validator => _validator;

    public ChunkRuleEngine RuleEngine => _validator.RuleEngine;

    /// <summary>契约切片标识：{strategy}#{index}。</summary>
    public static string BuildChunkId(string strategy, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{strategy}#{index}");

    /// <summary>归一化：补齐元信息、字符数、关系指针。</summary>
    public IReadOnlyList<ContractChunk> Normalize(
        DocumentFeature document,
        IReadOnlyList<RawChunk> rawChunks,
        ChunkStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rawChunks);
        ArgumentNullException.ThrowIfNull(options);

        if (document.Metadata is null)
        {
            throw new ArgumentException("invalid_input：document.metadata 不得为空。", nameof(document));
        }

        for (int i = 0; i < rawChunks.Count; i++)
        {
            if (rawChunks[i].Text is null)
            {
                throw new ArgumentException($"invalid_input：rawChunks[{i}].text 不得为空。", nameof(rawChunks));
            }
        }

        string strategy = options.StrategyName;
        var ordered = rawChunks.OrderBy(c => c.Index).ToList();
        var chunks = new List<ContractChunk>(ordered.Count);

        foreach (var raw in ordered)
        {
            chunks.Add(new ContractChunk
            {
                ChunkId = BuildChunkId(strategy, raw.Index),
                Index = raw.Index,
                Text = raw.Text,
                StartOffset = raw.StartOffset,
                EndOffset = raw.EndOffset,
                Strategy = strategy,
                Metadata = document.Metadata,
                CharCount = ChunkTextMetrics.CountCharacters(raw.Text),
                UnitKind = raw.UnitKind,
                UnitKey = raw.UnitKey,
                StructureLevel = raw.StructureLevel,
                StartsAtUnitBoundary = raw.StartsAtUnitBoundary,
                EndsAtUnitBoundary = raw.EndsAtUnitBoundary,
                HeadingPath = raw.HeadingPath
            });
        }

        return _relationBuilder.Build(chunks);
    }

    /// <summary>由执行器完成切片并判定。</summary>
    public JudgeReport JudgeWithExecutor(
        DocumentFeature document,
        IChunkExecutor executor,
        ChunkStrategyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(executor);

        var effective = options ?? new ChunkStrategyOptions
        {
            StrategyType = executor.StrategyType,
            TargetSize = 600
        };

        var rawChunks = executor.Execute(document, effective);
        return Judge(document, rawChunks, effective);
    }

    /// <summary>对调用方提交的原始切片判定。</summary>
    public JudgeReport Judge(
        DocumentFeature document,
        IReadOnlyList<RawChunk> rawChunks,
        ChunkStrategyOptions options)
    {
        var chunks = Normalize(document, rawChunks, options);
        return BuildReport(document, chunks, options);
    }

    /// <summary>对已归一化的契约切片判定（会重建关系指针）。</summary>
    public JudgeReport JudgeContractChunks(
        DocumentFeature document,
        IReadOnlyList<ContractChunk> chunks,
        ChunkStrategyOptions options)
    {
        var linked = _relationBuilder.Build(chunks);
        return BuildReport(document, linked, options);
    }

    /// <summary>对已归一化的契约切片与双库产物判定（会重建关系指针，保留上下文父指针）。</summary>
    public JudgeReport JudgeContractChunks(
        DocumentFeature document,
        IReadOnlyList<ContractChunk> chunks,
        ChunkStrategyOptions options,
        IReadOnlyList<ContextParentChunk> parentChunks,
        IReadOnlyList<ContextParentChildPair> pairs)
    {
        var linked = _relationBuilder.Build(chunks);
        return BuildReport(document, linked, options, parentChunks, pairs);
    }

    /// <summary>对调用方提交的原始切片与双库产物判定。</summary>
    public JudgeReport Judge(
        DocumentFeature document,
        IReadOnlyList<RawChunk> rawChunks,
        ChunkStrategyOptions options,
        IReadOnlyList<ContextParentChunk> parentChunks,
        IReadOnlyList<ContextParentChildPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(parentChunks);
        ArgumentNullException.ThrowIfNull(pairs);

        var chunks = Normalize(document, rawChunks, options);

        // 父侧 childChunkIds 为权威，子侧 contextParentChunkId 为派生视图（§7.3）：
        // RawChunk 不携带关系指针，必须按 pairs 回填，否则 summary 的子块归属统计恒为 0。
        var childToParent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (pair.ChildChunkIds is null)
            {
                continue;
            }

            foreach (var childId in pair.ChildChunkIds)
            {
                childToParent.TryAdd(childId, pair.ContextParentChunkId);
            }
        }

        if (childToParent.Count > 0)
        {
            chunks = chunks
                .Select(c => childToParent.TryGetValue(c.ChunkId, out var parentId)
                    ? c with { Relations = c.Relations with { ContextParentChunkId = parentId } }
                    : c)
                .ToList();
        }

        return BuildReport(document, chunks, options, parentChunks, pairs);
    }

    /// <summary>生成双库独立产物包，与判定报告解耦。</summary>
    public ParentChildPackage BuildPackage(
        DocumentFeature document,
        ChunkStrategyOptions options,
        IReadOnlyList<ContextParentChunk> parentChunks,
        IReadOnlyList<ContextParentChildPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(parentChunks);
        ArgumentNullException.ThrowIfNull(pairs);

        return new ParentChildPackage
        {
            DocumentId = document.DocumentId,
            Strategy = options.StrategyName,
            GeneratedAt = DateTimeOffset.Now,
            ParentChunks = parentChunks,
            Pairs = pairs
        };
    }

    private JudgeReport BuildReport(
        DocumentFeature document,
        IReadOnlyList<ContractChunk> chunks,
        ChunkStrategyOptions options,
        IReadOnlyList<ContextParentChunk>? parentChunks = null,
        IReadOnlyList<ContextParentChildPair>? pairs = null)
    {
        var parents = parentChunks ?? [];
        var parentPairs = pairs ?? [];
        var validation = _validator.Validate(document, chunks, options, parents, parentPairs);
        var appliedRules = RuleEngine.Rules
            .Select(r => new AppliedRule
            {
                RuleId = r.RuleId,
                Description = r.Description,
                Severity = r.Severity,
                IsBaseRule = r.IsBaseRule
            })
            .ToList();

        return new JudgeReport
        {
            ReportId = string.Create(
                CultureInfo.InvariantCulture,
                $"{document.DocumentId}:{options.StrategyName}:{Guid.NewGuid():N}"),
            DocumentId = document.DocumentId,
            Strategy = options.StrategyName,
            TargetSize = options.TargetSize,
            GeneratedAt = DateTimeOffset.Now,
            Passed = validation.Passed,
            Summary = Summarize(chunks, parents, options),
            Issues = validation.Issues,
            AppliedRules = appliedRules
        };
    }

    private static ChunkReportSummary Summarize(
        IReadOnlyList<ContractChunk> chunks,
        IReadOnlyList<ContextParentChunk> parentChunks,
        ChunkStrategyOptions options)
    {
        var sizes = chunks.Select(c => ChunkTextMetrics.CountCharacters(c.Text)).ToList();
        int total = sizes.Sum();
        int childWithParent = chunks.Count(c => c.Relations.ContextParentChunkId is not null);

        var parentSizes = parentChunks.Select(p => ChunkTextMetrics.CountCharacters(p.Text)).OrderBy(s => s).ToList();
        int threshold = options.ParentChild?.ParentMaxChars ?? ContractConstants.ParentMaxCharsDebugDefault;

        return new ChunkReportSummary
        {
            ChunkCount = chunks.Count,
            TotalCharCount = total,
            MinCharCount = sizes.Count == 0 ? 0 : sizes.Min(),
            MaxCharCount = sizes.Count == 0 ? 0 : sizes.Max(),
            AverageCharCount = chunks.Count == 0
                ? 0
                : (int)Math.Round(total / (double)chunks.Count, MidpointRounding.AwayFromZero),
            EmptyChunkCount = chunks.Count(c => ChunkTextMetrics.IsBlank(c.Text)),
            OversizedChunkCount = sizes.Count(s => s > ContractConstants.MaxChunkChars),
            ParentChunkCount = parentChunks.Count,
            ChildWithParentCount = childWithParent,
            ParentCoverageRatio = chunks.Count == 0 ? 0d : (double)childWithParent / chunks.Count,
            ChildWithParentRatio = chunks.Count == 0 ? 0d : (double)childWithParent / chunks.Count,
            ParentCharP95 = Percentile95(parentSizes),
            OversizedParentChunkCount = parentSizes.Count(s => s > threshold),
            HardCutChunkCount = string.Equals(options.ChunkMode, ChunkModes.Page, StringComparison.Ordinal)
                ? 0
                : chunks.Count(c => HardCutRule.IsHardCut(c.Text, options.TargetSize))
        };
    }

    /// <summary>P95 最近秩法；无父块时为 0。</summary>
    private static int Percentile95(IReadOnlyList<int> sortedSizes)
    {
        if (sortedSizes.Count == 0)
        {
            return 0;
        }

        int rank = (int)Math.Ceiling(0.95 * sortedSizes.Count);
        return sortedSizes[Math.Clamp(rank - 1, 0, sortedSizes.Count - 1)];
    }
}
