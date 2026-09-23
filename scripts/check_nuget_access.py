#!/usr/bin/env python3
"""Check whether the NuGet service needed for restore is reachable."""

from __future__ import annotations

import sys
from urllib.error import URLError
from urllib.request import Request, urlopen

URL = "https://api.nuget.org/v3/index.json"

try:
  request = Request(URL, method="HEAD")
  with urlopen(request, timeout=15) as response:
    status = getattr(response, "status", 200)
    if status < 200 or status >= 400:
      raise RuntimeError(f"HTTP {status}")
except (OSError, RuntimeError, URLError) as exc:
  print(f"WARNING: NuGet network prerequisite unavailable: {exc}", file=sys.stderr)
  raise SystemExit(2)

print("NuGet network prerequisite available.")
