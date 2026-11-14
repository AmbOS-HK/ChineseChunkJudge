using System.Text.Json.Nodes;
using ChineseChunkJudge.Cli;
using Xunit;

namespace ChineseChunkJudge.Cli.Tests;

/// <summary>
/// 进程内 CLI：文件进、文件出，请求/响应形状对齐现有契约，不启动 HTTP，不自动加载推荐模板。
/// </summary>
public sealed class CliCommandTests
{
    private static readonly string[] JudgeReportRequired =
    [
        "reportId", "documentId", "strategy", "targetSize", "generatedAt",
        "passed", "summary", "issues", "appliedRules"
    ];

    private static readonly string[] SummaryRequired =
    [
        "chunkCount", "totalCharCount", "minCharCount", "maxCharCount", "averageCharCount",
        "emptyChunkCount", "oversizedChunkCount", "parentChunkCount", "childWithParentCount",
        "parentCoverageRatio", "childWithParentRatio", "parentCharP95",
        "oversizedParentChunkCount", "hardCutChunkCount"
    ];

    private static readonly string[] OptionKeys =
    [
        "strategyType", "targetSize", "maxChunkSize", "minChunkSize", "preserveStructure",
        "overlap", "extensions", "chunkMode", "parentChild"
    ];

    [Fact]
    public void Judge_SampleRequest_WritesPassingReport()
    {
        using var workspace = new TempWorkspace();
        string output = workspace.PathFor("report.json");

        int code = CliApp.Run(
            ["judge", "--in", SamplePath("judge-request.sample.json"), "--out", output],
            workspace.Error);

        Assert.Equal(0, code);
        var report = ReadObject(output);
        foreach (string key in JudgeReportRequired)
        {
            Assert.True(report.ContainsKey(key), $"判定报告缺少 {key}");
        }

        Assert.Equal("synthetic-cli-001", report["documentId"]!.GetValue<string>());
        Assert.Equal("structure@400", report["strategy"]!.GetValue<string>());
        Assert.Equal(400, report["targetSize"]!.GetValue<int>());
        Assert.True(report["passed"]!.GetValue<bool>());
        Assert.Empty(report["issues"]!.AsArray());
        Assert.Equal(21, report["appliedRules"]!.AsArray().Count);

        var summary = report["summary"]!.AsObject();
        foreach (string key in SummaryRequired)
        {
            Assert.True(summary.ContainsKey(key), $"summary 缺少 {key}");
        }

        Assert.Equal(2, summary["chunkCount"]!.GetValue<int>());
        Assert.Equal(15, summary["totalCharCount"]!.GetValue<int>());

        var options = report["effectiveOptions"]!.AsObject();
        Assert.Equal("structure", options["strategyType"]!.GetValue<string>());
        Assert.Equal(400, options["targetSize"]!.GetValue<int>());
        foreach (string key in options.Select(property => property.Key))
        {
            Assert.Contains(key, OptionKeys);
        }

        AssertNoInventedFields(report);
    }

    [Fact]
    public void Judge_SampleChunks_MatchContentSlices()
    {
        var request = ReadObject(SamplePath("judge-request.sample.json"));
        string content = request["document"]!["content"]!.GetValue<string>();
        var chunks = request["rawChunks"]!.AsArray();

        Assert.Equal(2, chunks.Count);
        foreach (var chunk in chunks)
        {
            int start = chunk!["startOffset"]!.GetValue<int>();
            int end = chunk["endOffset"]!.GetValue<int>();
            Assert.Equal(chunk["text"]!.GetValue<string>(), content[start..end]);
        }

        Assert.Equal(content.Length, chunks[^1]!["endOffset"]!.GetValue<int>());
    }

