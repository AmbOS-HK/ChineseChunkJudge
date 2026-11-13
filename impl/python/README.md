# impl/python（占位）

**状态**：占位目录，尚无实现代码，未纳入任何构建或测试流程。

**定位**：规划中的 Python 参考实现——便于与 ChunkJudgeBench 直接联动（Python 生态的切分器较多）。

## 落地时必须遵守

- `contract/` 是唯一权威标准：不得增删契约核心字段，不得删除或降级基础校验规则。
- 输入输出须符合 `contract/schema/*.json`（JSON Schema 2020-12）与 `contract/openapi.yaml`（OpenAPI 3.1.0）。
- 变更顺序固定：先改 `contract/`，再同步本实现，并补对应单元测试。
- 职责边界与 C# 参考实现一致：只做归一化、切片质检、父子层级组装、`JudgeReport` 输出，不含文档解析与切片算法（执行器由外部插件注入）。
- `startOffset` / `endOffset` 的下标口径须在本实现文档中显式声明；C# 参考实现使用 UTF-16 code unit 下标，Python 的字符串下标是 code point，两者不等价，必须写明换算或声明口径差异。

## 当前内容

- `README.md`（本文件）

仅有本占位说明，无源码、依赖声明或测试。
