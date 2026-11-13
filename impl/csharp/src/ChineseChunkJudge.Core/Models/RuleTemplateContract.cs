namespace ChineseChunkJudge.Core.Models;

/// <summary>文档结构特征枚举（封闭集合，扩枚举走次版本）。</summary>
public static class ChunkTraits
{
    public const string ArticleNumbering = "article_numbering";
    public const string SectionNumbering = "section_numbering";
    public const string WeakStructure = "weak_structure";
    public const string DenseTable = "dense_table";
    public const string PdfNoStructure = "pdf_no_structure";
}

/// <summary>切分方式枚举。</summary>
public static class ChunkModes
{
    public const string Regex = "regex";
    public const string Smart = "smart";
    public const string Page = "page";
}

/// <summary>切片规则模板（Spec 侧）：只描述决策，不执行切分；库名等部署标识不进模板。</summary>
public sealed record ChunkRuleTemplate
{
    public required string TemplateId { get; init; }

    /// <summary>模板自身版本，与契约版本独立。</summary>
    public required string Version { get; init; }

    public required string Name { get; init; }

    public required TemplateAppliesTo AppliesTo { get; init; }

    public required DecisionTable Decision { get; init; }

    public TemplateValidation? Validation { get; init; }
}

public sealed record TemplateAppliesTo
{
    public IReadOnlyList<string> DocumentCategories { get; init; } = [];

    public IReadOnlyList<string> Traits { get; init; } = [];
}

public sealed record DecisionTable
{
    /// <summary>有序，先匹配先生效；traits 全部满足才命中。</summary>
    public IReadOnlyList<DecisionRule> Rules { get; init; } = [];

    /// <summary>强制兜底：未命中任何规则时使用，不允许无决策结果。</summary>
    public required FallbackRule Fallback { get; init; }
}

public sealed record DecisionRule
{
    public IReadOnlyList<string> Traits { get; init; } = [];

    public required string ChunkMode { get; init; }

    /// <summary>chunkMode=regex 时必填，其余模式禁止携带。</summary>
    public string? Pattern { get; init; }

    public required int ChunkSize { get; init; }

    public ParentChildOptions? ParentChild { get; init; }

    public bool ForbidLineStartAnchor { get; init; } = true;

    public bool ForbidHeadingSplit { get; init; } = true;

    public bool PreserveSeparator { get; init; } = true;
}

public sealed record FallbackRule
{
    /// <summary>契约固定为 smart。</summary>
    public required string ChunkMode { get; init; }

    public required int ChunkSize { get; init; }

    public ParentChildOptions? ParentChild { get; init; }
}

public sealed record TemplateValidation
{
    public bool HardCutZero { get; init; } = true;

    /// <summary>回归验收标识，执行归 ChunkJudgeBench。</summary>
    public string? AcceptanceRef { get; init; }
}

/// <summary>选型结果来源（0.8.0）。</summary>
public static class TemplateSelectionReasons
{
    public const string RuleMatched = "rule_matched";

    public const string Fallback = "fallback";
}

/// <summary>模板决策结果：命中规则或强制兜底。</summary>
public sealed record TemplateDecision
{
    public required string TemplateId { get; init; }

    /// <summary>命中的规则下标；UsedFallback=true 时为 null。</summary>
    public int? MatchedRuleIndex { get; init; }

    public required bool UsedFallback { get; init; }

    /// <summary>类别过滤后的候选模板数；单模板形态固定为 1。</summary>
    public required int CandidateCount { get; init; }

    /// <summary>rule_matched / fallback。</summary>
    public required string SelectionReason { get; init; }

    public required string ChunkMode { get; init; }

    public string? Pattern { get; init; }

    public required int ChunkSize { get; init; }

    public ParentChildOptions? ParentChild { get; init; }
}
