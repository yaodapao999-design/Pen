#!/usr/bin/env python3
import json
import sys
from pathlib import Path


def main() -> int:
    payload = json.load(sys.stdin)
    fp = payload.get("file_path")
    if not isinstance(fp, str) or not fp.endswith(".md"):
        return 0
    p = Path(fp)
    parts = p.parts
    try:
        i = parts.index("Assets")
    except ValueError:
        return 0
    if len(parts) < i + 4:
        return 0
    if parts[i + 1] != "Plans":
        return 0

    # afterFileEdit currently supports no stdout fields; log for Hooks output channel.
    print(f"[plan_docs_after_file_edit_audit] edited={p}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as e:
        print(f"[plan_docs_after_file_edit_audit] error: {e}", file=sys.stderr)
        raise SystemExit(0)
