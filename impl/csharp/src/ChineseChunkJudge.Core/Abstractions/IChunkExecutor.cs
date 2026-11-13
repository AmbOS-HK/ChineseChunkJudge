using ChineseChunkJudge.Core.Models;

namespace ChineseChunkJudge.Core.Abstractions;

/// <summary>
/// 切片执行器：契约中唯一的「切片入口」抽象。
/// 实现只负责按各自算法产出 <see cref="RawChunk"/>，补齐元信息、字符计数、关系指针由
/// <see cref="ChunkJudgeService"/> 统一完成，保证不同语言/不同策略的产出可比。
/// </summary>
public interface IChunkExecutor
{
    /// <summary>
    /// 策略类型，对应契约策略名的 {type} 段，例如 structure。
    /// 必须满足正则 ^[a-z][a-z0-9_-]*$。
    /// </summary>
    string StrategyType { get; }

    /// <summary>
    /// 执行切片。<see cref="RawChunk.Text"/> 必须是原文区间的精确子串，
    /// 不得 trim、不得改写、不得拼接。
    /// </summary>
    /// <param name="document">输入文档特征。</param>
    /// <param name="options">策略选项，目标块长与硬上限见契约第 5 节。</param>
    /// <returns>按 index 升序排列的原始切片。</returns>
    IReadOnlyList<RawChunk> Execute(DocumentFeature document, ChunkStrategyOptions options);
}
