using System.Text.Json;
using System.Text.Json.Nodes;
using ChineseChunkJudge.Core;
using ChineseChunkJudge.Core.Models;
using ChineseChunkJudge.Core.RuleEngine;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>
/// 序列化口径与 contract/schema/contract-chunk.json 对齐（K-01）：
/// relations 的三个 required 指针必须显式输出 null，可选指针不得因 null 而出现（CCJ-013）。
/// </summary>
public sealed class JsonContractTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void FirstChunk_SerializesRequiredRelationPointersAsExplicitNull()
    {
        var node = SerializeFirstChunk();

        var relations = node["relations"]!.AsObject();
        Assert.True(relations.ContainsKey("previousChunkId"), "首块必须输出 previousChunkId（null 也要出现）");
        Assert.Null(relations["previousChunkId"]);
        Assert.Equal("structure@1200#1", relations["nextChunkId"]!.GetValue<string>());
        Assert.True(relations.ContainsKey("parentChunkId"), "parentChunkId 为 Schema required，null 也要出现");
        Assert.Null(relations["parentChunkId"]);
    }

    [Fact]
    public void LastChunk_SerializesRequiredRelationPointersAsExplicitNull()
    {
        var chunks = NormalizeTwoChunks();
        var node = JsonNode.Parse(JsonSerializer.Serialize(chunks[1], ChunkJson.CreateOptions(indented: false)))!;

        var relations = node["relations"]!.AsObject();
        Assert.Equal("structure@1200#0", relations["previousChunkId"]!.GetValue<string>());
        Assert.True(relations.ContainsKey("nextChunkId"), "末块必须输出 nextChunkId（null 也要出现）");
        Assert.Null(relations["nextChunkId"]);
        Assert.True(relations.ContainsKey("parentChunkId"));
        Assert.Null(relations["parentChunkId"]);
    }

    [Fact]
    public void SingleLibrary_DoesNotEmitContextParentChunkIdKey()
    {
        foreach (var chunk in NormalizeTwoChunks())
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(chunk, ChunkJson.CreateOptions(indented: false)))!;
            var relations = node["relations"]!.AsObject();

            Assert.False(
                relations.ContainsKey("contextParentChunkId"),
                "双库关闭时 contextParentChunkId 不得出现，否则会被 CCJ-013 判为违规");
        }
    }

    [Fact]
    public void SerializedChunk_CarriesAllSchemaRequiredFields()
    {
        // 与 contract/schema/contract-chunk.json 的 required 列表一一对应。
        string[] topLevelRequired =
        [
            "chunkId", "index", "text", "startOffset", "endOffset",
            "strategy", "metadata", "charCount", "relations"
        ];
        string[] relationRequired = ["previousChunkId", "nextChunkId", "parentChunkId"];

        foreach (var chunk in NormalizeTwoChunks())
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(chunk, ChunkJson.CreateOptions(indented: false)))!.AsObject();

            foreach (var field in topLevelRequired)
            {
                Assert.True(node.ContainsKey(field), $"缺少 Schema required 字段：{field}");
            }

            var relations = node["relations"]!.AsObject();
            foreach (var field in relationRequired)
            {
                Assert.True(relations.ContainsKey(field), $"relations 缺少 Schema required 字段：{field}");
            }
        }
    }

    [Fact]
    public void RuleManifest_ListsTwentyOneBaseRulesAtContractVersion()
    {
        var manifest = new ChunkJudgeService().GetRuleManifest();

        Assert.Equal("0.10.0", manifest.ContractVersion);
        Assert.Equal(21, manifest.Rules.Count);
        Assert.Equal(
            ChunkRuleEngine.DefaultBaseRuleIds.OrderBy(id => id, StringComparer.Ordinal),
            manifest.Rules.Select(rule => rule.RuleId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(manifest.Rules, rule => Assert.True(rule.IsBaseRule));
    }

    [Fact]
    public void RuleManifest_SerializesDescriptorWithExactlyFourKeys()
    {
        var manifest = new ChunkJudgeService().GetRuleManifest();
        var root = JsonNode.Parse(JsonSerializer.Serialize(manifest, ChunkJson.CreateOptions(indented: false)))!.AsObject();

        Assert.Equal(JsonValueKind.String, root["contractVersion"]!.GetValueKind());

        string[] expectedKeys = ["description", "isBaseRule", "ruleId", "severity"];
        foreach (var rule in root["rules"]!.AsArray())
        {
            string[] actual = rule!.AsObject()
                .Select(property => property.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedKeys, actual);
        }
    }

    private static JsonNode SerializeFirstChunk()
    {
        var chunks = NormalizeTwoChunks();
        return JsonNode.Parse(JsonSerializer.Serialize(chunks[0], ChunkJson.CreateOptions(indented: false)))!;
    }

    private static IReadOnlyList<ContractChunk> NormalizeTwoChunks()
    {
        var document = new DocumentFeature
        {
            DocumentId = "doc-json",
            Metadata = new DocumentMetadata
            {
                Title = "序列化口径样例",
                SourcePath = "docs/json.md",
                DocumentType = "markdown",
                PageInfo = "1",
                CreateTime = FixedTime
            },
            Content = "第一条 为了规范管理，制定本规范。第二条 本规范适用于全部项目。"
        };

        var raw = new List<RawChunk>
        {
            new() { Index = 0, Text = "第一条 为了规范管理，制定本规范。", StartOffset = 0, EndOffset = 17 },
            new() { Index = 1, Text = "第二条 本规范适用于全部项目。", StartOffset = 17, EndOffset = 32, UnitKind = "article" }
        };

        var options = new ChunkStrategyOptions { StrategyType = "structure", TargetSize = 1200 };

        return new ChunkJudgeService().Normalize(document, raw, options);
    }
}
