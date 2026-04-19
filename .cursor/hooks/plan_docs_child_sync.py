#!/usr/bin/env python3
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


def _extract_edited_path(tool_name: str, tool_input: Any, tool_output: str) -> Path | None:
    # tool_input is usually a dict for Write-like tools, but be defensive.
    if isinstance(tool_input, str):
        maybe = _read_json_maybe(tool_input)
        if isinstance(maybe, dict):
            tool_input = maybe

    if isinstance(tool_input, dict):
        for k in ("path", "file_path", "target_file", "target", "uri"):
            v = tool_input.get(k)
            if isinstance(v, str) and v.strip():
                return Path(v)

    out = _read_json_maybe(tool_output)
    if isinstance(out, dict):
        for k in ("path", "file_path", "file", "target_file"):
            v = out.get(k)
            if isinstance(v, str) and v.strip():
                return Path(v)

    return None


def _is_under_plans_gdd_dir(p: Path) -> bool:
    parts = p.parts
    try:
        i = parts.index("Assets")
    except ValueError:
        return False
    # .../Assets/Plans/<gdd>/...
    return (
        len(parts) >= i + 4
        and parts[i + 1] == "Plans"
        and parts[i + 2] != ""  # gdd name
    )


def _gdd_root_for(path: Path) -> Path | None:
    parts = path.parts
    try:
        i = parts.index("Assets")
    except ValueError:
        return None
    if len(parts) < i + 4:
        return None
    if parts[i + 1] != "Plans":
        return None
    return Path(*parts[: i + 3])  # .../Assets/Plans/<gdd>


def _list_children(gdd: Path) -> dict[str, list[Path]]:
    impl = gdd / "implementation"
    children: list[Path] = []
    if impl.is_dir():
        children.extend(sorted(p for p in impl.glob("*.md") if p.is_file()))
    editor = gdd / "editor-setup.md"
    if editor.is_file():
        children.append(editor)
    return {"implementation": sorted(children) if impl.is_dir() else [], "editor_setup": [editor] if editor.is_file() else []}


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
    # Only handle relative links; ignore weird schemes.
    return (base_file.parent / target).resolve()


def _pairs_from_afterfileedit_edits(edits: Any) -> list[tuple[str, str]]:
    if not isinstance(edits, list):
        return []
    pairs: list[tuple[str, str]] = []
    for e in edits:
        if not isinstance(e, dict):
            continue
        old = e.get("old_string")
        new = e.get("new_string")
        if isinstance(old, str) and isinstance(new, str) and old != new and old.strip():
            pairs.append((old, new))
    return pairs


@dataclass
class Finding:
    severity: str  # error|warn|info
    child: Path
    message: str


def _check_rename_residuals(child: Path, text: str, pairs: list[tuple[str, str]]) -> list[Finding]:
    findings: list[Finding] = []
    for old, new in pairs:
        if not old.strip():
            continue
        if old in text and new not in text and old != new:
            # Heuristic: if old token still exists, likely stale (not perfect).
            findings.append(
                Finding(
                    "warn",
                    child,
                    f"仍包含旧片段（可能需同步替换）：{old!r} -> {new!r}",
                )
            )
    return findings


def _cs_paths_from_implementation(text: str) -> list[str]:
    # Capture `Assets/.../*.cs` mentions in implementation docs.
    return sorted(set(CS_PATH_RE.findall(text)))


