# 21 AEC.Chat 小循环构建方案（2026-11-14）

- 状态：**方案，未实施**。本轮不改 `contract/`、不改 `impl/`、不改 `templates/`。
- 依据：`contract/`（现行 0.9.0）、`docs/08`（模板生产线）、`docs/12`（类别→模板→Bench 映射）、`docs/14`（K-28 无 HTTP 宿主）、`docs/18`、AEC.Chat `Law/_src/bailian/切片判定单.md` 与建库脚本、`D:\ChunkJudgeBench\HANDOFF.md`。
- 约束：本文只定义流程与接口形态，不新增契约条目；所有"当前不可用"的判断均给出证据位置。

## 0. 一句话

小循环 = **单批文档在导入百炼之前**，走完「打特征 → 选模板 → 实切 → 取真实切片 → 判定 → 回填证据」的一次闭环。它不重建全库、不跑 27 问全集，也不产生共享模板——只产生可复用的证据。

## 1. 三个圈的分界（先分清，否则流程会互相污染）

| 圈 | 触发 | 范围 | 出口物 | 现有承载 |
| --- | --- | --- | --- | --- |
| **小循环**（本文） | 每批导入前 | ≤10 份/批（AEC 规范 §10-1） | `TemplateDecision` + `JudgeReport` + 判定记录行 | **无，待建** |
| **大循环** | 每库建成/规范变更 | 全库重建 + Metadata 对账 + 27 问 + 发布 | 发布版检索/问答服务 | AEC.Chat 现有脚本与 `kb_lint --online` |
| **演进循环** | 同签名累计 ≥30 份 | 模板晋升/回退 | 共享模板（带版本） | `docs/08` §6，**未实施** |

关键：`check_hardcut.sh`、`kb_lint §3.7`、27 问验收**现在是导入后的检查**。小循环的价值就是把"结构性"那一部分（硬切、边界、尺寸）**提前到导入前**；27 问仍留在大循环，不进小循环。

## 2. 三个系统的职责（不得越界）

| 系统 | 能做什么 | 不能做什么 | 当前落地形态 |
| --- | --- | --- | --- |
| AEC.Chat | 唯一接触百炼的一方：建库、导入、拉取真实切片、检索/问答验收 | 不定义判定规则、不产出文档特征、不改契约 | `Law/_src/bailian/`：`bl` CLI + `create_v2` 直连接口 + `aec-rag` skill |
| ChineseChunkJudge | 选型（`TemplateResolver`）、判定（CCJ-001..017）、双库产物包 | **不执行切分、不解析文档、不产出特征**（§3.1） | C# 库 `impl/csharp`，**无 HTTP 宿主**（docs/14 K-28） |
| ChunkJudgeBench | 多策略/多尺寸的检索证据质量评测、可复现报告 | 不做合规判定、不产出模板、不做文档分类 | Python CLI，四种内置策略，**不能吃外部切片** |

## 3. 小循环时序（7 步）

```
S1 批次登记        批次清单（≤10 份，记来源/年度/结构变体）
S2 特征打标        documentCategory（14 类草案）+ traits（5 枚举）
S3 选型            templates[] + 类别 + traits → TemplateDecision
S4 百炼实切        import --chunk-mode / --chunk-size / --separator
S5 取真实切片      chunk list → RawChunk（含 offset 定位）
S6 判定            rawChunks + options → JudgeReport
S7 回填与归档      判定记录行 + 证据四项 → 交大循环/演进循环
```

