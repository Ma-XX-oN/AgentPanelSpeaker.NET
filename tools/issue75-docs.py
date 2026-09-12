from pathlib import Path

PATH = Path('DESIGN.md')
text = PATH.read_text(encoding='utf-8')

old_pin = '''The bridge is intentionally pinned to AIConversationCore commit `a6fd322aece692cd0c90bc89f11228b3a4e83520`; both the C# client and Node worker reject a mismatched core revision.\n'''
new_pin = '''The bridge is intentionally pinned to AIConversationCore commit `169814bf407ac3b5c9f3757b724df10aeddef5de`; the C# client, Node worker, bundled runtime, and integration workflow reject a mismatched core revision.\n'''
if text.count(old_pin) != 1:
  raise RuntimeError('Expected one current Core-pin paragraph to replace.')
text = text.replace(old_pin, new_pin)

anchor = new_pin + '\n'
section = '''## Canonical transcript word identity\n\nAIConversationCore owns transcript-word identity.  Every canonical transcript word has one positive numeric `word_id`; Core-rendered HTML exposes the same handle as `id="word-N"` on the owner and `data-word-id="N"` on any additional rendered pieces of that same canonical word.  `SpeechFragmentWord.Id`, `TranscriptPlaybackPosition.WordId`, Find/Ctrl+click seek requests, and browser playback therefore refer to the same immutable Core handle.  Visible text, browser token ordinals, `NodeId`, and reconstructed node-word indexes are not alternate identities for Core-backed transcript words.\n\nCore-backed playback highlights the exact `word-N` owner and all of its `data-word-id` pieces.  When that word is outside the materialized virtual window, `TranscriptView` does not search visible text or maintain a word-to-unit map.  The display projection retains the Core session that produced its HTML units, asks `AIConversationCoreClient.LocateRetainedWord()` for the requested `word_id`, receives Core's containing unit ID, resolves only that unit ID to the app-owned virtual-window index, and materializes the resulting window.  Core is therefore the sole semantic word-to-unit resolver; AgentPanelSpeaker owns only viewport/materialization policy.\n\nSpeech eligibility is policy over immutable Core identity, not identity mutation.  `SpeechService` publishes contiguous Core word-ID ranges that are currently seekable.  The browser retains those ranges centrally and updates one CSS/CSSOM policy surface for the Ctrl affordance instead of adding or removing eligibility classes on every word node.  Changing role, fence, revision, or other speech policy cannot renumber or rewrite canonical word identity.\n\n'''
if text.count(anchor) != 1:
  raise RuntimeError('Expected one Core-pin insertion anchor.')
text = text.replace(anchor, anchor + section)

PATH.write_text(text, encoding='utf-8', newline='\n')