def main() -> int:
    payload = json.load(sys.stdin)

    tool_name = str(payload.get("tool_name") or "")
    tool_input = payload.get("tool_input")
    tool_output = str(payload.get("tool_output") or "")

    edited = _extract_edited_path(tool_name, tool_input, tool_output)
    if edited is None:
        print(json.dumps({}))
        return 0

    edited = edited.expanduser()
    try:
        edited_exists = edited.exists()
    except OSError:
        edited_exists = False

    if not edited_exists:
        # Some tools may write then move; still try best-effort by path string.
        pass

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

    # Broken links from game-design -> implementation
    if game_design.is_file():
        gd_text = _read_text(game_design)
        for target in _markdown_links(gd_text):
            if ".." in Path(target).parts:
                # avoid path traversal weirdness in reports
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

    # If game-design changed: ensure it links all implementation/*.md (lightweight)
    if role == "gdd" and impl_dir.is_dir():
        impl_files = sorted(p for p in impl_dir.glob("*.md") if p.is_file())
        gd_text = _read_text(game_design) if game_design.is_file() else ""
        for impl in impl_files:
            rel = _rel(game_design, impl) if game_design.is_file() else str(impl)
            # Accept either ./implementation/x.md or implementation/x.md
            if impl.name not in gd_text and rel not in gd_text and rel.lstrip("./") not in gd_text:
                findings.append(
                    Finding(
                        "warn",
                        game_design,
                        f"game-design.md 可能缺少指向实现计划的链接：{rel}",
                    )
                )

    # Rename residual checks across children when we can infer edit pairs from tool_output (afterFileEdit-like)
    out_obj = _read_json_maybe(tool_output)
    edits = out_obj.get("edits") if isinstance(out_obj, dict) else None
    pairs = _pairs_from_afterfileedit_edits(edits)

    targets: list[Path] = []
    if role == "gdd":
        targets = [*children["implementation"], *([editor_setup] if editor_setup.is_file() else [])]
    elif role == "implementation":
        targets = [*([game_design] if game_design.is_file() else []), *([editor_setup] if editor_setup.is_file() else [])]
    elif role == "editor_setup":
        targets = [*([game_design] if game_design.is_file() else []), *children["implementation"]]
    else:
        # Any other md under the GDD folder: still scan obvious children lightly
        targets = [*children["implementation"], *([editor_setup] if editor_setup.is_file() else []), *([game_design] if game_design.is_file() else [])]

    # de-dupe while preserving order
    seen: set[str] = set()
    uniq_targets: list[Path] = []
    for t in targets:
        k = str(t)
        if k in seen:
            continue
        seen.add(k)
        if t == edited:
            continue
        uniq_targets.append(t)

    for child in uniq_targets:
        if not child.is_file():
            continue
        text = _read_text(child)
        findings.extend(_check_rename_residuals(child, text, pairs))

    # implementation -> editor-setup coverage for declared .cs paths
    if role == "implementation" and editor_setup.is_file():
        impl_text = _read_text(edited)
        ed_text = _read_text(editor_setup)
        for cs in _cs_paths_from_implementation(impl_text):
            if cs not in ed_text:
                findings.append(
                    Finding(
                        "warn",
                        editor_setup,
                        f"editor-setup.md 可能未覆盖实现计划中提到的脚本路径：`{cs}`",
                    )
                )

    # editor-setup -> implementation coverage for declared .cs paths (reverse direction)
    if role == "editor_setup":
        ed_text = _read_text(edited)
        ed_cs = sorted(set(CS_PATH_RE.findall(ed_text)))
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
    lines.append("### Hook：Plans 下级文档同步检查（postToolUse）")
    lines.append(f"- 触发工具：`{tool_name}`")
    lines.append(f"- 已编辑文件：`{edited}`")
    lines.append(f"- GDD 目录：`{gdd_root}`")
    lines.append("")
    # group by child
    by_child: dict[str, list[Finding]] = {}
    for f in findings:
        by_child.setdefault(str(f.child), []).append(f)

    for child, fs in sorted(by_child.items(), key=lambda kv: kv[0]):
        lines.append(f"#### {child}")
        for f in fs:
            lines.append(f"- **{f.severity}**: {f.message}")
        lines.append("")

    lines.append("> 说明：这是确定性启发式检查，用于提醒“可能需要同步修改”；不代表一定错误。")

    print(json.dumps({"additional_context": "\n".join(lines).strip() + "\n"}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as e:
        # Fail open for hooks unless user sets failClosed (we don't).
        print(json.dumps({}), flush=True)
        print(f"[plan_docs_child_sync] error: {e}", file=sys.stderr)
        raise SystemExit(0)
