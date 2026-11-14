using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChineseChunkJudge.Core;
using ChineseChunkJudge.Core.Builders;
using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Cli;

/// <summary>
/// 进程内命令行。直接调用 Core，用文件读写契约 JSON，不启动 HTTP 服务。
/// </summary>
public static class CliApp
{
    public const int Success = 0;
    public const int ContractError = 1;
    public const int UsageError = 2;

    private const string UsageText =
        """
        用法：
          ccj judge --in <JudgeRequest.json> --out <JudgeReport.json>
          ccj resolve --in <TemplateResolveRequest.json> --out <TemplateDecision.json>
          ccj rules --out <RuleManifest.json>

        judge 与 resolve 读取请求文件并写出响应文件。rules 只写出当前规则清单。
        不读取标准输入，也不把响应当作标准输出。
        resolve 的模板必须放在请求文件的 template 或 templates 字段里，本命令不会自行寻找模板。
        判定报告 passed=false 时退出码仍为 0。请求无法执行时把错误 JSON 写入 --out，退出码为 1。
        参数不完整或无法识别时退出码为 2，不写输出文件。
        """;

    public static int Run(string[] args) => Run(args, Console.Error);

    public static int Run(string[] args, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || IsHelp(args))
        {
            error.WriteLine(UsageText);
            return args.Length == 0 ? UsageError : Success;
        }

