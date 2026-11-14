using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using ChineseChunkJudge.Core.Validators;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>CCJ-018..021（0.10.0 新增基础规则）与 effectiveOptions 回显的单元测试。</summary>
public sealed class ContractConsistencyRulesTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void CCJ018_TargetSizeOutOfRange_IsError()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0, strategy: "structure@2000");
        var options = CreateOptions("structure", 2000);

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyConsistency);
    }

    [Fact]
    public void CCJ018_StrategyTypeMismatch_IsError()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0, strategy: "sentence@600");
        var options = CreateOptions("structure", 600);

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyConsistency && i.Detail!["field"] == "strategyType");
    }

    [Fact]
    public void CCJ018_TargetSizeMismatch_IsError()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0, strategy: "structure@900");
        var options = CreateOptions("structure", 600);

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyConsistency && i.Detail!["field"] == "targetSize");
    }

    [Fact]
    public void CCJ018_Consistent_NoIssue()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0, strategy: "structure@600");
        var options = CreateOptions("structure", 600);

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyConsistency);
    }

    [Fact]
    public void CCJ018_InvalidStrategyName_LeavesItToCCJ002()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0, strategy: "结构@600#坏");
        var options = CreateOptions("structure", 600);

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyName);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyConsistency);
    }

    [Fact]
    public void CCJ019_OverConfiguredMax_IsWarning()
    {
        string content = new string('文', 1100);
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0);
        var options = CreateOptions("structure", 600) with { MaxChunkSize = 600 };

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.True(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.OversizedAgainstMax);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.OversizedChunk);
    }

    [Fact]
    public void CCJ019_Beyond1500_OnlyCCJ004()
    {
        string content = new string('文', 1600);
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0);
        var options = CreateOptions("structure", 600) with { MaxChunkSize = 1200 };

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.OversizedChunk);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.OversizedAgainstMax);
    }

    [Fact]
    public void CCJ019_DefaultMaxChunkSize_NeverTriggers()
    {
        string content = new string('文', 1520);
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0);
        var options = CreateOptions("structure", 600);

        var result = new ChunkValidator().Validate(document, [chunk], options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.OversizedAgainstMax);
    }

    [Fact]
    public void CCJ020_MetadataMismatch_IsError()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0) with
        {
            Metadata = CreateMetadata() with { Title = "另一份文档" }
        };

        var result = new ChunkValidator().Validate(document, [chunk]);

        Assert.False(result.Passed);
        Assert.Contains(
            result.Issues,
            i => i.RuleId == ChunkRuleIds.MetadataEquality && i.Detail!["field"] == "title");
    }

    [Fact]
    public void CCJ020_MetadataEqual_NoIssue()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0);

        var result = new ChunkValidator().Validate(document, [chunk]);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.MetadataEquality);
    }

    [Fact]
    public void CCJ021_ParentIndexGap_AndCharCountMismatch_AreErrors()
    {
        string content = "第一段。第二段。";
        var document = CreateDocument(content);
        var chunks = new[] { CreateChunk(0, "第一段。", 0) };
        var parents = new[]
        {
            CreateParent(0, "第一段。", 0),
            CreateParent(2, "第二段。", 4)
        };
        var pairs = new[]
        {
            new ContextParentChildPair
            {
                ContextParentChunkId = "structure@600#p0",
                ChildChunkIds = ["structure@600#0"]
            }
        };
        var options = CreateOptions("structure", 600) with { ParentChild = new ParentChildOptions { Enabled = true } };

        var result = new ChunkValidator().Validate(document, chunks, options, parents, pairs);

        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentStructure && i.Detail!["field"] == "index");
        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.ParentCoverage);
    }

    [Fact]
    public void CCJ021_ParentOffsetOutOfRange_IsError()
    {
        string content = "第一段。";
        var document = CreateDocument(content);
        var chunks = new[] { CreateChunk(0, "第一段。", 0) };
        var parents = new[] { CreateParent(0, "第一段。", 0) with { EndOffset = 99 } };
        var pairs = new[]
        {
            new ContextParentChildPair
            {
                ContextParentChunkId = "structure@600#p0",
                ChildChunkIds = ["structure@600#0"]
            }
        };
        var options = CreateOptions("structure", 600) with { ParentChild = new ParentChildOptions { Enabled = true } };

        var result = new ChunkValidator().Validate(document, chunks, options, parents, pairs);

        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.ParentStructure && i.Detail!["field"] == "range");
    }

    [Fact]
    public void EffectiveOptions_IsEchoedOnReport()
    {
        string content = "正文内容。";
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0);
        var options = CreateOptions("structure", 600) with { MaxChunkSize = 900 };

        var report = new ChunkJudgeService().JudgeContractChunks(document, [chunk], options);

        Assert.NotNull(report.EffectiveOptions);
        Assert.Equal(options.TargetSize, report.EffectiveOptions!.TargetSize);
        Assert.Equal(options.MaxChunkSize, report.EffectiveOptions.MaxChunkSize);
    }

    private static DocumentMetadata CreateMetadata() => new()
    {
        Title = "一致性样例",
        SourcePath = "docs/consistency.md",
        DocumentType = "markdown",
        PageInfo = "1",
        CreateTime = FixedTime
    };

    private static DocumentFeature CreateDocument(string content) => new()
    {
        DocumentId = "doc-consistency",
        Metadata = CreateMetadata(),
        Content = content,
        Language = "zh"
    };

    private static ChunkStrategyOptions CreateOptions(string strategyType, int targetSize) => new()
    {
        StrategyType = strategyType,
        TargetSize = targetSize
    };

    private static ContractChunk CreateChunk(int index, string text, int startOffset, string strategy = "structure@600") => new()
    {
        ChunkId = $"{strategy}#{index}",
        Index = index,
        Text = text,
        StartOffset = startOffset,
        EndOffset = startOffset + text.Length,
        Strategy = strategy,
        Metadata = CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text)
    };

    private static ContextParentChunk CreateParent(int index, string text, int startOffset) => new()
    {
        ParentChunkId = $"structure@600#p{index}",
        Index = index,
        Text = text,
        StartOffset = startOffset,
        EndOffset = startOffset + text.Length,
        Strategy = "structure@600",
        Metadata = CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text)
    };
}
