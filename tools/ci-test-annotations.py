#!/usr/bin/env python3
"""Turn failed tests from TRX reports into GitHub Actions error annotations.

Usage: python3 tools/ci-test-annotations.py <results-dir>

Job logs of this repository need admin rights to download, but check-run annotations are public
(GET /repos/{owner}/{repo}/check-runs/{job_id}/annotations), so a failing test – including an
intermittent one – can be identified from the annotation alone. Also appends a summary to
$GITHUB_STEP_SUMMARY when it is set. Always exits 0; the test step itself reports the failure.
"""

import os
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
STACK_LINES = 8
MESSAGE_CHARS = 4000


def escape_data(s: str) -> str:
    return s.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_property(s: str) -> str:
    return escape_data(s).replace(":", "%3A").replace(",", "%2C")


def source_location(stack: str, workspace: str) -> tuple[str, str] | None:
    """First stack frame inside the repository as (relative path, line)."""
    for match in re.finditer(r" in (.+?):line (\d+)", stack):
        path = match.group(1).replace("\\", "/")
        root = workspace.replace("\\", "/").rstrip("/") + "/"
        if workspace and path.lower().startswith(root.lower()):
            return path[len(root):], match.group(2)
    return None


def main() -> int:
    results = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "TestResults")
    workspace = os.environ.get("GITHUB_WORKSPACE", "")
    failures = []
    for trx in sorted(results.rglob("*.trx")):
        try:
            root = ET.parse(trx).getroot()
        except ET.ParseError as e:
            print(f"::warning title=Unreadable TRX::{escape_data(f'{trx}: {e}')}")
            continue
        for result in root.iterfind(".//t:UnitTestResult", NS):
            if result.get("outcome") not in ("Failed", "Error", "Timeout", "Aborted"):
                continue
            message = result.findtext(".//t:ErrorInfo/t:Message", "", NS).strip()
            stack = result.findtext(".//t:ErrorInfo/t:StackTrace", "", NS).rstrip()
            failures.append((result.get("testName", "?"), result.get("duration", "?"), message, stack))

    for name, duration, message, stack in failures:
        body = message[:MESSAGE_CHARS]
        if stack:
            body += "\n\n" + "\n".join(stack.splitlines()[:STACK_LINES])
        props = f"title={escape_property(f'{name} ({duration})')}"
        location = source_location(stack, workspace)
        if location:
            props = f"file={escape_property(location[0])},line={location[1]}," + props
        print(f"::error {props}::{escape_data(body)}")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary and failures:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(f"### {len(failures)} failed test(s)\n\n")
            for name, duration, message, stack in failures:
                f.write(f"<details><summary><code>{name}</code> ({duration})</summary>\n\n```\n{message}\n\n{stack}\n```\n</details>\n\n")

    print(f"{len(failures)} failed test(s) in {results}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
