# ChineseChunkJudge 切片判定规则

| 项目 | 值 |
| --- | --- |
| 契约版本 | 0.10.1 |
| 状态 | 生效 |
| 权威文件 | 本文件 + `contract/openapi.yaml` + `contract/schema/*.json` |
| 适用范围 | 任意语言、任意实现产出的文本切片结果 |

> 本文件是判定标准的唯一权威来源。`impl/` 下的实现必须对齐本文件，不得私自增删核心字段、不得删除或降级基础规则。
> 变更顺序固定：先改本文件与 schema，再同步参考实现（见第 12 节）。

## 1. 定位与边界

ChineseChunkJudge 判定的是「切片结果是否符合契约」，不判定「哪种切片策略更好」。策略优劣由评测工具 **ChunkJudgeBench** 在同一契约下横向评测得出，本仓库只提供判定基线与参考实现。

本契约与语言无关。`impl/csharp` 是第一个参考实现，不是标准的组成部分。

### 1.1 不做什么（边界约束）

- **不内置文档解析器**：PDF / Word / HTML 等版面解析外置，输入只有已解析的文本与元信息。
- **不内置切片算法**：切分器是外部插件，本仓库任何项目（含 `samples/`）都不得交付生产切分算法。
- **不判定策略优劣**：策略基线由 ChunkJudgeBench 产出并回填，本仓库只负责判定。

## 2. 术语

| 术语 | 定义 |
| --- | --- |
| 文档特征 DocumentFeature | 一次判定请求的输入：正文 + 元信息 + 可选结构提示 |
| 原始切片 RawChunk | 切片器直接产出、尚未补齐契约字段的片段 |
| 契约切片 ContractChunk | 已补齐元信息、字符计数、关系指针的片段，是判定对象 |
| 判定报告 JudgeReport | 一次判定的完整结果：摘要 + 问题清单 + 生效规则 |
| 策略名 strategy | `{type}@{targetSize}`，例如 `structure@600` |
| 基础规则 base rule | 契约强制生效、任何实现不得删除的规则 |
| 结构父 structuralParentChunkId | 文档大纲层级的最近前序 heading 父块，即旧字段 `parentChunkId` 的语义 |
| 上下文父块 contextParent | 双库模式下与子块配对、供下游拼接上下文的粗粒度块；取自原文区间，互不重叠 |
| 父子配对 ContextParentChildPair | `contextParentChunkId` + `childChunkIds[]`，1:N 为主、1:1 合法 |
| 父子产物包 ParentChildPackage | 独立输出产物：全部上下文父块 + 父子关联映射，与 `JudgeReport` 解耦 |
| 切片规则模板 ChunkRuleTemplate | 选型判定（Spec 侧）契约：文档特征 → 切分方式 + 参数 + 验证口径 + 有序决策表，见第 13 节 |
| 恰长硬切 | 切片长度恰好等于 `chunkSize` 且结尾不在允许句读集合（页码容错），由 CCJ-015 判定 |
| 字符数 charCount | 文本的 Unicode 码位（code point）总数 |
| 切片标识 chunkId | 切片的全局唯一身份，非空且不可重复，由 CCJ-016 强制 |

## 3. 数据流转

```
DocumentFeature + ChunkStrategyOptions
        │
        ├─ 执行器 IChunkExecutor ──────────► RawChunk[]
        │
        ├─ 归一化（补元信息、算字符数、构建关系）► ContractChunk[]
        │
        ├─ 规则引擎 ChunkRuleEngine ────────► ChunkIssue[]
        │
        └─ 汇总 ──────────────────────────► JudgeReport
```

双库模式（`parentChild.enabled=true`）下另产出 **`ParentChildPackage`**：全部上下文父块 + 父子关联映射。`JudgeReport` 只承载判定结论、告警与规则检查结果，不承载父块集合，二者解耦，实现方可只消费报告或同时消费产物包。

双库模式的数据流约束：**先按结构单元划分上下文父块，再在父块区间内切子块**（先父后子）。实现仍归外部执行器，本契约只约束产出物结构。由此可推出：双库模式下子块区间天然互不重叠，CCJ-006 应恒不触发；一旦触发即说明上游未遵守该数据流。

### 3.1 执行器插件边界（强制）

- `IChunkExecutor` 是**插件接入点**，不是本仓库提供的算法。执行器实现必须来自仓库外部，以插件形式注入。
- 参考实现的职责仅限：归一化、切片质检、父子层级组装、`JudgeReport` 输出。
- 策略类型 `{type}` 由插件的 `StrategyType` 提供；`targetSize` 由调用方通过选项指定。
- `samples/DemoConsoleApp` 不内置任何切分算法，必须通过 `--plugin <插件dll路径>` 反射加载外部执行器；未提供插件或加载失败时以非零码退出，**不得输出伪造报告**。
- 演示插件桩放在仓库外（当前为 `D:\ChineseChunkJudge.Plugins\StructureExecutorStub`），其存在不改变"本仓库不内置算法"的边界。
- 插件引用 Core 时不得把 `ChineseChunkJudge.Core.dll` 复制进插件输出目录（C# 侧用 `Private="false"`），避免加载出双份 Core 导致接口类型不匹配。

