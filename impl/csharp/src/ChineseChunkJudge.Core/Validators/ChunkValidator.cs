using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;

namespace ChineseChunkJudge.Core.Validators;

/// <summary>校验结果。</summary>
public sealed record ChunkValidationResult
{
    public required IReadOnlyList<ChunkIssue> Issues { get; init; }

    public int ErrorCount => Issues.Count(i => i.Severity == ChunkIssueSeverity.Error);

    public int WarningCount => Issues.Count(i => i.Severity == ChunkIssueSeverity.Warning);

    public int InfoCount => Issues.Count(i => i.Severity == ChunkIssueSeverity.Info);

    /// <summary>契约口径：不存在 error 级问题即通过。</summary>
    public bool Passed => ErrorCount == 0;
}

/// <summary>契约切片校验器：把规则引擎的求值结果包装成校验结果。</summary>
public sealed class ChunkValidator
{
    public ChunkValidator(ChunkRuleEngine? ruleEngine = null)
    {
        RuleEngine = ruleEngine ?? new ChunkRuleEngine();
    }

    public ChunkRuleEngine RuleEngine { get; }

    /// <param name="document">输入文档，提供正文基线与元信息基线。</param>
    /// <param name="chunks">待校验的契约切片。</param>
    /// <param name="options">策略选项，传入后才执行 CCJ-007。</param>
    public ChunkValidationResult Validate(
        DocumentFeature document,
        IReadOnlyList<ContractChunk> chunks,
        ChunkStrategyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);

        var issues = RuleEngine.Evaluate(new ChunkValidationContext
        {
            Document = document,
            Chunks = chunks,
            Options = options
        });

        return new ChunkValidationResult { Issues = issues };
    }
}