    [Fact]
    public void Judge_MismatchedOffset_WritesReportWithCcj005AndExitsZero()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("judge-request.sample.json"));
        request["rawChunks"]![0]!["endOffset"] = 1;
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("report.json");

        int code = CliApp.Run(["judge", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(0, code);
        var report = ReadObject(output);
        Assert.False(report["passed"]!.GetValue<bool>());
        Assert.Contains(
            report["issues"]!.AsArray(),
            issue => issue!["ruleId"]!.GetValue<string>() == "CCJ-005");
        Assert.Equal(
            "合成样例正文。仅供命令行自测。",
            ReadObject(input)["document"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Judge_UncoveredTail_DoesNotInventCoverageOrExtraChunks()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("judge-request.sample.json"));
        request["document"]!["content"] = "合成样例正文。仅供命令行自测。未覆盖的尾部。";
        request["rawChunks"]!.AsArray().RemoveAt(1);
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("report.json");

        int code = CliApp.Run(["judge", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(0, code);
        var report = ReadObject(output);
        Assert.True(report["passed"]!.GetValue<bool>());
        Assert.Equal(1, report["summary"]!["chunkCount"]!.GetValue<int>());
        Assert.Equal(7, report["summary"]!["totalCharCount"]!.GetValue<int>());
        AssertNoInventedFields(report);
    }

    [Fact]
    public void Judge_MetadataRegion_IsRejectedWithoutReport()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("judge-request.sample.json"));
        request["document"]!["metadata"]!.AsObject()["region"] = "synthetic";
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("error.json");

        int code = CliApp.Run(["judge", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(1, code);
        var body = ReadObject(output);
        Assert.Equal("invalid_request", body["error"]!["code"]!.GetValue<string>());
        Assert.False(body.ContainsKey("passed"));
    }

    [Fact]
    public void Judge_MissingDocument_IsInvalidRequest()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("judge-request.sample.json"));
        request.Remove("document");
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("error.json");

        int code = CliApp.Run(["judge", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(1, code);
        Assert.Equal("invalid_request", ReadObject(output)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Judge_MissingInputFile_WritesInvalidRequest()
    {
        using var workspace = new TempWorkspace();
        string output = workspace.PathFor("error.json");

        int code = CliApp.Run(
            ["judge", "--in", workspace.PathFor("absent.json"), "--out", output],
            workspace.Error);

        Assert.Equal(1, code);
        Assert.Equal("invalid_request", ReadObject(output)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Judge_StdinMarker_IsUsageErrorAndWritesNothing()
    {
        using var workspace = new TempWorkspace();
        string output = workspace.PathFor("report.json");

        int code = CliApp.Run(["judge", "--in", "-", "--out", output], workspace.Error);

        Assert.Equal(2, code);
        Assert.False(File.Exists(output));
        Assert.Contains("--in", workspace.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_WithoutOut_WritesNoSiblingFile()
    {
        using var workspace = new TempWorkspace();
        string input = workspace.PathFor("in.json");
        File.Copy(SamplePath("judge-request.sample.json"), input);

        int code = CliApp.Run(["judge", "--in", input], workspace.Error);

        Assert.Equal(2, code);
        Assert.Equal(["in.json"], workspace.FileNames().OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Contains("--out", workspace.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_SampleRequest_WritesRuleMatchedDecision()
    {
        using var workspace = new TempWorkspace();
        string output = workspace.PathFor("decision.json");
        string originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.Root);

        try
        {
            int code = CliApp.Run(
                ["resolve", "--in", SamplePath("resolve-request.sample.json"), "--out", output],
                workspace.Error);

            Assert.Equal(0, code);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }

        var decision = ReadObject(output);
        Assert.Equal("synthetic-note-v0", decision["templateId"]!.GetValue<string>());
        Assert.False(decision["usedFallback"]!.GetValue<bool>());
        Assert.Equal(0, decision["matchedRuleIndex"]!.GetValue<int>());
        Assert.Equal(1, decision["candidateCount"]!.GetValue<int>());
        Assert.Equal("rule_matched", decision["selectionReason"]!.GetValue<string>());
        Assert.Equal("smart", decision["chunkMode"]!.GetValue<string>());
        Assert.Equal(400, decision["chunkSize"]!.GetValue<int>());
        Assert.False(decision.ContainsKey("pattern"));
        AssertNoInventedFields(decision);
    }

    [Fact]
    public void Resolve_UnmatchedTrait_OmitsMatchedRuleIndex()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("resolve-request.sample.json"));
        request["traits"] = new JsonArray("pdf_no_structure");
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("decision.json");

        int code = CliApp.Run(["resolve", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(0, code);
        var decision = ReadObject(output);
        Assert.True(decision["usedFallback"]!.GetValue<bool>());
        Assert.Equal("fallback", decision["selectionReason"]!.GetValue<string>());
        Assert.False(decision.ContainsKey("matchedRuleIndex"));
        Assert.Equal("smart", decision["chunkMode"]!.GetValue<string>());
        Assert.Equal(300, decision["chunkSize"]!.GetValue<int>());
    }

    [Fact]
    public void Resolve_TemplatesArray_SelectsFirstCategoryMatch()
    {
        using var workspace = new TempWorkspace();
        var single = ReadObject(SamplePath("resolve-request.sample.json"));
        var template = single["template"]!.AsObject();
        var other = template.DeepClone().AsObject();
        other["templateId"] = "synthetic-other-v0";
        other["appliesTo"]!["documentCategories"] = new JsonArray("other_kind");

        var request = new JsonObject
        {
            ["documentCategories"] = new JsonArray("synthetic_note"),
            ["traits"] = new JsonArray("weak_structure"),
            ["templates"] = new JsonArray(other, template.DeepClone())
        };
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("decision.json");

        int code = CliApp.Run(["resolve", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(0, code);
        var decision = ReadObject(output);
        Assert.Equal("synthetic-note-v0", decision["templateId"]!.GetValue<string>());
        Assert.Equal(1, decision["candidateCount"]!.GetValue<int>());
        Assert.Equal("rule_matched", decision["selectionReason"]!.GetValue<string>());
    }

    [Fact]
    public void Resolve_BothTemplateForms_IsInvalidRequest()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("resolve-request.sample.json"));
        request["templates"] = new JsonArray(request["template"]!.DeepClone());
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("error.json");

        int code = CliApp.Run(["resolve", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(1, code);
        Assert.Equal("invalid_request", ReadObject(output)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Resolve_NeitherTemplateForm_IsInvalidRequest()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("resolve-request.sample.json"));
        request.Remove("template");
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("error.json");

        int code = CliApp.Run(["resolve", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(1, code);
        Assert.Equal("invalid_request", ReadObject(output)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Resolve_NoApplicableTemplate_UsesClosedErrorCode()
    {
        using var workspace = new TempWorkspace();
        var request = ReadObject(SamplePath("resolve-request.sample.json"));
        var template = request["template"]!.DeepClone().AsObject();
        template["appliesTo"]!["documentCategories"] = new JsonArray("other_kind");
        request.Remove("template");
        request["templates"] = new JsonArray(template);
        string input = workspace.Write("request.json", request);
        string output = workspace.PathFor("error.json");

        int code = CliApp.Run(["resolve", "--in", input, "--out", output], workspace.Error);

        Assert.Equal(1, code);
        var error = ReadObject(output)["error"]!.AsObject();
        Assert.Equal("no_applicable_template", error["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(error["message"]!.GetValue<string>()));
    }

    [Fact]
    public void Rules_WritesManifestFile()
    {
        using var workspace = new TempWorkspace();
        string output = workspace.PathFor("rules.json");

        int code = CliApp.Run(["rules", "--out", output], workspace.Error);

        Assert.Equal(0, code);
        var manifest = ReadObject(output);
        Assert.Equal(["contractVersion", "rules"], manifest.Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal("0.10.1", manifest["contractVersion"]!.GetValue<string>());

        string[] descriptorKeys = ["description", "isBaseRule", "ruleId", "severity"];
        var rules = manifest["rules"]!.AsArray();
        Assert.Equal(21, rules.Count);
        Assert.Contains(rules, rule => rule!["ruleId"]!.GetValue<string>() == "CCJ-001");
        Assert.Contains(rules, rule => rule!["ruleId"]!.GetValue<string>() == "CCJ-021");
        foreach (var rule in rules)
        {
            Assert.Equal(
                descriptorKeys,
                rule!.AsObject().Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
            Assert.True(rule["isBaseRule"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void UnknownCommand_IsUsageError()
    {
        using var workspace = new TempWorkspace();

        int code = CliApp.Run(["listen"], workspace.Error);

        Assert.Equal(2, code);
        Assert.Contains("judge", workspace.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void CliProject_ReferencesCoreOnlyAndDoesNotAutoLoadTemplates()
    {
        string projectDir = FindProjectDir();
        string csproj = File.ReadAllText(Path.Combine(projectDir, "ChineseChunkJudge.Cli.csproj"));
        Assert.Contains("ChineseChunkJudge.Core.csproj", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("AspNetCore", csproj, StringComparison.Ordinal);

        string source = string.Join(
            "\n",
            Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string[] forbidden =
        [
            "aec-law-v1",
            "aec-plans-v1",
            "templates/recommended",
            "templates\\recommended",
            "AEC.Chat",
            "ChunkJudgeBench",
            "Console.In",
            "Console.Read",
            "Console.OpenStandardInput",
            "WebApplication",
            "MapPost",
            "Kestrel",
            "doc_url"
        ];
        foreach (string token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }
    }

    private static void AssertNoInventedFields(JsonObject root)
    {
        string[] forbidden = ["region", "year", "admin_level", "plan_period", "coverage", "doc_url", "offset"];
        foreach (string key in EnumerateKeys(root))
        {
            Assert.DoesNotContain(key, forbidden);
        }
    }

    private static IEnumerable<string> EnumerateKeys(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                yield return property.Key;
                foreach (string nested in EnumerateKeys(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                foreach (string nested in EnumerateKeys(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static JsonObject ReadObject(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static string SamplePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "cli", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"找不到样例文件 {fileName}。");
    }

    private static string FindProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "ChineseChunkJudge.Cli");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到 ChineseChunkJudge.Cli 项目目录。");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "ccj-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Error = new StringWriter();
        }

        public string Root { get; }

        public StringWriter Error { get; }

        public string ErrorText => Error.ToString();

        public string PathFor(string fileName) => Path.Combine(Root, fileName);

        public string Write(string fileName, JsonNode node)
        {
            string path = PathFor(fileName);
            File.WriteAllText(path, node.ToJsonString() + "\n");
            return path;
        }

        public IEnumerable<string> FileNames() =>
            Directory.GetFiles(Root).Select(Path.GetFileName)!;

        public void Dispose()
        {
            Error.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
