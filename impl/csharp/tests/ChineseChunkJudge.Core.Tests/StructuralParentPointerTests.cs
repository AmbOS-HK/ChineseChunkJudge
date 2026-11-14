using ChineseChunkJudge.Core.Builders;
using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>结构父指针只补 null、不覆盖非空值（契约 §7.1 / CCJ-014）。</summary>
public sealed class StructuralParentPointerTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Build_KeepsDistinctExplicitParents()
    {
        var chunk = CreateChunk(0, "正文。") with
        {
            Relations = new ChunkRelations
            {
                ParentChunkId = "parentA#0",
                StructuralParentChunkId = "parentB#0"
            }
        };

        var linked = new ChunkRelationBuilder().Build([chunk]);

        Assert.Equal("parentA#0", linked[0].Relations.ParentChunkId);
        Assert.Equal("parentB#0", linked[0].Relations.StructuralParentChunkId);
    }

    [Fact]
    public void Build_FillsStructuralParentFromLegacyParent()
    {
        var chunk = CreateChunk(0, "正文。") with
        {
            Relations = new ChunkRelations { ParentChunkId = "parentA#0" }
        };

        var linked = new ChunkRelationBuilder().Build([chunk]);

        Assert.Equal("parentA#0", linked[0].Relations.StructuralParentChunkId);
    }

    [Fact]
    public void Build_FillsLegacyParentFromStructuralParent()
    {
        var chunk = CreateChunk(0, "正文。") with
        {
            Relations = new ChunkRelations { StructuralParentChunkId = "parentB#0" }
        };

        var linked = new ChunkRelationBuilder().Build([chunk]);

        Assert.Equal("parentB#0", linked[0].Relations.ParentChunkId);
    }

    [Fact]
    public void Build_InfersBothParentsFromPrecedingHeading()
    {
        var heading = CreateChunk(0, "第一章", unitKind: ContractConstants.HeadingUnitKind, level: 1);
        var body = CreateChunk(1, "正文。");

        var linked = new ChunkRelationBuilder().Build([heading, body]);

        Assert.Equal("structure@600#0", linked[1].Relations.ParentChunkId);
        Assert.Equal("structure@600#0", linked[1].Relations.StructuralParentChunkId);
    }

    [Fact]
    public void JudgeContractChunks_ReportsAliasMismatch_CCJ014()
    {
        var metadata = CreateMetadata();
        var document = new DocumentFeature
        {
            DocumentId = "doc-parent",
            Metadata = metadata,
            Content = "正文。",
            Language = "zh"
        };
        var chunk = CreateChunk(0, "正文。") with
        {
            Relations = new ChunkRelations
            {
                ParentChunkId = "parentA#0",
                StructuralParentChunkId = "parentB#0"
            }
        };
        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 600 };

        var report = new ChunkJudgeService().JudgeContractChunks(document, [chunk], options);

        Assert.Contains(
            report.Issues,
            issue => issue.RuleId == ChunkRuleIds.ParentAliasConsistency
                     && issue.Severity == ChunkIssueSeverity.Warning);
    }

    private static DocumentMetadata CreateMetadata() => new()
    {
        Title = "结构父样例",
        SourcePath = "docs/parent.md",
        DocumentType = "markdown",
        PageInfo = "1",
        CreateTime = FixedTime
    };

    private static ContractChunk CreateChunk(int index, string text, string? unitKind = null, int? level = null) => new()
    {
        ChunkId = $"structure@600#{index}",
        Index = index,
        Text = text,
        StartOffset = 0,
        EndOffset = text.Length,
        Strategy = "structure@600",
        Metadata = CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text),
        UnitKind = unitKind,
        StructureLevel = level
    };
}
