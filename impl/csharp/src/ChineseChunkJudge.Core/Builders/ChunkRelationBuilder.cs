using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.Builders;

/// <summary>
/// 关系构建器：按 index 顺序补 previousChunkId / nextChunkId，
/// 并按契约 7.1 解析 parentChunkId。
/// </summary>
public sealed class ChunkRelationBuilder
{
    /// <summary>构建关系。显式指定的 parentChunkId 会被保留。</summary>
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
            string? parent = ResolveParent(ordered, i, chunk);

            result.Add(chunk with
            {
                Relations = chunk.Relations with
                {
                    PreviousChunkId = previous,
                    NextChunkId = next,
                    ParentChunkId = parent
                }
            });
        }

        return result;
    }

    private static string? ResolveParent(IReadOnlyList<ContractChunk> ordered, int index, ContractChunk chunk)
    {
        if (!string.IsNullOrWhiteSpace(chunk.Relations.ParentChunkId))
        {
            return chunk.Relations.ParentChunkId;
        }

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
