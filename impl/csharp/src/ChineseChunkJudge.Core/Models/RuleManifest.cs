namespace ChineseChunkJudge.Core.Models;

/// <summary>
/// GET /rules 里的一条规则。与报告中的 AppliedRule 分开：
/// 两边字段现在碰巧相同，任一侧以后加字段都不能把另一侧带出 Schema。
/// </summary>
public sealed record RuleDescriptor
{
    public required string RuleId { get; init; }

    public required string Description { get; init; }

    public required ChunkIssueSeverity Severity { get; init; }

    public required bool IsBaseRule { get; init; }
}

/// <summary>GET /rules 的响应体：契约版本，加上当前引擎里全部生效规则。</summary>
public sealed record RuleManifest
{
    public required string ContractVersion { get; init; }

    public required IReadOnlyList<RuleDescriptor> Rules { get; init; }
}
