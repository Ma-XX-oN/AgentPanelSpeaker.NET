from pathlib import Path

path = Path('AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8-sig')

old = '      ExecuteVoidScript(webView, "resetRetainedPlayback();");\n'
new = ('      ExecuteVoidScript(\n'
       '        webView,\n'
       '        "resetRetainedPlayback(); retireCanonicalPlayback(false); " +\n'
       '        "requestedPlaybackWordId = 0;");\n')
if old in text:
  text = text.replace(old, new, 1)
elif 'requestedPlaybackWordId = 0;' not in text:
  raise SystemExit('Issue #57 browser-state isolation anchor was not found.')

old = '''    Message wheel = Message.Create(\n      form.Handle,\n      0x020A,\n      new IntPtr(120L << 16),\n      IntPtr.Zero);\n    _ = form.PreFilterMessage(ref wheel);\n    PumpMessages(150);\n'''
new = '''    Message wheel = Message.Create(\n      form.Handle,\n      0x020A,\n      new IntPtr(120L << 16),\n      IntPtr.Zero);\n    _ = form.PreFilterMessage(ref wheel);\n    // Restore the Follow setting so the test does not leave unsaved state, and\n    // unregister this synthetic form from the application message filter before\n    // disposal. The RED must come from missing diagnostics, not harness teardown.\n    Message restoreKeyDown = Message.Create(\n      form.Handle,\n      0x0100,\n      new IntPtr((int)Keys.Oemplus),\n      IntPtr.Zero);\n    _ = form.PreFilterMessage(ref restoreKeyDown);\n    Message restoreKeyUp = Message.Create(\n      form.Handle,\n      0x0101,\n      new IntPtr((int)Keys.Oemplus),\n      IntPtr.Zero);\n    _ = form.PreFilterMessage(ref restoreKeyUp);\n    Application.RemoveMessageFilter(form);\n    PumpMessages(150);\n'''
if old in text:
  text = text.replace(old, new, 1)
elif 'Application.RemoveMessageFilter(form);' not in text:
  raise SystemExit('Issue #77 cleanup anchor was not found.')

path.write_text(text, encoding='utf-8')
