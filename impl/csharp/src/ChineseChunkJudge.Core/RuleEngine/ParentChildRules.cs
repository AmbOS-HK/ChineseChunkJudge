using System.Globalization;
using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.RuleEngine;

/// <summary>双库规则的共享上下文读取。</summary>
internal static class ParentChildRuleContext
{
    public static bool IsEnabled(ChunkValidationContext context) =>
        context.Options?.ParentChild?.Enabled ?? false;

    public static int ParentMaxChars(ChunkValidationContext context) =>
        context.Options?.ParentChild?.ParentMaxChars ?? ContractConstants.ParentMaxCharsDebugDefault;
}

/// <summary>CCJ-013：双库关闭时不得产出父块相关字段。</summary>
public sealed class ParentChildDisabledRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentChildDisabled;

    public string Description => "parentChild.enabled=false 时，输出不得携带 parentChunks 或 contextParentChunkId，否则判定不通过。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ParentChildRuleContext.IsEnabled(context))
        {
            yield break;
        }

        if (context.ParentChunks.Count > 0)
        {
            yield return new ChunkIssue
            {
                RuleId = RuleId,
                Severity = Severity,
                Message = $"双库开关 enabled=false，但输出携带 {context.ParentChunks.Count} 个上下文父块。",
                Detail = new Dictionary<string, string>
                {
                    ["field"] = "parentChunks",
                    ["count"] = context.ParentChunks.Count.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        if (context.Pairs.Count > 0)
        {
            yield return new ChunkIssue
            {
                RuleId = RuleId,
                Severity = Severity,
                Message = $"双库开关 enabled=false，但输出携带 {context.Pairs.Count} 条父子配对（内含 contextParentChunkId 声明）。",
                Detail = new Dictionary<string, string>
                {
                    ["field"] = "pairs",
                    ["count"] = context.Pairs.Count.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        foreach (var chunk in context.Chunks)
        {
            if (chunk.Relations.ContextParentChunkId is not null)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"双库开关 enabled=false，但切片 {chunk.ChunkId} 携带 contextParentChunkId={chunk.Relations.ContextParentChunkId}。",
                    Detail = new Dictionary<string, string>
                    {
                        ["field"] = "contextParentChunkId",
                        ["value"] = chunk.Relations.ContextParentChunkId
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-014：弃用别名与新字段值一致性。</summary>
public sealed class ParentAliasConsistencyRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentAliasConsistency;

    public string Description => "parentChunkId（deprecated）与 structuralParentChunkId 同时出现时值必须一致。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var chunk in context.Chunks)
        {
            var legacy = chunk.Relations.ParentChunkId;
            var structural = chunk.Relations.StructuralParentChunkId;
            if (legacy is null || structural is null)
            {
                continue;
            }

            if (!string.Equals(legacy, structural, StringComparison.Ordinal))
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"切片 {chunk.ChunkId} 的 parentChunkId={legacy} 与 structuralParentChunkId={structural} 不一致。",
                    Detail = new Dictionary<string, string>
                    {
                        ["parentChunkId"] = legacy,
                        ["structuralParentChunkId"] = structural
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-009：上下文父块长度告警，与 CCJ-004 作用域拆分、独立计数。</summary>
public sealed class ParentOversizedRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentOversized;

    public string Description => "上下文父块字符数超过 parentMaxChars 时告警（不阻断）。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int threshold = ParentChildRuleContext.ParentMaxChars(context);
        foreach (var parent in context.ParentChunks)
        {
            int charCount = ChunkTextMetrics.CountCharacters(parent.Text);
            if (charCount > threshold)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = parent.ParentChunkId,
                    Message = $"上下文父块 {parent.ParentChunkId} 字符数 {charCount} 超过告警阈值 {threshold}。",
                    Detail = new Dictionary<string, string>
                    {
                        ["charCount"] = charCount.ToString(CultureInfo.InvariantCulture),
                        ["threshold"] = threshold.ToString(CultureInfo.InvariantCulture)
                    }
                };
            }
        }
    }
}

/// <summary>CCJ-008：父子配对结构完整性。</summary>
public sealed class ParentChildPairStructureRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentChildPairStructure;

    public string Description => "childChunkIds 非空、配对 ID 可解析、父子两侧一致、每个子块仅归属一个上下文父块，父块 ID 唯一且跨父块引用可解析。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ParentChildRuleContext.IsEnabled(context))
        {
            yield break;
        }

        var parentIds = context.ParentChunks.Select(p => p.ParentChunkId).ToHashSet(StringComparer.Ordinal);
        var childIds = context.Chunks.Select(c => c.ChunkId).ToHashSet(StringComparer.Ordinal);
        var childToParents = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var group in context.ParentChunks.GroupBy(p => p.ParentChunkId, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            yield return Issue(group.Key, $"父块 ID {group.Key} 重复出现 {group.Count()} 次，父块标识必须唯一。");
        }

        foreach (var parent in context.ParentChunks)
        {
            foreach (var relatedId in parent.RelatedContextParentIds)
            {
                if (!parentIds.Contains(relatedId))
                {
                    yield return Issue(parent.ParentChunkId, $"父块 {parent.ParentChunkId} 的跨父块引用 {relatedId} 无法在父块集合中解析。");
                }
            }
        }

        foreach (var group in context.Pairs.GroupBy(p => p.ContextParentChunkId, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            yield return Issue(group.Key, $"父块 {group.Key} 存在 {group.Count()} 条配对，同一父块只允许一条配对。");
        }

        foreach (var pair in context.Pairs)
        {
            if (pair.ChildChunkIds is not null)
            {
                foreach (var duplicate in pair.ChildChunkIds.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                {
                    yield return Issue(pair.ContextParentChunkId, $"父块 {pair.ContextParentChunkId} 的 childChunkIds 内子块 {duplicate.Key} 重复 {duplicate.Count()} 次。");
                }
            }

            if (!parentIds.Contains(pair.ContextParentChunkId))
            {
                yield return Issue(null, $"配对的 contextParentChunkId={pair.ContextParentChunkId} 无法在父块集合中解析。");
            }

            if (pair.ChildChunkIds is null || pair.ChildChunkIds.Count == 0)
            {
                yield return Issue(pair.ContextParentChunkId, "childChunkIds 为空，配对必须至少包含一个子块。");
                continue;
            }

            foreach (var childId in pair.ChildChunkIds)
            {
                if (!childIds.Contains(childId))
                {
                    yield return Issue(pair.ContextParentChunkId, $"childChunkIds 中的 {childId} 无法在子块集合中解析。");
                }

                if (!childToParents.TryGetValue(childId, out var parents))
                {
                    parents = [];
                    childToParents[childId] = parents;
                }

                parents.Add(pair.ContextParentChunkId);
            }
        }

        foreach (var (childId, parents) in childToParents)
        {
            if (parents.Distinct(StringComparer.Ordinal).Count() > 1)
            {
                yield return Issue(null, $"子块 {childId} 归属多个上下文父块：{string.Join(", ", parents.Distinct(StringComparer.Ordinal))}。");
            }
        }

        foreach (var chunk in context.Chunks)
        {
            var parentId = chunk.Relations.ContextParentChunkId;
            if (parentId is null)
            {
                continue;
            }

            if (!parentIds.Contains(parentId))
            {
                yield return Issue(chunk.ChunkId, $"切片 {chunk.ChunkId} 的 contextParentChunkId={parentId} 无法在父块集合中解析。");
                continue;
            }

            bool paired = context.Pairs.Any(p =>
                string.Equals(p.ContextParentChunkId, parentId, StringComparison.Ordinal) &&
                p.ChildChunkIds is not null &&
                p.ChildChunkIds.Contains(chunk.ChunkId, StringComparer.Ordinal));

            if (!paired)
            {
                yield return Issue(chunk.ChunkId, $"切片 {chunk.ChunkId} 声明归属 {parentId}，但父侧 childChunkIds 未包含该子块，两侧不一致。");
            }
        }
    }

    private ChunkIssue Issue(string? chunkId, string message) => new()
    {
        RuleId = RuleId,
        Severity = Severity,
        ChunkId = chunkId,
        Message = message
    };
}

/// <summary>CCJ-010：上下文父块互不重叠，且集合对原文做完整无重叠划分。</summary>
public sealed class ParentCoverageRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentCoverage;

    public string Description => "上下文父块须为原文精确子串、互不重叠，且拼接后等价于原文。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ParentChildRuleContext.IsEnabled(context) || context.ParentChunks.Count == 0)
        {
            yield break;
        }

        string content = context.Document.Content;
        var ordered = context.ParentChunks.OrderBy(p => p.StartOffset).ToList();

        foreach (var parent in ordered)
        {
            if (parent.StartOffset < 0 || parent.EndOffset <= parent.StartOffset || parent.EndOffset > content.Length)
            {
                yield return Issue(parent.ParentChunkId, $"父块 {parent.ParentChunkId} 偏移区间 [{parent.StartOffset}, {parent.EndOffset}) 越界或长度非正。");
                continue;
            }

            string expected = content.Substring(parent.StartOffset, parent.EndOffset - parent.StartOffset);
            if (!string.Equals(expected, parent.Text, StringComparison.Ordinal))
            {
                yield return Issue(parent.ParentChunkId, $"父块 {parent.ParentChunkId} 正文与原文区间不一致。");
            }
        }

        if (ordered[0].StartOffset != 0)
        {
            yield return Issue(ordered[0].ParentChunkId, $"父块集合未覆盖原文起始位置，首个父块起始偏移为 {ordered[0].StartOffset}。");
        }

        for (int i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            if (current.StartOffset < previous.EndOffset)
            {
                yield return Issue(current.ParentChunkId, $"父块 {previous.ParentChunkId} 与 {current.ParentChunkId} 区间重叠，违反无重叠约束。");
            }
            else if (current.StartOffset > previous.EndOffset)
            {
                yield return Issue(current.ParentChunkId, $"父块 {previous.ParentChunkId} 与 {current.ParentChunkId} 之间存在未覆盖区间 [{previous.EndOffset}, {current.StartOffset})。");
            }
        }

        var last = ordered[^1];
        if (last.EndOffset != content.Length)
        {
            yield return Issue(last.ParentChunkId, $"父块集合未覆盖原文末尾，最后父块结束偏移 {last.EndOffset}，正文长度 {content.Length}。");
        }
    }

    private ChunkIssue Issue(string chunkId, string message) => new()
    {
        RuleId = RuleId,
        Severity = Severity,
        ChunkId = chunkId,
        Message = message
    };
}

/// <summary>CCJ-011：跨父块引用不得成环（含自引用）。</summary>
public sealed class ParentReferenceCycleRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ParentReferenceCycle;

    public string Description => "跨父块引用构成有向无环图，检出环路（含自引用）告警。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var edges = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var parent in context.ParentChunks)
        {
            // 重复父块 ID 由 CCJ-008 报告，这里保留首个，避免字典构造抛异常中断判定。
            edges.TryAdd(parent.ParentChunkId, parent.RelatedContextParentIds);
        }

        foreach (var (parentId, related) in edges)
        {
            if (related.Contains(parentId, StringComparer.Ordinal))
            {
                yield return Issue(parentId, $"父块 {parentId} 自引用，构成环路。");
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var id in edges.Keys)
        {
            foreach (var cycle in Visit(id))
            {
                yield return cycle;
            }
        }

        IEnumerable<ChunkIssue> Visit(string id)
        {
            if (state.TryGetValue(id, out int s))
            {
                if (s == 2)
                {
                    yield break;
                }

                if (s == 1)
                {
                    int start = stack.IndexOf(id);
                    var path = stack.Skip(start).Append(id);
                    yield return Issue(id, $"跨父块引用成环：{string.Join(" -> ", path)}。");
                }

                yield break;
            }

            state[id] = 1;
            stack.Add(id);
            if (edges.TryGetValue(id, out var related))
            {
                foreach (var next in related)
                {
                    // 自引用已在上方单独报告，DFS 中跳过，避免同一问题重复计数。
                    if (string.Equals(next, id, StringComparison.Ordinal) || !edges.ContainsKey(next))
                    {
                        continue;
                    }

                    foreach (var issue in Visit(next))
                    {
                        yield return issue;
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
        }
    }

    private ChunkIssue Issue(string chunkId, string message) => new()
    {
        RuleId = RuleId,
        Severity = Severity,
        ChunkId = chunkId,
        Message = message
    };
}

/// <summary>CCJ-012：子块区间必须落在其归属上下文父块区间内。</summary>
public sealed class ChildWithinParentRule : IChunkRule
{
    public string RuleId => ChunkRuleIds.ChildWithinParent;

    public string Description => "子块派生自父块区间，子块区间必须 ⊆ 归属 contextParent 区间。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ParentChildRuleContext.IsEnabled(context))
        {
            yield break;
        }

        var parents = new Dictionary<string, ContextParentChunk>(StringComparer.Ordinal);
        foreach (var parent in context.ParentChunks)
        {
            parents.TryAdd(parent.ParentChunkId, parent);
        }
        foreach (var chunk in context.Chunks)
        {
            var parentId = chunk.Relations.ContextParentChunkId;
            if (parentId is null || !parents.TryGetValue(parentId, out var parent))
            {
                continue;
            }

            if (chunk.StartOffset < parent.StartOffset || chunk.EndOffset > parent.EndOffset)
            {
                yield return new ChunkIssue
                {
                    RuleId = RuleId,
                    Severity = Severity,
                    ChunkId = chunk.ChunkId,
                    Message = $"子块 {chunk.ChunkId} 区间 [{chunk.StartOffset}, {chunk.EndOffset}) 超出父块 {parentId} 区间 [{parent.StartOffset}, {parent.EndOffset})。",
                    Detail = new Dictionary<string, string>
                    {
                        ["contextParentChunkId"] = parentId,
                        ["childStart"] = chunk.StartOffset.ToString(CultureInfo.InvariantCulture),
                        ["childEnd"] = chunk.EndOffset.ToString(CultureInfo.InvariantCulture)
                    }
                };
            }
        }
    }
}
