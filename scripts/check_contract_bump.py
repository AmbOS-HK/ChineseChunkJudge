#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""契约版本守门（CI 调用）：改了 contract/ 就必须升版，且只能升一个段。

判定顺序（任一不满足即 FAIL）：
  1. contract/ 无改动 → 通过（无需升版）
  2. 有改动但 rules.md 版本未变 → 失败：改契约不升版
  3. 版本格式必须是 MAJOR.MINOR.PATCH、无前导零
  4. 头版本必须严格大于基线，且只有一个段递增（不许跳号、不许回退、不许跨段）
  5. §8.3 新增基础规则编号（CCJ-###）→ 至少 MINOR
  6. §8.3 删除基础规则编号 → 必须 MAJOR（基础规则不可删除，删编号即破坏性变更）

刻意不做的事：不从 Schema diff 推断 major/minor（required 增删的语义判断仍需人评审，
机器只兜住"改了就升版"这一条底线）。

用法：
    python scripts/check_contract_bump.py --base origin/main
    python scripts/check_contract_bump.py --base HEAD~1
    python scripts/check_contract_bump.py --self-test

退出码：
    0  通过（未改契约，或改动与版本位匹配）
    1  违规
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

RULES_PATH = "contract/rules.md"
SEMVER = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
RULES_ROW = re.compile(r"^\|\s*契约版本\s*\|\s*(.*?)\s*\|\s*$")
BASE_RULE_ROW = re.compile(r"^\|\s*(CCJ-\d{3})\s*\|", re.M)


def parse_version(text: str | None) -> str | None:
    if not text:
        return None
    for line in text.splitlines():
        m = RULES_ROW.match(line)
        if m:
            return m.group(1).strip()
    return None


def parse_rule_ids(text: str | None) -> set[str]:
    return set(BASE_RULE_ROW.findall(text or ""))


def bump_kind(base: tuple[int, int, int], head: tuple[int, int, int]) -> str:
    """判定版本位：MAJOR / MINOR / PATCH / INVALID。

    高位递增时低位置零属正常（0.9.0 → 1.0.0 是 MAJOR，不是"跨两段"）；
    判定的实质是"恰好升一位，且低位按规范归零"。
    """
    if head == base:
        return "NONE"
    if head[0] == base[0] + 1 and head[1:] == (0, 0):
        return "MAJOR"
    if head[0] == base[0] and head[1] == base[1] + 1 and head[2] == 0:
        return "MINOR"
    if head[0] == base[0] and head[1] == base[1] and head[2] == base[2] + 1:
        return "PATCH"
    return "INVALID"


def evaluate_bump(
    base_version: str | None,
    head_version: str | None,
    changed_files: list[str],
    new_rules: set[str],
    removed_rules: set[str],
) -> list[str]:
    """纯逻辑判定：返回违规说明列表，空列表表示通过。"""
    contract_files = [f for f in changed_files if f.replace("\\", "/").startswith("contract/")]
    if not contract_files:
        return []

    errors: list[str] = []
    if base_version is None:
        errors.append("基线 rules.md 未找到「契约版本」表头行，无法判定")
        return errors
    if head_version is None:
        errors.append("当前 rules.md 未找到「契约版本」表头行")
        return errors
    if not SEMVER.match(head_version):
        errors.append(f"版本格式非法：{head_version}（应为无前导零的 MAJOR.MINOR.PATCH）")
        return errors
    if not SEMVER.match(base_version):
        errors.append(f"基线版本格式非法：{base_version}")
        return errors

    if head_version == base_version:
        errors.append(
            f"契约有 {len(contract_files)} 个文件改动，但版本仍为 {head_version}——改契约必须升版"
        )
        return errors

    base = tuple(int(x) for x in base_version.split("."))
    head = tuple(int(x) for x in head_version.split("."))
    if head <= base:
        errors.append(f"版本未递增或回退：{base_version} → {head_version}")
        return errors

    kind = bump_kind(base, head)
    if kind == "INVALID":
        errors.append(
            f"版本位非法：{base_version} → {head_version}"
            "（只允许 MAJOR 且低位归零、MINOR 且 PATCH 归零、PATCH +1 三种）"
        )
        return errors

    major_bumped, minor_bumped = kind == "MAJOR", kind == "MINOR"
    if removed_rules and not major_bumped:
        errors.append(
            "删除了基础规则编号 "
            + "、".join(sorted(removed_rules))
            + f"，必须走 MAJOR（当前 {base_version} → {head_version}）"
        )
    if new_rules and not (major_bumped or minor_bumped):
        errors.append(
            "新增基础规则编号 "
            + "、".join(sorted(new_rules))
            + f"，至少走 MINOR（当前 {base_version} → {head_version}）"
        )
    return errors


