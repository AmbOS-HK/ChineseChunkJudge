using ChineseChunkJudge.Core.Builders;
using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using ChineseChunkJudge.Core.Validators;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>契约判定规则与关系构建的单元测试。</summary>
public sealed class ChunkValidatorTests
{
    private const string SampleContent = "第一段内容。第二段内容。";
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ValidChunks_PassWithoutIssues()
    {
        var document = CreateDocument(SampleContent);
        var chunks = new[]
        {
            CreateChunk(0, "第一段内容。", 0),
            CreateChunk(1, "第二段内容。", 6)
        };

        var result = new ChunkValidator().Validate(document, chunks);

        Assert.True(result.Passed);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void EmptyChunk_ReportsWarning_CCJ003()
    {
        var document = CreateDocument("   ");
        var chunk = CreateChunk(0, "   ", 0);

        var result = new ChunkValidator().Validate(document, new[] { chunk });

        Assert.True(result.Passed);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.EmptyChunk && i.Severity == ChunkIssueSeverity.Warning);
    }

    [Fact]
    public void OversizedChunk_ReportsWarning_CCJ004()
    {
        var content = new string('文', 1600);
        var document = CreateDocument(content);
        var chunk = CreateChunk(0, content, 0);

        var result = new ChunkValidator().Validate(document, new[] { chunk });

        Assert.True(result.Passed);
        var issue = Assert.Single(result.Issues, i => i.RuleId == ChunkRuleIds.OversizedChunk);
        Assert.Equal(ChunkIssueSeverity.Warning, issue.Severity);
        Assert.Equal("1600", issue.Detail!["charCount"]);
    }