| 步 | 输入 | 动作 | 产物 | 失败处置 |
| --- | --- | --- | --- | --- |
| S1 | 待导入文件、所属库 | 按 ≤10 份切批，登记 `sourceGroup`（地区/年度/来源） | 批次清单 | 超 10 份拆批；同源集中则拆开，防同源过拟合 |
| S2 | 同名正文、结构特征 | 按 `docs/05` 口径打类别与 traits；拿不准只打 `weak_structure` | `documentCategories[]` + `traits[]` | 类别不在 14 类 → `other`，不猜 |
| S3 | 类别 + traits + 模板集 | 按 `appliesTo` 过滤、数组顺序择优、未命中必须落 `smart` 兜底 | `TemplateDecision`（`usedFallback`/`selectionReason`/`candidateCount`） | `no_applicable_template` → 补模板或显式落 `other` 兜底，不得静默用旧参数 |
| S4 | 决策 | 以**导入参数**为准实切（建库配置不作数） | 百炼实际切片 | 导入失败 → 本批中止，不改参数重试 |
| S5 | `chunk list` 结果 + 平台解析后正文 | 逐块定位 offset、重编 `index`/`chunkId` | `RawChunk[]` | 定位失败 → **不产契约报告**（见 §4.3） |
| S6 | `RawChunk[]` + `options` | 归一化 + 判定 | `JudgeReport` | error → 回 S3/S4，本批不导入 |
| S7 | 判定报告 + Bench 结果 | 写判定记录行；证据四项归档 | 记录行 + 证据包 | 缺任一项，该批不进晋升评估（`docs/08` §5） |

## 4. 接口与数据（今天真实可用的形态）

### 4.1 选型请求（语义同 `POST /templates/resolve`）

```json
{
  "templates": [ "<aec-law-v1.json>", "<aec-plans-v1.json>" ],
  "documentCategories": ["law_regulation"],
  "traits": ["article_numbering"]
}
```

响应取 `templateId / chunkMode / pattern / chunkSize / usedFallback / selectionReason / candidateCount`。**今天只能库内调用**：仓库没有 HTTP 宿主（docs/14 K-28），端点行为只到库 API 层。

### 4.2 metadata 映射：两套"五字段"不是同一组

AEC 侧 `meta_extract_info`（实测，`state/doc_list_law_final.json`）= `region / admin_level / doc_type / year / plan_period`；
契约 `document.metadata` = `title / sourcePath / documentType / pageInfo / createTime`，`additionalProperties: false`，**塞不进前者**。

| 契约字段 | 取值来源 | 语义偏差（必须登记，不能掩盖） |
| --- | --- | --- |
| `title` | `doc_name` | 一致 |
| `sourcePath` | 原始 URL 或 `file_path` | 一致 |
| `documentType` | `doc_type`：`md→markdown`、`pdf→pdf`、`html→html`、`docx→docx`，其余 `other` | 平台用 `md`，契约用 `markdown`，需映射 |
| `pageInfo` | **无来源** | 契约要求"页码范围或章节位置"，平台不提供；暂用固定占位（如 `全篇`）属权宜，不是事实 |
| `createTime` | `gmt_modified`（毫秒） | **实为索引构建时间，不是文档创建时间**；按 ISO 8601 +08:00 转换 |

AEC 的 `region / admin_level / year / plan_period` 留在 AEC 侧做检索过滤与 `sourceGroup`，**不进契约**（进了会被 Schema 拒）。

### 4.3 `RawChunk` 生成规则（小循环最容易造假的一步）

`raw-chunk.json` 要求 `index / text / startOffset / endOffset`，且 CCJ-005 会校验 `text == content[startOffset..endOffset)`。

实测百炼 `chunk list` 只给 `_chunk_id`、`text`、`metadata.content`，**不返回字符偏移**（`state/chunks_final/law_national.json`）。因此：

1. `document.content` 必须取**平台解析后的正文**（chunk metadata 里的 `doc_url`，形如 `.../xxx.md`），不能用本地原始 MD——平台会剥除 `###` 标记（判定单 §2-1 已坐实）。
2. 定位从上一块的 `endOffset` 起向后查，允许按 `overlap` 回退，避免重复段落锚错位置。
3. 定位失败（空白归一/字符替换造成不一致）→ **不生成契约判定报告**；只出"可判项诊断"（CCJ-003/004/015/016/017 这类只依赖 `text`、`index`、`charCount` 的规则），并在报告头写明"非契约报告"。禁止拼接合成正文来骗过 CCJ-005。
4. `chunkId` 由本地重编为 `{strategy}#{index}`（如 `regex@1200#0`），`index` 从 0 连续；平台 `_chunk_id` 只作溯源。
5. `chunkMode=page` 的文档**不进契约判定**：页块在解析后正文里通常不连续，偏移必然不成立；其验证走 AEC 侧 `check_hardcut.sh` + 人工抽查，CCJ-015 本身也对 page 豁免。

