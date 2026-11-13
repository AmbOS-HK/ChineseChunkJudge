# ChineseChunkJudge

中文名称：中文切片判定框架

> 本项目不是一个新的 chunk 算法库，而是一套面向 Word / PDF / HTML 载体下中文专业文档（政府报告、工程报告、项目报告、司法鉴定、合同协议、规范、法规等）的「切片分流规范 + 质检判定 + 双库父子块配套契约」，可对接任意底层切分器（包括 Chatchat、HanLP 等）。

名称释义：
- Chinese：面向中文专业文档；
- Chunk：RAG 文本切片；
- Judge：负责策略路由、切片质检、合规判定。

## 仓库结构

```
ChineseChunkJudge/
├─ README.md
├─ LICENSE
├─ .gitignore
├─ docs/                              # 规划文档，非标准
│  ├─ 00-定位与边界.md
│  ├─ 01-术语表.md
│  ├─ 02-契约演进.md
│  ├─ 03-实现矩阵.md
│  └─ 04-对外文案口径.md
├─ contract/                          # 权威标准，先改这里
│  ├─ rules.md                        # 判定规则正文
│  ├─ openapi.yaml                    # 判定服务 HTTP 契约（OpenAPI 3.1）
│  └─ schema/
│     ├─ document-feature.json        # 输入文档结构
│     ├─ chunk-strategy-options.json  # 策略选项
│     ├─ raw-chunk.json               # 原始切片
│     ├─ contract-chunk.json          # 契约切片
│     ├─ judge-report.json            # 判定报告
│     ├─ parent-child-package.json    # 双库产物包（0.2.0 新增）
│     └─ chunk-rule-template.json     # 切片规则模板（0.3.0 新增）
└─ impl/
   ├─ csharp/                         # 首个参考实现（net10.0），已实现
   │  ├─ ChineseChunkJudge.sln
   │  ├─ src/ChineseChunkJudge.Core/
   │  │  ├─ Abstractions/IChunkExecutor.cs
   │  │  ├─ Models/ChunkContract.cs
   │  │  ├─ Models/ParentChildContract.cs
   │  │  ├─ Models/RuleTemplateContract.cs
   │  │  ├─ RuleEngine/ChunkRuleEngine.cs
   │  │  ├─ RuleEngine/ParentChildRules.cs
   │  │  ├─ RuleEngine/HardCutRule.cs
   │  │  ├─ Validators/ChunkValidator.cs
   │  │  ├─ Builders/ChunkRelationBuilder.cs
   │  │  ├─ Builders/TemplateResolver.cs
   │  │  ├─ ChunkJudgeService.cs
   │  │  └─ ChineseChunkJudge.Core.csproj
   │  ├─ tests/ChineseChunkJudge.Core.Tests/
   │  │  ├─ ChunkValidatorTests.cs
   │  │  ├─ ParentChildRulesTests.cs
   │  │  ├─ RuleTemplateAndHardCutTests.cs
   │  │  └─ ChineseChunkJudge.Core.Tests.csproj
   │  └─ samples/DemoConsoleApp/
   │     ├─ Program.cs
   │     └─ DemoConsoleApp.csproj
   ├─ go/                             # 占位，未实现（规划：高性能服务版）
   │  └─ README.md
   ├─ python/                         # 占位，未实现（规划：与 ChunkJudgeBench 联动）
   │  └─ README.md
   └─ rust/                           # 占位，未实现（规划：极致性能）
      └─ README.md
```

## 契约优先

`contract/` 下的文件是唯一权威标准：

| 文件 | 作用 |
| --- | --- |
| `contract/rules.md` | 术语、元信息、策略命名、规则编号与级别、报告结构、变更流程 |
| `contract/openapi.yaml` | 判定服务 HTTP 接口（`/rules`、`/chunks/normalize`、`/judge`、`/judge/execute`） |
| `contract/schema/*.json` | 七类数据结构的 JSON Schema（2020-12），含双库产物包与规则模板 |

修改顺序固定：**先改 `contract/`，再同步 `impl/`**。参考实现不得私自增删契约核心字段，也不得删除或降级基础校验规则。

