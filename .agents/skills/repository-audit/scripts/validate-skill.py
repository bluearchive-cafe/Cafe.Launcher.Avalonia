#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / "SKILL.md"

errors: list[str] = []

if not SKILL.exists():
    errors.append("missing SKILL.md")
else:
    text = SKILL.read_text(encoding="utf-8")
    if not text.startswith("---\n"):
        errors.append("SKILL.md: missing YAML frontmatter")
    front = text.split("---", 2)[1] if text.startswith("---") else ""
    if not re.search(r"(?m)^name:\s*repository-audit\s*$", front):
        errors.append("SKILL.md: name must be repository-audit")
    m = re.search(r"(?m)^description:\s*(.+)$", front)
    if not m or not m.group(1).strip().startswith("Use when"):
        errors.append("SKILL.md: description must start with 'Use when'")
    if len(front) > 1024:
        errors.append("SKILL.md: frontmatter exceeds 1024 characters")

    # Validate local file references written inside backticks.
    refs = set(re.findall(r"`((?:references|templates|profiles|tests|scripts)/[^`]+)`", text))
    for ref in sorted(refs):
        p = ROOT / ref
        if not p.exists():
            errors.append(f"SKILL.md: broken reference: {ref}")

for json_path in (ROOT / "templates").glob("*.json"):
    try:
        json.loads(json_path.read_text(encoding="utf-8"))
    except Exception as exc:
        errors.append(f"{json_path.relative_to(ROOT)}: invalid JSON: {exc}")

required = [
    "references/core-policy.md",
    "references/execution.md",
    "references/findings.md",
    "references/lifecycle.md",
    "references/modes.md",
    "references/report-generation.md",
    "templates/report.md",
    "templates/repository-map.md",
    "tests/scenarios.md",
]
for ref in required:
    if not (ROOT / ref).exists():
        errors.append(f"missing required file: {ref}")

if errors:
    print("repository-audit skill validation FAILED")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("repository-audit skill validation OK")
print(f"root: {ROOT}")
print(f"files: {sum(1 for p in ROOT.rglob('*') if p.is_file())}")
