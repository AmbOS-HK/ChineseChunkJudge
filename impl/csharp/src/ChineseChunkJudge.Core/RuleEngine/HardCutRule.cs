using System.Globalization;
using System.Text.RegularExpressions;
using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.RuleEngine;

/// <summary>
/// CCJ-015：恰长硬切。charCount 恰好等于 chunkSize 且结尾不在允许句读集合 → error。
/// page 模式豁免；上下文父块不适用；超限由 CCJ-004 处理。
/// </summary>
public sealed class HardCutRule : IChunkRule
{
    private static readonly Regex TerminalPunctuation = new(
        @"[。！？；…\.!\?;][”’）】》]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PageNumberEnding = new(
        @"[。！？；…\.!\?;]\s*\d{1,4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string RuleId => ChunkRuleIds.HardCut;

    public string Description => "恰长硬切：切片长度恰好等于 chunkSize 且结尾不在允许句读集合（页码容错），page 模式豁免。";

    public ChunkIssueSeverity Severity => ChunkIssueSeverity.Error;

    public bool IsBaseRule => true;

    public IEnumerable<ChunkIssue> Evaluate(ChunkValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        if (options is null || string.Equals(options.ChunkMode, ChunkModes.Page, StringComparison.Ordinal))
        {
            yield break;
        }

        int chunkSize = options.TargetSize;
        foreach (var chunk in context.Chunks)
        {
            if (!IsHardCut(chunk.Text, chunkSize))
            {
                continue;
            }

            yield return new ChunkIssue
            {
                RuleId = RuleId,
                Severity = Severity,
                ChunkId = chunk.ChunkId,
                Message = $"切片 {chunk.ChunkId} 长度恰好等于 chunkSize={chunkSize} 且结尾不在句读集合，判定为恰长硬切。",
                Detail = new Dictionary<string, string>
                {
                    ["charCount"] = ChunkTextMetrics.CountCharacters(chunk.Text).ToString(CultureInfo.InvariantCulture),
                    ["chunkSize"] = chunkSize.ToString(CultureInfo.InvariantCulture),
                    ["ending"] = GetEnding(chunk.Text)
                }
            };
        }
    }

    /// <summary>共享判定谓词，供规则与 summary 计数使用。</summary>
    public static bool IsHardCut(string text, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (ChunkTextMetrics.CountCharacters(text) != chunkSize)
        {
            return false;
        }

        string trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (PageNumberEnding.IsMatch(trimmed))
        {
            return false;
        }

        return !TerminalPunctuation.IsMatch(trimmed);
    }

    private static string GetEnding(string text)
    {
        string trimmed = text.TrimEnd();
        return trimmed.Length <= 12 ? trimmed : trimmed[^12..];
    }
}
