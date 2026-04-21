#!/usr/bin/env python3
"""
Plan docs 下级同步检查（适配 Claude Code 的 PostToolUse）。

触发条件：PostToolUse，tool_name 匹配 Write|Edit|MultiEdit
目标文件：落在 Assets/Plans/<gdd>/ 下的 .md
作用：
  1) 基本审计日志（stderr），合并了原 plan_docs_after_file_edit_audit.py 的功能
  2) 扫描 game-design.md / implementation/*.md / editor-setup.md 之间的同步问题：
     - game-design.md 中的相对 md 链接断链
     - game-design.md 缺少指向 implementation/*.md 的链接
     - 兄弟文档可能遗漏的重命名残留（基于本次 Edit/MultiEdit 的 old/new 对）
     - implementation <-> editor-setup 之间 `Assets/**/*.cs` 脚本路径覆盖一致性

输出：通过 hookSpecificOutput.additionalContext 把发现的问题注入回模型上下文。
Fail open：异常时返回空 JSON，不阻塞。
"""
from __future__ import annotations

import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


LINK_RE = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
CS_PATH_RE = re.compile(r"`(Assets/[^`]+\.cs)`")


def _read_json_maybe(s: str) -> Any | None:
    if not s:
        return None
    try:
        return json.loads(s)
    except Exception:
        return None


def _extract_edited_path(tool_input: Any, tool_response: Any) -> Path | None:
    if isinstance(tool_input, str):
        maybe = _read_json_maybe(tool_input)
        if isinstance(maybe, dict):
            tool_input = maybe

    if isinstance(tool_input, dict):
        for k in ("file_path", "path", "target_file", "target", "uri"):
            v = tool_input.get(k)
            if isinstance(v, str) and v.strip():
                return Path(v)

    if isinstance(tool_response, str):
        maybe = _read_json_maybe(tool_response)
        if isinstance(maybe, dict):
            tool_response = maybe

    if isinstance(tool_response, dict):
        for k in ("filePath", "file_path", "path", "file", "target_file"):
            v = tool_response.get(k)
            if isinstance(v, str) and v.strip():
                return Path(v)

    return None


def _edit_pairs_from_tool_input(tool_name: str, tool_input: Any) -> list[tuple[str, str]]:
    """从 Edit / MultiEdit 的 tool_input 中提取 (old, new) 对，用于兄弟文档残留扫描。"""
    if isinstance(tool_input, str):
        maybe = _read_json_maybe(tool_input)
        if isinstance(maybe, dict):
            tool_input = maybe
    if not isinstance(tool_input, dict):
        return []

    pairs: list[tuple[str, str]] = []
    if tool_name == "Edit":
        old = tool_input.get("old_string")
        new = tool_input.get("new_string")
        if isinstance(old, str) and isinstance(new, str) and old != new and old.strip():
            pairs.append((old, new))
    elif tool_name == "MultiEdit":
        edits = tool_input.get("edits")
        if isinstance(edits, list):
            for e in edits:
                if not isinstance(e, dict):
                    continue
                old = e.get("old_string")
                new = e.get("new_string")
                if isinstance(old, str) and isinstance(new, str) and old != new and old.strip():
                    pairs.append((old, new))
    return pairs


def _is_under_plans_gdd_dir(p: Path) -> bool:
    parts = p.parts
    try:
        i = parts.index("Assets")
    except ValueError:
        return False
    return len(parts) >= i + 4 and parts[i + 1] == "Plans" and parts[i + 2] != ""


def _gdd_root_for(path: Path) -> Path | None:
    parts = path.parts
    try:
        i = parts.index("Assets")
    except ValueError:
        return None
    if len(parts) < i + 4 or parts[i + 1] != "Plans":
        return None
    return Path(*parts[: i + 3])


def _list_children(gdd: Path) -> dict[str, list[Path]]:
    impl = gdd / "implementation"
    children: list[Path] = []
    if impl.is_dir():
        children.extend(sorted(p for p in impl.glob("*.md") if p.is_file()))
    editor = gdd / "editor-setup.md"
    return {
        "implementation": sorted(children) if impl.is_dir() else [],
        "editor_setup": [editor] if editor.is_file() else [],
    }


def _read_text(p: Path) -> str:
    return p.read_text(encoding="utf-8", errors="replace")


def _rel(from_file: Path, to_file: Path) -> str:
    try:
        return os.path.relpath(to_file, start=from_file.parent)
    except Exception:
        return str(to_file)


def _markdown_links(text: str) -> list[str]:
    out: list[str] = []
    for m in LINK_RE.finditer(text):
        target = m.group(1).strip()
        if target.startswith("#") or target.startswith("http"):
            continue
        out.append(target.split("#", 1)[0])
    return out


def _resolve_link(base_file: Path, target: str) -> Path:
    return (base_file.parent / target).resolve()


@dataclass
class Finding:
    severity: str
    child: Path
    message: str


def _check_rename_residuals(child: Path, text: str, pairs: list[tuple[str, str]]) -> list[Finding]:
    findings: list[Finding] = []
    for old, new in pairs:
        if not old.strip() or old == new:
            continue
        if old in text and new not in text:
            findings.append(
                Finding(
                    "warn",
                    child,
                    f"仍包含旧片段（可能需同步替换）：{old!r} -> {new!r}",
                )
            )
    return findings


def _cs_paths_from_text(text: str) -> list[str]:
    return sorted(set(CS_PATH_RE.findall(text)))