当前契约版本 **0.3.0**：基础规则 CCJ-001..CCJ-015；0.2.0 落地双库产物 `ParentChildPackage`（默认关闭）与双父指针（`parentChunkId` 弃用保留至 1.0.0）；0.3.0 落地 Spec 侧 `ChunkRuleTemplate`（`/templates/resolve` 选型判定，强制 smart 兜底）与 CCJ-015 恰长硬切（error）。C# 参考实现已同步，`dotnet build` 0 警告 0 错误、36 项单元测试全部通过（2026-11-13）。

## 参考实现

`impl/csharp` 是首个参考实现，目标框架 `net10.0`。契约与语言无关：任何语言实现，只要输入输出符合 `contract/schema/*.json` 与 `contract/openapi.yaml`，即属同一契约下的实现，按规划可继续补 Go / Python / Rust 参考实现。

`impl/go`、`impl/python`、`impl/rust` 目前为占位目录，每个目录只有一份 `README.md` 说明定位与落地约束，无源码、无构建文件、未纳入构建与测试流程。

下表为 C# 参考实现的组件构成：

| 组件 | 职责 |
| --- | --- |
| `IChunkExecutor` | **外部插件接入点**，只定义接口，本仓库不提供算法实现 |
| `ChunkContract` 模型 | 契约数据结构，与 schema 一一对应 |
| `ChunkRuleEngine` | 规则注册与执行；扩展只能新增，基础规则不可删除 |
| `ChunkValidator` | 对契约切片执行规则，汇总问题清单 |
| `ChunkRelationBuilder` | 构建 `previousChunkId` / `nextChunkId` / `parentChunkId` |
| `ChunkJudgeService` | 归一化 + 校验 + 汇总，产出 `JudgeReport` |

## 边界约束（不做什么）

- **不内置文档解析**：PDF / Word / HTML 解析外置，输入只有文本与元信息。
- **不内置切片算法**：切分器是外部插件，`samples/` 同样不含生产算法；Demo 通过 `--plugin` 反射加载外部执行器，未提供插件时以非零码退出，不输出报告。
- 演示插件桩在仓库外：`D:\ChineseChunkJudge.Plugins\StructureExecutorStub`，不属于本仓库交付物。

## 快速开始

```powershell
# 1) 构建仓库外插件桩（一次性）
dotnet build D:\ChineseChunkJudge.Plugins\StructureExecutorStub

# 2) 构建本仓库并跑测试
cd D:\ChineseChunkJudge\impl\csharp
dotnet build
dotnet test

# 3) 用外部插件运行 Demo
dotnet run --project samples/DemoConsoleApp -- `
  --plugin "D:\ChineseChunkJudge.Plugins\StructureExecutorStub\bin\Debug\net10.0\StructureExecutorStub.dll" `
  --size 600
```

Demo 会输出一份 `structure@600` 的样例 JudgeReport JSON；策略类型取自插件的 `StrategyType`，`--size` 缺省 600。

## 强制约束

1. **Metadata 固定五字段**：`Title`、`SourcePath`、`DocumentType`、`PageInfo`、`CreateTime`，不可删减、不可改名、不可改类型。
2. **质检强制规则**：单块上限 1500 字符告警（CCJ-004）、空切片告警（CCJ-003）；可扩展校验，不可删除基础校验。
3. **策略命名**：`{type}@{targetSize}`，如 `structure@600`、`sentence@900`；基线由 ChunkJudgeBench 产出。
4. **编码**：全仓库 UTF-8。
5. **契约语言无关，实现可扩展**：`impl/csharp` 为首个参考实现；任何语言实现只要输入输出符合 `contract/schema/*.json` 与 `contract/openapi.yaml`，即属同一契约下的实现（规划：C# / Go / Python / Rust），各实现不得增删契约核心字段、不得降级基础规则。
6. **算法外置**：本仓库（含 samples）不交付切分算法与文档解析器，执行器由外部插件实现 `IChunkExecutor` 注入。

## 许可

自研代码以仓库根目录 `LICENSE`（MIT, Copyright (c) 2026 AmbOS-HK）为准。
