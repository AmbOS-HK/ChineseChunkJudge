using System.Reflection;
using System.Text.Json;
using ChineseChunkJudge.Core;
using ChineseChunkJudge.Core.Abstractions;
using ChineseChunkJudge.Core.Models;

// Demo 不内置任何切分算法。执行器必须来自仓库外的插件程序集，
// 通过 --plugin 传入；未提供插件时打印用法并以非零码退出，不输出伪造报告。

if (!TryParseArgs(args, out string? pluginPath, out string? typeName, out int targetSize))
{
    PrintUsage();
    return 2;
}

if (!File.Exists(pluginPath))
{
    Console.Error.WriteLine($"插件程序集不存在：{pluginPath}");
    PrintUsage();
    return 3;
}

IChunkExecutor executor;
try
{
    executor = LoadExecutor(pluginPath, typeName);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"加载插件执行器失败：{ex.Message}");
    return 4;
}

var content = """
# 切片判定契约样例文档

本样例用于演示 ChineseChunkJudge 的判定流程：先把一份带结构的文档交给外部插件执行器，产出原始切片；再补齐元信息、字符数与关系指针，得到契约切片；最后由规则引擎给出判定报告。样例中的文档、策略与报告结构都可以直接替换成真实业务数据，切分算法本身不在本仓库内。

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
    StrategyType = executor.StrategyType,
    TargetSize = targetSize
};

var service = new ChunkJudgeService();
var report = service.JudgeWithExecutor(document, executor, options);

Console.WriteLine(JsonSerializer.Serialize(report, ChunkJson.CreateOptions()));
return 0;

static bool TryParseArgs(string[] args, out string? pluginPath, out string? typeName, out int targetSize)
{
    pluginPath = null;
    typeName = null;
    targetSize = 600;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--plugin" when i + 1 < args.Length:
                pluginPath = args[++i];
                break;
            case "--type" when i + 1 < args.Length:
                typeName = args[++i];
                break;
            case "--size" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed):
                targetSize = parsed;
                i++;
                break;
            default:
                return false;
        }
    }

    return !string.IsNullOrWhiteSpace(pluginPath) && targetSize >= 1 && targetSize <= ContractConstants.MaxChunkChars;
}

static IChunkExecutor LoadExecutor(string pluginPath, string? typeName)
{
    var assembly = Assembly.LoadFrom(Path.GetFullPath(pluginPath));
    var candidates = assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IChunkExecutor).IsAssignableFrom(t))
        .ToList();

    if (!string.IsNullOrWhiteSpace(typeName))
    {
        candidates = candidates.Where(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal)).ToList();
    }

    if (candidates.Count == 0)
    {
        throw new InvalidOperationException("程序集中没有实现 IChunkExecutor 的具体类型。");
    }

    if (candidates.Count > 1)
    {
        string names = string.Join("、", candidates.Select(t => t.FullName));
        throw new InvalidOperationException($"程序集中有多个执行器，请用 --type 指定：{names}");
    }

    if (Activator.CreateInstance(candidates[0]) is not IChunkExecutor instance)
    {
        throw new InvalidOperationException($"类型 {candidates[0].FullName} 无法实例化，需要公共无参构造函数。");
    }

    return instance;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
用法：
  dotnet run --project samples/DemoConsoleApp -- --plugin <插件dll路径> [--type <全类型名>] [--size 600]

说明：
  本 Demo 不内置切分算法，必须加载实现了 IChunkExecutor 的外部插件。
  演示插件桩（仓库外）：D:\ChineseChunkJudge.Plugins\StructureExecutorStub
  构建后传入：D:\ChineseChunkJudge.Plugins\StructureExecutorStub\bin\Debug\net10.0\StructureExecutorStub.dll
""");
}
