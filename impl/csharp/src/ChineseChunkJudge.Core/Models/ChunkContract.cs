using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChineseChunkJudge.Core.Models;

/// <summary>契约常量。与 contract/rules.md 第 5、8 节保持一致，不得在实现侧放宽。</summary>
public static class ContractConstants
{
    /// <summary>单块字符数硬上限，对应 CCJ-004。</summary>
    public const int MaxChunkChars = 1500;

    /// <summary>策略名校验正则：{type}@{targetSize}。</summary>
    public const string StrategyNamePattern = @"^[a-z][a-z0-9_-]*@[1-9][0-9]*$";

    /// <summary>契约版本号，与 contract/rules.md 表头一致。</summary>
    public const string ContractVersion = "0.10.0";

    /// <summary>父块长度告警阈值的临时调试默认值，非标准基准；正式值由样本 P95 标定。</summary>
    public const int ParentMaxCharsDebugDefault = 6000;

    /// <summary>标题单元的 unitKind 取值，关系构建依赖该值。</summary>
    public const string HeadingUnitKind = "heading";
}

/// <summary>
/// 文档元信息。固定五字段，不可删减、不可改名、不可改类型。
/// </summary>
public sealed record DocumentMetadata
{
    public const string FieldTitle = "title";
    public const string FieldSourcePath = "sourcePath";
    public const string FieldDocumentType = "documentType";
    public const string FieldPageInfo = "pageInfo";
    public const string FieldCreateTime = "createTime";

    /// <summary>固定五字段清单，判定规则 CCJ-001 以此为唯一依据。</summary>
    public static readonly IReadOnlyList<string> RequiredFields =
    [
        FieldTitle,
        FieldSourcePath,
        FieldDocumentType,
        FieldPageInfo,
        FieldCreateTime
    ];

    public required string Title { get; init; }

    public required string SourcePath { get; init; }

    public required string DocumentType { get; init; }

    public required string PageInfo { get; init; }

    public required DateTimeOffset CreateTime { get; init; }

    /// <summary>判断指定元信息字段是否缺失（字符串去空白后为空，时间取默认值视为缺失）。</summary>
    public bool IsFieldMissing(string fieldName) => fieldName switch
    {
        FieldTitle => string.IsNullOrWhiteSpace(Title),
        FieldSourcePath => string.IsNullOrWhiteSpace(SourcePath),
        FieldDocumentType => string.IsNullOrWhiteSpace(DocumentType),
        FieldPageInfo => string.IsNullOrWhiteSpace(PageInfo),
        FieldCreateTime => CreateTime == default,
        _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "未知元信息字段。")
    };
}

/// <summary>结构提示中的单元边界。</summary>
public sealed record UnitBoundary
{
    public required string Kind { get; init; }

    public required int StartOffset { get; init; }

    public required int EndOffset { get; init; }

    public int? Level { get; init; }
}

/// <summary>可选结构提示。判定结果不得依赖该字段。</summary>
public sealed record StructureHints
{
    public int HeadingCount { get; init; }

    public IReadOnlyList<UnitBoundary> UnitBoundaries { get; init; } = [];
}

/// <summary>一次判定请求的输入文档特征。</summary>
public sealed record DocumentFeature
{
    public required string DocumentId { get; init; }

    public required DocumentMetadata Metadata { get; init; }

    public required string Content { get; init; }

    public string Language { get; init; } = "zh";

    public StructureHints? StructureHints { get; init; }

    /// <summary>正文字符数（Unicode 码位数）。</summary>
    public int CharCount => ChunkTextMetrics.CountCharacters(Content);
}

/// <summary>切片策略选项。策略名由 StrategyType 与 TargetSize 拼接得出。</summary>
public sealed record ChunkStrategyOptions
{
    public required string StrategyType { get; init; }

    public required int TargetSize { get; init; }

    public int MaxChunkSize { get; init; } = ContractConstants.MaxChunkChars;

    public int MinChunkSize { get; init; } = 1;

    public bool PreserveStructure { get; init; } = true;

    public int Overlap { get; init; }

    public IReadOnlyDictionary<string, string>? Extensions { get; init; }

    /// <summary>双库配置，缺省为关闭。</summary>
    public ParentChildOptions? ParentChild { get; init; }

    /// <summary>切分方式（regex / smart / page），由模板决策回填；page 模式豁免 CCJ-015。</summary>
    public string? ChunkMode { get; init; }

    /// <summary>契约策略名：{type}@{targetSize}。</summary>
    public string StrategyName => $"{StrategyType}@{TargetSize}";
}

/// <summary>切片器直接产出的片段，不含契约元信息。</summary>
public sealed record RawChunk
{
    public required int Index { get; init; }

    public required string Text { get; init; }

    public required int StartOffset { get; init; }

    public required int EndOffset { get; init; }

    public string? UnitKind { get; init; }

    public string? UnitKey { get; init; }

    public int? StructureLevel { get; init; }

    public bool StartsAtUnitBoundary { get; init; } = true;

    public bool EndsAtUnitBoundary { get; init; } = true;

    /// <summary>祖先标题链，自最外层到最内层；由执行器产出，本仓只透传（C-17）。</summary>
    public IReadOnlyList<string>? HeadingPath { get; init; }
}