### 3.2 归一化不判定（0.10.0）

`POST /chunks/normalize` 只做 `RawChunk → ContractChunk` 的字段补齐（元信息快照、字符计数、关系指针），**不做判定**、不产出 `JudgeReport`，因此 **CCJ-013 不适用于本端点**。

- 响应保持既有形态（`ContractChunk[]` 裸数组），**不新增父块回显**；父块集合与配对由 `POST /packages/parent-child` 产出。
- `parentChild.enabled=false` 且请求携带 `parentChunks` / `pairs` → 由宿主返回 **400 `invalid_request`**（与 CCJ-013 的语义保持一致，但归一化侧不产生报告）。

## 4. 文档特征 DocumentFeature

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 文档标识 | `documentId` | string | 是 | 文档唯一标识，判定报告按此回指 |
| 元信息 | `metadata` | object | 是 | 固定五字段，见 4.1 |
| 正文 | `content` | string | 是 | 原始文本，偏移的基准串 |
| 语种 | `language` | string | 否 | 默认 `zh` |
| 结构提示 | `structureHints` | object | 否 | 见 4.2 |

### 4.1 Metadata 固定五字段（强制）

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 标题 | `title` | string | 是 | 文档标题，非空白 |
| 来源路径 | `sourcePath` | string | 是 | 原文位置，文件相对路径或来源 URL |
| 文档类型 | `documentType` | enum | 是 | `text` / `markdown` / `html` / `pdf` / `docx` / `other` |
| 页信息 | `pageInfo` | string | 是 | 页码范围或章节位置，如 `1-3`、`第2章` |
| 创建时间 | `createTime` | string(date-time) | 是 | ISO 8601，含时区偏移 |

约束：

- 五字段为契约核心字段，**不可删减、不可改名、不可改类型**。
- 任一字段缺失或为空即触发 **CCJ-001**，判定不通过。
- 每份 `ContractChunk` 都必须携带与文档一致的五字段快照。
- 新增字段属于契约变更，须走第 12 节流程。

### 4.2 structureHints

可选。用于向执行器提示结构边界，缺失时执行器自行识别，判定结果不得依赖该字段。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `headingCount` | integer | 标题数量，≥ 0 |
| `unitBoundaries` | array | 结构单元边界列表 |
| `unitBoundaries[].kind` | enum | `heading` / `article` / `paragraph` / `sentence` / `table` / `code` |
| `unitBoundaries[].startOffset` | integer | 单元起始下标，≥ 0 |
| `unitBoundaries[].endOffset` | integer | 单元结束下标，开区间 |
| `unitBoundaries[].level` | integer | 标题层级，仅 heading 使用，≥ 1 |

## 5. 策略命名与选项

### 5.1 命名规范（强制）

```
strategy = {type}@{targetSize}
```

- `type`：`^[a-z][a-z0-9_-]*$`，小写字母开头，只含小写字母、数字、下划线、连字符。
- `targetSize`：正整数，取值 `1..1500`，不允许前导零。
- 完整校验正则：`^[a-z][a-z0-9_-]*@(?:[1-9]|[1-9][0-9]|[1-9][0-9][0-9]|1[0-4][0-9][0-9]|1500)$`（区间与 §5.1 的 1..1500 同源，可穷举；`@0600`、`@1501`、`@2000` 均不匹配）。
- 策略名与选项的一致性由 **CCJ-018** 校验（`{type}` = `options.strategyType`、`{targetSize}` = `options.targetSize` 且在 1..1500 内）。
- 示例：`structure@600`、`sentence@900`、`fixed@300`。

基线参数由 ChunkJudgeBench 产出，本契约不规定各 `type` 的具体切片算法。

### 5.2 ChunkStrategyOptions

| 字段 | JSON 名 | 类型 | 必填 | 默认 | 说明 |
| --- | --- | --- | --- | --- | --- |
| 策略类型 | `strategyType` | string | 是 | — | 对应 `{type}` |
| 目标块长 | `targetSize` | integer | 是 | — | 1..1500 |
| 单块字符告警阈值 | `maxChunkSize` | integer | 否 | 1500 | 告警阈值，不得放宽；切片字符数超过该值且未超 1500 时由 **CCJ-019** 告警 |
| 单块下限 | `minChunkSize` | integer | 否 | 1 | 1..targetSize |
| 保留结构 | `preserveStructure` | boolean | 否 | true | 是否按结构单元切分 |
| 重叠字符数 | `overlap` | integer | 否 | 0 | 0..targetSize-1 |
| 扩展参数 | `extensions` | object | 否 | — | string→string，实现自行解释，不得改变本契约字段语义 |
| 双库开关与父块阈值 | `parentChild` | object | 否 | 见下 | 双库（Parent/Child）配置，见 5.3 |
| 切分方式 | `chunkMode` | string | 否 | — | 枚举 `regex` / `smart` / `page`，由模板决策回填；`page` 模式豁免 CCJ-015 |