def main() -> int:
    payload = json.load(sys.stdin)

    tool_name = str(payload.get("tool_name") or "")
    tool_input = payload.get("tool_input")
    tool_response = payload.get("tool_response")

    edited = _extract_edited_path(tool_input, tool_response)
    if edited is None:
        print(json.dumps({}))
        return 0

    edited = edited.expanduser()

    if edited.suffix.lower() != ".md":
        print(json.dumps({}))
        return 0

    if not _is_under_plans_gdd_dir(edited):
        print(json.dumps({}))
        return 0

    gdd_root = _gdd_root_for(edited)
    if gdd_root is None:
        print(json.dumps({}))
        return 0

    print(f"[plan_docs_sync] edited={edited}", file=sys.stderr)

    game_design = gdd_root / "game-design.md"
    impl_dir = gdd_root / "implementation"
    editor_setup = gdd_root / "editor-setup.md"

    role = "other"
    if edited.name == "game-design.md" and edited.parent == gdd_root:
        role = "gdd"
    elif edited.parent == impl_dir and edited.suffix.lower() == ".md":
        role = "implementation"
    elif edited.name == "editor-setup.md" and edited.parent == gdd_root:
        role = "editor_setup"

    children = _list_children(gdd_root)
    findings: list[Finding] = []

    if game_design.is_file():
        gd_text = _read_text(game_design)
        for target in _markdown_links(gd_text):
            if ".." in Path(target).parts:
                continue
            resolved = _resolve_link(game_design, target)
            if target.endswith(".md") and not resolved.exists():
                findings.append(
                    Finding(
                        "error",
                        game_design,
                        f"链接目标不存在：{target}（解析为 {resolved}）",
                    )
                )

    if role == "gdd" and impl_dir.is_dir():
        impl_files = sorted(p for p in impl_dir.glob("*.md") if p.is_file())
        gd_text = _read_text(game_design) if game_design.is_file() else ""
        for impl in impl_files:
            rel = _rel(game_design, impl) if game_design.is_file() else str(impl)
            if impl.name not in gd_text and rel not in gd_text and rel.lstrip("./") not in gd_text:
                findings.append(
                    Finding(
                        "warn",
                        game_design,
                        f"game-design.md 可能缺少指向实现计划的链接：{rel}",
                    )
                )

    pairs = _edit_pairs_from_tool_input(tool_name, tool_input)

    if role == "gdd":
        targets = [*children["implementation"], *children["editor_setup"]]
    elif role == "implementation":
        targets = [*([game_design] if game_design.is_file() else []), *children["editor_setup"]]
    elif role == "editor_setup":
        targets = [*([game_design] if game_design.is_file() else []), *children["implementation"]]
    else:
        targets = [
            *children["implementation"],
            *children["editor_setup"],
            *([game_design] if game_design.is_file() else []),
        ]

    seen: set[str] = set()
    uniq_targets: list[Path] = []
    for t in targets:
        k = str(t)
        if k in seen or t == edited:
            continue
        seen.add(k)
        uniq_targets.append(t)

    for child in uniq_targets:
        if not child.is_file():
            continue
        text = _read_text(child)
        findings.extend(_check_rename_residuals(child, text, pairs))

    if role == "implementation" and editor_setup.is_file():
        impl_text = _read_text(edited) if edited.is_file() else ""
        ed_text = _read_text(editor_setup)
        for cs in _cs_paths_from_text(impl_text):
            if cs not in ed_text:
                findings.append(
                    Finding(
                        "warn",
                        editor_setup,
                        f"editor-setup.md 可能未覆盖实现计划中提到的脚本路径：`{cs}`",
                    )
                )

    if role == "editor_setup":
        ed_text = _read_text(edited) if edited.is_file() else ""
        ed_cs = _cs_paths_from_text(ed_text)
        if ed_cs and impl_dir.is_dir():
            impl_files = sorted(p for p in impl_dir.glob("*.md") if p.is_file())
            joined_impl = "\n".join(_read_text(p) for p in impl_files)
            for cs in ed_cs:
                if cs not in joined_impl:
                    findings.append(
                        Finding(
                            "warn",
                            edited,
                            f"editor-setup.md 提到的脚本路径未在任何 implementation/*.md 中出现：`{cs}`（实现计划可能需要补充/同步）",
                        )
                    )

    if not findings:
        print(json.dumps({}))
        return 0

    lines: list[str] = []
    lines.append("### Hook：Plans 下级文档同步检查（PostToolUse）")
    lines.append(f"- 触发工具：`{tool_name}`")
    lines.append(f"- 已编辑文件：`{edited}`")
    lines.append(f"- GDD 目录：`{gdd_root}`")
    lines.append("")
    by_child: dict[str, list[Finding]] = {}
    for f in findings:
        by_child.setdefault(str(f.child), []).append(f)

    for child, fs in sorted(by_child.items(), key=lambda kv: kv[0]):
        lines.append(f"#### {child}")
        for f in fs:
            lines.append(f"- **{f.severity}**: {f.message}")
        lines.append("")

    lines.append("> 说明：这是确定性启发式检查，用于提醒“可能需要同步修改”；不代表一定错误。")

    output = {
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": "\n".join(lines).strip() + "\n",
        }
    }
    print(json.dumps(output, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as e:
        print(json.dumps({}), flush=True)
        print(f"[plan_docs_sync] error: {e}", file=sys.stderr)
        raise SystemExit(0)
