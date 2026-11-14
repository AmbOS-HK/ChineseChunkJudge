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
    public void Resolve_SingleTemplate_ReportsSingleCandidate()
    {
        var decision = new TemplateResolver().Resolve(CreateTemplate(), ["law"], [ChunkTraits.ArticleNumbering]);

        Assert.Equal(1, decision.CandidateCount);
        Assert.Equal(TemplateSelectionReasons.RuleMatched, decision.SelectionReason);
    }

    [Fact]
    public void ResolveMany_PrefersRuleMatchedCandidate_OverEarlierFallbackCandidate()
    {
        var generic = CreateTemplate() with
        {
            TemplateId = "generic-report",
            AppliesTo = new TemplateAppliesTo { DocumentCategories = ["report"] },
            Decision = new DecisionTable
            {
                Rules =
                [
                    new DecisionRule
                    {
                        Traits = [ChunkTraits.DenseTable],
                        ChunkMode = ChunkModes.Smart,
                        ChunkSize = 800
                    }
                ],
                Fallback = new FallbackRule { ChunkMode = ChunkModes.Smart, ChunkSize = 600 }
            }
        };
        var law = CreateTemplate() with { TemplateId = "law-template" };

        var decision = new TemplateResolver().Resolve(
            [generic, law],
            ["report", "law"],
            [ChunkTraits.ArticleNumbering]);

        Assert.Equal("law-template", decision.TemplateId);
        Assert.Equal(2, decision.CandidateCount);
        Assert.Equal(TemplateSelectionReasons.RuleMatched, decision.SelectionReason);
        Assert.Equal(ChunkModes.Regex, decision.ChunkMode);
    }

    [Fact]
    public void ResolveMany_AllFallback_UsesFirstCandidateFallback()
    {
        var first = CreateTemplate() with
        {
            TemplateId = "first",
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
                    }
                ],
                Fallback = new FallbackRule { ChunkMode = ChunkModes.Smart, ChunkSize = 900 }
            }
        };
        var second = CreateTemplate() with
        {
            TemplateId = "second",
            Decision = new DecisionTable
            {
                Rules =
                [
                    new DecisionRule
                    {
                        Traits = [ChunkTraits.DenseTable],
                        ChunkMode = ChunkModes.Smart,
                        ChunkSize = 1500
                    }
                ],
                Fallback = new FallbackRule { ChunkMode = ChunkModes.Smart, ChunkSize = 1200 }
            }
        };

        var decision = new TemplateResolver().Resolve(
            [first, second],
            ["law"],
            [ChunkTraits.WeakStructure]);

        Assert.Equal("first", decision.TemplateId);
        Assert.True(decision.UsedFallback);
        Assert.Equal(TemplateSelectionReasons.Fallback, decision.SelectionReason);
        Assert.Equal(900, decision.ChunkSize);
        Assert.Equal(2, decision.CandidateCount);
    }

    [Fact]
    public void ResolveMany_NoCategoryIntersection_Throws()
    {
        var template = CreateTemplate();

        var exception = Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve([template], ["plans"], [ChunkTraits.ArticleNumbering]));

        Assert.Contains("no_applicable_template", exception.Message);
    }

    [Fact]
    public void ResolveMany_EmptyTemplateList_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve([], ["law"], []));

        Assert.Contains("no_applicable_template", exception.Message);
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
    public void ClosingMarkBetweenPunctuationAndDigits_IsHardCut()
    {
        Assert.True(HardCutRule.IsHardCut("甲乙丙丁戊。】123", 10));
    }

    [Fact]
    public void PageNumberAfterPunctuation_IsToleratedWithOrWithoutSpace()
    {
        Assert.False(HardCutRule.IsHardCut("甲乙丙丁戊。 123", 10));
        Assert.False(HardCutRule.IsHardCut("甲乙丙丁戊。123", 9));
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

    [Fact]
    public void InvalidChunkMode_Fails_CCJ007()
    {
        string text = new string('文', 10) + "。";
        var (document, chunks, options) = CreateCase(text);
        options = options with { ChunkMode = "recursive" };

        var result = new ChunkValidator().Validate(document, chunks, options);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.RuleId == ChunkRuleIds.InvalidOptions && i.Detail!["option"] == "chunkMode");
    }

    [Fact]
    public void Resolve_InvalidRegexPattern_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Rules = [template.Decision.Rules[0] with { Pattern = "(" }, template.Decision.Rules[1]]
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.ArticleNumbering]));
    }

    [Fact]
    public void Resolve_ChunkSizeOutOfRange_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Rules = [template.Decision.Rules[0] with { ChunkSize = 2000 }, template.Decision.Rules[1]]
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.ArticleNumbering]));
    }

    [Fact]
    public void Resolve_FallbackChunkSizeOutOfRange_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Fallback = template.Decision.Fallback with { ChunkSize = 0 }
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.DenseTable]));
    }

    [Fact]
    public void Resolve_UnknownChunkMode_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Rules = [template.Decision.Rules[0], template.Decision.Rules[1] with { ChunkMode = "recursive" }]
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.WeakStructure]));
    }

    [Fact]
    public void Resolve_UnmatchedInvalidRule_StillThrows()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Rules =
                [
                    template.Decision.Rules[0],
                    new DecisionRule
                    {
                        Traits = [ChunkTraits.DenseTable],
                        ChunkMode = ChunkModes.Regex,
                        Pattern = "(",
                        ChunkSize = 1200
                    }
                ]
            }
        };

        Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.ArticleNumbering]));
    }

    [Fact]
    public void Resolve_UnknownRuleTrait_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            Decision = template.Decision with
            {
                Rules = [template.Decision.Rules[0] with { Traits = ["not_a_trait"] }, template.Decision.Rules[1]]
            }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.ArticleNumbering]));

        Assert.Contains("not_a_trait", ex.Message);
        Assert.Contains("decision.rules[0].traits", ex.Message);
    }

    [Fact]
    public void Resolve_UnknownAppliesToTrait_Throws()
    {
        var template = CreateTemplate();
        var invalid = template with
        {
            AppliesTo = template.AppliesTo with { Traits = ["not_a_trait"] }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new TemplateResolver().Resolve(invalid, ["law"], [ChunkTraits.ArticleNumbering]));

        Assert.Contains("not_a_trait", ex.Message);
        Assert.Contains("appliesTo.traits", ex.Message);
    }

    [Fact]
    public void Resolve_EachClosedTrait_Succeeds()
    {
        string[] legal =
        [
            ChunkTraits.ArticleNumbering,
            ChunkTraits.SectionNumbering,
            ChunkTraits.WeakStructure,
            ChunkTraits.DenseTable,
            ChunkTraits.PdfNoStructure
        ];

        foreach (string trait in legal)
        {
            var template = new ChunkRuleTemplate
            {
                TemplateId = "trait-ok",
                Version = "1.0.0",
                Name = "合法特征模板",
                AppliesTo = new TemplateAppliesTo
                {
                    DocumentCategories = ["law"],
                    Traits = [trait]
                },
                Decision = new DecisionTable
                {
                    Rules =
                    [
                        new DecisionRule
                        {
                            Traits = [trait],
                            ChunkMode = ChunkModes.Smart,
                            ChunkSize = 800
                        }
                    ],
                    Fallback = new FallbackRule { ChunkMode = ChunkModes.Smart, ChunkSize = 600 }
                }
            };

            var decision = new TemplateResolver().Resolve(template, ["law"], [trait]);

            Assert.False(decision.UsedFallback);
            Assert.Equal(ChunkModes.Smart, decision.ChunkMode);
        }
    }

    [Fact]
    public void ResolveMany_NullTemplate_Throws()
    {
        IReadOnlyList<ChunkRuleTemplate> templates = [null!];

        Assert.Throws<ArgumentNullException>(() =>
            new TemplateResolver().Resolve(templates, ["law"], [ChunkTraits.ArticleNumbering]));
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