### 5.3 parentChild（双库配置）

| 字段 | JSON 名 | 类型 | 必填 | 默认 | 说明 |
| --- | --- | --- | --- | --- | --- |
| 是否启用 | `enabled` | boolean | 否 | `false` | 强布尔：关闭即本批次不得产出父块相关字段 |
| 父块长度告警阈值 | `parentMaxChars` | integer | 否 | 6000（**临时调试值，非标准基准**） | 正整数；契约**不设全局硬封顶常量**；正式基准由 ≥50 份目标业务文档父块长度 P95 标定 |

- `parentMaxChars` 是**告警阈值配置项**，不是上限：CCJ-009 仅产生告警，不阻塞输出。
- 正式基准值由 ChunkJudgeBench 统计输出（P50/P90/P95/P99）后回填；在样本测算完成前，6000 仅用于本地调试，不纳入正式基线。
- 部署侧可自行追加上限，但不属于本契约。

策略名由 `strategyType` 与 `targetSize` 拼接得出，不单独传入。选项非法触发 **CCJ-007**。

## 6. 原始切片 RawChunk

执行器的唯一产出物，不含契约元信息。

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 序号 | `index` | integer | 是 | 从 0 起，连续 |
| 正文 | `text` | string | 是 | 必须与原文区间逐字一致 |
| 起始偏移 | `startOffset` | integer | 是 | 见第 11 节 |
| 结束偏移 | `endOffset` | integer | 是 | 开区间 |
| 单元类型 | `unitKind` | enum | 否 | `heading` / `article` / `paragraph` / `sentence` / `table` / `code` |
| 单元键 | `unitKey` | string | 否 | 结构单元标识，用于跨块归并 |
| 结构层级 | `structureLevel` | integer | 否 | 块起始处标题层级 |
| 起始齐边界 | `startsAtUnitBoundary` | boolean | 否 | 默认 true |
| 结束齐边界 | `endsAtUnitBoundary` | boolean | 否 | 默认 true |
| 标题路径 | `headingPath` | array&lt;string&gt; | 否 | 自最外层到最内层的祖先标题链，由执行器/解析侧产出，本仓只透传保序，不推断、不补全（见 6.1） |

### 6.1 headingPath

- 语义：块所在位置的结构路径，**最外层在前、最内层在后**；不含块自身标题，只含祖先标题。
- 产出方：执行器或文档解析侧。本仓（`Normalize` 与 Judge）只原样透传，缺失即不输出，不根据正文或 `unitKind` 反推。
- 校验：本期**不新增判定规则**；空串、重复项、末项与块标题是否一致均不校验（见 `docs/06` 未决点）。
- 用途：供 `rag_generation` 拼接上下文与引用回溯，属通用能力，不绑定任何单一消费者。

## 7. 契约切片 ContractChunk

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 切片标识 | `chunkId` | string | 是 | `{strategy}#{index}`，全文档唯一 |
| 序号 | `index` | integer | 是 | 与 RawChunk 一致，从 0 起连续 |
| 正文 | `text` | string | 是 | 必须为原文精确子串，不得 trim 或改写 |
| 起始偏移 | `startOffset` | integer | 是 | 见第 11 节 |
| 结束偏移 | `endOffset` | integer | 是 | 开区间 |
| 策略名 | `strategy` | string | 是 | 符合 5.1 |
| 元信息 | `metadata` | object | 是 | 五字段快照，与文档一致 |
| 字符数 | `charCount` | integer | 是 | Unicode 码位数 |
| 单元类型 | `unitKind` | enum | 否 | 继承自 RawChunk |
| 单元键 | `unitKey` | string | 否 | 继承自 RawChunk |
| 结构层级 | `structureLevel` | integer | 否 | 继承自 RawChunk |
| 起始齐边界 | `startsAtUnitBoundary` | boolean | 否 | 默认 true |
| 结束齐边界 | `endsAtUnitBoundary` | boolean | 否 | 默认 true |
| 标题路径 | `headingPath` | array&lt;string&gt; | 否 | 继承自 RawChunk，原样透传保序，见 6.1 |
| 关系 | `relations` | object | 是 | 见 7.1 |

### 7.1 relations

| 字段 | JSON 名 | 类型 | 说明 |
| --- | --- | --- | --- |
| 前一块 | `previousChunkId` | string \| null | 按 `index` 顺序的前一个切片 |
| 后一块 | `nextChunkId` | string \| null | 按 `index` 顺序的后一个切片 |
| 结构父（旧名，弃用） | `parentChunkId` | string \| null | **deprecated**，1.0.0 移除；语义等同 `structuralParentChunkId` |
| 结构父 | `structuralParentChunkId` | string \| null | 文档大纲层级的最近前序 heading 父块，见下 |
| 上下文父 | `contextParentChunkId` | string \| null | 双库模式下归属的上下文父块；`parentChild.enabled=false` 时不得出现 |

结构父语义：普通块指向**最近的前序 heading 块**；heading 块自身指向**层级更浅的最近前序 heading 块**。调用方已显式指定时保留原值，不覆盖。

