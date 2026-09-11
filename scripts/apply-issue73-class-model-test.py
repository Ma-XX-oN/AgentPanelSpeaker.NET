from pathlib import Path

path = Path('AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''  window.dispatchEvent(new KeyboardEvent('keydown', {\n    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true\n  }));\n  const words = [...document.querySelectorAll('.word')].map(word => ({\n    text:word.textContent,\n    nodeId:Number(word.dataset.nodeId || 0),\n    nodeWordIndex:Number(word.dataset.nodeWordIndex ?? -1),\n    selectable:word.dataset.voiceSelectable === '1'\n  }));\n  return JSON.stringify({\n    mode:document.body.classList.contains('voice-pointer-select-mode'),\n    words\n  });\n'''
new = '''  const renderedWords = [...document.querySelectorAll('.word')];\n  const classesBeforeCtrl = renderedWords.map(word => word.className);\n  window.dispatchEvent(new KeyboardEvent('keydown', {\n    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true\n  }));\n  const classesAfterCtrl = renderedWords.map(word => word.className);\n  const words = renderedWords.map(word => ({\n    text:word.textContent,\n    nodeId:Number(word.dataset.nodeId || 0),\n    nodeWordIndex:Number(word.dataset.nodeWordIndex ?? -1),\n    selectable:word.classList.contains('voice-selectable'),\n    excluded:word.classList.contains('voice-excluded')\n  }));\n  return JSON.stringify({\n    mode:document.body.classList.contains('voice-pointer-select-mode'),\n    ctrlChangedWordClasses:JSON.stringify(classesBeforeCtrl) !==\n      JSON.stringify(classesAfterCtrl),\n    words\n  });\n'''
if new not in text:
  if old not in text:
    raise SystemExit('Initial browser class-model anchor not found.')
  text = text.replace(old, new, 1)

old = '''      Require(initial.GetProperty("mode").GetBoolean(),\n        "Holding Ctrl did not enable the voice-pointer selection affordance.");\n      JsonElement words = initial.GetProperty("words");\n'''
new = '''      Require(initial.GetProperty("mode").GetBoolean(),\n        "Holding Ctrl did not enable the voice-pointer selection affordance.");\n      Require(!initial.GetProperty("ctrlChangedWordClasses").GetBoolean(),\n        "Ctrl-down iterated/mutated individual word classes instead of only toggling page state.");\n      JsonElement words = initial.GetProperty("words");\n'''
if new not in text:
  if old not in text:
    raise SystemExit('Ctrl mutation assertion anchor not found.')
  text = text.replace(old, new, 1)

text = text.replace(
  'RequireWord(words[0], "alpha", 42, 0, selectable: true);',
  'RequireWord(words[0], "alpha", 42, 0, selectable: true, excluded: false);')
text = text.replace(
  'RequireWord(words[1], "beta", 42, 1, selectable: true);',
  'RequireWord(words[1], "beta", 42, 1, selectable: true, excluded: false);')
text = text.replace(
  'RequireWord(words[2], "gamma", 42, 2, selectable: false);',
  'RequireWord(words[2], "gamma", 42, 2, selectable: true, excluded: true);')

old = '''    .filter(word => word.dataset.voiceSelectable === '1')\n'''
new = '''    .filter(word => word.classList.contains('voice-selectable') &&\n      !word.classList.contains('voice-excluded'))\n'''
if new not in text:
  if old not in text:
    raise SystemExit('Replacement selector anchor not found.')
  text = text.replace(old, new, 1)

old = '''    int nodeWordIndex,\n    bool selectable)\n'''
new = '''    int nodeWordIndex,\n    bool selectable,\n    bool excluded)\n'''
if new not in text:
  if old not in text:
    raise SystemExit('RequireWord signature anchor not found.')
  text = text.replace(old, new, 1)

old = '''    Require(word.GetProperty("selectable").GetBoolean() == selectable,\n      $"Rendered word {text} has the wrong current speech eligibility.");\n'''
new = '''    Require(word.GetProperty("selectable").GetBoolean() == selectable,\n      $"Rendered word {text} has the wrong structural selectable class.");\n    Require(word.GetProperty("excluded").GetBoolean() == excluded,\n      $"Rendered word {text} has the wrong current exclusion class.");\n'''
if new not in text:
  if old not in text:
    raise SystemExit('RequireWord assertion anchor not found.')
  text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
