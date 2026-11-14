# HANDOFF：ChineseChunkJudge 交接给 Grok Bot

> 交接日期：2026-11-14
> 仓库：`https://github.com/AmbOS-HK/ChineseChunkJudge.git`
> 分支：`main`
> 本文是 Grok Bot 的入口说明；`contract/` 是唯一权威，`docs/` 是规划与审查记录，不具契约效力。简明版入口见 `docs/24-交接说明-2026-11-14.md`；两者冲突时，以 `contract/` 与本文的“当前事实”为准。

## 0. 交接前必做：先确认 GitHub 上是当前状态

截至交接准备时，本地 `main` **领先 `origin/main` 多个提交**，工作树干净。Grok Bot 从 GitHub 拉取，若不先推送，它拿到的仍是 0.10.0 之前的版本。**提交数量以实际 `git log origin/main..HEAD` 为准，不要照抄本文数字。**

交接前由业主执行：

```powershell
$g = "C:\Program Files\Git\cmd\git.exe"
& $g -C "D:\ChineseChunkJudge" status --porcelain          # 应为空
& $g -C "D:\ChineseChunkJudge" log --oneline origin/main..HEAD
& $g -C "D:\ChineseChunkJudge" push origin main
& $g -C "D:\ChineseChunkJudge" log --oneline origin/main..HEAD   # 应为空
```

未推送提交的数量与 SHA **不在本文写死**（每提交一次就变）。以命令结果为准：

```powershell
git -C "D:\ChineseChunkJudge" rev-list --oneline origin/main..HEAD
```

> 口径提示：`docs/23` §6 末行原记“工作树仍未 git commit”，该文件已自行更正为“后续已提交为 `42dca21`，现状以 `git status` 为准”。本文不再重复该警告。

## 1. 项目定位

`ChineseChunkJudge` 不是新的 chunk 算法库，而是面向中文专业文档的：

- **Spec 侧选型判定**：给定文档类别与结构特征，选择切片规则模板；
- **Judge 侧质检判定**：校验切片结果是否符合契约，输出 `JudgeReport`；
- **双库 Parent/Child 产物契约**：父块补上下文，默认关闭。

不负责：文档解析、NLP 推断、内置切片算法、HTTP 宿主、向量化、评测。评测在独立仓 `D:\ChunkJudgeBench`（GitHub：`AmbOS-HK/ChunkJudgeBench`）。

## 2. 当前事实（以仓库为准）

| 项 | 状态 |
| --- | --- |
| 契约版本 | **0.10.0**，三处同值：`contract/rules.md` 表头、`contract/openapi.yaml` `info.version`、C# `ContractConstants.ContractVersion` |
| 基础规则 | **CCJ-001..CCJ-021**，默认注册 **21 条** |
| C# 参考实现 | `impl/csharp`，`net10.0`；`dotnet test` **101/101**，build 0 警告 0 错误 |
| 契约门禁 | `check_contract_version.py` PASS；`check_contract_integrity.py` PASS；`check_contract_bump.py --self-test` 9/9 |
| CI | `.github/workflows/ci.yml`：版本 / 结构 / 升版守门 + build + test |
| 模板 | `templates/recommended` 6 份 starter，均 `0.1.0`、未标定、不得自动加载 |
| 类别枚举 | **14 类模板族草案，未冻结**；见 `docs/05` §2、`docs/11`、`docs/12` |
| 远端 | `origin = https://github.com/AmbOS-HK/ChineseChunkJudge.git` |

复现命令：

```powershell
$dotnet = "C:\Users\Simon Lau\AppData\Local\Microsoft\dotnet\dotnet.exe"
& $dotnet test "D:\ChineseChunkJudge\impl\csharp\ChineseChunkJudge.sln"

python D:\ChineseChunkJudge\scripts\check_contract_version.py
python D:\ChineseChunkJudge\scripts\check_contract_integrity.py
python D:\ChineseChunkJudge\scripts\check_contract_bump.py --self-test
```