/// <summary>契约切片的关系指针。</summary>
public sealed record ChunkRelations
{
    /// <summary>按 index 顺序的前一个切片；首块为 null，但字段必须出现（Schema required）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PreviousChunkId { get; init; }

    /// <summary>按 index 顺序的后一个切片；末块为 null，但字段必须出现（Schema required）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? NextChunkId { get; init; }

    /// <summary>deprecated，1.0.0 移除；语义等同 StructuralParentChunkId，二者同现值必须一致（CCJ-014）。字段为 Schema required，null 也必须出现。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ParentChunkId { get; init; }

    /// <summary>结构父：文档大纲层级的最近前序 heading 父块。Schema 未列入 required，null 时省略。</summary>
    public string? StructuralParentChunkId { get; init; }

    /// <summary>上下文父：双库模式下归属的 contextParent；双库关闭时不得出现（CCJ-013），null 时省略。</summary>
    public string? ContextParentChunkId { get; init; }
}

/// <summary>已补齐元信息、字符计数与关系指针的契约切片，是判定的直接对象。</summary>
public sealed record ContractChunk
{
    public required string ChunkId { get; init; }

    public required int Index { get; init; }

    public required string Text { get; init; }

    public required int StartOffset { get; init; }

    public required int EndOffset { get; init; }

    public required string Strategy { get; init; }

    public required DocumentMetadata Metadata { get; init; }

    public int CharCount { get; init; }

    public string? UnitKind { get; init; }

    public string? UnitKey { get; init; }

    public int? StructureLevel { get; init; }

    public bool StartsAtUnitBoundary { get; init; } = true;

    public bool EndsAtUnitBoundary { get; init; } = true;

    /// <summary>祖先标题链，继承自 RawChunk，原样透传保序（§6.1）。</summary>
    public IReadOnlyList<string>? HeadingPath { get; init; }

    public ChunkRelations Relations { get; init; } = new();
}

/// <summary>问题级别。error 阻断判定，warning 仅告警。</summary>
public enum ChunkIssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>单条判定问题。</summary>
public sealed record ChunkIssue
{
    public required string RuleId { get; init; }

    public required ChunkIssueSeverity Severity { get; init; }

    public string? ChunkId { get; init; }

    public required string Message { get; init; }

    public IReadOnlyDictionary<string, string>? Detail { get; init; }
}

/// <summary>判定报告摘要。</summary>
public sealed record ChunkReportSummary
{
    public required int ChunkCount { get; init; }

    public required int TotalCharCount { get; init; }

    public required int MinCharCount { get; init; }

    public required int MaxCharCount { get; init; }

    public required int AverageCharCount { get; init; }

    public required int EmptyChunkCount { get; init; }

    public required int OversizedChunkCount { get; init; }

    public int ParentChunkCount { get; init; }

    public int ChildWithParentCount { get; init; }

    /// <summary>deprecated，1.0.0 移除；语义等同 ChildWithParentRatio。</summary>
    public double ParentCoverageRatio { get; init; }

    /// <summary>childWithParentCount / 子块总数；无子块时为 0。0.4.0 起为正式字段。</summary>
    public double ChildWithParentRatio { get; init; }

    public int ParentCharP95 { get; init; }

    /// <summary>超过 parentMaxChars 的父块数量，与 OversizedChunkCount 独立累加。</summary>
    public int OversizedParentChunkCount { get; init; }

    /// <summary>恰长硬切子块数量（CCJ-015），验收口径为每文档必须为 0。</summary>
    public int HardCutChunkCount { get; init; }
}

/// <summary>报告中的生效规则描述。</summary>
public sealed record AppliedRule
{
    public required string RuleId { get; init; }

    public required string Description { get; init; }

    public required ChunkIssueSeverity Severity { get; init; }

    public required bool IsBaseRule { get; init; }
}

/// <summary>一次判定的完整结果。</summary>
public sealed record JudgeReport
{
    public required string ReportId { get; init; }

    public required string DocumentId { get; init; }

    public required string Strategy { get; init; }

    public required int TargetSize { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required bool Passed { get; init; }

    public required ChunkReportSummary Summary { get; init; }

    public required IReadOnlyList<ChunkIssue> Issues { get; init; }

    public required IReadOnlyList<AppliedRule> AppliedRules { get; init; }

    /// <summary>判定所依据的选项原样回显（可选），对应契约 §9 的 effectiveOptions。</summary>
    public ChunkStrategyOptions? EffectiveOptions { get; init; }
}

/// <summary>字符计量工具：统一按 Unicode 码位计数，与契约第 11 节一致。</summary>
public static class ChunkTextMetrics
{
    /// <summary>统计 Unicode 码位数量。</summary>
    public static int CountCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int count = 0;
        foreach (var unused in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    /// <summary>是否为空白文本（空串、半角/全角空格、制表符、换行）。</summary>
    public static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);
}

/// <summary>契约序列化口径：camelCase 属性、字符串枚举、非 ASCII 不转义。</summary>
public static class ChunkJson
{
    public static JsonSerializerOptions CreateOptions(bool indented = true) => new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
