using System.Text.Json;
using ChineseChunkJudge.Core.Models;
using Xunit;

namespace ChineseChunkJudge.Core.Tests;

/// <summary>C-17 标题路径扩展（0.8.0）：仅透传与保序，不推断、不补全。</summary>
public sealed class HeadingPathTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Normalize_PreservesHeadingPathOrder()
    {
        var raw = new RawChunk
        {
            Index = 0,
            Text = "第一条 为了规范……",
            StartOffset = 0,
            EndOffset = 10,
            UnitKind = "article",
            HeadingPath = ["第一章 总则", "第一节 适用范围"]
        };

        var chunks = CreateService().Normalize(CreateDocument(), [raw], CreateOptions());

        Assert.Equal(["第一章 总则", "第一节 适用范围"], chunks[0].HeadingPath);
    }

    [Fact]
    public void Normalize_WithoutHeadingPath_LeavesNullAndOmitsJsonField()
    {
        var raw = new RawChunk
        {
            Index = 0,
            Text = "第一条 为了规范……",
            StartOffset = 0,
            EndOffset = 10
        };

        var chunks = CreateService().Normalize(CreateDocument(), [raw], CreateOptions());
        var json = JsonSerializer.Serialize(chunks[0], ChunkJson.CreateOptions(indented: false));

        Assert.Null(chunks[0].HeadingPath);
        Assert.DoesNotContain("headingPath", json);
    }

    [Fact]
    public void Normalize_DoesNotInferHeadingPathFromUnitKind()
    {
        var raw = new RawChunk
        {
            Index = 0,
            Text = "总则",
            StartOffset = 0,
            EndOffset = 2,
            UnitKind = "heading"
        };

        var chunks = CreateService().Normalize(CreateDocument(), [raw], CreateOptions());

        Assert.Null(chunks[0].HeadingPath);
    }

    [Fact]
    public void Normalize_DoesNotRewriteEmptyStringElement()
    {
        var raw = new RawChunk
        {
            Index = 0,
            Text = "第一条 为了规范……",
            StartOffset = 0,
            EndOffset = 10,
            HeadingPath = ["", "第一章 总则"]
        };

        var chunks = CreateService().Normalize(CreateDocument(), [raw], CreateOptions());

        Assert.Equal(["", "第一章 总则"], chunks[0].HeadingPath);
    }

    private static ChunkJudgeService CreateService() => new();

    private static ChunkStrategyOptions CreateOptions() => new()
    {
        StrategyType = "structure",
        TargetSize = 1200,
        MaxChunkSize = 1500
    };

    private static DocumentFeature CreateDocument() => new()
    {
        DocumentId = "doc-heading",
        Metadata = new DocumentMetadata
        {
            Title = "标题路径样例",
            SourcePath = "docs/heading.md",
            DocumentType = "markdown",
            PageInfo = "1",
            CreateTime = FixedTime
        },
        Content = "第一条 为了规范……",
        Language = "zh"
    };
}