## 3. 已定事项：不要重新讨论

以下已经拍板，Grok Bot 不应擅自改方向：

1. **契约先行**：先改 `contract/`，再同步 `impl/`；参考实现不得私增核心字段、不得删除/降级基础规则。
2. **Spec + Judge 双判定**：选型判定与质检判定分开；`/templates/resolve` 只选模板，不解析文档。
3. **C# 是首个参考实现**，不是唯一实现；契约语言无关。
4. **Bench 独立仓**：评测、阈值标定、模板 A/B 不并入本仓。
5. **双库默认关闭**：父块不重叠、引用不成环；`parentChild.enabled=false` 时带父块/配对 → 400。
6. **不内置解析与算法**：`IChunkExecutor` 只是外部插件接入点；仓库不交付开源切分器。
7. **0.x 只增不删**：新增字段/规则/端点走 `MINOR`；删除字段、字段转必填、改既有语义走 `MAJOR`；`parentChunkId` 清理锁定 1.0.0。
8. **14 类 `documentCategory` 是“模板族 / 主导检索单元”**，不是文书体裁分类；`judicial_document` 已改名 `litigation_document`。
9. **类别尚未冻结**：冻结门槛是每类 ≥30 份标注文档、双人 Cohen's kappa ≥0.70、按类模板 A/B 不劣于 smart fallback（见 `docs/11`）。
10. **类别枚举未进 `contract/`**：`openapi.yaml` 的 `documentCategories` 仍是普通 `string` 数组；C# 也无 `DocumentCategories` 常量。冻结后才落契约。

## 4. 当前未闭合事项

### 4.1 类别与模板

- `docs/05` §2：14 类模板族草案；`docs/11`：结构效度审核与实证计划；`docs/12`：类别→默认模板 / Bench 分组映射。
- 已有 starter 6 类：`law_regulation`、`technical_standard`、`government_plan`、`engineering_project_document`、`litigation_document`、`manual`。
- 待建 starter 7 类：`government_report`、`internal_policy`、`contract`、`appraisal_report`、`official_document`、`tabular_record`、`qa_pair`。
- `other` 仅 fallback，不建 starter。
- 文件名/templateId 旧名：`engineering_project_report.json`、`judicial_document.json`；`appliesTo` 已用新类别值。
- 企业非项目工作报告暂无主类，落 `other`；是否新增 `enterprise_report` 待语料占比。

### 4.2 Bench（独立仓，尚未接入）

- 当前 `D:\ChunkJudgeBench` 没有 `documentCategory` / `benchGroup` 字段；现有 44 条问答只能做全局基线。
- `docs/12` 定义：`benchGroup` = 类别；`benchFamily` = `article` / `section` / `case` / `short` / `step` / `table` / `qa` / `fallback`；不跨 family 平均。
- 需改：`data/corpus_manifest.json`、`data/qa.jsonl`、`build_qa.py`、`evaluate.py`、`report.py`、`selfcheck.py`。
- 最小样本：每类 ≥30 文档、≥60 条 `gold_quote` 问答。

### 4.3 契约遗留

- **K-15 未做**：`chunkMode` / `unitKind` / `severity` 抽 `$defs`、`RuleDescriptor` 与 `appliedRule` 合并；纯结构收敛，留后续 PATCH。
- **K-28 开放**：无 HTTP 宿主，400/404/413 与资源边界无法端到端验证；宿主落地后另起 MINOR。
- **K-02 锁定 1.0.0**：`parentChunkId` 删除、`structuralParentChunkId` 转必填。
- **C-01 / C-02 / C-03**：类别枚举、特征产出方边界与 400 口径、`payload` 未排期；C-01 待 14 类实证。
- `usagePurpose` 三值、`DocumentFeature` 是否承载特征、父块是否带 `headingPath`、百炼 `chunk list` 是否返回 offset：均未定。

