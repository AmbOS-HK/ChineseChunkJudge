using System.Text.RegularExpressions;
using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.Builders;

/// <summary>
/// 选型判定：按规则模板决策切分方式与参数，不执行切分。
/// 单模板形态下 appliesTo 不参与准入校验（0.7.1 行为）；模板集形态（0.8.0）按类别过滤后按数组顺序择优。
/// 模板在进入选型前整体校验：所有规则（含未命中规则）都要合法，避免坏规则潜伏到命中时才报错。
/// 非 fallback 决策规则的 traits 不得为空：Enumerable.All 对空序列恒为真，空数组会变成通配并遮蔽后续规则。
/// </summary>
public sealed class TemplateResolver
{
    private static readonly HashSet<string> ClosedTraits = new(StringComparer.Ordinal)
    {
        ChunkTraits.ArticleNumbering,
        ChunkTraits.SectionNumbering,
        ChunkTraits.WeakStructure,
        ChunkTraits.DenseTable,
        ChunkTraits.PdfNoStructure
    };

    /// <summary>单模板选型（兼容形态，appliesTo 不参与准入）。</summary>
    public TemplateDecision Resolve(
        ChunkRuleTemplate template,
        IReadOnlyList<string> documentCategories,
        IReadOnlyList<string> traits)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(documentCategories);
        ArgumentNullException.ThrowIfNull(traits);

