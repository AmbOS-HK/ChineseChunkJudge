# ChineseChunkJudge

> 语言无关的文本切片（chunking）判定契约规范，附带 .NET 10 / C# 参考实现。

ChineseChunkJudge 不是一个切片器，而是一套**切片判定契约**：它规定一份文档被切片之后，产出的切片必须满足哪些结构、元信息与边界约束，并输出一份可复现的判定报告（JudgeReport）。任何语言的切片实现，只要按本契约产出数据结构，就能被同一套规则判定，并被同一评测工具（ChunkJudgeBench）横向比较。

## 仓库结构

```
ChineseChunkJudge/
├─ README.md
├─ LICENSE
├─ .gitignore
├─ contract/                          # 权威标准，先改这里
│  ├─ rules.md                        # 判定规则正文
│  ├─ openapi.yaml                    # 判定服务 HTTP 契约（OpenAPI 3.1）
│  └─ schema/
│     ├─ document-feature.json        # 输入文档结构
│     ├─ chunk-strategy-options.json  # 策略选项
│     ├─ raw-chunk.json               # 原始切片
│     ├─ contract-chunk.json          # 契约切片
│     └─ judge-report.json            # 判定报告
└─ impl/
   └─ csharp/                         # 第一个参考实现（net10.0）
      ├─ ChineseChunkJudge.sln
      ├─ src/ChineseChunkJudge.Core/
      │  ├─ Abstractions/IChunkExecutor.cs
      │  ├─ Models/ChunkContract.cs
      │  ├─ RuleEngine/ChunkRuleEngine.cs
      │  ├─ Validators/ChunkValidator.cs
      │  ├─ Builders/ChunkRelationBuilder.cs
      │  ├─ ChunkJudgeService.cs
      │  └─ ChineseChunkJudge.Core.csproj
      ├─ tests/ChineseChunkJudge.Core.Tests/
      │  ├─ ChunkValidatorTests.cs
      │  └─ ChineseChunkJudge.Core.Tests.csproj
      └─ samples/DemoConsoleApp/
         ├─ Program.cs
         └─ DemoConsoleApp.csproj
```

## 契约优先

`contract/` 下的文件是唯一权威标准：

| 文件 | 作用 |
| --- | --- |
| `contract/rules.md` | 术语、元信息、策略命名、规则编号与级别、报告结构、变更流程 |
| `contract/openapi.yaml` | 判定服务 HTTP 接口（`/rules`、`/chunks/normalize`、`/judge`、`/judge/execute`） |
| `contract/schema/*.json` | 五类数据结构的 JSON Schema（2020-12） |

修改顺序固定：**先改 `contract/`，再同步 `impl/`**。参考实现不得私自增删契约核心字段，也不得删除或降级基础校验规则。

## 参考实现

`impl/csharp` 是唯一的参考实现，目标框架 `net10.0`：

| 组件 | 职责 |
| --- | --- |
| `IChunkExecutor` | 各语言/各策略的切片入口，只产出 `RawChunk` |
| `ChunkContract` 模型 | 契约数据结构，与 schema 一一对应 |
| `ChunkRuleEngine` | 规则注册与执行；扩展只能新增，基础规则不可删除 |
| `ChunkValidator` | 对契约切片执行规则，汇总问题清单 |
| `ChunkRelationBuilder` | 构建 `previousChunkId` / `nextChunkId` / `parentChunkId` |
| `ChunkJudgeService` | 归一化 + 校验 + 汇总，产出 `JudgeReport` |

## 快速开始

```powershell
cd D:\ChineseChunkJudge\impl\csharp
dotnet build
dotnet test
dotnet run --project samples/DemoConsoleApp
```

Demo 会输出一份 `structure@600` 的样例 JudgeReport JSON。

## 强制约束

1. **Metadata 固定五字段**：`Title`、`SourcePath`、`DocumentType`、`PageInfo`、`CreateTime`，不可删减、不可改名、不可改类型。
2. **质检强制规则**：单块上限 1500 字符告警（CCJ-004）、空切片告警（CCJ-003）；可扩展校验，不可删除基础校验。
3. **策略命名**：`{type}@{targetSize}`，如 `structure@600`、`sentence@900`；基线由 ChunkJudgeBench 产出。
4. **编码**：全仓库 UTF-8。
5. **单一参考实现**：当前只做 C#，不新增其他语言的参考实现。

## 许可

自研代码以仓库根目录 `LICENSE`（MIT, Copyright (c) 2026 AmbOS-HK）为准。