### 4.4 判定门槛（小循环的硬闸）

| 判据 | 阈值 | 不通过的处置 |
| --- | --- | --- |
| `passed` | `true`（无 error） | 回 S3 调模板或回 S4 换分流 |
| `summary.hardCutChunkCount` | `0` | 调 `chunkSize`（表格密集件 1500 封顶）或改 regex |
| `summary.oversizedChunkCount` | `0` | 同上 |
| CCJ-016 / CCJ-017 | 无 error | 修 `chunkId`/`index`/`charCount` 映射，不放宽 |
| Bench 三项（R@5、引文完整率、单元完整率） | 不低于现役模板或基线 | 回退模板版本（`docs/08` §6.2） |

### 4.5 Bench 侧缺的入口

`chinese_chunk/cli.py` 只有 `chunk`/`eval` 两个子命令，`evaluate_strategy` 内部 `build_chunker(...)` 自建切片器，**无法评测"别人切好的块"**。要接小循环，最小改造是：

1. 新增 `load_chunks_jsonl(path)`，把 `{doc, index, text, start_offset, end_offset, heading_path}` 还原为 `Chunk`；
2. `evaluate_chunks(docs, qa, chunks)` 复用现有指标计算，只跳过切分步骤；
3. CLI 增 `eval --chunks out/aec_batch.jsonl --label aec-template-v1`；
4. `corpus_manifest.json` 增 `documentCategory` / `benchFamily`（`docs/12` §5 已列）。

## 5. 判定单 V1.0 → 规则模板（迁移对照）

《切片判定单 V1.0》是**人读流程单**（库归属→结构判定→导入→硬切验证→回归），不是机器可读模板。字段级缺口 6 项：`templateId`、`version`、`name`、`appliesTo`、`decision.rules`（有序）、`fallback`；另有 `validation.acceptanceRef`。迁移后流程单**保留**，作为 SOP；模板只承接"决策"那一段。

| 判定单要素 | 模板落点 |
| --- | --- |
| 库归属（aec-law / aec-plans） | **不进模板**（部署维度）；由调用方选模板集 |
| 正文有 `第X条` | `decision.rules[].traits=[article_numbering]` + `chunkMode=regex` + `chunkSize=1200` |
| 有 `（一）` 分节 | `traits=[section_numbering]` + 同参数 |
| 弱结构叙事 | `traits=[weak_structure]` + `smart`（law 1200 / plans 1000） |
| 密集指标表 | `traits=[dense_table]` + `smart` 1500（仅 plans 分支，放在 rules 首位） |
| 恰长硬切=0 | `validation.hardCutZero=true` |
| 27 问回归 | `validation.acceptanceRef="aec-27q"`（执行归 Bench/大循环） |
| `禁 ^ 多行锚`、`禁 h3 切分`、`保留分隔符` | `forbidLineStartAnchor/forbidHeadingSplit/preserveSeparator` 三个枚举字段 |

两份迁移草案（**已按 `chunk-rule-template.json` 校验 PASS，未入 `templates/`、未标定**）：

`aec-law-v1`（3 条规则，fallback smart 1200）：