## 5. Grok Bot 的下一步任务（按顺序）

### 任务 A：先把交接基线跑通

1. 从 GitHub 克隆，确认 `HEAD` 含 0.10.0 与 docs/23。
2. 跑 `dotnet test`（101 项）与三条门禁。
3. 不要改 `contract/`；先读 `README.md`、`docs/00`、`docs/05`、`docs/11`、`docs/12`、`docs/14`、`docs/15`、`docs/17`、`docs/23`。

**验收**：101/101 通过、三条门禁 PASS、`git status` 空。

### 任务 B：补两份 P1 starter

先做 `government_report` 与 `appraisal_report`；当前完全空白，且是 P1 展示位 / 结论段核心。

**要求**：

- 新建 `templates/recommended/government_report.json`、`appraisal_report.json`；
- 遵循 `contract/schema/chunk-rule-template.json`；
- `version: 0.1.0`、`validation.hardCutZero: true`、`acceptanceRef: "aec-27q"`；
- 不标定数值，不声明“最优参数”；
- `appliesTo.documentCategories` 精确对应新枚举；
- 更新 `templates/recommended/README.md` 与 `docs/12` 状态；
- 用 JSON Schema 校验，不手写“看起来对”。

**验收**：两份模板过正式 Schema；`appliesTo` 在 14 类内；README 与 docs/12 同步；不自动加载。

### 任务 C：再补 `tabular_record` 与 `contract`

这两类切坏后果最直接：表头/数据行被拆、条款义务被切断。

**验收**：同任务 B；另加“表格不拦腰切”“条款边界不跨条”的正反用例。

### 任务 D：Bench 接入分组

在 `D:\ChunkJudgeBench` 完成：

- manifest 增 `documentCategory` / `benchFamily`；
- QA 增 `benchGroup` 或由 `doc` 反查；
- `evaluate.py` 按组汇总；`report.py` 输出类别覆盖表；
- `selfcheck.py` 校验 14 值、`other` 不排名、缺样本标红；
- 不跨 family 求平均。

**验收**：能输出 14 类覆盖表；每类样本数可见；无样本的组明确标“未验证”，不得合成总分。

### 任务 E：类别冻结门禁

- 每类 ≥30 文档、≥60 问答；
- 双人标注 Cohen's kappa ≥0.70；单类 ≥0.60；
- 按类模板 vs smart fallback 的 A/B；至少一项核心指标优于 fallback，且无显著劣化；
- 四个“条”类（`law_regulation` / `technical_standard` / `internal_policy` / `contract`）若 A/B 无差异，考虑合并，不要为凑 14 类硬撑；
- 全部通过后，才把 14 类写入 `contract/rules.md` §13.1、`openapi.yaml` `enum`、`chunk-rule-template.json` `enum`、C# `DocumentCategories`，并按 `MINOR` 升版。

**未通过前**：`docs/05` 保持“草案，未冻结”，`contract/` 不动。

## 6. 硬边界（违反即退回）

1. 不得在类别未冻结时改 `contract/` 的 `documentCategories` 枚举。
2. 不得删除或改写既有基础规则的判定条件；扩规则只能新增编号。
3. 不得把 `templates/recommended` 设为自动加载；必须部署方显式指定。
4. 不得把 14 类说成“已科学证明”；只能说“结构效度通过、实证未完成”。
5. 不得把 Bench 的评测并进本仓；Bench 改动在独立仓。
6. 不得为了“14 类”这个数字保留四个无法区分的“条”类；A/B 无差异就合并。
7. 不得把企业工作报告偷偷塞进 `government_report`；先统计语料，再决定是否新增 `enterprise_report`。
8. 不得修改 `docs/14`（审查快照）；更正另开编号。
9. 不得跳过 `check_contract_bump.py` 直接升版；CI 会拦。
10. 不得把 `docs/23` §6 的“未提交”当现状；以 `git status` 为准。