        ValidateTemplate(template);
        var decision = ResolveWithinTemplate(template, traits);
        return decision with
        {
            CandidateCount = 1,
            SelectionReason = decision.UsedFallback
                ? TemplateSelectionReasons.Fallback
                : TemplateSelectionReasons.RuleMatched
        };
    }

    /// <summary>
    /// 模板集选型（0.8.0）：类别交集过滤 → 数组顺序即优先级 →
    /// 取顺序最前的规则命中候选；全部未命中时取顺序最前候选的 fallback。
    /// </summary>
    public TemplateDecision Resolve(
        IReadOnlyList<ChunkRuleTemplate> templates,
        IReadOnlyList<string> documentCategories,
        IReadOnlyList<string> traits)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(documentCategories);
        ArgumentNullException.ThrowIfNull(traits);

        if (templates.Count == 0)
        {
            throw new ArgumentException("no_applicable_template：templates 不得为空。", nameof(templates));
        }

        foreach (var template in templates)
        {
            ValidateTemplate(template);
        }

        var categorySet = documentCategories.ToHashSet(StringComparer.Ordinal);
        var candidates = templates
            .Where(t => t.AppliesTo.DocumentCategories.Any(categorySet.Contains))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "no_applicable_template：没有任何模板的 appliesTo 与请求类别有交集。",
                nameof(templates));
        }

        foreach (var candidate in candidates)
        {
            var decision = TryResolveWithinTemplate(candidate, traits);
            if (decision is not null)
            {
                return decision with
                {
                    CandidateCount = candidates.Count,
                    SelectionReason = TemplateSelectionReasons.RuleMatched
                };
            }
        }

        var fallback = BuildFallback(candidates[0]);
        return fallback with
        {
            CandidateCount = candidates.Count,
            SelectionReason = TemplateSelectionReasons.Fallback
        };
    }

    private static TemplateDecision ResolveWithinTemplate(
        ChunkRuleTemplate template,
        IReadOnlyList<string> traits)
    {
        var matched = TryResolveWithinTemplate(template, traits);
        if (matched is not null)
        {
            return matched;
        }

        return BuildFallback(template);
    }

    private static TemplateDecision? TryResolveWithinTemplate(
        ChunkRuleTemplate template,
        IReadOnlyList<string> traits)
    {
        var documentTraits = traits.ToHashSet(StringComparer.Ordinal);

        for (int i = 0; i < template.Decision.Rules.Count; i++)
        {
            var rule = template.Decision.Rules[i];
            if (!rule.Traits.All(documentTraits.Contains))
            {
                continue;
            }

            return new TemplateDecision
            {
                TemplateId = template.TemplateId,
                MatchedRuleIndex = i,
                UsedFallback = false,
                CandidateCount = 1,
                SelectionReason = TemplateSelectionReasons.RuleMatched,
                ChunkMode = rule.ChunkMode,
                Pattern = rule.Pattern,
                ChunkSize = rule.ChunkSize,
                ParentChild = rule.ParentChild
            };
        }

        return null;
    }

    private static TemplateDecision BuildFallback(ChunkRuleTemplate template)
    {
        var fallback = template.Decision.Fallback;
        return new TemplateDecision
        {
            TemplateId = template.TemplateId,
            MatchedRuleIndex = null,
            UsedFallback = true,
            CandidateCount = 1,
            SelectionReason = TemplateSelectionReasons.Fallback,
            ChunkMode = fallback.ChunkMode,
            Pattern = null,
            ChunkSize = fallback.ChunkSize,
            ParentChild = fallback.ParentChild
        };
    }

    private static void ValidateTemplate(ChunkRuleTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.AppliesTo is null || template.Decision is null || template.Decision.Fallback is null)
        {
            throw new ArgumentException("invalid_template：appliesTo / decision / decision.fallback 均不得为空。", nameof(template));
        }

        ValidateFallback(template);
        ValidateClosedTraits(template.AppliesTo.Traits, "appliesTo.traits");

        for (int i = 0; i < template.Decision.Rules.Count; i++)
        {
            ValidateRule(template.Decision.Rules[i], i);
        }
    }

    private static void ValidateFallback(ChunkRuleTemplate template)
    {
        if (!string.Equals(template.Decision.Fallback.ChunkMode, ChunkModes.Smart, StringComparison.Ordinal))
        {
            throw new ArgumentException("decision.fallback.chunkMode 契约固定为 smart。", nameof(template));
        }

        ValidateChunkSize(template.Decision.Fallback.ChunkSize, "decision.fallback");
    }

    private static void ValidateRule(DecisionRule rule, int index)
    {
        if (rule.Traits is null || rule.Traits.Count == 0)
        {
            throw new ArgumentException(
                $"decision.rules[{index}].traits 不得为空：空数组会使该规则在选型时恒命中并遮蔽后续规则。");
        }

        ValidateClosedTraits(rule.Traits, $"decision.rules[{index}].traits");

        bool isRegex = string.Equals(rule.ChunkMode, ChunkModes.Regex, StringComparison.Ordinal);

        if (!isRegex
            && !string.Equals(rule.ChunkMode, ChunkModes.Smart, StringComparison.Ordinal)
            && !string.Equals(rule.ChunkMode, ChunkModes.Page, StringComparison.Ordinal))
        {
            throw new ArgumentException($"decision.rules[{index}]：chunkMode 只能是 regex / smart / page。");
        }

        ValidateChunkSize(rule.ChunkSize, $"decision.rules[{index}]");

        if (isRegex)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                throw new ArgumentException($"decision.rules[{index}]：chunkMode=regex 时 pattern 必填。");
            }

            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.None);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"decision.rules[{index}]：pattern 不是合法正则。", ex);
            }
        }
        else if (rule.Pattern is not null)
        {
            throw new ArgumentException($"decision.rules[{index}]：非 regex 模式禁止携带 pattern。");
        }
    }

    private static void ValidateClosedTraits(IReadOnlyList<string> traits, string location)
    {
        for (int i = 0; i < traits.Count; i++)
        {
            string trait = traits[i];
            if (!ClosedTraits.Contains(trait))
            {
                throw new ArgumentException($"{location}[{i}] 的取值「{trait}」不在 traits 封闭枚举内。");
            }
        }
    }

    private static void ValidateChunkSize(int chunkSize, string location)
    {
        if (chunkSize is < 1 or > 1500)
        {
            throw new ArgumentException($"{location}.chunkSize 必须在 1..1500，实际为 {chunkSize}。");
        }
    }
}
