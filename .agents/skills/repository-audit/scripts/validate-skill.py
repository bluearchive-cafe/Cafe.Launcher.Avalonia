#!/usr/bin/env python3
from __future__ import annotations

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
    match = re.match(r"\A---\n(.*?)\n---(?:\n|$)", text, re.DOTALL)
    if not match:
        errors.append("SKILL.md: missing YAML frontmatter")
    front = match.group(1) if match else ""
    if not re.search(r"(?m)^name:\s*repository-audit\s*$", front):
        errors.append("SKILL.md: name must be repository-audit")
    description = re.search(r"(?m)^description:[ \t]*(\S[^\n]*)$", front)
    if not description:
        errors.append("SKILL.md: missing description")

# Check the whole package, including links inside conditionally loaded references.
# Backtick paths with these prefixes are package-relative; Markdown links are
# relative to the containing document. This is a structural check, not a YAML
# schema validator or an evaluation of the agent's behavior.
for document in sorted(ROOT.rglob("*.md")):
    text = document.read_text(encoding="utf-8")
    code_refs = re.findall(
        r"`((?:references|templates|profiles|tests|scripts)/[^`]+)`", text
    )
    links = re.findall(r"\]\(([^\s)]+)\)", text)
    targets = [(ROOT, ref) for ref in code_refs]
    targets.extend((document.parent, ref) for ref in links)
    for base, ref in sorted(set(targets)):
        if re.match(r"[a-zA-Z][a-zA-Z0-9+.-]*:", ref) or ref.startswith("#"):
            continue
        path = ref.split("#", 1)[0]
        if not (base / path).exists():
            errors.append(f"{document.relative_to(ROOT)}: broken reference: {ref}")

if errors:
    print("repository-audit skill validation FAILED")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)

print("repository-audit skill validation OK")
print(f"root: {ROOT}")
print(f"files: {sum(1 for p in ROOT.rglob('*') if p.is_file())}")