## 7. 文件优先级

**权威**：

- `contract/rules.md`
- `contract/openapi.yaml`
- `contract/schema/*.json`

**实现**：

- `impl/csharp/src/ChineseChunkJudge.Core/`
- `impl/csharp/tests/ChineseChunkJudge.Core.Tests/`

**类别与模板（当前主线）**：

- `docs/05`：14 类草案
- `docs/11`：结构效度与实证门槛
- `docs/12`：类别→模板 / Bench 映射
- `templates/recommended/`

**历史与台账**：

- `docs/14` 审查快照、`docs/15` 整改台账、`docs/16` 复检、`docs/17` 0.10.0 清单、`docs/22` 实现偏差复检、`docs/23` 落契记录
- `docs/10` 是旧版 12 类审核记录，不是当前类别定义
- `docs/21` 是 AEC.Chat 侧方案，非契约审查线

## 8. 交接确认清单

- [ ] `origin/main` 已包含交接时本地 `HEAD` 及之前全部提交（本次交接至少应包含 `4525aad`）
- [ ] `git status --porcelain` 为空
- [ ] `dotnet test` 101/101
- [ ] 三条门禁 PASS
- [ ] Grok Bot 已读 `README.md`、`docs/05`、`docs/11`、`docs/12`
- [ ] Grok Bot 已确认“14 类未冻结，不得改 contract”
- [ ] Grok Bot 已确认 Bench 是独立仓，不在本仓改评测逻辑

## 9. 仓库级设置与合并权限（2026-11-14 实测）

以下设置存在于 GitHub 仓库设置，**不在 Git 跟踪范围内**，克隆拿不到，故在此登记。取值均为 API 回读值，不是推测：

| 项 | 实际值 |
| --- | --- |
| 仓库描述 | 面向中文专业文档的切片质检与判定契约框架，插件化接入任意底层切分器，不内置切分算法 |
| Topics | `chinese`、`chunking`、`csharp`、`dotnet`、`json-schema`、`nlp`、`openapi`、`rag` |
| Website 与文档链接 | 两者同为 `https://github.com/AmbOS-HK/ChineseChunkJudge/tree/main/docs`。机制：`documentation` 字段跟随 `homepage`，直接 `PATCH` 该字段会被静默忽略 |
| `main` 分支保护 | 要求走 PR；必需状态检查 2 项；`strict=true`（分支须与最新 `main` 同步）；禁强推、禁删分支；`enforce_admins=false` |

已登记为**必需**的检查只有两项：`contract version (3 places must match)`、`build & test (net10.0)`。

对 Grok Bot 的直接影响：

1. 若以**非管理员**身份（独立账号或 GitHub App）推送：`git push origin main` 会被拒，必须开 PR 合入，且 PR 需上述两项检查通过。
2. 若以**仓库管理员**身份：`enforce_admins=false` 使其可绕过上述限制直接推送。
3. `contract-integrity`、`contract-bump` 两个作业**尚未登记为必需检查**：会执行、会红，但不阻断合并。是否登记为必需由业主决定，**Bot 不要自行修改仓库设置**。

## 10. 交接就绪判定（推送是前置条件）

GitHub 上没有的提交，Bot 克隆就看不到。判定“可交接”只看两条命令，不看任何文档里的数字：

```powershell
git -C "D:\ChineseChunkJudge" fetch origin
git -C "D:\ChineseChunkJudge" rev-list --count origin/main..HEAD   # 必须为 0
git -C "D:\ChineseChunkJudge" status --porcelain                   # 必须为空
```

<div align="left">注：曾出现过本机到 `github.com:443` 间歇不可达、而 `api.github.com` 正常的情况，此时 `git push` 会连续超时。这不是仓库问题，恢复后重推即可；不要改用无关通道或改写历史绕过。</div>