别名一致性：`parentChunkId` 与 `structuralParentChunkId` 同时出现时值必须一致，不一致触发 **CCJ-014**（warning）。0.2.0 只增不删，`parentChunkId` 保留以兼容现有消费方。

### 7.2 上下文父块 ContextParentChunk

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 父块标识 | `parentChunkId` | string | 是 | 全文档唯一，建议 `{strategy}#p{index}` |
| 序号 | `index` | integer | 是 | 从 0 起连续（父块序号，由 **CCJ-021** 校验） |
| 正文 | `text` | string | 是 | 必须为原文精确子串，不得改写 |
| 起始偏移 | `startOffset` | integer | 是 | 见第 11 节 |
| 结束偏移 | `endOffset` | integer | 是 | 开区间 |
| 策略名 | `strategy` | string | 是 | 符合 5.1 |
| 元信息 | `metadata` | object | 是 | 五字段快照，与文档一致 |
| 字符数 | `charCount` | integer | 是 | Unicode 码位数（由 **CCJ-021** 校验） |
| 起始齐边界 | `startsAtUnitBoundary` | boolean | 否 | 默认 true，见下「父块边界」约束 |
| 结束齐边界 | `endsAtUnitBoundary` | boolean | 否 | 默认 true，见下「父块边界」约束 |
| 跨父块引用 | `relatedContextParentIds` | array | 否 | 显式 ID 引用，替代文本重叠；ID 必须可解析，禁止自引用与成环 |

约束：

- 上下文父块**互不重叠**，且集合对原文做**完整无重叠划分**（拼接后等价于原文），由 **CCJ-010** 校验；子块不参与该覆盖校验。
- 父块必须落在完整结构单元边界上（`startsAtUnitBoundary` / `endsAtUnitBoundary` 为 true），不得切在半句或条款中间。
- 父块本期不携带 `headingPath`（C-17 只作用于子块）。
- 跨父块引用构成**有向无环图**，检出环路（含自引用）触发 **CCJ-011**（warning，不阻断）。

### 7.3 父子配对与产物包 ParentChildPackage

`ContextParentChildPair`：

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 上下文父块标识 | `contextParentChunkId` | string | 是 | 必须能在父块集合中解析 |
| 子块标识数组 | `childChunkIds` | array | 是 | **非空**；1:N 为主，1:1 为合法边界，不告警 |

`ParentChildPackage`（独立产物，与 `JudgeReport` 解耦）：

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 文档标识 | `documentId` | string | 是 | 回指 DocumentFeature |
| 策略名 | `strategy` | string | 是 | 符合 5.1 |
| 生成时间 | `generatedAt` | string(date-time) | 是 | ISO 8601 |
| 上下文父块集合 | `parentChunks` | array | 是 | 见 7.2 |
| 父子配对集合 | `pairs` | array | 是 | 见上 |

配对基数：一个 contextParent 可绑定多个 childChunk（1:N），允许 1:1。父块侧 `childChunkIds[]` 为**权威来源**，子块侧 `contextParentChunkId` 为派生视图，两侧不一致由 **CCJ-008** 检出。

`parentChild.enabled=false` 时，输出中不得携带 `parentChunks` / `contextParentChunkId`（含 `pairs` 内的声明），检出触发 **CCJ-013**（**error，`passed=false`**）。RawChunk 不携带关系指针，实现必须按父侧 `childChunkIds[]` 回填子侧派生视图，否则归属统计失真。

## 8. 判定规则

### 8.1 级别语义

| 级别 | 含义 | 对 passed 的影响 |
| --- | --- | --- |
| `error` | 违反契约核心约束 | 阻断，`passed = false` |
| `warning` | 质量缺陷，不阻断 | 仅计入告警 |
| `info` | 信息性记录 | 不影响 |

`passed` 的判定口径：问题清单中不存在 `error` 级问题。

### 8.2 契约常量

| 常量 | 值 | 说明 |
| --- | --- | --- |
| `MAX_CHUNK_CHARS` | 1500 | 子块字符数**告警阈值**，任何选项不得放宽；CCJ-004 作用域仅 Child Chunk |
| `PARENT_MAX_CHARS_DEBUG` | 6000 | 父块长度告警阈值的**临时调试默认值**，非标准基准；正式值由样本 P95 标定，契约不设全局硬封顶 |

### 8.3 基础规则表（CCJ-001..CCJ-021）

