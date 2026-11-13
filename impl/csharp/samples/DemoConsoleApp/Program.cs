using System.Text.Json;
using ChineseChunkJudge.Core;
using ChineseChunkJudge.Core.Abstractions;
using ChineseChunkJudge.Core.Models;

var content = """
# 切片判定契约样例文档

本样例用于演示 ChineseChunkJudge 的判定流程：先把一份带结构的文档交给切片执行器，产出原始切片；再补齐元信息、字符数与关系指针，得到契约切片；最后由规则引擎给出判定报告。样例中的文档、策略与报告结构都可以直接替换成真实业务数据。

## 第一章 适用范围

本契约适用于任意语言的文本切片实现。切片器只负责按各自算法切分正文，不负责补充元信息，也不负责判断自身产出是否合规。判定由独立的规则引擎完成，因此同一份文档在 fixed、sentence、recursive、structure 四种策略下产出的结果，可以用同一套规则比较结构完整性与边界质量。

## 第二章 元信息要求

每份文档必须携带五个固定字段：标题、来源路径、文档类型、页信息与创建时间。这五个字段不可删减、不可改名，也不可改变类型。任一字段缺失或为空，判定结果为不通过，并在问题清单中逐字段列出。契约切片必须携带与文档一致的元信息快照，避免切片脱离文档上下文后无法追溯来源。

## 第三章 判定规则

基础规则共有七条，覆盖元信息完整性、策略命名规范、空切片、单块字符数上限、偏移一致性、区间重叠与策略选项合法性。其中空切片与单块超过一千五百字符属于告警级别，不阻断判定；元信息缺失、策略名不合规、偏移与原文不一致属于错误级别，直接判定不通过。基础规则可以扩展，但不能删除，也不能降级。

## 第四章 关系与报告

归一化阶段会为每个切片补上前驱、后继与父块指针。父块指向最近的前序标题块，标题块自身的父块指向层级更浅的前序标题块，从而在不额外存储树结构的情况下恢复章节层级。判定报告汇总切片数量、字符数分布、空切片与超限切片数量，并逐条列出问题、级别与机读细节，便于评测工具直接消费。
""";

var document = new DocumentFeature
{
    DocumentId = "doc-demo-001",
    Metadata = new DocumentMetadata
    {
        Title = "切片判定契约样例文档",
        SourcePath = "samples/demo-document.md",
        DocumentType = "markdown",
        PageInfo = "1-1",
        CreateTime = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(8))
    },
    Content = content,
    Language = "zh"
};

var options = new ChunkStrategyOptions
{
    StrategyType = "structure",
    TargetSize = 600
};

var service = new ChunkJudgeService();
var report = service.JudgeWithExecutor(document, new StructureExecutor(), options);

Console.WriteLine(JsonSerializer.Serialize(report, ChunkJson.CreateOptions()));

/// <summary>
/// 结构优先执行器（演示用）：按标题/段落单元贪心装箱，
/// 单个单元超过硬上限时按中文句末标点二次切分，保证不产生超限块。
/// </summary>
internal sealed class StructureExecutor : IChunkExecutor
{
    public string StrategyType => "structure";

    public IReadOnlyList<RawChunk> Execute(DocumentFeature document, ChunkStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        int limit = Math.Min(options.TargetSize, options.MaxChunkSize);
        var units = SplitIntoUnits(document.Content, options.MaxChunkSize);
        var chunks = new List<RawChunk>();

        int? start = null;
        int end = 0;
        string? unitKind = null;
        int? level = null;

        foreach (var unit in units)
        {
            int unitEnd = unit.Start + unit.Length;
            if (start.HasValue && unitEnd - start.Value > limit)
            {
                chunks.Add(CreateChunk(document.Content, chunks.Count, start.Value, end, unitKind, level));
                start = null;
            }

            if (!start.HasValue)
            {
                start = unit.Start;
                unitKind = unit.Kind;
                level = unit.Level;
            }

            end = unitEnd;
        }

        if (start.HasValue)
        {
            chunks.Add(CreateChunk(document.Content, chunks.Count, start.Value, end, unitKind, level));
        }

        return chunks;
    }

    private static RawChunk CreateChunk(string content, int index, int start, int end, string? unitKind, int? level) => new()
    {
        Index = index,
        Text = content.Substring(start, end - start),
        StartOffset = start,
        EndOffset = end,
        UnitKind = unitKind,
        StructureLevel = level,
        StartsAtUnitBoundary = true,
        EndsAtUnitBoundary = true
    };

    private static IReadOnlyList<Unit> SplitIntoUnits(string content, int maxUnitLength)
    {
        var units = new List<Unit>();
        int lineStart = 0;
        int paragraphStart = -1;

        void FlushParagraph(int endOffset)
        {
            if (paragraphStart >= 0 && endOffset > paragraphStart)
            {
                units.Add(new Unit(paragraphStart, endOffset - paragraphStart, "paragraph", null));
                paragraphStart = -1;
            }
        }

        while (true)
        {
            int newlineIndex = content.IndexOf('\n', lineStart);
            int lineEnd = newlineIndex < 0 ? content.Length : newlineIndex;
            string line = content.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                FlushParagraph(lineStart);
                units.Add(new Unit(lineStart, lineEnd - lineStart, ContractConstants.HeadingUnitKind, CountHeadingLevel(trimmed)));
            }
            else if (line.Trim().Length == 0)
            {
                FlushParagraph(lineStart);
            }
            else if (paragraphStart < 0)
            {
                paragraphStart = lineStart;
            }

            if (newlineIndex < 0)
            {
                break;
            }

            lineStart = newlineIndex + 1;
        }

        FlushParagraph(content.Length);
        return SplitOversizedUnits(content, units, maxUnitLength);
    }

    private static IReadOnlyList<Unit> SplitOversizedUnits(string content, IReadOnlyList<Unit> units, int maxUnitLength)
    {
        if (maxUnitLength <= 0)
        {
            maxUnitLength = 1;
        }

        var result = new List<Unit>();
        foreach (var unit in units)
        {
            int unitEnd = unit.Start + unit.Length;
            if (unit.Length <= maxUnitLength)
            {
                result.Add(unit);
                continue;
            }

            int offset = unit.Start;
            while (unitEnd - offset > maxUnitLength)
            {
                int cut = FindCut(content, offset + maxUnitLength, offset, unitEnd);
                result.Add(new Unit(offset, cut - offset, unit.Kind, unit.Level));
                offset = cut;
            }

            if (unitEnd > offset)
            {
                result.Add(new Unit(offset, unitEnd - offset, unit.Kind, unit.Level));
            }
        }

        return result;
    }

    private static int FindCut(string content, int preferred, int start, int end)
    {
        for (int i = Math.Min(preferred, end); i > start; i--)
        {
            char c = content[i - 1];
            if (c is '。' or '！' or '？' or '；' or '\n')
            {
                return i;
            }
        }

        return Math.Min(preferred, end);
    }

    private static int CountHeadingLevel(string trimmedLine)
    {
        int level = 0;
        foreach (char c in trimmedLine)
        {
            if (c != '#')
            {
                break;
            }

            level++;
        }

        return Math.Max(level, 1);
    }

    private sealed record Unit(int Start, int Length, string Kind, int? Level);
}