        string command = args[0];
        string[] rest = args.Length == 1 ? [] : args[1..];
        return command switch
        {
            "judge" => RunJudge(rest, error),
            "resolve" => RunResolve(rest, error),
            "rules" => RunRules(rest, error),
            _ => Usage(error, $"无法识别的子命令：{command}")
        };
    }

    private static int RunJudge(string[] args, TextWriter error)
    {
        if (!TryBind(args, requireInput: true, out string? input, out string? output, out string? problem))
        {
            return Usage(error, problem);
        }

        if (!TryRead(input!, error, output!, out string? json))
        {
            return ContractError;
        }

        JudgeRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize<JudgeRequestDto>(json!, ReadOptions());
        }
        catch (JsonException ex)
        {
            return WriteContractError(output!, error, "invalid_request", ex.Message);
        }

        if (request?.Document is null || request.Options is null || request.RawChunks is null)
        {
            return WriteContractError(output!, error, "invalid_request", "JudgeRequest 必须包含 document、options、rawChunks。");
        }

        try
        {
            var service = new ChunkJudgeService();
            JudgeReport report = request.ParentChunks is null && request.Pairs is null
                ? service.Judge(request.Document, request.RawChunks, request.Options)
                : service.Judge(
                    request.Document,
                    request.RawChunks,
                    request.Options,
                    request.ParentChunks ?? [],
                    request.Pairs ?? []);
            return WriteJson(output!, error, report);
        }
        catch (ArgumentException ex)
        {
            return WriteContractError(output!, error, CodeFor(ex), ex.Message);
        }
    }

    private static int RunResolve(string[] args, TextWriter error)
    {
        if (!TryBind(args, requireInput: true, out string? input, out string? output, out string? problem))
        {
            return Usage(error, problem);
        }

        if (!TryRead(input!, error, output!, out string? json))
        {
            return ContractError;
        }

        TemplateResolveRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize<TemplateResolveRequestDto>(json!, ReadOptions());
        }
        catch (JsonException ex)
        {
            return WriteContractError(output!, error, "invalid_request", ex.Message);
        }

        if (request?.DocumentCategories is null || request.Traits is null)
        {
            return WriteContractError(
                output!,
                error,
                "invalid_request",
                "TemplateResolveRequest 必须包含 documentCategories 与 traits。");
        }

        bool hasTemplate = request.Template is not null;
        bool hasTemplates = request.Templates is not null;
        if (hasTemplate == hasTemplates)
        {
            return WriteContractError(output!, error, "invalid_request", "template 与 templates 必须恰好提供一个。");
        }

        try
        {
            var resolver = new TemplateResolver();
            TemplateDecision decision = hasTemplate
                ? resolver.Resolve(request.Template!, request.DocumentCategories, request.Traits)
                : resolver.Resolve(request.Templates!, request.DocumentCategories, request.Traits);
            return WriteJson(output!, error, decision);
        }
        catch (ArgumentException ex)
        {
            return WriteContractError(output!, error, CodeFor(ex), ex.Message);
        }
    }

    private static int RunRules(string[] args, TextWriter error)
    {
        if (!TryBind(args, requireInput: false, out _, out string? output, out string? problem))
        {
            return Usage(error, problem);
        }

        var manifest = new ChunkJudgeService().GetRuleManifest();
        return WriteJson(output!, error, manifest);
    }

    private static bool TryBind(
        string[] args,
        bool requireInput,
        out string? input,
        out string? output,
        out string? problem)
    {
        input = null;
        output = null;
        problem = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is not ("--in" or "--out"))
            {
                problem = $"无法识别的参数：{arg}";
                return false;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
            {
                problem = $"缺少 {arg} 的文件路径。";
                return false;
            }

            string path = args[++i];
            if (path == "-")
            {
                problem = "不接受标准输入或标准输出。请使用 --in 与 --out 指定文件。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                problem = $"{arg} 的文件路径为空。";
                return false;
            }

            if (arg == "--in")
            {
                if (input is not null)
                {
                    problem = "--in 重复。";
                    return false;
                }

                input = path;
            }
            else if (output is not null)
            {
                problem = "--out 重复。";
                return false;
            }
            else
            {
                output = path;
            }
        }

        if (requireInput && input is null)
        {
            problem = "缺少 --in。";
            return false;
        }

        if (!requireInput && input is not null)
        {
            problem = "rules 不读取 --in。";
            return false;
        }

        if (output is null)
        {
            problem = "缺少 --out。";
            return false;
        }

        return true;
    }

    private static bool TryRead(string input, TextWriter error, string output, out string? json)
    {
        try
        {
            json = File.ReadAllText(input);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            json = null;
            WriteContractError(output, error, "invalid_request", $"无法读取请求文件：{ex.Message}");
            return false;
        }
    }

    private static string CodeFor(Exception ex)
    {
        if (ex.Message.Contains("no_applicable_template", StringComparison.Ordinal))
        {
            return "no_applicable_template";
        }

        if (ex.Message.Contains("invalid_input", StringComparison.Ordinal)
            && ex.Message.Contains("rawChunks", StringComparison.Ordinal))
        {
            return "invalid_chunk";
        }

        return "invalid_request";
    }

    private static int WriteContractError(string output, TextWriter error, string code, string message)
    {
        var envelope = new ErrorEnvelope
        {
            Error = new ErrorBody
            {
                Code = code,
                Message = message
            }
        };
        int written = WriteJson(output, error, envelope);
        return written == Success ? ContractError : written;
    }

    private static int WriteJson<T>(string path, TextWriter error, T value)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                error.WriteLine($"输出目录不存在：{directory}");
                return ContractError;
            }

            string json = JsonSerializer.Serialize(value, ChunkJson.CreateOptions()) + "\n";
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(ex.Message);
            return ContractError;
        }
    }

    private static JsonSerializerOptions ReadOptions()
    {
        JsonSerializerOptions options = ChunkJson.CreateOptions(indented: false);
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        return options;
    }

    private static int Usage(TextWriter error, string? problem)
    {
        if (!string.IsNullOrWhiteSpace(problem))
        {
            error.WriteLine(problem);
        }

        error.WriteLine(UsageText);
        return UsageError;
    }

    private static bool IsHelp(string[] args) =>
        args.Length == 1 && args[0] is "--help" or "-h" or "help";
}

internal sealed class JudgeRequestDto
{
    public DocumentFeature? Document { get; init; }

    public ChunkStrategyOptions? Options { get; init; }

    public List<RawChunk>? RawChunks { get; init; }

    public List<ContextParentChunk>? ParentChunks { get; init; }

    public List<ContextParentChildPair>? Pairs { get; init; }
}

internal sealed class TemplateResolveRequestDto
{
    public ChunkRuleTemplate? Template { get; init; }

    public List<ChunkRuleTemplate>? Templates { get; init; }

    public List<string>? DocumentCategories { get; init; }

    public List<string>? Traits { get; init; }
}

internal sealed class ErrorEnvelope
{
    public required ErrorBody Error { get; init; }
}

internal sealed class ErrorBody
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
