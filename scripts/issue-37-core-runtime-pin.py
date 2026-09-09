from pathlib import Path

CORE_COMMIT = "6c92799c1b14693001e8b913465f4a12b0b1e1ab"
path = Path("tools/AIConversationCore-runtime/CORE_COMMIT")
current = path.read_text(encoding="utf-8").strip()
if current != "7eb7f4fca630aa0a132e93799e878120aaf353b9":
  raise SystemExit(f"unexpected deployed Core runtime pin: {current}")
path.write_text(CORE_COMMIT + "\n", encoding="utf-8")
