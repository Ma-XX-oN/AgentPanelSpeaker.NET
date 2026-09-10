from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

old = '''  private string BuildReplaceWindowScript(\n    TranscriptWindow window,\n    bool preserve,\n    int? anchorRecordNumber = null,\n    string? anchorSourceId = null,\n    double? anchorOffset = null,\n    int? focusVirtualIndex = null,\n    string? focusEdge = null,\n    string? structureProbeId = null,\n    TranscriptStructureSnapshot? expectedStructure = null,\n    WindowScriptBuildMetrics? metrics = null,\n    long? transactionId = null,\n    string? renderReason = null,\n    long? requestSequence = null)\n  {\n'''
new = '''  private string BuildReplaceWindowScript(\n    TranscriptWindow window,\n    bool preserve,\n    int? anchorRecordNumber = null,\n    string? anchorSourceId = null,\n    double? anchorOffset = null,\n    int? focusVirtualIndex = null,\n    string? focusEdge = null,\n    string? structureProbeId = null,\n    TranscriptStructureSnapshot? expectedStructure = null)\n  {\n    return BuildReplaceWindowScriptInstrumented(\n      window,\n      preserve,\n      anchorRecordNumber,\n      anchorSourceId,\n      anchorOffset,\n      focusVirtualIndex,\n      focusEdge,\n      structureProbeId,\n      expectedStructure,\n      metrics: null,\n      transactionId: null,\n      renderReason: null,\n      requestSequence: null);\n  }\n\n  private string BuildReplaceWindowScriptInstrumented(\n    TranscriptWindow window,\n    bool preserve,\n    int? anchorRecordNumber = null,\n    string? anchorSourceId = null,\n    double? anchorOffset = null,\n    int? focusVirtualIndex = null,\n    string? focusEdge = null,\n    string? structureProbeId = null,\n    TranscriptStructureSnapshot? expectedStructure = null,\n    WindowScriptBuildMetrics? metrics = null,\n    long? transactionId = null,\n    string? renderReason = null,\n    long? requestSequence = null)\n  {\n'''
if text.count(old) != 1:
  raise SystemExit(f'Expanded BuildReplaceWindowScript signature: expected 1 match, found {text.count(old)}')
text = text.replace(old, new, 1)

old_call = '''      string replacementScript = BuildReplaceWindowScript(\n        window,\n        preserve: false,\n        anchorRecordNumber: anchorRecordNumber,\n        anchorSourceId: anchorSourceId,\n        anchorOffset: anchorOffset,\n        focusVirtualIndex: focalIndex,\n        metrics: scriptMetrics,\n        transactionId: transactionId,\n        renderReason: reason,\n        requestSequence: requestSequence);\n'''
new_call = '''      string replacementScript = BuildReplaceWindowScriptInstrumented(\n        window,\n        preserve: false,\n        anchorRecordNumber: anchorRecordNumber,\n        anchorSourceId: anchorSourceId,\n        anchorOffset: anchorOffset,\n        focusVirtualIndex: focalIndex,\n        metrics: scriptMetrics,\n        transactionId: transactionId,\n        renderReason: reason,\n        requestSequence: requestSequence);\n'''
if text.count(old_call) != 1:
  raise SystemExit(f'Instrumented manual-window call: expected 1 match, found {text.count(old_call)}')
text = text.replace(old_call, new_call, 1)

path.write_text(text, encoding='utf-8')
