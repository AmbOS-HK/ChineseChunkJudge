# 贡献指南

本仓库是 ChineseChunkJudge（中文切片判定框架）的契约与参考实现仓库。改动前请先读 `docs/00-定位与边界.md` 与 `contract/rules.md`，本仓库能做什么、不能做什么由这两份文件确定。

## 1. 契约先行（唯一允许的改动顺序）

`contract/` 是唯一权威标准，`impl/` 只是它的参考实现。顺序固定，不可颠倒：

1. 改 `contract/rules.md`；
2. 改 `contract/schema/*.json`；
3. 改 `contract/openapi.yaml`；
4. 同步 `impl/` 参考实现；
5. 补对应的单元测试；
6. 更新 `docs/02-契约演进.md` 中该变更条目的状态。

依据：`contract/rules.md` §12。禁止实现侧单方面修改核心字段；发现契约与实际不符时，先改契约再改实现。

## 2. 版本号

格式固定为 `MAJOR.MINOR.PATCH`，三处必须同值：`contract/rules.md` 表头、`contract/openapi.yaml` 的 `info.version`、C# 的 `ContractConstants.ContractVersion`。当前三处均为 `0.9.0`（2026-11-14 核对）。

一次变更只递增一个段；同一批次同时含 minor 与 patch 性质时取较高位。0.x 阶段同样适用。判定表见 `contract/rules.md` §12.1。

自动校验：

```powershell
python scripts/check_contract_version.py
```

退出码 `0` 表示三处同值且格式合法；`1` 表示任一缺失、格式非法（含前导零）或三处不等，并在输出里指出是哪一处。CI 的 `contract-version` 作业在每次 push / PR 到 `main` 时执行同一条命令，提 PR 前请先在本地跑通。

改契约还必须通过升版守门（`contract/` 有改动却没升版会被拦）：

```powershell
python scripts/check_contract_bump.py --base origin/main   # PR 口径
python scripts/check_contract_bump.py --base HEAD~1        # 本地提交后自查
```

判定链：`contract/` 无改动 → 通过；有改动但版本未变 → 失败；只允许三种版本位（MAJOR 且低位归零 / MINOR 且 PATCH 归零 / PATCH +1）；新增基础规则编号至少 MINOR；删除基础规则编号必须 MAJOR。逻辑用例可离线跑：`python scripts/check_contract_bump.py --self-test`。

## 3. 环境与构建

- .NET SDK 10.0.x，目标框架 `net10.0`
- 编码统一 UTF-8；换行入库为 LF（见 `.gitattributes` 与 `.editorconfig`）

```powershell
cd impl/csharp
dotnet build      # 期望 0 警告 0 错误
dotnet test       # 当前 101 项，全通过
```

契约结构检查（Schema 可解析、`$ref` 无悬空、模板与产物样本合规）：

```powershell
python scripts/check_contract_integrity.py   # 需 PyYAML：pip install pyyaml
```

CI 在 `push` 与 `pull_request` 到 `main` 时执行上述检查与 `dotnet build`、`dotnet test`，见 `.github/workflows/ci.yml`。本地绿而 CI 红，视为未完成。

## 4. 提交信息

沿用仓库既有风格：`<type>(<scope>): <中文描述>`。`type` 取 `feat` / `fix` / `docs` / `test` / `refactor` / `chore`；`scope` 取 `contract` / `impl` / `docs` 等。

既有示例：

- `feat(contract): 0.10.0 CCJ-018..021 策略一致性 / 阈值告警 / 元信息等值 / 父块结构
- `docs: 补类别到默认模板与 Bench 分组映射`

术语红线：提交信息、代码注释与对外文案必须按 `docs/04-对外文案口径.md` §3 执行，其中列出的错误用法一律不得出现（判定报告须写作 `JudgeReport`；分流只作路由维度，策略名须为 `{type}@{targetSize}`；单块上限是硬上限加告警，不是可豁免）。

## 5. 提 PR 前自查

- [ ] 改动先落在 `contract/`，实现与契约一致
- [ ] 版本号三处同值，且只递增了一个段（`check_contract_bump.py` 通过）
- [ ] `check_contract_integrity.py` 通过（Schema / `$ref` / 模板与产物样本）
- [ ] `dotnet build` 0 警告 0 错误，`dotnet test` 全通过
- [ ] 新增规则只增不删；基础规则不可删除、不可降级
- [ ] 未触碰 `docs/04` §3 的术语红线
- [ ] 未把 `bin/`、`obj/`、`TestResults/`、`*.trx` 等构建产物带进提交

## 6. 不做什么

- 不内置文档解析：PDF / Word / HTML 解析外置，输入只有文本与元信息。
- 不内置切分算法：`IChunkExecutor` 是插件接入点，`samples/` 不含生产算法。
- 演示插件桩在仓库之外（`D:\ChineseChunkJudge.Plugins\StructureExecutorStub`），不属于本仓库交付物。
