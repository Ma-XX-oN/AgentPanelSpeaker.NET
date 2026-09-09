from pathlib import Path

path = Path('AgentPanelSpeaker/Issue46IndependentRegressionOracleTestRunner.cs')
text = path.read_text(encoding='utf-8')

entry_anchor = '''      ("independent-oracle/test-runner-rejects-partial-completion",\n        TestRunnerRejectsPartialCompletion),\n'''
entry_replacement = '''      ("independent-oracle/mainform-lease-completes-shown-before-return",\n        TestMainFormLeaseCompletesShownBeforeReturn),\n      ("independent-oracle/test-runner-rejects-partial-completion",\n        TestRunnerRejectsPartialCompletion),\n'''
if entry_anchor not in text:
  raise SystemExit('test-list anchor not found')
text = text.replace(entry_anchor, entry_replacement, 1)

method_anchor = '''  /// <summary>\n  /// Proves a zero exit code plus a suite header cannot be mistaken for a\n'''
method = '''  /// <summary>\n  /// Proves the off-screen MainForm lease does not return while the production\n  /// MainForm Shown event is still queued. A late Shown event can observe test\n  /// fixture fields installed immediately after lease construction and start an\n  /// asynchronous history preview that mutates retained speech history.\n  /// </summary>\n  private static void TestMainFormLeaseCompletesShownBeforeReturn()\n  {\n    using var lease = CreateOffscreenMainForm();\n    bool shownAfterLeaseReturned = false;\n    lease.Form.Shown += (_, _) => shownAfterLeaseReturned = true;\n\n    Application.DoEvents();\n\n    Require(\n      !shownAfterLeaseReturned,\n      "MainFormTestLease returned before the production MainForm Shown " +\n      "lifecycle completed.");\n  }\n\n'''
if method_anchor not in text:
  raise SystemExit('method anchor not found')
text = text.replace(method_anchor, method + method_anchor, 1)

path.write_text(text, encoding='utf-8')
