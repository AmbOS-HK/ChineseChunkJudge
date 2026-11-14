using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.Builders;

/// <summary>
/// 关系构建器：按 index 重算 previousChunkId / nextChunkId，
/// 并按契约 7.1 补齐结构父指针。非空值一律保留，只填 null。
/// </summary>
public sealed class ChunkRelationBuilder
{
    /// <summary>构建关系。两个结构父指针都已给出时各留原值；只缺一侧时用另一侧补齐；两侧都缺时才按 heading 推断。</summary>
    public IReadOnlyList<ContractChunk> Build(IReadOnlyList<ContractChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var ordered = chunks.OrderBy(c => c.Index).ToList();
        var result = new List<ContractChunk>(ordered.Count);

        for (int i = 0; i < ordered.Count; i++)
        {
            var chunk = ordered[i];
            string? previous = i > 0 ? ordered[i - 1].ChunkId : null;
            string? next = i < ordered.Count - 1 ? ordered[i + 1].ChunkId : null;
            var (parent, structural) = ResolveStructuralParents(ordered, i, chunk);

            result.Add(chunk with
            {
                Relations = chunk.Relations with
                {
                    PreviousChunkId = previous,
                    NextChunkId = next,
                    ParentChunkId = parent,
                    StructuralParentChunkId = structural
                }
            });
        }

        return result;
    }

    private static (string? Parent, string? Structural) ResolveStructuralParents(
        IReadOnlyList<ContractChunk> ordered,
        int index,
        ContractChunk chunk)
    {
        string? parent = chunk.Relations.ParentChunkId;
        string? structural = chunk.Relations.StructuralParentChunkId;

        if (parent is not null && structural is not null)
        {
            return (parent, structural);
        }

        if (parent is not null)
        {
            return (parent, parent);
        }

        if (structural is not null)
        {
            return (structural, structural);
        }

        string? inferred = InferHeadingParent(ordered, index, chunk);
        return (inferred, inferred);
    }

    private static string? InferHeadingParent(IReadOnlyList<ContractChunk> ordered, int index, ContractChunk chunk)
    {
        bool isHeading = IsHeading(chunk);
        int? level = chunk.StructureLevel;

        for (int i = index - 1; i >= 0; i--)
        {
            var candidate = ordered[i];
            if (!IsHeading(candidate))
            {
                continue;
            }

            if (isHeading && level.HasValue && candidate.StructureLevel.HasValue
                && candidate.StructureLevel.Value >= level.Value)
            {
                continue;
            }

            return candidate.ChunkId;
        }

        return null;
    }

    private static bool IsHeading(ContractChunk chunk) =>
        string.Equals(chunk.UnitKind, ContractConstants.HeadingUnitKind, StringComparison.OrdinalIgnoreCase);
}
