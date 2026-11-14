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
├─ .gitattributes                     # 文本入库统一 LF
├─ .editorconfig                      # UTF-8 / 缩进 / 换行
├─ CONTRIBUTING.md                    # 契约先行流程与提交规范
├─ CODE_OF_CONDUCT.md
├─ SECURITY.md                        # 安全问题报告渠道
├─ .github/
│  ├─ workflows/
│  │  └─ ci.yml                       # push / PR 到 main：版本 / 结构 / 升版守门 + build + test
│  ├─ ISSUE_TEMPLATE/
│  │  ├─ bug_report.yml
│  │  ├─ feature_request.yml
│  │  └─ config.yml
│  └─ PULL_REQUEST_TEMPLATE.md
├─ scripts/
│  ├─ check_contract_version.py       # 契约版本三处同值校验（CI 调用）
│  ├─ check_contract_integrity.py     # Schema / $ref / 消费物结构校验（CI 调用）
│  ├─ check_contract_bump.py          # 改契约必须升版的守门（CI 调用，PR 触发）
│  └─ fixtures/                       # 参考实现产物样本，供结构校验
├─ docs/                              # 规划与审查文档，非标准
│  ├─ 00-定位与边界.md
│  ├─ 01-术语表.md
│  ├─ 02-契约演进.md
│  ├─ 03-实现矩阵.md
│  ├─ 04-对外文案口径.md
│  ├─ 05-C01-C02-文档类别与特征产出方草案.md
│  ├─ 06-C17-标题路径扩展-立项.md
│  ├─ 07-C18-选型入口接收模板集-立项.md
│  ├─ 08-模板生产线与自动适配-规划.md
│  ├─ 09-全量完整性自查-2026-11-14.md
│  ├─ 10-类别清单规范性审核-2026-11-14.md
│  ├─ 11-14类模板族规范性与科学性验证-2026-11-14.md
│  ├─ 12-类别到默认模板与Bench分组映射-2026-11-14.md
│  ├─ 13-仓库合规整改-2026-11-14.md
│  ├─ 14-契约全量完整性审查-2026-11-14.md
│  ├─ 15-契约缺陷整改台账-2026-11-14.md
│  ├─ 16-契约安检复检-首批-2026-11-14.md
│  ├─ 17-0.10.0变更清单-2026-11-14.md
│  ├─ 18-实现符合性复查-2026-11-14.md
│  ├─ 19-实现偏差修复方案与施工指令-2026-11-14.md
│  ├─ 20-Cursor施工任务单-实现偏差修复-2026-11-14.md
│  ├─ 21-AEC.Chat小循环构建方案-2026-11-14.md    # AEC.Chat 侧方案（未实施），非契约审查线
│  ├─ 22-实现偏差修复复检-2026-11-14.md
│  ├─ 23-0.10.0落契记录-2026-11-14.md
│  └─ 24-交接说明-2026-11-14.md          # 接手先读：基线、铁律、待办、禁区
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
├─ templates/
│  └─ recommended/                    # 推荐模板族（6 类 + 说明）
│     ├─ README.md
│     ├─ engineering_project_report.json
│     ├─ government_plan.json
│     ├─ judicial_document.json
│     ├─ law_regulation.json
│     ├─ manual.json
│     └─ technical_standard.json
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
   │  │  ├─ HeadingPathTests.cs
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

修改顺序固定：**先改 `contract/`，再同步 `impl/`**。参考实现不得私自增删契约核心字段，也不得删除或降级基础校验规则。完整流程与提交规范见 `CONTRIBUTING.md`。

当前契约版本 **0.10.0**：基础规则 CCJ-001..CCJ-021；0.2.0 落地双库产物 `ParentChildPackage`（默认关闭）与双父指针（`parentChunkId` 弃用保留至 1.0.0）；0.3.0 落地 Spec 侧 `ChunkRuleTemplate`（`/templates/resolve` 选型判定，强制 smart 兜底）与 CCJ-015 恰长硬切（error）；0.4.0 将 summary 的 `parentCoverageRatio` 更名为 `childWithParentRatio`（旧字段弃用保留至 1.0.0）；0.5.0 由 CCJ-008 补齐父块 ID / 配对 / pair 内子块的重复检测；0.6.0 再补齐跨父块悬空引用检测；0.7.0 新增 CCJ-016 切片标识唯一；0.7.1 固化 `MAJOR.MINOR.PATCH` 版本号规则（§12.1）；0.8.0 新增 `headingPath` 透传（C-17）与模板集选型 `/templates/resolve`（C-18）；0.9.0 新增 CCJ-017 标识与序号一致性（`chunkId={strategy}#{index}`、`index` 从 0 连续、`charCount` 与码位数一致）；0.10.0 新增 CCJ-018 策略名与选项一致（error）、CCJ-019 超 `maxChunkSize` 告警（warning）、CCJ-020 元信息等值（error）、CCJ-021 父块结构完整性（error，收编原 CCJ-010 的父块偏移多判），`strategy`/`chunkId` 的 5 处 pattern 收紧到 1..1500，报告增可选 `effectiveOptions`，错误码收为 5 码封闭枚举，新增 `POST /judge/contract-chunks`，normalize 明确「只归一化、不判定」。C# 参考实现已同步，`dotnet build` 0 警告 0 错误、101 项单元测试全部通过（2026-11-14 本地实测，含 0.10.0 四条新规则的 13 项正反用例与 6 处实现偏差回归用例，见 `docs/22`、`docs/23`；CI 每次 push / PR 自动复跑）。

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
- 演示插件桩不在本仓库内（本机开发示例位于 `D:\ChineseChunkJudge.Plugins\StructureExecutorStub`，不属交付物；第三方使用须自备实现 `IChunkExecutor` 的插件）。