| 编号 | 级别 | 触发条件 | 输出要点 |
| --- | --- | --- | --- |
| CCJ-001 | error | Metadata 五字段任一缺失或为空 | 逐字段报告，含命中处数 |
| CCJ-002 | error | `strategy` 不匹配 `^[a-z][a-z0-9_-]*@[1-9][0-9]*$` | 报告非法策略名 |
| CCJ-003 | warning | 切片正文为空或仅空白（含全角空格、制表符、换行） | 报告空切片标识 |
| CCJ-004 | warning | `charCount > 1500` | 报告实际字符数 |
| CCJ-005 | error | 偏移越界、`endOffset <= startOffset`、或 `text` 与原文该区间不一致 | 报告区间与差异类型 |
| CCJ-006 | warning | 相邻切片区间重叠（前一块 `endOffset` > 后一块 `startOffset`） | 报告相邻切片对。注：`overlap > 0` 时的相邻重叠属**预期告警**，仍按 warning 报出；`summary` 不另设重叠计数字段 |
| CCJ-007 | error | 选项非法：`targetSize` 越界、`maxChunkSize > 1500`、`maxChunkSize < targetSize`、`minChunkSize` 越界、`overlap` 越界、`parentMaxChars` 非正整数、`chunkMode` 非法枚举 | 逐项报告非法参数 |
| CCJ-008 | error | 配对结构不完整：`childChunkIds` 为空或数组内重复、ID 无法解析、父子两侧不一致、一个子块归属多个上下文父块、父块 ID 重复、子块 ID 重复、同一父块存在多条配对、跨父块引用 ID 无法解析 | 报告父块标识与问题类型 |
| CCJ-009 | warning | 上下文父块 `charCount > parentMaxChars` | 报告实际字符数与阈值，与 CCJ-004 分开计数 |
| CCJ-010 | error | 上下文父块区间重叠、非原文精确子串、或集合拼接后不等价于原文（未构成完整无重叠划分） | 报告区间与差异类型 |
| CCJ-011 | warning | 跨父块引用存在环路（含自引用） | 报告环路路径 |
| CCJ-012 | error | 子块区间未落在其归属 contextParent 区间内 | 报告子块与父块区间 |
| CCJ-013 | error | `parentChild.enabled=false` 时输出存在 `parentChunks` 或 `contextParentChunkId`（含 `pairs` 内的声明） | 报告检出字段，判定不通过 |
| CCJ-014 | warning | `parentChunkId`（deprecated）与 `structuralParentChunkId` 同时出现但值不一致 | 报告两个值 |
| CCJ-015 | error | 恰长硬切：`charCount` 恰好等于 `chunkSize` 且结尾不在允许句读集合（页码容错）；`page` 模式豁免，父块不适用 | 报告子块标识、长度与结尾片段 |
| CCJ-016 | error | 切片标识非法：`chunkId` 为空、纯空白或重复；单库与双库一律执行 | 报告 `chunkId` 与出现次数 |
| CCJ-017 | error | 切片标识、序号与计数不一致：`chunkId` 不等于 `{strategy}#{index}`；`index` 不是从 0 起连续（含重复）；`charCount` 不等于 `text` 的 Unicode 码位数。只作用于 Child Chunk | 报告字段、期望值与实际值 |
| CCJ-018 | error | 策略名与选项不一致：子块 `strategy` 的 `{type}` ≠ `options.strategyType`；或 `{targetSize}` ∉ `1..1500`；或 `{targetSize}` ≠ `options.targetSize`。**前置**：仅在该块 `strategy` 通过 CCJ-002（正则匹配成功）后执行，避免同一块报两条 | 报告期望值与实际值 |
| CCJ-019 | warning | 切片字符数超 `maxChunkSize`：`options.maxChunkSize < 1500` **且** `charCount > options.maxChunkSize` **且** `charCount <= 1500`。`maxChunkSize = 1500` 时本规则不触发；`charCount > 1500` 由 CCJ-004 独占，不双计 | 报告实际字符数与配置阈值 |
| CCJ-020 | error | 元信息与文档不等值：子块或父块 `metadata` 五字段与 `document.metadata` 不一致（`createTime` 按 ISO 8601 解析后比较时刻，解析失败按逐字比较） | 逐字段报告差异 |
| CCJ-021 | error | 父块结构完整性：`index` 非 `0..N-1` 连续（含重复、缺号）；`charCount` ≠ `text` 的 Unicode 码位数；`startOffset` / `endOffset` 越界或 `endOffset <= startOffset`。只作用于 ContextParentChunk | 报告字段、期望值与实际值 |

### 8.4 基础规则的不可变性

- CCJ-001..CCJ-021 全部为**基础规则**：不可删除、不可改编号、不可降级级别、不可改变判定条件。
- **规则作用域不重叠**：CCJ-018 只在 CCJ-002 通过后执行；CCJ-019 的 `charCount <= 1500` 排除区使其与 CCJ-004 互斥；CCJ-005 只遍历子块，CCJ-021 只校验父块，两者不双计；CCJ-021 承担父块的偏移与计数校验，**CCJ-010 仍只做**「区间重叠 / 非精确子串 / 拼接不等价」三项。
- 实现可以提供「扩展规则」新增校验，但**不得删除基础校验**，也不得用扩展规则豁免基础规则判出的问题。
- 基础规则可以按需关闭输出级别（如将 warning 降为 info）仅限实现内部展示层，不得改变 `JudgeReport` 中的契约语义。
- **长度校验作用域**：CCJ-004 作用域为 **Child Chunk**（阈值 1500，不可豁免）；上下文父块由 **CCJ-009** 独立约束（阈值 `parentMaxChars`，仅告警）。这是**规则作用域拆分**，不是对 CCJ-004 的豁免。
- 双库模式下的推论：父块互不重叠 + 全文划分 + 子块区间 ⊆ 父块区间，可推出子块区间互不重叠，CCJ-006 此时应恒不触发；触发即说明上游未遵守「先父后子」数据流。

