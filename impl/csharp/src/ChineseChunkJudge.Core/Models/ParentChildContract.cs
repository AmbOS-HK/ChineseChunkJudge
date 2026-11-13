namespace ChineseChunkJudge.Core.Models;

/// <summary>
/// 双库（Parent/Child）配置。enabled 为强布尔：关闭即本批次不得产出父块相关字段。
/// parentMaxChars 是告警阈值配置项，不是上限；6000 为临时调试默认值，正式基准由样本 P95 标定。
/// </summary>
public sealed record ParentChildOptions
{
    public bool Enabled { get; init; }

    public int ParentMaxChars { get; init; } = ContractConstants.ParentMaxCharsDebugDefault;
}

/// <summary>上下文父块：取自原文区间，互不重叠，集合对原文做完整无重叠划分。</summary>
public sealed record ContextParentChunk
{
    public required string ParentChunkId { get; init; }

    public required int Index { get; init; }

    public required string Text { get; init; }

    public required int StartOffset { get; init; }

    public required int EndOffset { get; init; }

    public required string Strategy { get; init; }

    public required DocumentMetadata Metadata { get; init; }

    public int CharCount { get; init; }

    public bool StartsAtUnitBoundary { get; init; } = true;

    public bool EndsAtUnitBoundary { get; init; } = true;

    /// <summary>跨父块显式引用，替代文本重叠；ID 必须可解析，禁止自引用与成环。</summary>
    public IReadOnlyList<string> RelatedContextParentIds { get; init; } = [];
}

/// <summary>父子配对：1:N 为主，1:1 为合法边界，childChunkIds 非空。</summary>
public sealed record ContextParentChildPair
{
    public required string ContextParentChunkId { get; init; }

    public required IReadOnlyList<string> ChildChunkIds { get; init; }
}

/// <summary>双库独立产物包，与 JudgeReport 解耦。</summary>
public sealed record ParentChildPackage
{
    public required string DocumentId { get; init; }

    public required string Strategy { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required IReadOnlyList<ContextParentChunk> ParentChunks { get; init; }

    public required IReadOnlyList<ContextParentChildPair> Pairs { get; init; }
}