    [Fact]
    public void MissingMetadataField_ReportsError_CCJ001()
    {
        var document = CreateDocument(SampleContent, CreateMetadata(string.Empty));
        var chunk = CreateChunk(0, "第一段内容。", 0);

        var result = new ChunkValidator().Validate(document, new[] { chunk });

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.MetadataComplete
                                            && i.Detail!["field"] == DocumentMetadata.FieldTitle);
    }

    [Fact]
    public void InvalidStrategyName_ReportsError_CCJ002()
    {
        var document = CreateDocument(SampleContent);
        var chunk = CreateChunk(0, "第一段内容。", 0, strategy: "structure_600");

        var result = new ChunkValidator().Validate(document, new[] { chunk });

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.StrategyName);
    }

    [Fact]
    public void OffsetMismatch_ReportsError_CCJ005()
    {
        var document = CreateDocument(SampleContent);
        var chunk = CreateChunk(0, "第二段内容。", 0);

        var result = new ChunkValidator().Validate(document, new[] { chunk });

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.OffsetConsistency);
    }

    [Fact]
    public void InvalidOptions_ReportsError_CCJ007()
    {
        var document = CreateDocument(SampleContent);
        var chunk = CreateChunk(0, "第一段内容。", 0);
        var options = new ChunkStrategyOptions
        {
            StrategyType = "structure",
            TargetSize = 2000,
            MaxChunkSize = 200
        };

        var result = new ChunkValidator().Validate(document, new[] { chunk }, options);

        Assert.False(result.Passed);
        Assert.True(result.Issues.Count(i => i.RuleId == ChunkRuleIds.InvalidOptions) >= 2);
    }

    [Fact]
    public void BaseRule_CannotBeRemoved()
    {
        var engine = new ChunkRuleEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Unregister(ChunkRuleIds.EmptyChunk));
        Assert.Throws<InvalidOperationException>(() => engine.Unregister(ChunkRuleIds.OversizedChunk));
    }

    [Fact]
    public void ExtensionRule_CanBeRegisteredAndEvaluated()
    {
        var engine = new ChunkRuleEngine().Register(new MinimumLengthRule());
        var document = CreateDocument(SampleContent);
        var chunk = CreateChunk(0, "第一段内容。", 0);

        var result = new ChunkValidator(engine).Validate(document, new[] { chunk });

        Assert.True(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == "CCJ-X001");
        var applied = new ChunkRuleEngine().Register(new MinimumLengthRule());
        Assert.Contains(applied.Rules, r => r.RuleId == "CCJ-X001" && !r.IsBaseRule);
    }

    [Fact]
    public void RelationBuilder_LinksNeighboursAndHeadingParent()
    {
        var chunks = new[]
        {
            CreateChunk(0, "第一段内容。", 0, unitKind: ContractConstants.HeadingUnitKind, level: 1),
            CreateChunk(1, "第二段内容。", 0),
            CreateChunk(2, "第一段内容。", 0, unitKind: ContractConstants.HeadingUnitKind, level: 2),
            CreateChunk(3, "第二段内容。", 0)
        };

        var linked = new ChunkRelationBuilder().Build(chunks);

        Assert.Null(linked[0].Relations.PreviousChunkId);
        Assert.Equal("structure@600#1", linked[0].Relations.NextChunkId);
        Assert.Equal("structure@600#0", linked[1].Relations.PreviousChunkId);
        Assert.Equal("structure@600#0", linked[1].Relations.ParentChunkId);
        Assert.Equal("structure@600#0", linked[2].Relations.ParentChunkId);
        Assert.Equal("structure@600#2", linked[3].Relations.ParentChunkId);
    }

    [Fact]
    public void Service_ProducesPassingReport()
    {
        var document = CreateDocument(SampleContent);
        var rawChunks = new[]
        {
            new RawChunk { Index = 0, Text = "第一段内容。", StartOffset = 0, EndOffset = 6 },
            new RawChunk { Index = 1, Text = "第二段内容。", StartOffset = 6, EndOffset = 12 }
        };
        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 600 };

        var report = new ChunkJudgeService().Judge(document, rawChunks, options);

        Assert.True(report.Passed);
        Assert.Equal("structure@600", report.Strategy);
        Assert.Equal(2, report.Summary.ChunkCount);
        Assert.Equal(12, report.Summary.TotalCharCount);
        Assert.Equal(7, report.AppliedRules.Count);
    }

    [Fact]
    public void CoreAssembly_ContainsNoChunkExecutorImplementation()
    {
        // 边界约束（口径 B）：本仓库不内置切片算法，Core 中不允许出现 IChunkExecutor 的具体实现。
        var executors = typeof(DocumentFeature).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => typeof(Abstractions.IChunkExecutor).IsAssignableFrom(t))
            .ToList();

        Assert.Empty(executors);
    }

    private static DocumentFeature CreateDocument(string content, DocumentMetadata? metadata = null) => new()
    {
        DocumentId = "doc-test",
        Metadata = metadata ?? CreateMetadata("样例文档"),
        Content = content,
        Language = "zh"
    };

    private static DocumentMetadata CreateMetadata(string title = "样例文档") => new()
    {
        Title = title,
        SourcePath = "docs/sample.md",
        DocumentType = "markdown",
        PageInfo = "1",
        CreateTime = FixedTime
    };

    private static ContractChunk CreateChunk(
        int index,
        string text,
        int start,
        string strategy = "structure@600",
        DocumentMetadata? metadata = null,
        string? unitKind = null,
        int? level = null) => new()
    {
        ChunkId = $"{strategy}#{index}",
        Index = index,
        Text = text,
        StartOffset = start,
        EndOffset = start + text.Length,
        Strategy = strategy,
        Metadata = metadata ?? CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text),
        UnitKind = unitKind,
        StructureLevel = level
    };

    /// <summary>扩展规则示例：切片字符数少于 60 时告警。</summary>
    private sealed class MinimumLengthRule : IChunkRule
    {
        public string RuleId => "CCJ-X001";

        public string Description => "切片字符数少于 60 时告警。";

        public ChunkIssueSeverity Severity => ChunkIssueSeverity.Warning;

        public bool IsBaseRule => false;

        public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
        {
            foreach (var chunk in context.Chunks)
            {
                if (ChunkTextMetrics.CountCharacters(chunk.Text) < 60)
                {
                    yield return new ChunkIssue
                    {
                        RuleId = RuleId,
                        Severity = Severity,
                        ChunkId = chunk.ChunkId,
                        Message = $"切片 {chunk.ChunkId} 字符数不足 60。",
                        Detail = new Dictionary<string, string> { ["charCount"] = ChunkTextMetrics.CountCharacters(chunk.Text).ToString() }
                    };
                }
            }
        }
    }
}