### 8.5 CCJ-015 恰长硬切判定细则

- **判定对象**：`ContractChunk`（子块）。`chunkMode=page` 豁免；上下文父块不适用。
- **判定条件（同时满足）**：`charCount` 恰好等于当前生效 `chunkSize`（`options.targetSize`）；切片结尾不属于允许结束集合。
- **允许结束集合**：中文句读 `。！？；…` 及英文 `.!?;`，含闭合形式（结尾为 `” ’ ） 】 》` 且前一位为句读）。
- **页码容错**：结尾匹配「句读 + 可选空白 + 1..4 位纯数字」视为页码，不计硬切。
- **超限不判**：`charCount > chunkSize` 由 CCJ-004 处理；小于不判。
- **级别**：error，`passed=false`；计数入 `summary.hardCutChunkCount`。

### 8.6 CCJ-017 标识与序号一致性判定细则

- **判定对象**：`ContractChunk`（子块）。父块不适用；父块标识仍由 CCJ-008 唯一性与格式建议约束。
- **`chunkId`**：必须严格等于 `{strategy}#{index}`，逐字比较；为空或纯空白时不判格式（由 CCJ-016 处理），避免重复报错。
- **`index`**：集合必须是 `0..N-1` 的完整序列，N 为子块总数；重复与缺号分别报告。
- **`charCount`**：必须等于 `text` 的 Unicode 码位数；`text` 为空串时 `charCount` 必须为 0。
- **级别**：error，`passed=false`。该规则为 0.9.0 新增，属收紧判定，不改变既有字段与规则语义。

## 9. 判定报告 JudgeReport

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 报告标识 | `reportId` | string | 是 | 单次判定唯一 |
| 文档标识 | `documentId` | string | 是 | 回指 DocumentFeature |
| 策略名 | `strategy` | string | 是 | 符合 5.1 |
| 目标块长 | `targetSize` | integer | 是 | 1..1500 |
| 生成时间 | `generatedAt` | string(date-time) | 是 | ISO 8601 |
| 是否通过 | `passed` | boolean | 是 | 见 8.1 |
| 摘要 | `summary` | object | 是 | 见 9.1 |
| 问题清单 | `issues` | array | 是 | 见 9.2，可为空数组 |
| 生效规则 | `appliedRules` | array | 是 | 见 9.3 |
| 生效选项 | `effectiveOptions` | object | 否 | 判定所依据的 `ChunkStrategyOptions` 原样回显（直接复用 5.2 定义，不复制字段）；用于复核 `targetSize` / `maxChunkSize` 等影响结论的取值 |

### 9.1 summary

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `chunkCount` | integer | 切片总数 |
| `totalCharCount` | integer | 字符数合计 |
| `minCharCount` | integer | 最短切片字符数，无切片时为 0 |
| `maxCharCount` | integer | 最长切片字符数，无切片时为 0 |
| `averageCharCount` | integer | 平均字符数，四舍五入取整（half-up，即 `AwayFromZero`） |
| `emptyChunkCount` | integer | 空切片数量 |
| `oversizedChunkCount` | integer | 超过 1500 字符的切片数量 |
| `parentChunkCount` | integer | 上下文父块总数，无双库产物时为 0 |
| `childWithParentCount` | integer | 绑定到上下文父块的子块数量 |
| `parentCoverageRatio` | number | **deprecated**，1.0.0 移除；语义等同 `childWithParentRatio`，实为子块归属比例，命名易误读为父块文本覆盖率 |
| `childWithParentRatio` | number | `childWithParentCount` / 子块总数；无子块时为 0。0.4.0 起为正式字段 |
| `parentCharP95` | integer | 上下文父块字符长度 P95，用**最近秩法**：将长度升序排序后取第 `ceil(0.95*N)-1` 个元素（0 基下标），N=0 时为 0 |
| `oversizedParentChunkCount` | integer | 超过 `parentMaxChars` 的父块数量，与 `oversizedChunkCount` 独立累加 |
| `hardCutChunkCount` | integer | 恰长硬切子块数量（CCJ-015），验收口径为每文档必须为 0 |

### 9.2 issue

| 字段 | JSON 名 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| 规则编号 | `ruleId` | string | 是 | `CCJ-###` 或扩展编号 |
| 级别 | `severity` | enum | 是 | `info` / `warning` / `error` |
| 切片标识 | `chunkId` | string \| null | 否 | 文档级问题为 null |
| 说明 | `message` | string | 是 | 人类可读，含关键数值 |
| 细节 | `detail` | object | 否 | string→string，机读字段 |

### 9.3 appliedRules

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `ruleId` | string | 规则编号 |
| `description` | string | 规则说明 |
| `severity` | enum | 声明级别 |
| `isBaseRule` | boolean | 是否基础规则 |

