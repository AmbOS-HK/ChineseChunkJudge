using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.Builders;

/// <summary>
/// 选型判定：按规则模板决策切分方式与参数，不执行切分。
/// appliesTo 是部署侧选择模板的依据，Resolve 不做门类准入校验；决策只看 rules + 强制 fallback。
/// </summary>
public sealed class TemplateResolver
{
    public TemplateDecision Resolve(
        ChunkRuleTemplate template,
        IReadOnlyList<string> documentCategories,
        IReadOnlyList<string> traits)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(documentCategories);
        ArgumentNullException.ThrowIfNull(traits);

        if (!string.Equals(template.Decision.Fallback.ChunkMode, ChunkModes.Smart, StringComparison.Ordinal))
        {
            throw new ArgumentException("decision.fallback.chunkMode 契约固定为 smart。", nameof(template));
        }

        var documentTraits = traits.ToHashSet(StringComparer.Ordinal);

        for (int i = 0; i < template.Decision.Rules.Count; i++)
        {
            var rule = template.Decision.Rules[i];
            if (!rule.Traits.All(documentTraits.Contains))
            {
                continue;
            }

            ValidateRule(rule, i);
            return new TemplateDecision
            {
                TemplateId = template.TemplateId,
                MatchedRuleIndex = i,
                UsedFallback = false,
                ChunkMode = rule.ChunkMode,
                Pattern = rule.Pattern,
                ChunkSize = rule.ChunkSize,
                ParentChild = rule.ParentChild
            };
        }

        var fallback = template.Decision.Fallback;
        return new TemplateDecision
        {
            TemplateId = template.TemplateId,
            MatchedRuleIndex = null,
            UsedFallback = true,
            ChunkMode = fallback.ChunkMode,
            Pattern = null,
            ChunkSize = fallback.ChunkSize,
            ParentChild = fallback.ParentChild
        };
    }

    private static void ValidateRule(DecisionRule rule, int index)
    {
        bool isRegex = string.Equals(rule.ChunkMode, ChunkModes.Regex, StringComparison.Ordinal);
        if (isRegex && string.IsNullOrWhiteSpace(rule.Pattern))
        {
            throw new ArgumentException($"decision.rules[{index}]：chunkMode=regex 时 pattern 必填。");
        }

        if (!isRegex && rule.Pattern is not null)
        {
            throw new ArgumentException($"decision.rules[{index}]：非 regex 模式禁止携带 pattern。");
        }
    }
}
