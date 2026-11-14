#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""校验契约版本三处同值（contract/rules.md §12.1 的强制约束）。

三处来源：
  1. contract/rules.md 表头   —— `| 契约版本 | X.Y.Z |`
  2. contract/openapi.yaml    —— `info.version`
  3. C# ContractConstants     —— `ContractVersion = "X.Y.Z"`（Models/ChunkContract.cs）

刻意不取的两个值（误取会得出错误的"版本变了"结论）：
  - openapi.yaml 顶层的 `openapi: 3.1.0`：那是 OpenAPI 规范版本，规则明确永不参与递增。
  - openapi.yaml 内的 `contractVersion:`：那是 schema 的数据字段，不是契约版本。
故 openapi 只解析 `info:` 块内部的 `version:`，块外一律不看。

用法：
    python scripts/check_contract_version.py
    python scripts/check_contract_version.py --root <仓库根目录>

退出码：
    0  三处同值，且格式为无前导零的 MAJOR.MINOR.PATCH
    1  任一缺失、格式非法、或三处不等
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

RULES = Path("contract") / "rules.md"
OPENAPI = Path("contract") / "openapi.yaml"
CSHARP_DIR = Path("impl") / "csharp" / "src"

# rules.md 表头行：| 契约版本 | 0.9.0 |
RULES_ROW = re.compile(r"^\|\s*契约版本\s*\|\s*(.*?)\s*\|\s*$")
# 严格三段十进制、无前导零（rules.md §12.1）
SEMVER = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
# C# 常量定义
CS_CONST = re.compile(r'ContractVersion\s*=\s*"([^"]*)"')


def read_rules_version(root: Path) -> tuple[str | None, str]:
    p = root / RULES
    if not p.is_file():
        return None, f"文件不存在：{RULES}"
    for line in p.read_text(encoding="utf-8").splitlines():
        m = RULES_ROW.match(line)
        if m:
            return m.group(1).strip(), f"{RULES} 表头（命中行：{line.strip()}）"
    return None, f"{RULES} 未找到「契约版本」表头行"


def read_openapi_version(root: Path) -> tuple[str | None, str]:
    p = root / OPENAPI
    if not p.is_file():
        return None, f"文件不存在：{OPENAPI}"
    in_info = False
    for line in p.read_text(encoding="utf-8").splitlines():
        if re.match(r"^info\s*:\s*$", line):
            in_info = True
            continue
        if not in_info:
            continue
        # 回到顶层键，说明 info 块结束
        if line and not line[0].isspace():
            break
        m = re.match(r"^\s+version\s*:\s*(\S+)\s*(?:#.*)?$", line)
        if m:
            return m.group(1).strip("'\""), f"{OPENAPI} 的 info.version"
    return None, f"{OPENAPI} 的 info 块内未找到 version"


def read_csharp_version(root: Path) -> tuple[str | None, str]:
    d = root / CSHARP_DIR
    if not d.is_dir():
        return None, f"目录不存在：{CSHARP_DIR}"
    found: list[tuple[Path, str]] = []
    for py in sorted(d.rglob("*.cs")):
        for m in CS_CONST.finditer(py.read_text(encoding="utf-8", errors="replace")):
            found.append((py, m.group(1).strip()))
    if not found:
        return None, f"{CSHARP_DIR} 下未找到 ContractVersion 常量"
    distinct = {v for _, v in found}
    if len(distinct) > 1:
        detail = "；".join(
            f"{p.relative_to(root).as_posix()} = {v}" for p, v in found
        )
        return None, f"ContractVersion 存在多个不同定义（{detail}）"
    p, v = found[0]
    return v, f"{p.relative_to(root).as_posix()} 的 ContractConstants.ContractVersion"


def main() -> int:
    ap = argparse.ArgumentParser(description="校验契约版本三处同值")
    ap.add_argument("--root", default=None, help="仓库根目录，默认按脚本位置推断")
    args = ap.parse_args()

    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parents[1]

    sources = [
        read_rules_version(root),
        read_openapi_version(root),
        read_csharp_version(root),
    ]

    print(f"仓库根目录：{root}")
    errors: list[str] = []
    values: list[str] = []
    for value, where in sources:
        if value is None:
            errors.append(f"缺失：{where}")
            print(f"  [缺失] {where}")
            continue
        values.append(value)
        print(f"  {value:<10} {where}")
        if not SEMVER.match(value):
            errors.append(f"格式非法（应为无前导零的 MAJOR.MINOR.PATCH）：{where} = {value}")

    if not errors and len(set(values)) > 1:
        pairs = " / ".join(sorted(set(values)))
        errors.append(f"三处版本不一致：{pairs}")

    if errors:
        print("")
        print("FAIL：契约版本校验未通过")
        for e in errors:
            print(f"  - {e}")
        print("")
        print("修正依据：contract/rules.md §12.1（三处同值、一次变更只递增一个段）")
        return 1

    print("")
    print(f"PASS：三处均为 {values[0]}（格式合法，同值）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