## 10. 扩展规则

- 编号从 `CCJ-X001` 起递增，格式 `^CCJ-X[0-9]{3}$`。
- 必须声明 `ruleId`、`description`、`severity`、`isBaseRule`，扩展规则的 `isBaseRule` 必须为 `false`。
- 编号全局唯一，不得与基础规则或已注册规则重名。
- 扩展规则只允许**新增问题**，不得修改、抑制或豁免基础规则已判出的问题。

## 11. 字符与偏移口径

- `startOffset` / `endOffset` 以原始 `content` 的下标为单位，区间为左闭右开 `[startOffset, endOffset)`。参考实现使用 UTF-16 code unit 下标，与 `String.Substring` 语义一致；其他语言实现须在文档中声明等价口径。
- `charCount` 为 Unicode 码位数量。对纯中文文本，码位数等于字符数。
- `text` 必须逐字等于 `content[startOffset..endOffset)`：不得 trim、不得改写、不得拼接、不得做全半角归一化。
- 「空切片」指 `text` 为空串或仅由空白字符（含半角/全角空格、制表符、换行）构成。

## 12. 变更流程（契约先行）

1. 修改 `contract/`：先改本文件，再改 `schema/*.json`，最后改 `openapi.yaml`。
2. 同步 `impl/` 参考实现，补充对应单元测试。
3. **版本号规则**（见 §12.1）：格式固定为 `MAJOR.MINOR.PATCH` 三段十进制，无前导零；三处必须同值。
4. 禁止实现侧单方面修改核心字段；发现契约与实际不符时，先改契约再改实现。

### 12.1 版本号规则（MAJOR.MINOR.PATCH）

版本号固定三段：`MAJOR.MINOR.PATCH`，每段十进制、无前导零、不得跳号、不得回退。判定按下列顺序，命中即停：

| 变更性质 | 版本位 | 判定位（命中即停） |
| --- | --- | --- |
| 删除字段、字段转必填、放宽判定条件、改变既有字段语义、改变规则级别 | `MAJOR` | 1 |
| 新增字段（含可选）、新增基础规则、新增端点、收紧判定条件、新增枚举值 | `MINOR` | 2 |
| 仅缺陷修复、契约文本澄清、文档/注释修订，不改任何可观察行为 | `PATCH` | 3 |

补充约束：

1. **三处同值**：`contract/rules.md` 表头、`contract/openapi.yaml` 的 `info.version`、C# `ContractConstants.ContractVersion` 必须一致；`openapi: 3.1.0` 是规范版本，永远不参与递增。
2. **违规即缺陷**：任一处漏改或数值不一致，按契约缺陷处理，不得只改文档了事。
3. **一次变更一个位**：同一批次只能递增一个段；若同时含 minor 与 patch 性质，取较高位。
4. **可观察行为**是判定基准：字段增删、必填性、枚举、规则编号/级别/判定条件变化属可观察行为；纯措辞、示例、排版属不可观察。
5. 0.x 阶段仍适用本规则；「只增不删、弃用别名 1.0.0 清理」的冻结约定优先于上表。

## 13. 切片规则模板 ChunkRuleTemplate（0.3.0，Spec 侧）

模板承载**选型判定**：文档特征 → 切分方式 + 参数 + 验证口径。模板只描述决策，不执行切分（执行仍归外部执行器，§1.1）；库名等部署标识不进模板，由部署侧映射到 `templateId`。

### 13.1 模板字段

| 字段 | 类型 | 必填 | 约定 |
| --- | --- | --- | --- |
| `templateId` | string | 是 | `^[a-z][a-z0-9_-]*$` |
| `version` | string | 是 | 模板自身版本，与契约版本独立 |
| `name` | string | 是 | 展示名 |
| `appliesTo` | object | 是 | `documentCategories: string[]`（必填）+ `traits: enum[]`（**可选**，仅供人读，**不参与准入**） |
| `decision` | object | 是 | 见 13.2 |
| `validation` | object | 否 | `hardCutZero: boolean = true`；`acceptanceRef: string` 可选（回归执行归 Bench） |

`traits` 封闭枚举：`article_numbering`（第X条/章）、`section_numbering`（（一）（二）分节）、`weak_structure`、`dense_table`、`pdf_no_structure`。扩枚举走次版本。

### 13.2 决策表

- `decision.rules`：有序数组，**先匹配先生效**；每条 = `{ traits, chunkMode, pattern?, chunkSize, parentChild?, strategyType?, forbidLineStartAnchor?, forbidHeadingSplit?, preserveSeparator? }`，`traits` 全部满足才命中。
- `decision.fallback`：**必填兜底**，`chunkMode=smart` 且 `chunkSize` 必填；未命中任何规则时必须落 fallback，不允许无决策结果。
- `chunkMode` ∈ `regex` / `smart` / `page`；`regex` 时 `pattern` 必填（`minLength 1`），其余模式禁止携带 `pattern`——由 Schema `if/then/else` 机器约束，不靠人读。
- `pattern` 的**单一来源**是模板与选型响应（`TemplateDecision.pattern`）；决策结果中的 `pattern` **不回填 `options`**，执行器需要时由宿主从选型响应取。
- 正则禁忌用枚举字段：`forbidLineStartAnchor: boolean = true`（禁 `^` 多行锚）、`forbidHeadingSplit: boolean = true`（禁 h3 标题切分）、`preserveSeparator: boolean = true`（保留分隔符）。
- `chunkSize`：integer，1..1500；`smart` 为软上限。
- `parentChild` 引用 §5.3 配置，不复制字段。