```json
{
  "templateId": "aec-law-v1",
  "version": "1.0.0",
  "name": "AEC 法规库切片模板（判定单 V1.0 迁移草案）",
  "appliesTo": {
    "documentCategories": ["law_regulation"],
    "traits": ["article_numbering", "section_numbering", "weak_structure"]
  },
  "decision": {
    "rules": [
      {
        "traits": ["article_numbering"],
        "chunkMode": "regex",
        "pattern": "(?=第[一二三四五六七八九十百零〇0-9]+[条章])",
        "chunkSize": 1200,
        "forbidLineStartAnchor": true,
        "forbidHeadingSplit": true,
        "preserveSeparator": true
      },
      {
        "traits": ["section_numbering"],
        "chunkMode": "regex",
        "pattern": "(?=（[一二三四五六七八九十]+）)",
        "chunkSize": 1200,
        "forbidLineStartAnchor": true,
        "forbidHeadingSplit": true,
        "preserveSeparator": true
      },
      {
        "traits": ["weak_structure"],
        "chunkMode": "smart",
        "chunkSize": 1200
      }
    ],
    "fallback": { "chunkMode": "smart", "chunkSize": 1200 }
  },
  "validation": { "hardCutZero": true, "acceptanceRef": "aec-27q" }
}
```

`aec-plans-v1`（4 条规则，`dense_table` 前置，fallback smart 1000）：

```json
{
  "templateId": "aec-plans-v1",
  "version": "1.0.0",
  "name": "AEC 规划文本切片模板（判定单 V1.0 迁移草案）",
  "appliesTo": {
    "documentCategories": ["government_plan"],
    "traits": ["article_numbering", "section_numbering", "weak_structure", "dense_table"]
  },
  "decision": {
    "rules": [
      { "traits": ["dense_table"], "chunkMode": "smart", "chunkSize": 1500 },
      {
        "traits": ["article_numbering"],
        "chunkMode": "regex",
        "pattern": "(?=第[一二三四五六七八九十百零〇0-9]+[条章])",
        "chunkSize": 1200,
        "forbidLineStartAnchor": true,
        "forbidHeadingSplit": true,
        "preserveSeparator": true
      },
      {
        "traits": ["section_numbering"],
        "chunkMode": "regex",
        "pattern": "(?=（[一二三四五六七八九十]+）)",
        "chunkSize": 1200,
        "forbidLineStartAnchor": true,
        "forbidHeadingSplit": true,
        "preserveSeparator": true
      },
      { "traits": ["weak_structure"], "chunkMode": "smart", "chunkSize": 1000 }
    ],
    "fallback": { "chunkMode": "smart", "chunkSize": 1000 }
  },
  "validation": { "hardCutZero": true, "acceptanceRef": "aec-27q" }
}
```

注意两点（不是笔误）：
1. 契约要求 `chunkSize` **必填**，`page` 模式也躲不掉；page 模板只能填名义值，且 CCJ-015 对它豁免——这条属契约侧遗留（0.10.0 未处理），迁移时先在 `page` 分支记名义值，别自己造"省略"。
2. `dense_table` 放在 plans 首条是**判定单 §4 的口径**（表格密集件 1500）；law 模板没有这张分支，因判定单未给 law 表格处置。要不要给 law 也加 `dense_table`，属未决（§9-4）。

## 6. 三个门与失败分流

| 门 | 判据 | 不通过时先动什么 | 记录字段 |
| --- | --- | --- | --- |
| 选型门 | `usedFallback=false`；连续 3 批 fallback 即报警 | 补模板（`docs/12` §4 补齐顺序） | `templateId`、`selectionReason`、`candidateCount` |
| 结构门 | `passed=true` 且 硬切 0、超限 0 | 先调 `chunkSize`，再换 `chunkMode`，最后才改 `pattern` | `reportId`、`issues[].ruleId` |
| 质量门 | Bench 三项不低于基线 | 回退上一模板版本，不删版本 | `acceptanceRef`、指标行 |

失败分流只有两个方向：**回 S3 改决策**，或**回 S4 换执行**。不允许在 S6 改判定规则让报告变绿（那是改裁判，不是改切片）。

## 7. 建设顺序（先手工跑通，再上管道）

