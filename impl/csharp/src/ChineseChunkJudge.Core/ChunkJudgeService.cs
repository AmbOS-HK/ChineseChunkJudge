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
                EndsAtUnitBoundary = raw.EndsAtUnitBoundary
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

    private JudgeReport BuildReport(
        DocumentFeature document,
        IReadOnlyList<ContractChunk> chunks,
        ChunkStrategyOptions options)
    {
        var validation = _validator.Validate(document, chunks, options);
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
            Summary = Summarize(chunks),
            Issues = validation.Issues,
            AppliedRules = appliedRules
        };
    }

    private static ChunkReportSummary Summarize(IReadOnlyList<ContractChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return new ChunkReportSummary
            {
                ChunkCount = 0,
                TotalCharCount = 0,
                MinCharCount = 0,
                MaxCharCount = 0,
                AverageCharCount = 0,
                EmptyChunkCount = 0,
                OversizedChunkCount = 0
            };
        }

        var sizes = chunks.Select(c => ChunkTextMetrics.CountCharacters(c.Text)).ToList();
        int total = sizes.Sum();

        return new ChunkReportSummary
        {
            ChunkCount = chunks.Count,
            TotalCharCount = total,
            MinCharCount = sizes.Min(),
            MaxCharCount = sizes.Max(),
            AverageCharCount = (int)Math.Round(total / (double)chunks.Count, MidpointRounding.AwayFromZero),
            EmptyChunkCount = chunks.Count(c => ChunkTextMetrics.IsBlank(c.Text)),
            OversizedChunkCount = sizes.Count(s => s > ContractConstants.MaxChunkChars)
        };
    }
}