### 13.3 模板集选型（0.8.0）

`POST /templates/resolve` 请求二选一：

| 形态 | 字段 | 语义 |
| --- | --- | --- |
| 单模板（旧） | `template` | 兼容保留，**deprecated**，1.0.0 移除；模板由调用方选定，`appliesTo` 不参与准入 |
| 模板集（0.8.0） | `templates[]` | 由本仓选型：类别过滤 + 顺序择优 |

约束：

1. `template` 与 `templates` **恰好提供一个**；同时提供或都不提供返回 400。该约束由 Schema `oneOf` 机器约束（见 `openapi.yaml` `TemplateResolveRequest`）。
2. `templates` 必须非空。
3. 类别过滤：保留 `appliesTo.documentCategories` 与请求 `documentCategories` 有交集的模板；`appliesTo.documentCategories` 为空数组**不视为通配**。`appliesTo.traits` **不参与准入**，仅供人读；模板与请求两侧的 `traits` 只用于 `decision.rules` 匹配。
4. 优先级：候选按请求数组顺序，顺序即优先级，不引入 `priority` 字段。
5. 择优：存在「规则命中」的候选时，取顺序最前命中者（`selectionReason=rule_matched`）；全部未命中时，取顺序最前候选的 `fallback`（`selectionReason=fallback`）。不得采用「首个候选无论是否命中都兜底」。
6. 类别过滤后为空：返回 400，错误码 `no_applicable_template`。
7. 响应字段：`templateId` 为**实际选中**的模板；新增 `candidateCount`（类别过滤后候选数）与 `selectionReason`（`rule_matched` / `fallback`）。
8. 单模板形态行为与 0.7.1 一致，`candidateCount` 固定为 1。

#### 13.3.1 决策结果 → `ChunkStrategyOptions` 映射（0.10.0）

选型结果只承载决策，落到执行需要按下表转换成选项：

| options 字段 | 来源 | 缺省 |
| --- | --- | --- |
| `strategyType` | `decision.strategyType` | `regex`→`regex`、`page`→`page`、`smart`→`smart` |
| `targetSize` | `decision.chunkSize` | 无缺省，必填 |
| `chunkMode` | `decision.chunkMode` | 无缺省 |
| `pattern` | `decision.pattern`（仅 regex） | 无；且**不回填 options** |
| `parentChild` | `decision.parentChild` | 不启用 |
| `strategy` | `{strategyType}@{targetSize}` | 由上式拼出 |

`page` 模式的 `chunkSize` 语义：**期望页长**。按页边界切，单块可短于或长于该值；CCJ-015 豁免不变；超过 1500 仍由 CCJ-004 告警。

`smart` → `smart` 是**策略名直映**（不推断算法族），`chunkMode` 与 `strategyType` 因此一一对应。

## 13.4 HTTP 契约摘要（0.10.0）

本节只给端点清单、请求必填与错误码登记表；**字段细节以 `contract/openapi.yaml` 为准，本节不复制字段定义**。

| 端点 | 请求必填 | 成功响应 |
| --- | --- | --- |
| `GET /rules` | — | `RuleManifest` |
| `POST /chunks/normalize` | `document`、`options`、`rawChunks` | `ContractChunk[]`（裸数组） |
| `POST /judge` | `document`、`options`、`rawChunks` | `JudgeReport` |
| `POST /judge/contract-chunks`（0.10.0） | `document`、`options`、`chunks` | `JudgeReport` |
| `POST /templates/resolve` | `documentCategories`、`traits`，且 `template` / `templates` 恰好一个 | `TemplateDecision` |
| `POST /packages/parent-child` | `document`、`options`、`chunks` | `ParentChildPackage` |
| `POST /judge/execute` | `document`、`options`、`executor` | `JudgeReport` |

**错误码登记表（封闭枚举，新增码需升版）**：

| 码 | HTTP | 场景 |
| --- | --- | --- |
| `invalid_request` | 400 | 请求体结构或必填缺失 |
| `invalid_options` | 400 | 选项非法（与 CCJ-007 同源） |
| `invalid_chunk` | 400 | 切片结构不合法，无法进入判定 |
| `no_applicable_template` | 400 | 类别过滤后无候选（§13.3 第 6 条） |
| `executor_not_registered` | 404 | `/judge/execute` 未注册执行器 |

资源边界（请求体大小、切片数量上限等）本期**不写入契约**：本仓无 HTTP 宿主，写了也无法执行与验证，待宿主落地时另起 MINOR 定义。
