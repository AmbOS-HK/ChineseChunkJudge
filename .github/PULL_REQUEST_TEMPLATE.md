## 关联

- 契约变更条目：`docs/02-契约演进.md` 第 ___ 条
- 版本位：`MAJOR` / `MINOR` / `PATCH`（只递增一个段）
- 影响的基础规则编号：CCJ-___

## 改动类型

- [ ] `contract`（`rules.md` / `schema/*.json` / `openapi.yaml`）
- [ ] `impl`（C# 参考实现）
- [ ] `docs`
- [ ] 构建 / CI

## 契约一致性

- [ ] 先改 `contract/`，再同步 `impl/`
- [ ] 版本号三处同值：`rules.md` 表头、`openapi.yaml` 的 `info.version`、`ContractConstants.ContractVersion`
- [ ] `python scripts/check_contract_version.py` 本地通过（退出码 0）
- [ ] 基础规则只增不删、不降级；未删除或降级既有校验
- [ ] 未触碰 `docs/04-对外文案口径.md` §3 的术语红线

## 验证

- [ ] `dotnet build`：0 警告 0 错误
- [ ] `dotnet test`：全通过

```
（粘贴本机测试结果尾部，例如：已通过! - 失败: 0，通过: 70，已跳过: 0，总计: 70）
```

## 说明

（改动动机、影响范围、是否涉及弃用字段与清理版本）