def git(root: Path, *args: str) -> str:
    out = subprocess.run(
        ["git", "-C", str(root), *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip() or f"git {' '.join(args)} 失败")
    return out.stdout


def git_show(root: Path, ref: str, path: str) -> str | None:
    try:
        return git(root, "show", f"{ref}:{path}")
    except RuntimeError:
        return None


def run_self_test() -> int:
    cases = [
        ("无契约改动", (("0.9.0", "0.9.0", ["README.md"], set(), set()), 0)),
        ("改契约不升版", (("0.9.0", "0.9.0", ["contract/rules.md"], set(), set()), 1)),
        ("新增规则但只升 PATCH", (("0.9.0", "0.9.1", ["contract/rules.md"], {"CCJ-018"}, set()), 1)),
        ("新增规则升 MINOR", (("0.9.0", "0.10.0", ["contract/rules.md"], {"CCJ-018"}, set()), 0)),
        ("删除规则但只升 MINOR", (("0.9.0", "0.10.0", ["contract/rules.md"], set(), {"CCJ-017"}), 1)),
        ("删规则升 MAJOR", (("0.9.0", "1.0.0", ["contract/rules.md"], set(), {"CCJ-017"}), 0)),
        ("跳两段", (("0.9.0", "1.1.0", ["contract/rules.md"], set(), set()), 1)),
        ("版本回退", (("0.9.0", "0.8.5", ["contract/rules.md"], set(), set()), 1)),
        ("修改 schema 只升 PATCH", (("0.9.0", "0.9.1", ["contract/schema/raw-chunk.json"], set(), set()), 0)),
    ]
    failed = 0
    for name, ((base, head, files, new_rules, removed), expect) in cases:
        errs = evaluate_bump(base, head, files, new_rules, removed)
        got = 1 if errs else 0
        ok = got == expect
        failed += 0 if ok else 1
        print(f"  [{'OK ' if ok else 'BAD'}] {name}: {'FAIL' if got else 'PASS'}"
              + (f"（{errs[0]}）" if errs else ""))
    print("")
    print(f"self-test: {len(cases) - failed}/{len(cases)} 通过")
    return 1 if failed else 0


def main() -> int:
    ap = argparse.ArgumentParser(description="契约版本守门")
    ap.add_argument("--root", default=None, help="仓库根目录，默认按脚本位置推断")
    ap.add_argument("--base", default="origin/main", help="基线 git ref，默认 origin/main")
    ap.add_argument("--self-test", action="store_true", help="只跑内置判定用例，不读 git")
    args = ap.parse_args()

    if args.self_test:
        return run_self_test()

    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parents[1]
    try:
        head_files = git(root, "diff", "--name-only", f"{args.base}..HEAD", "--", "contract").splitlines()
        base_md = git_show(root, args.base, RULES_PATH)
        head_md = (root / RULES_PATH).read_text(encoding="utf-8")
    except RuntimeError as exc:
        print(f"FAIL：无法读取 git 基线 {args.base}：{exc}")
        print("提示：CI 需先 fetch 基线分支（git fetch origin main）；本地可改用 --base HEAD~1")
        return 1

    base_version = parse_version(base_md)
    head_version = parse_version(head_md)
    base_rules = parse_rule_ids(base_md)
    head_rules = parse_rule_ids(head_md)
    new_rules = head_rules - base_rules
    removed_rules = base_rules - head_rules

    changed = [f for f in head_files if f.strip()]
    errors = evaluate_bump(base_version, head_version, changed, new_rules, removed_rules)

    print(f"仓库根目录：{root}")
    print(f"基线：{args.base}（契约版本 {base_version}）")
    print(f"当前：HEAD（契约版本 {head_version}）")
    print(f"契约改动文件：{len(changed)} 个")
    for f in changed:
        print(f"  - {f}")
    if new_rules:
        print(f"新增基础规则：{'、'.join(sorted(new_rules))}")
    if removed_rules:
        print(f"删除基础规则：{'、'.join(sorted(removed_rules))}")

    if errors:
        print("")
        print("FAIL：契约版本守门未通过")
        for e in errors:
            print(f"  - {e}")
        print("")
        print("修正依据：contract/rules.md §12.1（三处同值、一次只递增一个段）")
        return 1

    print("")
    if not changed:
        print("PASS：contract/ 无改动")
    else:
        print(f"PASS：契约改动与版本位匹配（{base_version} → {head_version}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