| 阶段 | 内容 | 验收断言 |
| --- | --- | --- |
| **P0 口径冻结** | 用本方案 §4.2/§4.3/§5 对 3 份文档（1 法规 + 1 纲要 + 1 表格密集件）手工跑一遍：选型→导入→取片→判定→填记录行 | 3 份都产出 `JudgeReport`；硬切数与 `check_hardcut.sh` 逐份一致；偏移不一致的份数被如实记为"不可判定" |
| **P1 AEC 侧批处理** | 特征打标、`chunk list` → `RawChunk`（含定位器）、调 Core 判定、生成判定记录行 | 同一批重复执行结果一致（可复现）；定位失败率有统计，不隐藏 |
| **P2 Bench 接外部切片** | `load_chunks_jsonl` + `evaluate_chunks` + `--chunks` | 对同一批块，"外部切片"与"内置同参数策略"的边界/单元指标可对照；`documentCategory` 缺失时报告标红 |
| **P3 接回大循环** | 小循环通过后才允许导入；大循环仍保留 27 问 | 一批被小循环拦下时，百炼库内不出现该文档 |

前置依赖必须写清楚：`0.10.0` **尚未落契**（CCJ-018..021 未生效）、`docs/20` 的 P1–P6 仍在施工、无 HTTP 宿主。因此 **P1 首版走"库内引用 Core"，不要假设 `/judge` 可用**。

## 8. 缺口清单（带证据）

| 编号 | 缺口 | 证据 |
| --- | --- | --- |
| G1 | 无 HTTP 宿主，`/judge`、`/templates/resolve` 端点不可用 | `docs/14` K-28、`docs/18` §"没有 HTTP 宿主" |
| G2 | 百炼 `chunk list` 不返回字符偏移 | `state/chunks_final/law_national.json` 的 metadata 只有 `_chunk_id`/`content` |
| G3 | 两套 metadata 五字段不同，`pageInfo` 无来源、`createTime` 语义错位 | §4.2 表；`state/doc_list_law_final.json` |
| G4 | Bench 不能评测外部切片 | `chinese_chunk/cli.py` 仅 `build_chunker` 路径 |
| G5 | Bench 无 `documentCategory`/`benchGroup`，44 条问答只能做全局基线 | `docs/12` §5、Bench HANDOFF §3 |
| G6 | 14 类未冻结，7 类无 starter | `docs/12` §6 |
| G7 | 文档特征产出方（C-01/C-02）未实现 | `docs/05` 草案、`docs/02` 台账 |
| G8 | 判定单非机器可读，缺 6 项模板字段 | §5；判定单原文无 `templateId/decision/fallback` |
| G9 | CCJ-015 与 `check_hardcut.sh` 口径未逐字比对 | `docs/02` §3.7 校准项；`docs/20` P2 已收窄页码容错 |
| G10 | 仓库内无"百炼 AEG"接入代码或文档，只有 `bl` CLI + `create_v2` + `aec-rag` skill | `D:\AEC.Chat\AEC.Chat` 无 `.csproj`（`.cs` 仅见于打包 Python 运行时的 tcl 资源），即无承载应用的 .NET 工程；`Law/_src/bailian` 为脚本层 |

## 9. 未决点（需业主拍板）

1. **小循环是否含 Bench 抽样**：每批都跑（成本高、样本薄），还是每周或每 3 批跑一次（默认建议后者）。
2. **首版集成形态**：库内引用 Core（快、但 AEC.Chat 需 .NET 工程承载），还是先补 HTTP 宿主（干净、但要立 K-28 立项）。
3. **偏移定位失败的口径**：允许出"非契约诊断报告"（建议），还是该文档直接判"不可入小循环"。
4. **law 是否需要 `dense_table` 分支**：判定单未写，加上会改变 1500 的适用范围。
5. **判定单与模板谁是母本**：判定单继续当 SOP（建议），模板只承接决策段；还是把判定单改造成模板的渲染视图。

## 10. 一句话

**AEC.Chat 出送切物与真实切片，ChineseChunkJudge 出决策与判定，Bench 出质量证据；三者用 JSON 产物对接，不用服务耦合。** 小循环能成立的前提不是"三家都连上了"，而是 §4.3 的偏移规则与 §4.2 的元数据映射先钉死——这两处一旦含糊，判定报告就是假的。
