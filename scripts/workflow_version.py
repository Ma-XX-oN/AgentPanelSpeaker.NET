#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "VERSION"
DEVELOPMENT_VERSION = re.compile(r"^\d+\.\d+\.\d+-issue\.\d+\.\d+$")


def main() -> int:
  version = SOURCE.read_text(encoding="utf-8").strip()
  if DEVELOPMENT_VERSION.fullmatch(version) is None:
    print(f"invalid development version: {version}", file=sys.stderr)
    return 1
  print(version)
  return 0


if __name__ == "__main__":
  raise SystemExit(main())