## 快速开始

```powershell
# 1) 构建外部插件桩（一次性；<插件目录> 替换为你自己的插件目录）
dotnet build <插件目录>\StructureExecutorStub

# 2) 构建本仓库并跑测试
cd impl/csharp
dotnet build
dotnet test

# 3) 用外部插件运行 Demo
dotnet run --project samples/DemoConsoleApp -- `
  --plugin "<插件目录>\StructureExecutorStub\bin\Debug\net10.0\StructureExecutorStub.dll" `
  --size 600
```

Demo 会输出一份 `structure@600` 的样例 JudgeReport JSON；策略类型取自插件的 `StrategyType`，`--size` 缺省 600。

## 强制约束

1. **Metadata 固定五字段**：`Title`、`SourcePath`、`DocumentType`、`PageInfo`、`CreateTime`，不可删减、不可改名、不可改类型。
2. **质检强制规则**：单块上限 1500 字符告警（CCJ-004）、空切片告警（CCJ-003）；可扩展校验，不可删除基础校验。
3. **策略命名**：`{type}@{targetSize}`，如 `structure@600`、`sentence@900`；基线由 ChunkJudgeBench 产出。
4. **编码**：全仓库 UTF-8；文本文件入库统一 LF（`.gitattributes` / `.editorconfig`）。
5. **契约语言无关，实现可扩展**：`impl/csharp` 为首个参考实现；任何语言实现只要输入输出符合 `contract/schema/*.json` 与 `contract/openapi.yaml`，即属同一契约下的实现（规划：C# / Go / Python / Rust），各实现不得增删契约核心字段、不得降级基础规则。
6. **算法外置**：本仓库（含 samples）不交付切分算法与文档解析器，执行器由外部插件实现 `IChunkExecutor` 注入。

## 版本与发布

- 契约版本 `0.10.0` 对应 tag `v0.10.0`；版本号规则见 `contract/rules.md` §12.1，三处（`rules.md` 表头、`openapi.yaml` 的 `info.version`、`ContractConstants.ContractVersion`）必须同值。
- 三处同值由 `scripts/check_contract_version.py` 自动校验（`python scripts/check_contract_version.py`），CI 作业 `contract-version` 在每次 push / PR 时执行。
- tag 只标注契约版本，不代表实现已稳定；`0.x` 阶段仍可能出现破坏性调整，弃用字段统一保留至 `1.0.0` 清理。

## 参与与自动化检查

- 贡献流程、提交规范、PR 自查清单见 `CONTRIBUTING.md`；契约先行的顺序不可颠倒。
- 安全问题按 `SECURITY.md` 私下报告，不要开公开 Issue。
- 行为准则见 `CODE_OF_CONDUCT.md`；Issue 与 PR 模板在 `.github/` 下。
- CI（`.github/workflows/ci.yml`）在 push 与 PR 到 `main` 时执行四类检查：`check_contract_version.py`（三处版本同值）、`check_contract_integrity.py`（Schema / `$ref` / 模板与产物样本结构）、`check_contract_bump.py`（PR 上校验"改契约必升版"，一次只升一个段），以及 `dotnet build` + `dotnet test`；当前基线为 0 警告 0 错误、101 项测试通过。
- 契约缺陷与整改状态见 `docs/15`；本轮审查结论与关闭证据见 `docs/14`、`docs/16`。当前 4 项阻断级契约缺陷（K-03、K-04、K-05、K-11）仍开放，契约不宜宣布冻结。

## 许可

自研代码以仓库根目录 `LICENSE`（MIT, Copyright (c) 2026 AmbOS-HK）为准。
