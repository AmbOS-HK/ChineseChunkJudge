#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""契约结构完整性校验（CI 调用）。

覆盖四类机器可查的契约缺陷：
  1. Schema 可解析、顶层键齐备（$schema / $id / title / type）
  2. 所有 $ref（同文件 #/...、跨文件 ./x.json#/...）可解析，无悬空指针
  3. openapi.yaml 可解析、每个操作有 operationId、响应 $ref 可解析
  4. 用 Schema 校验两类消费物：templates/recommended/*.json 与 scripts/fixtures/*.sample.json

不覆盖（需人工或专业校验器）：JSON Schema 的 if-then / oneOf / format 语义、
字段语义是否与 rules.md 一致（见 docs/14 的字段双向比对方法）。

用法：
    python scripts/check_contract_integrity.py
    python scripts/check_contract_integrity.py --root <仓库根目录>

退出码：
    0  全部通过
    1  存在结构错误（含 $ref 悬空、Schema 校验失败、缺少 PyYAML）
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

TOP_REQUIRED = ["$schema", "$id", "title", "type"]

FIXTURE_SCHEMA = {
    "contract-chunk.sample.json": "contract-chunk.json",
    "judge-report.sample.json": "judge-report.json",
    "parent-child-package.sample.json": "parent-child-package.json",
}


class Contract:
    """加载并缓存 contract/ 下的 Schema 与 openapi。"""

    def __init__(self, root: Path) -> None:
        self.root = root
        self.contract = root / "contract"
        self.schema_dir = self.contract / "schema"
        self.errors: list[str] = []
        self.notes: list[str] = []
        self.schemas: dict[str, dict] = {}
        self.openapi: dict | None = None

    # ---------- 加载 ----------

    def load(self) -> None:
        for p in sorted(self.schema_dir.glob("*.json")):
            try:
                self.schemas[p.name] = json.loads(p.read_text(encoding="utf-8"))
            except Exception as exc:  # noqa: BLE001
                self.errors.append(f"[解析失败] schema/{p.name}: {exc}")
        if not self.schemas:
            self.errors.append("[缺失] contract/schema 下没有可解析的 JSON Schema")

        for name, doc in self.schemas.items():
            for key in TOP_REQUIRED:
                if key not in doc:
                    self.errors.append(f"[顶层键缺失] schema/{name}: 缺 {key}")

        try:
            import yaml  # noqa: PLC0415
        except ImportError:
            self.errors.append("[依赖缺失] 需要 PyYAML：pip install pyyaml（CI 已装）")
            return
        try:
            self.openapi = yaml.safe_load((self.contract / "openapi.yaml").read_text(encoding="utf-8"))
        except Exception as exc:  # noqa: BLE001
            self.errors.append(f"[解析失败] openapi.yaml: {exc}")

    # ---------- JSON Pointer / $ref ----------

    def pointer(self, doc, ptr: str):
        cur = doc
        for part in [x for x in ptr.lstrip("/").split("/") if x]:
            part = part.replace("~1", "/").replace("~0", "~")
            if isinstance(cur, list):
                try:
                    cur = cur[int(part)]
                except (ValueError, IndexError):
                    return None
            elif isinstance(cur, dict):
                if part not in cur:
                    return None
                cur = cur[part]
            else:
                return None
        return cur

    def resolve_ref(self, ref: str, base_file: Path):
        if ref.startswith("#"):
            doc = self.schemas.get(base_file.name) or self.openapi
            return self.pointer(doc, ref[1:])
        file_part, _, frag = ref.partition("#")
        target = (base_file.parent / file_part).resolve()
        if not target.is_file():
            return None
        doc = self.schemas.get(target.name)
        if doc is None:
            try:
                doc = json.loads(target.read_text(encoding="utf-8"))
            except Exception:  # noqa: BLE001
                return None
        return self.pointer(doc, frag) if frag else doc

    def walk_refs(self, node, base_file: Path, trail: str) -> None:
        if isinstance(node, dict):
            ref = node.get("$ref")
            if isinstance(ref, str):
                if self.resolve_ref(ref, base_file) is None:
                    self.errors.append(f"[$ref 不可解析] {trail}: {ref}")
            for k, v in node.items():
                self.walk_refs(v, base_file, f"{trail}/{k}")
        elif isinstance(node, list):
            for i, v in enumerate(node):
                self.walk_refs(v, base_file, f"{trail}[{i}]")

    def check_refs(self) -> None:
        for name, doc in self.schemas.items():
            self.walk_refs(doc, self.schema_dir / name, f"schema/{name}")
        if self.openapi:
            self.walk_refs(self.openapi, self.contract / "openapi.yaml", "openapi")

    # ---------- openapi ----------

    def check_openapi(self) -> None:
        if not self.openapi:
            return
        paths = self.openapi.get("paths") or {}
        ops = 0
        for path, item in paths.items():
            if not isinstance(item, dict):
                self.errors.append(f"[paths 结构非法] {path}")
                continue
            for method, op in item.items():
                if method not in ("get", "post", "put", "patch", "delete"):
                    continue
                ops += 1
                if not op.get("operationId"):
                    self.errors.append(f"[缺少 operationId] {method.upper()} {path}")
                if not op.get("responses"):
                    self.errors.append(f"[缺少 responses] {method.upper()} {path}")
        self.notes.append(f"openapi.yaml: {len(paths)} 条路径 / {ops} 个操作")

    # ---------- 最小 Schema 校验 ----------

    def validate(self, inst, sch, base_file: Path, path: str = "") -> list[str]:
        errs: list[str] = []
        sch = self._deref(sch, base_file)
        if not isinstance(sch, dict):
            return errs

        declared = sch.get("type")
        if declared:
            types = declared if isinstance(declared, list) else [declared]
            if inst is None and "null" in types:
                return errs
            ok = False
            for t in types:
                ok = ok or (t == "integer" and isinstance(inst, int) and not isinstance(inst, bool))
                ok = ok or (t == "number" and isinstance(inst, (int, float)) and not isinstance(inst, bool))
                ok = ok or (t == "string" and isinstance(inst, str))
                ok = ok or (t == "boolean" and isinstance(inst, bool))
                ok = ok or (t == "object" and isinstance(inst, dict))
                ok = ok or (t == "array" and isinstance(inst, list))
            if not ok:
                errs.append(f"{path}: 期望 {declared}，实为 {type(inst).__name__}")
                return errs

        if "const" in sch and inst != sch["const"]:
            errs.append(f"{path}: 应为 const {sch['const']!r}，实为 {inst!r}")
        if "enum" in sch and inst not in sch["enum"]:
            errs.append(f"{path}: {inst!r} 不在枚举 {sch['enum']}")

        if isinstance(inst, str):
            if "minLength" in sch and len(inst) < sch["minLength"]:
                errs.append(f"{path}: 长度 {len(inst)} < minLength {sch['minLength']}")
            if "pattern" in sch and not re.search(sch["pattern"], inst):
                errs.append(f"{path}: {inst!r} 不匹配 pattern {sch['pattern']}")

        if isinstance(inst, (int, float)) and not isinstance(inst, bool):
            if "minimum" in sch and inst < sch["minimum"]:
                errs.append(f"{path}: {inst} < minimum {sch['minimum']}")
            if "maximum" in sch and inst > sch["maximum"]:
                errs.append(f"{path}: {inst} > maximum {sch['maximum']}")

        if isinstance(inst, list):
            if "minItems" in sch and len(inst) < sch["minItems"]:
                errs.append(f"{path}: 长度 {len(inst)} < minItems {sch['minItems']}")
            if sch.get("uniqueItems"):
                keys = {json.dumps(x, sort_keys=True, ensure_ascii=False) for x in inst}
                if len(keys) != len(inst):
                    errs.append(f"{path}: 存在重复项（uniqueItems）")
            if isinstance(sch.get("items"), dict):
                for i, v in enumerate(inst):
                    errs.extend(self.validate(v, sch["items"], base_file, f"{path}[{i}]"))

        if isinstance(inst, dict):
            for key in sch.get("required", []):
                if key not in inst:
                    errs.append(f"{path}: 缺少必填字段 {key}")
            props = sch.get("properties", {})
            extra_allowed = sch.get("additionalProperties", True)
            for k, v in inst.items():
                if k in props:
                    errs.extend(self.validate(v, props[k], base_file, f"{path}.{k}"))
                elif extra_allowed is False:
                    errs.append(f"{path}: 未声明字段 {k}（additionalProperties=false）")
        return errs

    def _deref(self, node, base_file: Path):
        depth = 0
        while isinstance(node, dict) and "$ref" in node and depth < 32:
            ref = node["$ref"]
            file_part = ref.partition("#")[0]
            target_file = base_file if ref.startswith("#") else (base_file.parent / file_part).resolve()
            node = self.resolve_ref(ref, base_file)
            base_file = target_file if target_file.is_file() else base_file
            depth += 1
        return node

    # ---------- 消费物校验 ----------

    def check_templates(self) -> None:
        tdir = self.root / "templates" / "recommended"
        schema = self.schemas.get("chunk-rule-template.json")
        if schema is None or not tdir.is_dir():
            self.notes.append("templates/recommended: 跳过（Schema 或目录缺失）")
            return
        count = 0
        for p in sorted(tdir.glob("*.json")):
            count += 1
            try:
                inst = json.loads(p.read_text(encoding="utf-8"))
            except Exception as exc:  # noqa: BLE001
                self.errors.append(f"[模板解析失败] {p.name}: {exc}")
                continue
            for e in self.validate(inst, schema, self.schema_dir / "chunk-rule-template.json", p.name):
                self.errors.append(f"[模板不合规] {e}")
        self.notes.append(f"templates/recommended: {count} 份按 Schema 校验")

    def check_fixtures(self) -> None:
        fdir = self.root / "scripts" / "fixtures"
        if not fdir.is_dir():
            self.notes.append("scripts/fixtures: 目录不存在，跳过")
            return
        count = 0
        for p in sorted(fdir.glob("*.sample.json")):
            schema_name = FIXTURE_SCHEMA.get(p.name)
            if schema_name is None or schema_name not in self.schemas:
                self.errors.append(f"[未登记样本] {p.name}: 需在 FIXTURE_SCHEMA 中登记对应 Schema")
                continue
            count += 1
            inst = json.loads(p.read_text(encoding="utf-8"))
            base = self.schema_dir / schema_name
            for e in self.validate(inst, self.schemas[schema_name], base, p.name):
                self.errors.append(f"[产物样本不合规] {e}")
        self.notes.append(f"scripts/fixtures: {count} 份产物样本按 Schema 校验")


def main() -> int:
    ap = argparse.ArgumentParser(description="契约结构完整性校验")
    ap.add_argument("--root", default=None, help="仓库根目录，默认按脚本位置推断")
    args = ap.parse_args()
    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parents[1]

    c = Contract(root)
    c.load()
    c.check_refs()
    c.check_openapi()
    c.check_templates()
    c.check_fixtures()

    print(f"仓库根目录：{root}")
    for n in c.notes:
        print(f"  - {n}")

    if c.errors:
        print("")
        print(f"FAIL：契约结构校验未通过（{len(c.errors)} 项）")
        for e in c.errors:
            print(f"  - {e}")
        print("")
        print("修正依据：contract/rules.md §12（契约先行）、docs/14（契约全量完整性审查）")
        return 1

    print("")
    print(f"PASS：契约结构完整（Schema {len(c.schemas)} 个、$ref 全部可解析、消费物全部合规）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
