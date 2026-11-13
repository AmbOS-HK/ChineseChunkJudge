using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using ChineseChunkJudge.Core.Validators;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>双库（Parent/Child）规则 CCJ-008..CCJ-014 的单元测试。</summary>
public sealed class ParentChildRulesTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8));

    private const string SampleContent = "第一段。第二段。第三段。第四段。";

    [Fact]
    public void ValidDualLibrary_PassesAndSummarizes()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        var service = new ChunkJudgeService();

        var report = service.JudgeContractChunks(document, chunks, EnabledOptions(), parents, pairs);

        Assert.True(report.Passed);
        Assert.DoesNotContain(report.Issues, i => i.RuleId is ChunkRuleIds.ParentChildPairStructure
            or ChunkRuleIds.ParentCoverage or ChunkRuleIds.ChildWithinParent or ChunkRuleIds.ParentChildDisabled);
        Assert.Equal(2, report.Summary.ParentChunkCount);
        Assert.Equal(4, report.Summary.ChildWithParentCount);
        Assert.Equal(1d, report.Summary.ParentCoverageRatio);
        Assert.Equal(1d, report.Summary.ChildWithParentRatio);
        Assert.Equal(8, report.Summary.ParentCharP95);
        Assert.Equal(0, report.Summary.OversizedParentChunkCount);
        Assert.Contains(report.AppliedRules, r => r.RuleId == ChunkRuleIds.ParentCoverage && r.IsBaseRule);
    }

    [Fact]
    public void DisabledWithParentChunks_Fails_CCJ013()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 600 };

        var result = new ChunkValidator().Validate(document, chunks, options, parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildDisabled && i.Severity == ChunkIssueSeverity.Error);
    }

    [Fact]
    public void AliasMismatch_Warns_CCJ014()
    {
        var document = CreateDocument(SampleContent);
        var chunk = CreateChunk(0, "第一段。", 0) with
        {
            Relations = new ChunkRelations
            {
                ParentChunkId = "structure@600#9",
                StructuralParentChunkId = "structure@600#8"
            }
        };

        var result = new ChunkValidator().Validate(document, new[] { chunk });

        Assert.True(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentAliasConsistency && i.Severity == ChunkIssueSeverity.Warning);
    }

    [Fact]
    public void OversizedParent_Warns_CCJ009_IndependentFromChildCount()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        var service = new ChunkJudgeService();

        var report = service.JudgeContractChunks(document, chunks, EnabledOptions(parentMaxChars: 4), parents, pairs);

        Assert.True(report.Passed);
        Assert.Equal(2, report.Issues.Count(i => i.RuleId == ChunkRuleIds.ParentOversized));
        Assert.Equal(2, report.Summary.OversizedParentChunkCount);
        Assert.Equal(0, report.Summary.OversizedChunkCount);
    }

    [Fact]
    public void EmptyChildIds_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, _) = CreateValidDualLibrary();
        var pairs = new[]
        {
            new ContextParentChildPair { ContextParentChunkId = "structure@600#p0", ChildChunkIds = [] },
            new ContextParentChildPair { ContextParentChunkId = "structure@600#p1", ChildChunkIds = ["structure@600#2", "structure@600#3"] }
        };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure);
    }

    [Fact]
    public void ChildInTwoParents_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, _) = CreateValidDualLibrary();
        var pairs = new[]
        {
            new ContextParentChildPair { ContextParentChunkId = "structure@600#p0", ChildChunkIds = ["structure@600#0", "structure@600#1"] },
            new ContextParentChildPair { ContextParentChunkId = "structure@600#p1", ChildChunkIds = ["structure@600#1", "structure@600#2", "structure@600#3"] }
        };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure && i.Message.Contains("归属多个"));
    }

    [Fact]
    public void ParentOverlap_Fails_CCJ010()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents[1] = parents[1] with { StartOffset = 4, Text = "第二段。第三段。第四段。" };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentCoverage);
    }

    [Fact]
    public void ParentGap_Fails_CCJ010()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents.RemoveAt(1);

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentCoverage);
    }

    [Fact]
    public void ReferenceCycle_Warns_CCJ011()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents[0] = parents[0] with { RelatedContextParentIds = ["structure@600#p1"] };
        parents[1] = parents[1] with { RelatedContextParentIds = ["structure@600#p0"] };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.True(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentReferenceCycle && i.Severity == ChunkIssueSeverity.Warning);
    }

    [Fact]
    public void SelfReference_Warns_CCJ011()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents[0] = parents[0] with { RelatedContextParentIds = ["structure@600#p0"] };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.True(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentReferenceCycle);
    }

    [Fact]
    public void ChildOutsideParent_Fails_CCJ012()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        chunks[3] = chunks[3] with
        {
            Relations = chunks[3].Relations with { ContextParentChunkId = "structure@600#p0" }
        };
        pairs[1] = pairs[1] with { ChildChunkIds = ["structure@600#2"] };
        pairs[0] = pairs[0] with { ChildChunkIds = ["structure@600#0", "structure@600#1", "structure@600#3"] };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ChildWithinParent && i.ChunkId == "structure@600#3");
    }

    [Fact]
    public void InvalidParentMaxChars_Fails_CCJ007()
    {
        var document = CreateDocument(SampleContent);
        var chunk = CreateChunk(0, "第一段。", 0);
        var options = new ChunkStrategyOptions
        {
            StrategyType = "structure",
            TargetSize = 600,
            ParentChild = new ParentChildOptions { Enabled = true, ParentMaxChars = 0 }
        };

        var result = new ChunkValidator().Validate(document, new[] { chunk }, options);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.InvalidOptions && i.Detail!["option"] == "parentMaxChars");
    }

    [Fact]
    public void RawJudge_DerivesChildContextParentFromPairs()
    {
        var document = CreateDocument(SampleContent);
        var (_, parents, pairs) = CreateValidDualLibrary();
        var raws = new[]
        {
            new RawChunk { Index = 0, Text = "第一段。", StartOffset = 0, EndOffset = 4 },
            new RawChunk { Index = 1, Text = "第二段。", StartOffset = 4, EndOffset = 8 },
            new RawChunk { Index = 2, Text = "第三段。", StartOffset = 8, EndOffset = 12 },
            new RawChunk { Index = 3, Text = "第四段。", StartOffset = 12, EndOffset = 16 }
        };

        var report = new ChunkJudgeService().Judge(document, raws, EnabledOptions(), parents, pairs);

        Assert.True(report.Passed);
        Assert.Equal(4, report.Summary.ChildWithParentCount);
        Assert.Equal(1d, report.Summary.ParentCoverageRatio);
        Assert.Equal(1d, report.Summary.ChildWithParentRatio);
        Assert.DoesNotContain(report.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure);
    }

    [Fact]
    public void DisabledWithPairsOnly_Fails_CCJ013()
    {
        var document = CreateDocument(SampleContent);
        var chunks = new List<ContractChunk> { CreateChunk(0, "第一段。", 0) };
        var pairs = new List<ContextParentChildPair>
        {
            new() { ContextParentChunkId = "structure@600#p0", ChildChunkIds = ["structure@600#0"] }
        };
        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 600 };

        var result = new ChunkValidator().Validate(document, chunks, options, [], pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildDisabled && i.Detail!["field"] == "pairs");
    }

    [Fact]
    public void SelfReference_ReportsExactlyOnce()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents[0] = parents[0] with { RelatedContextParentIds = ["structure@600#p0"] };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.Single(result.Issues, i => i.RuleId == ChunkRuleIds.ParentReferenceCycle);
    }

    [Fact]
    public void DuplicateParentIds_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents.Add(parents[0] with { });

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure && i.Message.Contains("重复出现"));
    }

    [Fact]
    public void DuplicatePairsForSameParent_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        pairs.Add(pairs[0] with { });

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure && i.Message.Contains("只允许一条配对"));
    }

    [Fact]
    public void DuplicateChildWithinPair_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        pairs[0] = pairs[0] with { ChildChunkIds = ["structure@600#0", "structure@600#0", "structure@600#1"] };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure && i.Message.Contains("内子块"));
    }

    [Fact]
    public void DuplicateChildIds_Fails_CCJ016()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        chunks[1] = chunks[1] with { ChunkId = chunks[0].ChunkId };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ChunkIdentity && i.Message.Contains("重复出现"));
    }

    [Fact]
    public void DanglingRelatedParent_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        parents[0] = parents[0] with { RelatedContextParentIds = ["structure@600#missing"] };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentChildPairStructure && i.Message.Contains("跨父块引用") && i.Message.Contains("无法在父块集合中解析"));
    }

    [Fact]
    public void PairChildMissingContextParent_Fails_CCJ008()
    {
        var document = CreateDocument(SampleContent);
        var (chunks, parents, pairs) = CreateValidDualLibrary();
        chunks[0] = chunks[0] with { Relations = new ChunkRelations() };

        var result = new ChunkValidator().Validate(document, chunks, EnabledOptions(), parents, pairs);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i =>
            i.RuleId == ChunkRuleIds.ParentChildPairStructure &&
            i.Message.Contains("与父侧配对") &&
            i.Message.Contains("不一致"));
    }

    private static (List<ContractChunk> Chunks, List<ContextParentChunk> Parents, List<ContextParentChildPair> Pairs) CreateValidDualLibrary()
    {
        var chunks = new List<ContractChunk>
        {
            CreateChunk(0, "第一段。", 0, "structure@600#p0"),
            CreateChunk(1, "第二段。", 4, "structure@600#p0"),
            CreateChunk(2, "第三段。", 8, "structure@600#p1"),
            CreateChunk(3, "第四段。", 12, "structure@600#p1")
        };

        var parents = new List<ContextParentChunk>
        {
            CreateParent(0, "第一段。第二段。", 0),
            CreateParent(1, "第三段。第四段。", 8)
        };

        var pairs = new List<ContextParentChildPair>
        {
            new() { ContextParentChunkId = "structure@600#p0", ChildChunkIds = ["structure@600#0", "structure@600#1"] },
            new() { ContextParentChunkId = "structure@600#p1", ChildChunkIds = ["structure@600#2", "structure@600#3"] }
        };

        return (chunks, parents, pairs);
    }

    private static ChunkStrategyOptions EnabledOptions(int parentMaxChars = ContractConstants.ParentMaxCharsDebugDefault) => new()
    {
        StrategyType = "structure",
        TargetSize = 600,
        ParentChild = new ParentChildOptions { Enabled = true, ParentMaxChars = parentMaxChars }
    };

    private static DocumentFeature CreateDocument(string content) => new()
    {
        DocumentId = "doc-dual",
        Metadata = CreateMetadata(),
        Content = content,
        Language = "zh"
    };

    private static DocumentMetadata CreateMetadata() => new()
    {
        Title = "双库样例",
        SourcePath = "docs/dual.md",
        DocumentType = "markdown",
        PageInfo = "1",
        CreateTime = FixedTime
    };

    private static ContractChunk CreateChunk(int index, string text, int start, string? contextParentId = null) => new()
    {
        ChunkId = $"structure@600#{index}",
        Index = index,
        Text = text,
        StartOffset = start,
        EndOffset = start + text.Length,
        Strategy = "structure@600",
        Metadata = CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text),
        Relations = new ChunkRelations { ContextParentChunkId = contextParentId }
    };

    private static ContextParentChunk CreateParent(int index, string text, int start) => new()
    {
        ParentChunkId = $"structure@600#p{index}",
        Index = index,
        Text = text,
        StartOffset = start,
        EndOffset = start + text.Length,
        Strategy = "structure@600",
        Metadata = CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text)
    };
}
