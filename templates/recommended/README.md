# 建议模板（Starter Templates）

> **非规范性建议，未经目标语料评测。** 采用与否由部署方决定，本仓库不背书效果。

## 定位

- 本目录**不属于** `contract/`，不构成契约的一部分；模板语法仍以 `contract/schema/chunk-rule-template.json` 为准。
- **不得自动加载**：任何宿主、`TemplateResolver`、Demo 都不得默认读取本目录，必须由部署方显式指定模板文件。
- 模板内容由部署方维护；本目录只提供起步基线，可整体复制、可任意修改。
- 数值均为**未标定基线**，`parentMaxChars` 等参数不得引用本目录数值作为标准值。

## 当前清单（6 份，均面向 `rag_retrieval`）

> 14 类的默认模板、缺口与 Bench 分组见 `docs/12-类别到默认模板与Bench分组映射-2026-11-14.md`；本目录只提供 6 类 starter，不是全量映射。

| 文件 | 类别 | 结构假设 | 兜底 |
| --- | --- | --- | --- |
| `law_regulation.json` | `law_regulation` | 条章 → （一）分节 → 表格 → 弱结构 | smart 1200 |
| `technical_standard.json` | `technical_standard` | 条章 → （一）分节 → 弱结构 | smart 1200 |
| `government_plan.json` | `government_plan` | （一）分节 / 篇章 → 表格 → 弱结构 | smart 1200 |
| `engineering_project_report.json` | `engineering_project_document` | 表格优先 → 分节 → 弱结构 | smart 1200 |
| `judicial_document.json` | `litigation_document` | 表格 → 弱结构（无条章正则） | smart 1200 |
| `manual.json` | `manual` | 分节 → 表格 → 弱结构 | smart 1000 |

说明：文件名与 `templateId` 保留旧名（如 `government_plan.json`），仅用于稳定引用；`appliesTo.documentCategories` 已按 2026-11-14 新枚举迁移。`traits` 封闭枚举目前没有「篇/章」特征，`government_plan.json` 用 `section_numbering` 近似表达分节结构，篇章层级由执行器在特征产出时映射；这是已知近似，不是规范结论。

## 使用方式

1. 复制需要的模板到部署侧，改 `templateId`、`name`、`appliesTo` 与参数；
2. 校验结构：按 `contract/schema/chunk-rule-template.json` 验证；
3. 经 Bench 评测后再调整 `chunkSize`、`parentChild` 等数值，并把结论回填到部署侧模板版本，不回写本目录。

## AEC 判定单首稿（草案）

同目录另有两份首稿，放在这里是为了走现有的 `templates/recommended/*.json` 结构校验。它们不是上表 6 份 `*-starter` 的改写，也不是冻结的生产默认。`version` 为 `0.1.0`；`templateId` 里的 `v1` 只表示对应《切片判定单（V1.0）》。宿主、`TemplateResolver` 与 Demo 不得自动加载。选型 → 百炼实切 → 回拉切片 → 判定这条小循环尚未在本仓实施。

分隔符按判定单原文抄录，正则规则不带 `^` 多行锚（`forbidLineStartAnchor: true`）。`smart` 表示判定单里的「智能切分、不传 chunk-mode」。未命中任何规则时走该模板的 `fallback`，不允许空决策。`pdf_no_structure` 没有单独规则，因此落到 fallback；页模式 PDF 不作为契约判定路径，两份模板都没有 `chunkMode=page`。AEC 检索字段 `region`、`admin_level`、`year`、`plan_period` 不进入契约 `document.metadata`。

| 文件 | templateId | 决策（先匹配先生效） | 兜底 |
| --- | --- | --- | --- |
| `aec-law-v1.json` | `aec-law-v1` | 条章正则 1200 → `（一）` 正则 1200 → 弱结构 smart 1200。无 `dense_table` 分支 | smart 1200 |
| `aec-plans-v1.json` | `aec-plans-v1` | 密集表 smart 1500 → 条章正则 1200 → `（一）` 正则 1200 → 弱结构 smart 1000 | smart 1000 |

条章正则：`(?=第[一二三四五六七八九十百零〇0-9]+[条章])`。`（一）` 正则：`(?=（[一二三四五六七八九十]+）)`。规划文本的条章分支复用这条条章正则；`（一）` 分节仍用分节正则，与 `docs/21` §5 的字段映射一致。法规模板不设密集表分支。

## 明确不做的事

- 不提供 `rag_generation` / `model_training` 模板（相关维度尚未冻结）；
- 不提供全部 14 类（低权重类别留空，走强制 smart fallback 即合法；`tabular_record` / `qa_pair` 暂无 starter）；
- 不声明「最优参数」，不参与策略优劣判定。
