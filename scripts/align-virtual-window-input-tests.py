from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8-sig')

# TestBrowserWindowBehaviour deliberately invokes the C# virtual-window entry
# point after the user-selected scroll window has already been chosen. In the
# full application, the preceding genuine manual scroll has disabled Follow in
# both browser and host settings. Model that precondition explicitly here; the
# separate manual-scroll test exercises the physical wheel -> Follow-OFF path.
old = '''      PumpMessages(150);\n\n      Task shiftTask = InvokeTask(\n        view,\n        "RenderWindowForIndexAsync",\n        0,\n        "scroll-up",\n'''
new = '''      PumpMessages(150);\n\n      view.ApplySettings(\n        TranscriptSettings.Default with { FollowSpeech = false },\n        dark: false);\n      PumpMessages(500);\n\n      Task shiftTask = InvokeTask(\n        view,\n        "RenderWindowForIndexAsync",\n        0,\n        "scroll-up",\n'''
if old in text:
  text = text.replace(old, new, 1)
elif 'separate manual-scroll test exercises' not in text and \
     'TranscriptSettings.Default with { FollowSpeech = false }' not in text:
  raise SystemExit('Browser convergence Follow-OFF precondition anchor not found.')

# After the playback/programmatic-scroll guard expires, this phase is intended
# to represent an actual user wheel gesture at the upper edge. A naked
# scrollTo() is programmatic under the repaired contract and must not request a
# virtual-window shift.
old = '''      ExecuteVoidScript(webView, "window.scrollTo(0, 0);");\n'''
new = '''      ExecuteVoidScript(\n        webView,\n        """\n(() => {\n  window.dispatchEvent(new WheelEvent('wheel', {\n    deltaY: -160,\n    bubbles: true,\n    cancelable: true\n  }));\n  window.scrollTo(0, 0);\n})()\n""");\n'''
if old in text:
  text = text.replace(old, new, 1)
elif "deltaY: -160" not in text:
  raise SystemExit('Programmatic-scroll manual edge phase anchor not found.')

# The spacer-recovery scenario is likewise a fast real user scroll into the
# synthetic top spacer, so drive production's wheel listener before the scroll.
old = '''      ExecuteVoidScript(\n        webView,\n        "programmaticScrollUntil = 0; window.scrollTo(0, 0);");\n'''
new = '''      ExecuteVoidScript(\n        webView,\n        """\n(() => {\n  programmaticScrollUntil = 0;\n  window.dispatchEvent(new WheelEvent('wheel', {\n    deltaY: -160,\n    bubbles: true,\n    cancelable: true\n  }));\n  window.scrollTo(0, 0);\n})()\n""");\n'''
if old in text:
  text = text.replace(old, new, 1)
elif 'spacer-recovery scenario is likewise' not in text and \
     text.count("deltaY: -160") < 2:
  raise SystemExit('Spacer-recovery user-intent anchor not found.')

# Directional-prefetch assertions are explicitly about manual virtualization.
# Preserve the programmatic positioning phases above them, but precede each
# intended manual movement with a physical wheel event whose direction agrees
# with the movement being asserted.
old_up = '''        "programmaticScrollUntil = 0; window.scrollBy(0, -80);");\n'''
new_up = '''        """\n(() => {\n  programmaticScrollUntil = 0;\n  window.dispatchEvent(new WheelEvent('wheel', {\n    deltaY: -80,\n    bubbles: true,\n    cancelable: true\n  }));\n  window.scrollBy(0, -80);\n})()\n""");\n'''
count_up = text.count(old_up)
if count_up:
  if count_up != 2:
    raise SystemExit(f'Expected two upward directional-scroll anchors, got {count_up}.')
  text = text.replace(old_up, new_up)
elif text.count('deltaY: -80') < 2:
  raise SystemExit('Directional upward user-intent anchors not found.')

old_down = '''        "programmaticScrollUntil = 0; window.scrollBy(0, 160);");\n'''
new_down = '''        """\n(() => {\n  programmaticScrollUntil = 0;\n  window.dispatchEvent(new WheelEvent('wheel', {\n    deltaY: 160,\n    bubbles: true,\n    cancelable: true\n  }));\n  window.scrollBy(0, 160);\n})()\n""");\n'''
if old_down in text:
  text = text.replace(old_down, new_down, 1)
elif 'deltaY: 160' not in text:
  raise SystemExit('Directional downward user-intent anchor not found.')

path.write_text(text, encoding='utf-8')
