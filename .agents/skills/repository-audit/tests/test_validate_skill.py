"""Exercise package validation against isolated valid and broken skill packages."""

from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


PACKAGE = Path(__file__).resolve().parents[1]


class ValidateSkillTests(unittest.TestCase):
    def setUp(self):
        fixture = tempfile.TemporaryDirectory(prefix="cafe-audit-skill-")
        self.addCleanup(fixture.cleanup)
        self.root = Path(fixture.name) / "repository-audit"
        shutil.copytree(PACKAGE, self.root, ignore=shutil.ignore_patterns("__pycache__"))

    def validate(self):
        return subprocess.run(
            [sys.executable, "-X", "utf8", str(self.root / "scripts/validate-skill.py")],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )

    def replace_instruction(self, old, new):
        path = self.root / "SKILL.md"
        path.write_text(path.read_text(encoding="utf-8").replace(old, new), encoding="utf-8")

    def assert_rejected(self, message):
        result = self.validate()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(message, result.stdout)

    def test_valid_package(self):
        result = self.validate()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_description_can_use_other_wording(self):
        self.replace_instruction("description: Use when", "description: Assess")
        result = self.validate()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_missing_required_reference_is_rejected(self):
        (self.root / "references/core-policy.md").unlink()
        self.assert_rejected("broken reference: references/core-policy.md")

    def test_broken_link_in_nested_reference_is_rejected(self):
        path = self.root / "references/architecture.md"
        path.write_text("[Missing reference](missing.md)\n", encoding="utf-8")
        self.assert_rejected("broken reference: missing.md")

    def test_missing_description_is_rejected(self):
        self.replace_instruction("description:", "summary:")
        self.assert_rejected("missing description")

    def test_unclosed_frontmatter_is_rejected(self):
        self.replace_instruction("\n---\n\n# Repository Audit", "\n\n# Repository Audit")
        self.assert_rejected("missing YAML frontmatter")


if __name__ == "__main__":
    unittest.main()
