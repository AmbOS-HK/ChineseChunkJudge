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

## 3. 环境与构建

- .NET SDK 10.0.x，目标框架 `net10.0`
- 编码统一 UTF-8；换行入库为 LF（见 `.gitattributes` 与 `.editorconfig`）

```powershell
cd impl/csharp
dotnet build      # 期望 0 警告 0 错误
dotnet test       # 当前 70 项，全通过
```

CI 在 `push` 与 `pull_request` 到 `main` 时执行同样两条命令，见 `.github/workflows/ci.yml`。本地绿而 CI 红，视为未完成。

## 4. 提交信息

沿用仓库既有风格：`<type>(<scope>): <中文描述>`。`type` 取 `feat` / `fix` / `docs` / `test` / `refactor` / `chore`；`scope` 取 `contract` / `impl` / `docs` 等。

既有示例：

- `feat(contract): 0.9.0 CCJ-017 标识与序号一致性`
- `docs: 补类别到默认模板与 Bench 分组映射`

术语红线：提交信息、代码注释与对外文案必须按 `docs/04-对外文案口径.md` §3 执行，其中列出的错误用法一律不得出现（判定报告须写作 `JudgeReport`；分流只作路由维度，策略名须为 `{type}@{targetSize}`；单块上限是硬上限加告警，不是可豁免）。

## 5. 提 PR 前自查

- [ ] 改动先落在 `contract/`，实现与契约一致
- [ ] 版本号三处同值，且只递增了一个段
- [ ] `dotnet build` 0 警告 0 错误，`dotnet test` 全通过
- [ ] 新增规则只增不删；基础规则不可删除、不可降级
- [ ] 未触碰 `docs/04` §3 的术语红线
- [ ] 未把 `bin/`、`obj/`、`TestResults/`、`*.trx` 等构建产物带进提交

## 6. 不做什么

- 不内置文档解析：PDF / Word / HTML 解析外置，输入只有文本与元信息。
- 不内置切分算法：`IChunkExecutor` 是插件接入点，`samples/` 不含生产算法。
- 演示插件桩在仓库之外（`D:\ChineseChunkJudge.Plugins\StructureExecutorStub`），不属于本仓库交付物。
