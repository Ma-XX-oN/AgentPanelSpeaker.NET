from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

replacements = [
  (
    """  private const int StartupStageCount = 3;\n""",
    """  private const int StartupCanonicalEndPercent = 89;\n  private const int StartupSearchPercent = 94;\n  private const int StartupRenderPercent = 99;\n"""
  ),
  (
    """  private int _activeRenderGeneration = -1;\n  private CancellationTokenSource? _renderCancellation;\n""",
    """  private int _activeRenderGeneration = -1;\n  private int _startupProgressPhase;\n  private int _startupProgressPercent;\n  private CancellationTokenSource? _renderCancellation;\n"""
  ),
  (
    """    if (force)\n    {\n      ShowStartupStage(1, \"Preparing canonical transcript…\");\n    }\n    IProgress<int>? startupProgress = force\n      ? new Progress<int>(stage =>\n      {\n        if (generation != _renderGeneration ||\n            !string.Equals(\n              path,\n              _sessionPath,\n              StringComparison.OrdinalIgnoreCase))\n        {\n          return;\n        }\n        if (stage == 2)\n        {\n          ShowStartupStage(2, \"Building transcript search index…\");\n        }\n      })\n      : null;\n""",
    """    if (force)\n    {\n      _startupProgressPhase = 0;\n      _startupProgressPercent = 0;\n      ShowStartupProgress(1, \"Preparing canonical transcript…\", 0);\n    }\n    IProgress<TranscriptBuildProgress>? startupRecordProgress = force\n      ? new Progress<TranscriptBuildProgress>(progress =>\n      {\n        if (generation != _renderGeneration ||\n            !string.Equals(\n              path,\n              _sessionPath,\n              StringComparison.OrdinalIgnoreCase))\n        {\n          return;\n        }\n        ShowStartupProgress(\n          1,\n          \"Preparing canonical transcript…\",\n          ScaleStartupProgress(\n            progress.Completed,\n            progress.Total,\n            0,\n            StartupCanonicalEndPercent));\n      })\n      : null;\n    IProgress<int>? startupPhaseProgress = force\n      ? new Progress<int>(phase =>\n      {\n        if (generation != _renderGeneration ||\n            !string.Equals(\n              path,\n              _sessionPath,\n              StringComparison.OrdinalIgnoreCase))\n        {\n          return;\n        }\n        if (phase == 2)\n        {\n          ShowStartupProgress(\n            2,\n            \"Building transcript search index…\",\n            StartupSearchPercent);\n        }\n      })\n      : null;\n"""
  ),
  (
    """          () => identities = TranscriptNodeIdentityMap.Build(\n            path,\n            source,\n            token,\n            includeRolledBackTurns),\n""",
    """          () => identities = TranscriptNodeIdentityMap.Build(\n            path,\n            source,\n            token,\n            includeRolledBackTurns,\n            startupRecordProgress),\n"""
  ),
  (
    """        startupProgress?.Report(2);\n""",
    """        startupPhaseProgress?.Report(2);\n"""
  ),
  (
    """      if (force)\n      {\n        ShowStartupStage(3, \"Rendering visible transcript…\");\n      }\n""",
    """      if (force)\n      {\n        ShowStartupProgress(\n          3,\n          \"Rendering visible transcript…\",\n          StartupRenderPercent);\n      }\n"""
  ),
  (
    """      _lastWriteUtc = info.LastWriteTimeUtc;\n      _lastLength = info.Length;\n      HideLoading();\n""",
    """      _lastWriteUtc = info.LastWriteTimeUtc;\n      _lastLength = info.Length;\n      if (force)\n      {\n        ShowStartupProgress(\n          3,\n          \"Rendering visible transcript…\",\n          100);\n      }\n      HideLoading();\n"""
  ),
  (
    """  private void ShowStartupStage(int stage, string description)\n  {\n    Debug.Assert(stage >= 1 && stage <= StartupStageCount);\n    Debug.Assert(!string.IsNullOrWhiteSpace(description));\n    string name = string.IsNullOrWhiteSpace(_sessionDisplayName)\n      ? Path.GetFileName(_sessionPath) ?? string.Empty\n      : _sessionDisplayName;\n    string text = description + Environment.NewLine +\n      $\"Stage {stage} of {StartupStageCount}\";\n    if (!string.IsNullOrWhiteSpace(name))\n    {\n      text += Environment.NewLine + name;\n    }\n    ShowLoading(text);\n  }\n""",
    """  private void ShowStartupProgress(\n    int phase,\n    string description,\n    int percentage)\n  {\n    Debug.Assert(phase >= 1 && phase <= 3);\n    Debug.Assert(!string.IsNullOrWhiteSpace(description));\n    if (phase < _startupProgressPhase)\n    {\n      return;\n    }\n\n    _startupProgressPhase = phase;\n    _startupProgressPercent = Math.Max(\n      _startupProgressPercent,\n      Math.Clamp(percentage, 0, 100));\n    string name = string.IsNullOrWhiteSpace(_sessionDisplayName)\n      ? Path.GetFileName(_sessionPath) ?? string.Empty\n      : _sessionDisplayName;\n    string text = description + Environment.NewLine +\n      $\"{_startupProgressPercent}%\";\n    if (!string.IsNullOrWhiteSpace(name))\n    {\n      text += Environment.NewLine + name;\n    }\n    ShowLoading(text);\n  }\n\n  private static int ScaleStartupProgress(\n    int completed,\n    int total,\n    int startPercentage,\n    int endPercentage)\n  {\n    if (total <= 0)\n    {\n      return startPercentage;\n    }\n    int boundedCompleted = Math.Clamp(completed, 0, total);\n    int span = Math.Max(0, endPercentage - startPercentage);\n    return startPercentage + (int)((long)span * boundedCompleted / total);\n  }\n"""
  )
]

for old, new in replacements:
  count = text.count(old)
  if count != 1:
    raise SystemExit(
      f"Expected exactly one match, found {count}: {old[:100]!r}")
  text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8", newline="")
