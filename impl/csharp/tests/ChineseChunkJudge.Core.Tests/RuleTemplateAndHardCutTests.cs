using ChineseChunkJudge.Core.Builders;
using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using ChineseChunkJudge.Core.Validators;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>C-10 模板选型判定与 C-11 / CCJ-015 恰长硬切的单元测试。</summary>
public sealed class RuleTemplateAndHardCutTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Resolve_ArticleDocument_HitsRegexRule()
    {
        var template = CreateTemplate();

        var decision = new TemplateResolver().Resolve(template, ["law"], [ChunkTraits.ArticleNumbering]);

        Assert.False(decision.UsedFallback);
        Assert.Equal(0, decision.MatchedRuleIndex);
        Assert.Equal(ChunkModes.Regex, decision.ChunkMode);
        Assert.Equal("(?=第[一二三四五六七八九十百零〇0-9]+[条章])", decision.Pattern);
        Assert.Equal(1200, decision.ChunkSize);
    }

    [Fact]
    public void Resolve_WeakStructure_HitsSecondRule_NotFallback()
    {
        var template = CreateTemplate();

        var decision = new TemplateResolver().Resolve(template, ["law"], [ChunkTraits.WeakStructure]);

        Assert.False(decision.UsedFallback);
        Assert.Equal(1, decision.MatchedRuleIndex);
        Assert.Equal(ChunkModes.Smart, decision.ChunkMode);
        Assert.Null(decision.Pattern);
        Assert.Equal(1200, decision.ChunkSize);
    }

    [Fact]
    public void Resolve_Unmatched_UsesMandatorySmartFallback()
    {
        var template = CreateTemplate();

        var decision = new TemplateResolver().Resolve(template, ["plans"], [ChunkTraits.DenseTable]);

        Assert.True(decision.UsedFallback);
        Assert.Null(decision.MatchedRuleIndex);
        Assert.Equal(ChunkModes.Smart, decision.ChunkMode);
        Assert.Equal(1000, decision.ChunkSize);
    }

    [Fact]
    public void Resolve_RegexRuleWithoutPattern_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Rules = [template.Decision.Rules[0] with { Pattern = null }, template.Decision.Rules[1]]
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.ArticleNumbering]));
    }

    [Fact]
    public void Resolve_NonSmartFallback_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Fallback = template.Decision.Fallback with { ChunkMode = ChunkModes.Regex }
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], []));
    }

    [Fact]
    public void HardCut_ExactSizeWithBadEnding_Fails_CCJ015()
    {
        // 1200 字符、结尾为“不得大于”，属恰长硬切。
        string text = new string('文', 1196) + "不得大于";
        var (document, chunks, options) = CreateCase(text);

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.Issues, i => i.RuleId == ChunkRuleIds.HardCut);
        Assert.Equal(ChunkIssueSeverity.Error, issue.Severity);
        Assert.Equal("1200", issue.Detail!["chunkSize"]);
    }

    [Fact]
    public void ExactSizeWithSentenceEnding_Passes()
    {
        string text = new string('文', 1199) + "。";
        var (document, chunks, options) = CreateCase(text);

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.HardCut);
    }

    [Fact]
    public void ExactSizeWithClosingQuoteAfterSentenceEnding_Passes()
    {
        string text = new string('文', 1198) + "。”";
        var (document, chunks, options) = CreateCase(text);

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.HardCut);
    }

    [Fact]
    public void ExactSizeWithPageNumberEnding_PassesByTolerance()
    {
        string text = new string('文', 1195) + "。 123";
        var (document, chunks, options) = CreateCase(text);

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.HardCut);
    }

    [Fact]
    public void PageMode_IsExempt()
    {
        string text = new string('文', 1196) + "不得大于";
        var (document, chunks, options) = CreateCase(text);
        options = options with { ChunkMode = ChunkModes.Page };

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.HardCut);
    }

    [Fact]
    public void OverLimit_IsNotHardCut_CCJ004StillApplies()
    {
        string text = new string('文', 1196) + "不得大于abcde";
        var (document, chunks, options) = CreateCase(text);

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == ChunkRuleIds.HardCut);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Summary_CountsHardCuts()
    {
        string good = new string('文', 1199) + "。";
        string bad = new string('文', 1196) + "不得大于";
        string content = good + bad;
        var document = CreateDocument(content);
        var chunks = new[]
        {
            CreateChunk(0, good, 0),
            CreateChunk(1, bad, good.Length)
        };
        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 1200, MaxChunkSize = 1500 };

        var report = new ChunkJudgeService().JudgeContractChunks(document, chunks, options);

        Assert.Equal(1, report.Summary.HardCutChunkCount);
        Assert.False(report.Passed);
    }

    private static (DocumentFeature Document, ContractChunk[] Chunks, ChunkStrategyOptions Options) CreateCase(string text)
    {
        var document = CreateDocument(text);
        var chunks = new[] { CreateChunk(0, text, 0) };
        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 1200, MaxChunkSize = 1500 };
        return (document, chunks, options);
    }

    private static ChunkRuleTemplate CreateTemplate()
    {
        return new ChunkRuleTemplate
        {
            TemplateId = "aec-law-default",
            Version = "1.0.0",
            Name = "法规制度默认模板",
            AppliesTo = new TemplateAppliesTo { DocumentCategories = ["law"] },
            Decision = new DecisionTable
            {
                Rules =
                [
                    new DecisionRule
                    {
                        Traits = [ChunkTraits.ArticleNumbering],
                        ChunkMode = ChunkModes.Regex,
                        Pattern = "(?=第[一二三四五六七八九十百零〇0-9]+[条章])",
                        ChunkSize = 1200
                    },
                    new DecisionRule
                    {
                        Traits = [ChunkTraits.WeakStructure],
                        ChunkMode = ChunkModes.Smart,
                        ChunkSize = 1200
                    }
                ],
                Fallback = new FallbackRule { ChunkMode = ChunkModes.Smart, ChunkSize = 1000 }
            },
            Validation = new TemplateValidation { HardCutZero = true, AcceptanceRef = "aec-27q" }
        };
    }

    private static DocumentFeature CreateDocument(string content) => new()
    {
        DocumentId = "doc-template",
        Metadata = CreateMetadata(),
        Content = content,
        Language = "zh"
    };

    private static DocumentMetadata CreateMetadata() => new()
    {
        Title = "模板样例",
        SourcePath = "docs/template.md",
        DocumentType = "markdown",
        PageInfo = "1",
        CreateTime = FixedTime
    };

    private static ContractChunk CreateChunk(int index, string text, int start) => new()
    {
        ChunkId = $"structure@1200#{index}",
        Index = index,
        Text = text,
        StartOffset = start,
        EndOffset = start + text.Length,
        Strategy = "structure@1200",
        Metadata = CreateMetadata(),
        CharCount = ChunkTextMetrics.CountCharacters(text)
    };
}
