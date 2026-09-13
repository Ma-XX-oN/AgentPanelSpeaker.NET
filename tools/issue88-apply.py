#!/usr/bin/env python3
"""Apply the issue #88-#90 speech-ownership repair in deterministic stages."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
  return (ROOT / rel).read_text(encoding="utf-8")


def write(rel: str, text: str) -> None:
  (ROOT / rel).write_text(text, encoding="utf-8")


def replace_once(rel: str, old: str, new: str) -> None:
  text = read(rel)
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{rel}: expected one occurrence, found {count}: {old[:120]!r}")
  write(rel, text.replace(old, new, 1))


def replace_section(rel: str, start: str, end: str, replacement: str) -> None:
  text = read(rel)
  begin = text.find(start)
  if begin < 0:
    raise RuntimeError(f"{rel}: start marker not found: {start!r}")
  finish = text.find(end, begin)
  if finish < 0:
    raise RuntimeError(f"{rel}: end marker not found: {end!r}")
  write(rel, text[:begin] + replacement + text[finish:])


def wire_program() -> None:
  rel = "AgentPanelSpeaker/Program.cs"
  text = read(rel)
  if '"speech-ownership"' in text:
    return
  marker = '''      if (args.Length == 2 &&\n          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))\n'''
  block = '''      if (args.Length == 2 &&\n          string.Equals(\n            args[1],\n            "speech-ownership",\n            StringComparison.OrdinalIgnoreCase))\n      {\n        Environment.ExitCode = RunNamedSuite(\n          "speech-ownership",\n          Issue88SpeechOwnershipRegressionTestRunner.Run);\n        return;\n      }\n\n'''
  if marker not in text:
    raise RuntimeError("Program.cs: named-suite insertion point not found")
  text = text.replace(marker, block + marker, 1)
  marker2 = '''      int rewindCurrentFragment = RunIsolatedTestSuite(\n        "rewind-current-fragment");\n'''
  replacement2 = marker2 + '''      int speechOwnership = RunIsolatedTestSuite(\n        "speech-ownership");\n'''
  if marker2 not in text:
    raise RuntimeError("Program.cs: all-suite declaration insertion point not found")
  text = text.replace(marker2, replacement2, 1)
  marker3 = '''                             coreWordIdMigration == 0 &&\n                             rewindCurrentFragment == 0\n'''
  replacement3 = '''                             coreWordIdMigration == 0 &&\n                             rewindCurrentFragment == 0 &&\n                             speechOwnership == 0\n'''
  if marker3 not in text:
    raise RuntimeError("Program.cs: all-suite result insertion point not found")
  text = text.replace(marker3, replacement3, 1)
  write(rel, text)


def patch_data_contracts() -> None:
  replace_once(
    "AgentPanelSpeaker/SpeechFragment.cs",
    "  IReadOnlyList<SpeechFragmentWord>? TranscriptWords = null)\n",
    "  IReadOnlyList<SpeechFragmentWord>? TranscriptWords = null,\n  long FragmentId = -1)\n")

  replace_once(
    "AgentPanelSpeaker/SpeechWordBoundary.cs",
    "  string Text,\n  bool Exact);\n",
    "  string Text,\n  bool Exact,\n  int WordCount = 1);\n")

  replace_once(
    "AgentPanelSpeaker/SpeechMarkup.cs",
    '''internal sealed record SpeechMarkup(\n  string PlainText,\n  string SapiXml,\n  string SsmlContent);\n''',
    '''internal sealed record SpeechMarkupWord(\n  int WordIndex,\n  string Text,\n  int CharacterStart,\n  int CharacterLength);\n\n/// <summary>\n/// Carries source text plus equivalent speech markup and, when available,\n/// exact fragment-relative ownership for each canonical transcript word.\n/// </summary>\ninternal sealed record SpeechMarkup(\n  string PlainText,\n  string SapiXml,\n  string SsmlContent,\n  IReadOnlyList<SpeechMarkupWord>? Words = null);\n''')

  replace_once(
    "AgentPanelSpeaker/SpeechPlaybackBuffer.cs",
    '''internal sealed record SpeechPlaybackBuffer(\n  PcmWaveData Wave,\n  IReadOnlyList<SpeechWordBoundary> WordBoundaries);\n''',
    '''internal sealed record SpeechTrackingDegradation(\n  string Backend,\n  string VoiceName,\n  string Reason);\n\n/// <summary>\n/// Carries one PCM buffer, exact word boundaries, and an explicit reason when\n/// playback must degrade to whole-fragment highlighting.\n/// </summary>\ninternal sealed record SpeechPlaybackBuffer(\n  PcmWaveData Wave,\n  IReadOnlyList<SpeechWordBoundary> WordBoundaries,\n  SpeechTrackingDegradation? TrackingDegradation = null);\n''')

  replace_once(
    "AgentPanelSpeaker/TranscriptPlaybackPosition.cs",
    '''  long BoundaryTimestamp,\n  long? WordId = null);\n\ninternal enum TranscriptPlaybackState\n''',
    '''  long BoundaryTimestamp,\n  long? WordId = null,\n  long? FragmentId = null,\n  IReadOnlyList<long>? WordIds = null,\n  TranscriptPlaybackHighlightMode HighlightMode =\n    TranscriptPlaybackHighlightMode.Word);\n\ninternal enum TranscriptPlaybackHighlightMode\n{\n  Word,\n  Fragment\n}\n\ninternal enum TranscriptPlaybackState\n''')


def patch_fragment_ids() -> None:
  rel = "AgentPanelSpeaker/JsonlSessionMonitor.cs"
  text = read(rel)
  text = text.replace(
    "    long nextNodeId = 1;\n    var recentFingerprintQueue",
    "    long nextNodeId = 1;\n    long nextFragmentId = 0;\n    var recentFingerprintQueue",
    1)
  text = text.replace(
    "      ref nextNodeId,\n      recentFingerprintQueue,",
    "      ref nextNodeId,\n      ref nextFragmentId,\n      recentFingerprintQueue,",
    1)
  run_anchor = "    long nextNodeId = 1;\n    DateTime nextLatestRefreshUtc"
  if run_anchor not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: Run fragment counter anchor missing")
  text = text.replace(
    run_anchor,
    "    long nextNodeId = 1;\n    long nextFragmentId = 0;\n    DateTime nextLatestRefreshUtc",
    1)
  preindexed = '''        nextNodeId = preindexedHistory.Fragments.Count == 0\n          ? 1\n          : preindexedHistory.Fragments.Max(fragment => fragment.NodeId) + 1;\n'''
  if preindexed not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: preindexed anchor missing")
  text = text.replace(
    preindexed,
    preindexed + '''        nextFragmentId = preindexedHistory.Fragments.Count == 0\n          ? 0\n          : preindexedHistory.Fragments.Max(fragment => fragment.FragmentId) + 1;\n''',
    1)
  # Initial history call in Run.
  needle = '''          ref nextNodeId,\n          recentFingerprintQueue,\n          recentFingerprintSet,\n          preview,\n          pendingInputRequests,\n          settings.IncludeRolledBackTurns,\n          settings.IncludeUserContext);\n'''
  if needle not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: initial history call missing")
  text = text.replace(
    needle,
    needle.replace("          recentFingerprintQueue,", "          ref nextFragmentId,\n          recentFingerprintQueue,"),
    1)
  # Session switch resets the conversation-global fragment sequence.
  switch_anchor = '''            pendingInputRequests.Clear();\n            SpeechHistorySnapshot switchedHistory = LoadExistingHistory(\n'''
  if switch_anchor not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: switch anchor missing")
  text = text.replace(
    switch_anchor,
    '''            pendingInputRequests.Clear();\n            nextFragmentId = 0;\n            SpeechHistorySnapshot switchedHistory = LoadExistingHistory(\n''',
    1)
  switch_call = '''              ref nextNodeId,\n              recentFingerprintQueue,\n'''
  if switch_call not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: switched history call missing")
  text = text.replace(
    switch_call,
    '''              ref nextNodeId,\n              ref nextFragmentId,\n              recentFingerprintQueue,\n''',
    1)
  live_call = '''            line,\n            ref nextNodeId,\n            recentFingerprintQueue,\n'''
  if live_call not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: ProcessLine call missing")
  text = text.replace(
    live_call,
    '''            line,\n            ref nextNodeId,\n            ref nextFragmentId,\n            recentFingerprintQueue,\n''',
    1)
  line_signature = '''    string line,\n    ref long nextNodeId,\n    Queue<string> recentFingerprintQueue,\n'''
  if line_signature not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: ProcessLine signature missing")
  text = text.replace(
    line_signature,
    '''    string line,\n    ref long nextNodeId,\n    ref long nextFragmentId,\n    Queue<string> recentFingerprintQueue,\n''',
    1)
  node_call = '''          node,\n          ref nextNodeId,\n          recentFingerprintQueue,\n'''
  if node_call not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: ProcessNode live call missing")
  text = text.replace(
    node_call,
    '''          node,\n          ref nextNodeId,\n          ref nextFragmentId,\n          recentFingerprintQueue,\n''',
    1)
  node_signature = '''    ExtractedNode node,\n    ref long nextNodeId,\n    Queue<string> recentFingerprintQueue,\n'''
  if node_signature not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: ProcessNode signature missing")
  text = text.replace(
    node_signature,
    '''    ExtractedNode node,\n    ref long nextNodeId,\n    ref long nextFragmentId,\n    Queue<string> recentFingerprintQueue,\n''',
    1)
  assign_anchor = '''    DiagnosticLog.Write("jsonl.node_accepted", new\n'''
  if assign_anchor not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: fragment assignment anchor missing")
  text = text.replace(
    assign_anchor,
    '''    for (int fragmentIndex = 0; fragmentIndex < fragments.Count; ++fragmentIndex)\n    {\n      fragments[fragmentIndex] = fragments[fragmentIndex] with\n      {\n        FragmentId = nextFragmentId++\n      };\n    }\n\n''' + assign_anchor,
    1)
  history_signature = '''    bool speakExistingLatestTurn,\n    ref long nextNodeId,\n    Queue<string> recentFingerprintQueue,\n'''
  if history_signature not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: LoadExistingHistory signature missing")
  text = text.replace(
    history_signature,
    '''    bool speakExistingLatestTurn,\n    ref long nextNodeId,\n    ref long nextFragmentId,\n    Queue<string> recentFingerprintQueue,\n''',
    1)
  history_node_call = '''        node,\n        ref nextNodeId,\n        recentFingerprintQueue,\n'''
  if history_node_call not in text:
    raise RuntimeError("JsonlSessionMonitor.cs: history ProcessNode call missing")
  text = text.replace(
    history_node_call,
    '''        node,\n        ref nextNodeId,\n        ref nextFragmentId,\n        recentFingerprintQueue,\n''',
    1)
  write(rel, text)


def patch_sapi_engine() -> None:
  rel = "AgentPanelSpeaker/SapiSpeechEngine.cs"
  text = read(rel)
  text = text.replace(
    '''  public event Action<SpeechWordBoundary>? WordBoundary;\n''',
    '''  public event Action<SpeechWordBoundary>? WordBoundary;\n\n  /// <summary>\n  /// Raised when audio can be synthesized but exact word ownership is not\n  /// available. The caller must highlight the complete speech fragment.\n  /// </summary>\n  public event Action<SpeechTrackingDegradation>? WordTrackingUnavailable;\n''',
    1)
  old = '''        player = new WaveOutPlayer(speechBuffer.Wave);\n        wordBoundaries = speechBuffer.WordBoundaries;\n'''
  new = '''        if (speechBuffer.TrackingDegradation is not null)\n        {\n          RaiseWordTrackingUnavailable(speechBuffer.TrackingDegradation);\n        }\n        player = new WaveOutPlayer(speechBuffer.Wave);\n        wordBoundaries = speechBuffer.WordBoundaries;\n'''
  if old not in text:
    raise RuntimeError("SapiSpeechEngine.cs: speech command anchor missing")
  text = text.replace(old, new, 1)

  old = '''    var boundaries = new List<SpeechWordBoundary>();\n    PcmWaveData? outputFormat = null;\n'''
  new = '''    var boundaries = new List<SpeechWordBoundary>();\n    SpeechTrackingDegradation? trackingDegradation = null;\n    PcmWaveData? outputFormat = null;\n'''
  if old not in text:
    raise RuntimeError("SapiSpeechEngine.cs: playback buffer anchor missing")
  text = text.replace(old, new, 1)
  old = '''      PcmWaveData converted = rendered.Wave.ConvertToMono16(OutputSampleRate);\n      outputFormat ??= converted;\n'''
  new = '''      PcmWaveData converted = rendered.Wave.ConvertToMono16(OutputSampleRate);\n      outputFormat ??= converted;\n      trackingDegradation ??= rendered.TrackingDegradation;\n'''
  if old not in text:
    raise RuntimeError("SapiSpeechEngine.cs: rendered segment anchor missing")
  text = text.replace(old, new, 1)
  old = '''    return new SpeechPlaybackBuffer(\n      playback,\n      boundaries.Select(boundary => boundary with\n      {\n        AudioPosition = wakeOffset + boundary.AudioPosition\n      }).ToArray());\n'''
  new = '''    return new SpeechPlaybackBuffer(\n      playback,\n      boundaries.Select(boundary => boundary with\n      {\n        AudioPosition = wakeOffset + boundary.AudioPosition\n      }).ToArray(),\n      trackingDegradation);\n'''
  if old not in text:
    raise RuntimeError("SapiSpeechEngine.cs: SpeechPlaybackBuffer return missing")
  text = text.replace(old, new, 1)
  write(rel, text)

  render_start = '''  private RenderedSpeechSegment RenderSpeech(\n'''
  render_end = '''  /// <summary>\n  /// Renders one preview segment and applies its explicit unsupported-IPA\n'''
  render_replacement = '''  private RenderedSpeechSegment RenderSpeech(\n    SpeechMarkup markup,\n    SpeechProfileSettings profile,\n    VoiceBackend backend,\n    object? voiceObject,\n    object? voicesObject,\n    SystemSpeechSynthesizer synthesizer,\n    WinRtSpeechSynthesizer? windowsMediaSynthesizer)\n  {\n    IReadOnlyList<SpeechWordBoundary> boundaries;\n    SpeechTrackingDegradation? trackingDegradation = null;\n    PcmWaveData wave;\n    switch (backend.Backend)\n    {\n      case SpeechBackend.Sapi:\n        wave = RenderSapiSpeech(\n          markup,\n          profile,\n          backend.SapiIndex,\n          voiceObject,\n          voicesObject);\n        boundaries = Array.Empty<SpeechWordBoundary>();\n        trackingDegradation = new SpeechTrackingDegradation(\n          "Sapi",\n          profile.VoiceName,\n          "native_sapi_has_no_word_timing");\n        break;\n\n      case SpeechBackend.SystemSpeech:\n        wave = RenderSystemSpeech(\n          markup,\n          profile,\n          backend.ProviderVoiceId,\n          synthesizer,\n          out boundaries,\n          out trackingDegradation);\n        break;\n\n      case SpeechBackend.WindowsMedia:\n        wave = RenderWindowsMediaSpeech(\n          markup,\n          profile,\n          backend.ProviderVoiceId,\n          windowsMediaSynthesizer ?? throw new InvalidOperationException(\n            "The Windows.Media speech backend is unavailable."),\n          (WindowsMediaBookmarkMode)Volatile.Read(\n            ref _windowsMediaBookmarkMode),\n          out boundaries,\n          out trackingDegradation);\n        break;\n\n      default:\n        throw new InvalidOperationException(\n          "The selected voice has no speech backend.");\n    }\n    return new RenderedSpeechSegment(\n      wave,\n      boundaries,\n      trackingDegradation);\n  }\n\n'''
  replace_section(rel, render_start, render_end, render_replacement)

  system_start = '''  private static PcmWaveData RenderSystemSpeech(\n'''
  system_end = '''\n\n  /// <summary>\n  /// Maps a System.Speech source range to the intersecting display token.\n'''
  system_replacement = '''  private static PcmWaveData RenderSystemSpeech(\n    SpeechMarkup markup,\n    SpeechProfileSettings profile,\n    string providerVoiceId,\n    SystemSpeechSynthesizer synthesizer,\n    out IReadOnlyList<SpeechWordBoundary> boundaries,\n    out SpeechTrackingDegradation? trackingDegradation)\n  {\n    using var stream = new MemoryStream();\n    var collected = new List<SpeechWordBoundary>();\n    MatchCollection sourceTokens = SpeechTokenization.Matches(markup.PlainText);\n    int? synthesisCharacterOffset = null;\n    int previousTokenIndex = -1;\n    bool mappingFailed = false;\n    EventHandler<System.Speech.Synthesis.SpeakProgressEventArgs> handler =\n      (_, eventArgs) =>\n      {\n        int firstSourceTokenStart = sourceTokens.Count == 0\n          ? 0\n          : sourceTokens[0].Index;\n        synthesisCharacterOffset ??=\n          eventArgs.CharacterPosition - firstSourceTokenStart;\n        int offsetSourcePosition = Math.Clamp(\n          eventArgs.CharacterPosition - synthesisCharacterOffset.Value,\n          0,\n          markup.PlainText.Length);\n        int offsetSourceCount = Math.Clamp(\n          eventArgs.CharacterCount,\n          0,\n          markup.PlainText.Length - offsetSourcePosition);\n        int tokenIndex = FindSequentialTokenIndex(\n          sourceTokens,\n          previousTokenIndex,\n          eventArgs.Text,\n          offsetSourcePosition,\n          offsetSourceCount);\n        if (tokenIndex < 0)\n        {\n          mappingFailed = true;\n          DiagnosticLog.Write("sapi.speak_progress_unmapped", new\n          {\n            provider = "System.Speech",\n            voice = providerVoiceId,\n            eventArgs.Text,\n            eventArgs.CharacterPosition,\n            eventArgs.CharacterCount,\n            eventArgs.AudioPosition\n          });\n          return;\n        }\n\n        previousTokenIndex = tokenIndex;\n        Match sourceToken = sourceTokens[tokenIndex];\n        DiagnosticLog.Write("sapi.speak_progress", new\n        {\n          provider = "System.Speech",\n          voice = providerVoiceId,\n          markup.PlainText,\n          eventArgs.Text,\n          eventArgs.CharacterPosition,\n          eventArgs.CharacterCount,\n          eventArgs.AudioPosition,\n          synthesisCharacterOffset,\n          sourcePosition = sourceToken.Index,\n          sourceCount = sourceToken.Length,\n          tokenIndex,\n          sourceToken = sourceToken.Value\n        });\n        collected.Add(new SpeechWordBoundary(\n          eventArgs.AudioPosition,\n          tokenIndex,\n          sourceToken.Index,\n          sourceToken.Length,\n          eventArgs.Text,\n          Exact: true));\n      };\n    synthesizer.SelectVoice(providerVoiceId);\n    synthesizer.Rate = profile.Rate;\n    synthesizer.Volume = profile.Volume;\n    string ssml = BuildSsmlDocument(\n      markup.SsmlContent,\n      synthesizer.Voice.Culture.Name);\n    synthesizer.SpeakProgress += handler;\n    try\n    {\n      var outputFormat = new SpeechAudioFormatInfo(\n        SystemSpeechSampleRate,\n        AudioBitsPerSample.Sixteen,\n        AudioChannel.Mono);\n      synthesizer.SetOutputToAudioStream(stream, outputFormat);\n      synthesizer.SpeakSsml(ssml);\n    }\n    finally\n    {\n      synthesizer.SpeakProgress -= handler;\n      synthesizer.SetOutputToNull();\n    }\n    PcmWaveData wave = PcmWaveData.FromPcmSamples(\n      channels: 1,\n      sampleRate: SystemSpeechSampleRate,\n      bitsPerSample: 16,\n      samples: stream.ToArray());\n\n    int expectedTokenCount = sourceTokens.Count;\n    int mappedTokenCount = collected\n      .Select(boundary => boundary.WordIndex)\n      .Distinct()\n      .Count();\n    if (expectedTokenCount == 0)\n    {\n      boundaries = Array.Empty<SpeechWordBoundary>();\n      trackingDegradation = null;\n    }\n    else if (collected.Count == 0)\n    {\n      boundaries = Array.Empty<SpeechWordBoundary>();\n      trackingDegradation = new SpeechTrackingDegradation(\n        "SystemSpeech",\n        providerVoiceId,\n        "system_speech_returned_no_progress_events");\n    }\n    else if (mappingFailed || mappedTokenCount != expectedTokenCount)\n    {\n      boundaries = Array.Empty<SpeechWordBoundary>();\n      trackingDegradation = new SpeechTrackingDegradation(\n        "SystemSpeech",\n        providerVoiceId,\n        mappingFailed\n          ? "system_speech_word_mapping_failed"\n          : "system_speech_incomplete_word_mapping");\n    }\n    else\n    {\n      boundaries = collected;\n      trackingDegradation = null;\n    }\n    return wave;\n  }\n'''
  replace_section(rel, system_start, system_end, system_replacement)

  # Eliminate the sequential guess after a failed exact text/range mapping.
  replace_once(
    rel,
    '''    if (fallback > previousTokenIndex)\n    {\n      return fallback;\n    }\n    return start < tokens.Count ? start : -1;\n''',
    '''    return fallback > previousTokenIndex ? fallback : -1;\n''')
  replace_once(
    rel,
    '''    for (int index = 0; index < tokens.Count; ++index)\n    {\n      if (tokens[index].Index >= characterPosition)\n      {\n        return index;\n      }\n    }\n\n    return tokens.Count - 1;\n''',
    '''    return -1;\n''')

  windows_start = '''  private static PcmWaveData RenderWindowsMediaSpeech(\n'''
  windows_end = '''  /// <summary>\n  /// Converts Windows.Media SpeechWord cues into exact display-token ranges.\n'''
  windows_replacement = '''  private static PcmWaveData RenderWindowsMediaSpeech(\n    SpeechMarkup markup,\n    SpeechProfileSettings profile,\n    string providerVoiceId,\n    WinRtSpeechSynthesizer synthesizer,\n    WindowsMediaBookmarkMode bookmarkMode,\n    out IReadOnlyList<SpeechWordBoundary> boundaries,\n    out SpeechTrackingDegradation? trackingDegradation)\n  {\n    VoiceInformation voice = WinRtSpeechSynthesizer.AllVoices\n      .FirstOrDefault(candidate => string.Equals(\n        candidate.Id,\n        providerVoiceId,\n        StringComparison.OrdinalIgnoreCase)) ??\n      throw new InvalidOperationException(\n        "The selected Windows.Media voice is unavailable.");\n\n    synthesizer.Voice = voice;\n    synthesizer.Options.IncludeWordBoundaryMetadata = true;\n    synthesizer.Options.IncludeSentenceBoundaryMetadata = true;\n    synthesizer.Options.SpeakingRate = Math.Pow(2.0, profile.Rate / 10.0);\n    synthesizer.Options.AudioPitch = 1.0;\n    synthesizer.Options.AudioVolume = profile.Volume / 100.0;\n\n    bool requestBookmarks = bookmarkMode != WindowsMediaBookmarkMode.Off;\n    string bookmarkedSsml = string.Empty;\n    bool bookmarkedSsmlBuilt = requestBookmarks && TryBuildBookmarkedSsml(\n      markup,\n      voice.Language,\n      out bookmarkedSsml);\n    string degradationReason = requestBookmarks\n      ? bookmarkedSsmlBuilt\n        ? string.Empty\n        : "windows_media_bookmark_build_failed"\n      : "windows_media_bookmarks_disabled";\n    string ssml = bookmarkedSsmlBuilt\n      ? bookmarkedSsml\n      : BuildSsmlDocument(markup.SsmlContent, voice.Language);\n    bool retriedWithoutBookmarks = false;\n\n    SpeechSynthesisStream stream;\n    try\n    {\n      stream = synthesizer\n        .SynthesizeSsmlToStreamAsync(ssml)\n        .AsTask()\n        .GetAwaiter()\n        .GetResult();\n    }\n    catch (Exception exception) when (\n      exception is FormatException or COMException or ArgumentException)\n    {\n      DiagnosticLog.Write("speech.windows_media_ssml_rejected", new\n      {\n        voice = voice.DisplayName,\n        voice.Language,\n        bookmarkMode = bookmarkMode.ToString(),\n        bookmarkedSsmlBuilt,\n        ssml,\n        hresult = exception.HResult,\n        exception = exception.ToString()\n      });\n\n      if (!bookmarkedSsmlBuilt)\n      {\n        throw;\n      }\n\n      string fallbackSsml = BuildSsmlDocument(\n        markup.SsmlContent,\n        voice.Language);\n      DiagnosticLog.Write("speech.windows_media_ssml_retry_without_bookmarks", new\n      {\n        voice = voice.DisplayName,\n        voice.Language,\n        bookmarkMode = bookmarkMode.ToString(),\n        fallbackSsml\n      });\n      try\n      {\n        stream = synthesizer\n          .SynthesizeSsmlToStreamAsync(fallbackSsml)\n          .AsTask()\n          .GetAwaiter()\n          .GetResult();\n        retriedWithoutBookmarks = true;\n        degradationReason = "windows_media_bookmark_ssml_rejected";\n      }\n      catch (Exception fallbackException)\n      {\n        DiagnosticLog.Write("speech.windows_media_ssml_retry_failed", new\n        {\n          voice = voice.DisplayName,\n          voice.Language,\n          fallbackSsml,\n          hresult = fallbackException.HResult,\n          exception = fallbackException.ToString()\n        });\n        throw;\n      }\n    }\n\n    using (stream)\n    {\n      int byteCount = checked((int)stream.Size);\n      uint size = checked((uint)byteCount);\n      using IInputStream input = stream.GetInputStreamAt(0);\n      using var reader = new DataReader(input);\n      uint loaded = reader.LoadAsync(size)\n        .AsTask()\n        .GetAwaiter()\n        .GetResult();\n      if (loaded != size)\n      {\n        throw new InvalidDataException(\n          $"The Windows.Media speech stream ended after {loaded} of " +\n          $"{size} bytes.");\n      }\n\n      var bytes = new byte[byteCount];\n      reader.ReadBytes(bytes);\n      PcmWaveData wave = PcmWaveData.Parse(bytes);\n      if (bookmarkedSsmlBuilt && !retriedWithoutBookmarks &&\n          TryCreateWindowsMediaBookmarkBoundaries(\n            markup,\n            stream,\n            out IReadOnlyList<SpeechWordBoundary> bookmarkBoundaries,\n            out string bookmarkFailureReason))\n      {\n        boundaries = bookmarkBoundaries;\n        trackingDegradation = null;\n        DiagnosticLog.Write("speech.windows_media_bookmarks_used", new\n        {\n          voice = voice.DisplayName,\n          mode = bookmarkMode.ToString(),\n          boundaryCount = boundaries.Count\n        });\n      }\n      else\n      {\n        boundaries = Array.Empty<SpeechWordBoundary>();\n        if (degradationReason.Length == 0)\n        {\n          degradationReason = bookmarkFailureReason.Length == 0\n            ? "windows_media_bookmark_metadata_unavailable"\n            : bookmarkFailureReason;\n        }\n        trackingDegradation = new SpeechTrackingDegradation(\n          "WindowsMedia",\n          voice.DisplayName,\n          degradationReason);\n        DiagnosticLog.Write("speech.word_tracking_unavailable", new\n        {\n          backend = "WindowsMedia",\n          voice = voice.DisplayName,\n          reason = degradationReason,\n          highlightMode = "fragment"\n        });\n      }\n      return wave;\n    }\n  }\n\n'''
  replace_section(rel, windows_start, windows_end, windows_replacement)

  # Remove the complete reverse-inference SpeechWord path and the approximate
  # generator. Bookmarks are the only Windows.Media per-word provenance source.
  reverse_start = '''  /// <summary>\n  /// Converts Windows.Media SpeechWord cues into exact display-token ranges.\n'''
  reverse_end = '''  private static void ConfigureSapiVoice(\n'''
  text = read(rel)
  begin = text.find(reverse_start)
  finish = text.find(reverse_end, begin)
  if begin < 0 or finish < 0:
    raise RuntimeError("SapiSpeechEngine.cs: reverse mapping section not found")
  text = text[:begin] + text[finish:]
  write(rel, text)

  # Replace bookmark construction with a provenance-preserving implementation
  # that can put a mark at the start of a token even when that token spans
  # several SSML text nodes (spell-out, phoneme/substitution markup, breaks).
  bookmark_start = '''  private static bool TryBuildBookmarkedSsml(\n'''
  bookmark_end = '''  /// <summary>\n  /// Converts SpeechBookmark cues to display-token boundaries and removes\n'''
  bookmark_replacement = '''  private static bool TryBuildBookmarkedSsml(\n    SpeechMarkup markup,\n    string cultureName,\n    out string ssml)\n  {\n    try\n    {\n      XDocument document = XDocument.Parse(\n        BuildSsmlDocument(markup.SsmlContent, cultureName),\n        LoadOptions.PreserveWhitespace);\n      XNamespace ns = document.Root?.Name.Namespace ??\n        "http://www.w3.org/2001/10/synthesis";\n      IReadOnlyList<SpeechMarkupWord> words = GetMarkupWords(markup);\n      for (int wordIndex = words.Count - 1; wordIndex >= 0; --wordIndex)\n      {\n        SpeechMarkupWord word = words[wordIndex];\n        List<XText> textNodes = document\n          .DescendantNodes()\n          .OfType<XText>()\n          .ToList();\n        int[] nodeStarts = new int[textNodes.Count];\n        int running = 0;\n        for (int index = 0; index < textNodes.Count; ++index)\n        {\n          nodeStarts[index] = running;\n          running += textNodes[index].Value.Length;\n        }\n        string visibleText = string.Concat(textNodes.Select(node => node.Value));\n        int position = word.CharacterStart;\n        if (position < 0 || position + word.CharacterLength > visibleText.Length ||\n            !string.Equals(\n              visibleText.Substring(position, word.CharacterLength),\n              word.Text,\n              StringComparison.Ordinal))\n        {\n          DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new\n          {\n            reason = "Canonical word range was not preserved in generated SSML text.",\n            wordIndex,\n            word.Text,\n            word.CharacterStart,\n            word.CharacterLength\n          });\n          ssml = string.Empty;\n          return false;\n        }\n\n        int nodeIndex = -1;\n        for (int candidate = 0; candidate < textNodes.Count; ++candidate)\n        {\n          int nodeEnd = nodeStarts[candidate] + textNodes[candidate].Value.Length;\n          if (nodeStarts[candidate] <= position && position < nodeEnd)\n          {\n            nodeIndex = candidate;\n            break;\n          }\n        }\n        if (nodeIndex < 0)\n        {\n          DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new\n          {\n            reason = "Canonical word start did not fall inside an SSML text node.",\n            wordIndex,\n            word.Text,\n            word.CharacterStart\n          });\n          ssml = string.Empty;\n          return false;\n        }\n\n        XText node = textNodes[nodeIndex];\n        int local = position - nodeStarts[nodeIndex];\n        int available = node.Value.Length - local;\n        bool wholeWordInNode = available >= word.CharacterLength &&\n          string.Equals(\n            node.Value.Substring(local, word.CharacterLength),\n            word.Text,\n            StringComparison.Ordinal);\n        string synthesisText = GetBookmarkedSynthesisText(markup, words, wordIndex);\n        string prefix = node.Value[..local];\n        string suffix;\n        string spoken;\n        if (wholeWordInNode &&\n            !string.Equals(synthesisText, word.Text, StringComparison.Ordinal))\n        {\n          suffix = node.Value[(local + word.CharacterLength)..];\n          spoken = synthesisText;\n        }\n        else\n        {\n          suffix = node.Value[local..];\n          spoken = string.Empty;\n        }\n\n        var replacement = new List<object>();\n        if (prefix.Length != 0)\n        {\n          replacement.Add(new XText(prefix));\n        }\n        if (wordIndex != 0)\n        {\n          replacement.Add(new XElement(\n            ns + "mark",\n            new XAttribute("name", $"aps_{word.WordIndex}")));\n        }\n        if (spoken.Length != 0)\n        {\n          replacement.Add(new XText(spoken));\n        }\n        if (suffix.Length != 0)\n        {\n          replacement.Add(new XText(suffix));\n        }\n        node.ReplaceWith(replacement);\n      }\n\n      ssml = document.ToString(SaveOptions.DisableFormatting);\n      return true;\n    }\n    catch (Exception exception) when (\n      exception is System.Xml.XmlException or InvalidOperationException or\n      ArgumentOutOfRangeException)\n    {\n      DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new\n      {\n        exception = exception.ToString()\n      });\n      ssml = string.Empty;\n      return false;\n    }\n  }\n\n  private static IReadOnlyList<SpeechMarkupWord> GetMarkupWords(\n    SpeechMarkup markup)\n  {\n    if (markup.Words is { Count: > 0 } exactWords)\n    {\n      return exactWords;\n    }\n    MatchCollection tokens = SpeechTokenization.Matches(markup.PlainText);\n    return tokens\n      .Cast<Match>()\n      .Select((token, index) => new SpeechMarkupWord(\n        index,\n        token.Value,\n        token.Index,\n        token.Length))\n      .ToArray();\n  }\n\n  private static string GetBookmarkedSynthesisText(\n    SpeechMarkup markup,\n    IReadOnlyList<SpeechMarkupWord> words,\n    int index)\n  {\n    SpeechMarkupWord word = words[index];\n    if (IsLeadingDecimal(word.Text))\n    {\n      return "point " + word.Text[1..];\n    }\n    bool attachedPeriod = word.Text == "." &&\n      index + 1 < words.Count &&\n      word.CharacterStart + word.CharacterLength ==\n        words[index + 1].CharacterStart &&\n      words[index + 1].Text.Length != 0 &&\n      IsWordCharacter(words[index + 1].Text[0]);\n    return attachedPeriod ? "dot" : word.Text;\n  }\n\n'''
  replace_section(rel, bookmark_start, bookmark_end, bookmark_replacement)

  # Replace bookmark cue compaction: equal-time adjacent bookmarks represent
  # one sound owning several textual words, so preserve them as one range.
  create_start = '''  private static bool TryCreateWindowsMediaBookmarkBoundaries(\n'''
  create_end = '''  private static string BuildSsmlDocument(\n'''
  create_replacement = '''  private static bool TryCreateWindowsMediaBookmarkBoundaries(\n    SpeechMarkup markup,\n    SpeechSynthesisStream stream,\n    out IReadOnlyList<SpeechWordBoundary> boundaries,\n    out string failureReason)\n  {\n    IReadOnlyList<SpeechMarkupWord> words = GetMarkupWords(markup);\n    TimedMetadataTrack? track = stream.TimedMetadataTracks\n      .FirstOrDefault(candidate => string.Equals(\n        candidate.Label,\n        "SpeechBookmark",\n        StringComparison.OrdinalIgnoreCase));\n    if (track is null)\n    {\n      boundaries = Array.Empty<SpeechWordBoundary>();\n      failureReason = "windows_media_missing_speechbookmark_track";\n      return false;\n    }\n    if (words.Count == 0)\n    {\n      boundaries = Array.Empty<SpeechWordBoundary>();\n      failureReason = string.Empty;\n      return true;\n    }\n\n    var raw = new List<SpeechWordBoundary>();\n    SpeechMarkupWord first = words[0];\n    raw.Add(new SpeechWordBoundary(\n      TimeSpan.Zero,\n      first.WordIndex,\n      first.CharacterStart,\n      first.CharacterLength,\n      first.Text,\n      Exact: true));\n    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())\n    {\n      string identity = string.IsNullOrWhiteSpace(cue.Text)\n        ? cue.Id ?? string.Empty\n        : cue.Text;\n      Match match = Regex.Match(identity, @"aps_(\\d+)$");\n      if (!match.Success ||\n          !int.TryParse(match.Groups[1].Value, out int ownerIndex))\n      {\n        continue;\n      }\n      SpeechMarkupWord? word = words.FirstOrDefault(candidate =>\n        candidate.WordIndex == ownerIndex);\n      if (word is null)\n      {\n        boundaries = Array.Empty<SpeechWordBoundary>();\n        failureReason = "windows_media_bookmark_unknown_word_owner";\n        return false;\n      }\n      raw.Add(new SpeechWordBoundary(\n        cue.StartTime,\n        word.WordIndex,\n        word.CharacterStart,\n        word.CharacterLength,\n        word.Text,\n        Exact: true));\n    }\n\n    int[] expected = words.Select(word => word.WordIndex).Order().ToArray();\n    int[] observed = raw.Select(boundary => boundary.WordIndex)\n      .Distinct()\n      .Order()\n      .ToArray();\n    if (!observed.SequenceEqual(expected))\n    {\n      boundaries = Array.Empty<SpeechWordBoundary>();\n      failureReason = "windows_media_incomplete_speechbookmark_mapping";\n      return false;\n    }\n\n    raw.Sort(static (left, right) =>\n    {\n      int timeComparison =\n        left.AudioPosition.CompareTo(right.AudioPosition);\n      return timeComparison != 0\n        ? timeComparison\n        : left.WordIndex.CompareTo(right.WordIndex);\n    });\n    var grouped = new List<SpeechWordBoundary>();\n    for (int index = 0; index < raw.Count;)\n    {\n      int end = index + 1;\n      while (end < raw.Count &&\n             raw[end].AudioPosition == raw[index].AudioPosition)\n      {\n        ++end;\n      }\n      SpeechWordBoundary[] sameTime = raw[index..end]\n        .GroupBy(boundary => boundary.WordIndex)\n        .Select(group => group.First())\n        .OrderBy(boundary => boundary.WordIndex)\n        .ToArray();\n      for (int item = 1; item < sameTime.Length; ++item)\n      {\n        if (sameTime[item].WordIndex != sameTime[item - 1].WordIndex + 1)\n        {\n          boundaries = Array.Empty<SpeechWordBoundary>();\n          failureReason = "windows_media_noncontiguous_shared_sound_ownership";\n          return false;\n        }\n      }\n      SpeechWordBoundary firstBoundary = sameTime[0];\n      SpeechWordBoundary lastBoundary = sameTime[^1];\n      int rangeEnd = checked(\n        lastBoundary.CharacterPosition + lastBoundary.CharacterCount);\n      grouped.Add(firstBoundary with\n      {\n        CharacterCount = rangeEnd - firstBoundary.CharacterPosition,\n        Text = markup.PlainText.Substring(\n          firstBoundary.CharacterPosition,\n          rangeEnd - firstBoundary.CharacterPosition),\n        WordCount = sameTime.Length\n      });\n      index = end;\n    }\n\n    boundaries = grouped;\n    failureReason = string.Empty;\n    return true;\n  }\n\n'''
  replace_section(rel, create_start, create_end, create_replacement)

  # Add the explicit degradation event raiser and expand the rendered segment.
  rel_text = read(rel)
  raise_anchor = '''  private void RaiseCompleted()\n'''
  if raise_anchor not in rel_text:
    raise RuntimeError("SapiSpeechEngine.cs: RaiseCompleted anchor missing")
  rel_text = rel_text.replace(
    raise_anchor,
    '''  private void RaiseWordTrackingUnavailable(\n    SpeechTrackingDegradation degradation)\n  {\n    try\n    {\n      WordTrackingUnavailable?.Invoke(degradation);\n    }\n    catch (Exception exception)\n    {\n      DiagnosticLog.Write("speech.word_tracking_handler_failed", new\n      {\n        exception = exception.ToString()\n      });\n    }\n  }\n\n''' + raise_anchor,
    1)
  old_record = '''  private sealed record RenderedSpeechSegment(\n    PcmWaveData Wave,\n    IReadOnlyList<SpeechWordBoundary> WordBoundaries);\n'''
  new_record = '''  private sealed record RenderedSpeechSegment(\n    PcmWaveData Wave,\n    IReadOnlyList<SpeechWordBoundary> WordBoundaries,\n    SpeechTrackingDegradation? TrackingDegradation);\n'''
  if old_record not in rel_text:
    raise RuntimeError("SapiSpeechEngine.cs: RenderedSpeechSegment anchor missing")
  rel_text = rel_text.replace(old_record, new_record, 1)
  write(rel, rel_text)


def patch_speech_service() -> None:
  rel = "AgentPanelSpeaker/SpeechService.cs"
  text = read(rel)
  text = text.replace(
    '''  private int _activeWordIndex;\n''',
    '''  private int _activeWordIndex;\n  private int _activeWordCount = 1;\n  private TranscriptPlaybackHighlightMode _activeHighlightMode =\n    TranscriptPlaybackHighlightMode.Word;\n''',
    1)
  text = text.replace(
    '''    _engine.WordBoundary += EngineWordBoundary;\n''',
    '''    _engine.WordBoundary += EngineWordBoundary;\n    _engine.WordTrackingUnavailable += EngineWordTrackingUnavailable;\n''',
    1)
  text = text.replace(
    '''      _engine.WordBoundary -= EngineWordBoundary;\n''',
    '''      _engine.WordBoundary -= EngineWordBoundary;\n      _engine.WordTrackingUnavailable -= EngineWordTrackingUnavailable;\n''',
    1)
  # Exact boundary owns one or more adjacent fragment words.
  marker = '''      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);\n'''
  if marker not in text:
    raise RuntimeError("SpeechService.cs: boundary index marker missing")
  text = text.replace(
    marker,
    '''      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);\n      _activeWordCount = Math.Clamp(\n        boundary.WordCount,\n        1,\n        Math.Max(1, GetFragmentWordCount(fragment) - _activeWordIndex));\n      _activeHighlightMode = TranscriptPlaybackHighlightMode.Word;\n''',
    1)
  # Insert degradation handler before completion handler.
  completion_anchor = '''  /// <summary>\n  /// Advances serialized playback after one prompt completes or is cancelled.\n'''
  if completion_anchor not in text:
    raise RuntimeError("SpeechService.cs: completion anchor missing")
  degradation_handler = '''  /// <summary>\n  /// Degrades monitored playback to the complete canonical fragment when the\n  /// speech provider cannot prove finer word ownership.\n  /// </summary>\n  private void EngineWordTrackingUnavailable(\n    SpeechTrackingDegradation degradation)\n  {\n    lock (_sync)\n    {\n      if (_disposed ||\n          _activeKind != ActiveSpeechKind.History ||\n          _activeHistoryIndex < 0 ||\n          _activeHistoryIndex >= _history.Count)\n      {\n        return;\n      }\n      SpeechFragment fragment = _history[_activeHistoryIndex];\n      _activeHighlightMode = TranscriptPlaybackHighlightMode.Fragment;\n      _activeWordCount = 0;\n      _activeBoundaryTimestamp = Stopwatch.GetTimestamp();\n      DiagnosticLog.Write("speech.word_tracking_degraded", new\n      {\n        degradation.Backend,\n        degradation.VoiceName,\n        degradation.Reason,\n        fragment.FragmentId,\n        fragment.NodeId,\n        fragment.Text,\n        highlightMode = "fragment"\n      });\n      ReportPlaybackPositionLocked(\n        _isPaused\n          ? TranscriptPlaybackState.Paused\n          : TranscriptPlaybackState.Speaking);\n    }\n  }\n\n'''
  text = text.replace(completion_anchor, degradation_handler + completion_anchor, 1)
  # Start/restart/paused positions are exact textual locations until an engine
  # explicitly reports that this utterance has no fine-grained timing.
  start_marker = '''    _activeWordIndex = boundedWordIndex;\n    _activeWordBaseIndex = boundedWordIndex;\n'''
  if start_marker not in text:
    raise RuntimeError("SpeechService.cs: StartHistory active index anchor missing")
  text = text.replace(
    start_marker,
    '''    _activeWordIndex = boundedWordIndex;\n    _activeWordCount = 1;\n    _activeHighlightMode = TranscriptPlaybackHighlightMode.Word;\n    _activeWordBaseIndex = boundedWordIndex;\n''',
    1)
  # First history SpeakConfiguredLocked call.
  old_call = '''      SpeakConfiguredLocked(\n        spokenText,\n        profile,\n        pauseAfter,\n        pauseBefore);\n'''
  new_call = '''      SpeakConfiguredLocked(\n        spokenText,\n        profile,\n        pauseAfter,\n        pauseBefore,\n        fragment,\n        boundedWordIndex,\n        characterStart);\n'''
  if old_call not in text:
    raise RuntimeError("SpeechService.cs: history SpeakConfiguredLocked call missing")
  text = text.replace(old_call, new_call, 1)
  # Restart current word call.
  old_restart = '''    SpeakConfiguredLocked(\n      remaining,\n      _activeProfile,\n      _activePauseAfter);\n'''
  new_restart = '''    SpeakConfiguredLocked(\n      remaining,\n      _activeProfile,\n      _activePauseAfter,\n      pauseBefore: false,\n      fragment,\n      _activeWordIndex,\n      start);\n'''
  if old_restart not in text:
    raise RuntimeError("SpeechService.cs: restart SpeakConfiguredLocked call missing")
  text = text.replace(old_restart, new_restart, 1)
  # Expand configured speech signature and attach canonical fragment ranges to
  # speech markup without re-tokenizing canonical identity.
  old_signature = '''  private void SpeakConfiguredLocked(\n    string text,\n    SpeechProfileSettings profile,\n    bool pauseAfter,\n    bool pauseBefore = false)\n'''
  new_signature = '''  private void SpeakConfiguredLocked(\n    string text,\n    SpeechProfileSettings profile,\n    bool pauseAfter,\n    bool pauseBefore = false,\n    SpeechFragment? fragment = null,\n    int fragmentWordStart = 0,\n    int characterStart = 0)\n'''
  if old_signature not in text:
    raise RuntimeError("SpeechService.cs: SpeakConfiguredLocked signature missing")
  text = text.replace(old_signature, new_signature, 1)
  build_anchor = '''    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(\n      text,\n      normalized.Pitch,\n      spelledWords,\n      pronunciations,\n      pauseAfter,\n      pauseBefore);\n'''
  if build_anchor not in text:
    raise RuntimeError("SpeechService.cs: markup build anchor missing")
  text = text.replace(
    build_anchor,
    build_anchor + '''    if (fragment?.TranscriptWords is { Count: > 0 } transcriptWords)\n    {\n      SpeechMarkupWord[] exactWords = transcriptWords\n        .Skip(Math.Clamp(fragmentWordStart, 0, transcriptWords.Count))\n        .Select((word, index) => new SpeechMarkupWord(\n          index,\n          word.Text,\n          word.CharacterStart - characterStart,\n          word.CharacterLength))\n        .Where(word => word.CharacterStart >= 0 &&\n          word.CharacterStart + word.CharacterLength <= text.Length)\n        .ToArray();\n      markup = markup with { Words = exactWords };\n    }\n''',
    1)
  # Paused navigation is always an exact known textual word.
  paused_marker = '''    _activeWordIndex = boundedWordIndex;\n    _activeWordBaseIndex = 0;\n'''
  if paused_marker not in text:
    raise RuntimeError("SpeechService.cs: paused navigation anchor missing")
  text = text.replace(
    paused_marker,
    '''    _activeWordIndex = boundedWordIndex;\n    _activeWordCount = 1;\n    _activeHighlightMode = TranscriptPlaybackHighlightMode.Word;\n    _activeWordBaseIndex = 0;\n''',
    1)
  write(rel, text)

  # Replace position publishing with explicit one/many word ownership or the
  # whole fragment identity.
  report_start = '''  private void ReportPlaybackPositionLocked(TranscriptPlaybackState state)\n'''
  report_end = '''\n  /// <summary>\n  /// Resolves the current speech token index to its immutable Core word ID.\n'''
  report_replacement = '''  private void ReportPlaybackPositionLocked(TranscriptPlaybackState state)\n  {\n    if (state is TranscriptPlaybackState.None)\n    {\n      return;\n    }\n    SpeechFragment? fragment = _activeHistoryIndex >= 0 &&\n      _activeHistoryIndex < _history.Count\n        ? _history[_activeHistoryIndex]\n        : null;\n    long nodeId = fragment?.NodeId ?? -1;\n    IReadOnlyList<long>? wordIds =\n      _activeHighlightMode == TranscriptPlaybackHighlightMode.Word\n        ? GetActiveWordIdsLocked()\n        : null;\n    long? wordId = wordIds is { Count: > 0 } ? wordIds[0] : null;\n    long? fragmentId = fragment is { FragmentId: >= 0 }\n      ? fragment.FragmentId\n      : null;\n    PlaybackPositionChanged?.Invoke(new TranscriptPlaybackPosition(\n      state,\n      _activeTranscriptText,\n      _activeWordIndex,\n      _activeWord,\n      nodeId,\n      _activeCharacterPosition,\n      _activeCharacterCount,\n      _activeBoundaryTimestamp,\n      wordId,\n      fragmentId,\n      wordIds,\n      _activeHighlightMode));\n  }\n'''
  replace_section(rel, report_start, report_end, report_replacement)
  # Keep old single-ID helper for callers/tests, but derive it from the exact
  # ownership collection.
  helper_start = '''  private long? GetActiveWordIdLocked()\n'''
  helper_end = '''\n\n  private static int GetFragmentWordCount(SpeechFragment fragment)\n'''
  helper_replacement = '''  private long? GetActiveWordIdLocked()\n  {\n    IReadOnlyList<long>? wordIds = GetActiveWordIdsLocked();\n    return wordIds is { Count: > 0 } ? wordIds[0] : null;\n  }\n\n  private IReadOnlyList<long>? GetActiveWordIdsLocked()\n  {\n    if (_activeHistoryIndex < 0 || _activeHistoryIndex >= _history.Count)\n    {\n      return null;\n    }\n    IReadOnlyList<SpeechFragmentWord>? words =\n      _history[_activeHistoryIndex].TranscriptWords;\n    if (words is null ||\n        _activeWordIndex < 0 ||\n        _activeWordIndex >= words.Count)\n    {\n      return null;\n    }\n    int count = Math.Clamp(\n      _activeWordCount,\n      1,\n      words.Count - _activeWordIndex);\n    return words\n      .Skip(_activeWordIndex)\n      .Take(count)\n      .Select(word => word.Id)\n      .ToArray();\n  }\n'''
  replace_section(rel, helper_start, helper_end, helper_replacement)


def patch_transcript_view() -> None:
  rel = "AgentPanelSpeaker/TranscriptView.cs"
  text = read(rel)
  field_anchor = '''  private IReadOnlyList<SeekableTranscriptWordRange> _seekableVoiceRanges =\n    Array.Empty<SeekableTranscriptWordRange>();\n'''
  if field_anchor not in text:
    raise RuntimeError("TranscriptView.cs: field anchor missing")
  text = text.replace(
    field_anchor,
    field_anchor + '''  private IReadOnlyList<SpeechFragment> _speechFragments =\n    Array.Empty<SpeechFragment>();\n  private bool _speechFragmentsPosted;\n''',
    1)
  # Reset inventory only when selecting a different session.
  select_anchor = '''    _pendingPosition = null;\n    _lastLocatedContentPosition = null;\n'''
  if select_anchor not in text:
    raise RuntimeError("TranscriptView.cs: SelectSession reset anchor missing")
  text = text.replace(
    select_anchor,
    '''    _pendingPosition = null;\n    _lastLocatedContentPosition = null;\n    _speechFragments = Array.Empty<SpeechFragment>();\n    _speechFragmentsPosted = false;\n''',
    1)
  clear_anchor = '''    _pendingPosition = null;\n    CancelSearchIndexBuild();\n'''
  if clear_anchor not in text:
    raise RuntimeError("TranscriptView.cs: ClearSession anchor missing")
  text = text.replace(
    clear_anchor,
    '''    _pendingPosition = null;\n    _speechFragments = Array.Empty<SpeechFragment>();\n    _speechFragmentsPosted = false;\n    CancelSearchIndexBuild();\n''',
    1)
  # Public inventory API before settings API.
  settings_anchor = '''  /// <summary>\n  /// Applies current renderer settings immediately.\n'''
  if settings_anchor not in text:
    raise RuntimeError("TranscriptView.cs: settings anchor missing")
  inventory_methods = '''  /// <summary>\n  /// Replaces the conversation-global speech-fragment inventory used to create\n  /// stable frag-N wrappers around complete rendered fragments.\n  /// </summary>\n  public void SetSpeechFragments(IReadOnlyList<SpeechFragment> fragments)\n  {\n    ArgumentNullException.ThrowIfNull(fragments);\n    _speechFragments = fragments\n      .Where(fragment => fragment.FragmentId >= 0 &&\n        fragment.WordIds is { Count: > 0 })\n      .ToArray();\n    _speechFragmentsPosted = false;\n    if (_initialized)\n    {\n      PostSpeechFragments();\n    }\n  }\n\n  /// <summary>\n  /// Appends one live canonical fragment without changing earlier identities.\n  /// </summary>\n  public void AppendSpeechFragment(SpeechFragment fragment)\n  {\n    ArgumentNullException.ThrowIfNull(fragment);\n    if (fragment.FragmentId < 0 || fragment.WordIds is not { Count: > 0 })\n    {\n      return;\n    }\n    _speechFragments = _speechFragments\n      .Where(item => item.FragmentId != fragment.FragmentId)\n      .Append(fragment)\n      .OrderBy(item => item.FragmentId)\n      .ToArray();\n    _speechFragmentsPosted = false;\n    if (_initialized)\n    {\n      PostSpeechFragments();\n    }\n  }\n\n  private void PostSpeechFragments()\n  {\n    if (!_initialized || _speechFragmentsPosted)\n    {\n      return;\n    }\n    PostMessage(new\n    {\n      type = "speech-fragments",\n      fragments = _speechFragments.Select(fragment => new\n      {\n        fragmentId = fragment.FragmentId,\n        wordIds = fragment.WordIds\n      }).ToArray()\n    });\n    _speechFragmentsPosted = true;\n  }\n\n'''
  text = text.replace(settings_anchor, inventory_methods + settings_anchor, 1)
  # Ensure settings/application activity sends a pending inventory into the page.
  latest_settings_anchor = '''    PostMessage(new\n    {\n      type = "settings",\n'''
  if latest_settings_anchor not in text:
    raise RuntimeError("TranscriptView.cs: PostLatestSettings message anchor missing")
  # Insert after settings PostMessage block by a stable following method marker.
  following = '''  private void PostPlaybackPosition(TranscriptPlaybackPosition position)\n'''
  idx = text.find(following)
  if idx < 0:
    raise RuntimeError("TranscriptView.cs: PostPlaybackPosition marker missing")
  before = text[:idx]
  last = before.rfind("  }\n\n")
  # The preceding method is PostLatestSettings. Insert just before its closing
  # brace by locating its final PostMessage terminator.
  post_end = before.rfind("    });\n", 0, idx)
  if post_end < 0:
    raise RuntimeError("TranscriptView.cs: settings PostMessage terminator missing")
  post_end += len("    });\n")
  text = text[:post_end] + "    PostSpeechFragments();\n" + text[post_end:]
  # Log/post the new ownership fields.
  marker_log = '''      position.WordId,\n      followSpeech = _settings.FollowSpeech,\n'''
  if marker_log not in text:
    raise RuntimeError("TranscriptView.cs: marker log anchor missing")
  text = text.replace(
    marker_log,
    '''      position.WordId,\n      position.FragmentId,\n      position.WordIds,\n      highlightMode = position.HighlightMode.ToString(),\n      followSpeech = _settings.FollowSpeech,\n''',
    1)
  marker_post = '''      wordId = position.WordId,\n      characterPosition = position.CharacterPosition,\n'''
  if marker_post not in text:
    raise RuntimeError("TranscriptView.cs: playback message anchor missing")
  text = text.replace(
    marker_post,
    '''      wordId = position.WordId,\n      fragmentId = position.FragmentId,\n      wordIds = position.WordIds,\n      highlightMode = position.HighlightMode ==\n        TranscriptPlaybackHighlightMode.Fragment ? "fragment" : "word",\n      characterPosition = position.CharacterPosition,\n''',
    1)
  # Fragment highlight is canonical too; do not route it through legacy text lookup.
  canonical_route = '''    if (position.WordId is > 0)\n    {\n      PostPlaybackPosition(position);\n      return;\n    }\n'''
  if canonical_route not in text:
    raise RuntimeError("TranscriptView.cs: canonical route anchor missing")
  text = text.replace(
    canonical_route,
    '''    if (position.WordId is > 0 ||\n        position.WordIds is { Count: > 0 } ||\n        position.HighlightMode == TranscriptPlaybackHighlightMode.Fragment &&\n          position.FragmentId is >= 0)\n    {\n      PostSpeechFragments();\n      PostPlaybackPosition(position);\n      return;\n    }\n''',
    1)
  # CSS for the complete fragment wrapper includes whitespace.
  css_anchor = '''.word.paused, [id^=\"word-\"].paused, [data-word-id].paused {\n  outline: 2px solid var(--highlight);\n'''
  if css_anchor not in text:
    raise RuntimeError("TranscriptView.cs: word CSS anchor missing")
  text = text.replace(
    css_anchor,
    '''.speech-fragment { border-radius: 2px; }\n.speech-fragment.active { background: var(--highlight); }\n.speech-fragment.paused {\n  outline: 2px solid var(--highlight);\n  outline-offset: 1px;\n}\n''' + css_anchor,
    1)
  # Browser state.
  state_anchor = '''let currentCanonicalPlaybackElements = [];\nlet requestedPlaybackWordId = 0;\n'''
  if state_anchor not in text:
    raise RuntimeError("TranscriptView.cs: JS playback state anchor missing")
  text = text.replace(
    state_anchor,
    '''let currentCanonicalPlaybackElements = [];\nlet currentFragmentPlaybackElement = null;\nlet speechFragments = [];\nlet requestedPlaybackWordId = 0;\n''',
    1)
  # Recreate wrappers after canonical word spans are re-materialized.
  wrap_anchor = '''  wrapWords(nodeMap || []);\n  currentStructureMap = postStructureStage(\n'''
  if wrap_anchor not in text:
    raise RuntimeError("TranscriptView.cs: full replacement wrapWords anchor missing")
  text = text.replace(
    wrap_anchor,
    '''  wrapWords(nodeMap || []);\n  wrapSpeechFragments();\n  currentStructureMap = postStructureStage(\n''',
    1)
  window_wrap_anchor = '''  wrapWords(nodeMap || []);\n  const wrapWordsMilliseconds = performance.now() - phaseStarted;\n'''
  if window_wrap_anchor not in text:
    raise RuntimeError("TranscriptView.cs: virtual replacement wrapWords anchor missing")
  text = text.replace(
    window_wrap_anchor,
    '''  wrapWords(nodeMap || []);\n  wrapSpeechFragments();\n  const wrapWordsMilliseconds = performance.now() - phaseStarted;\n''',
    1)
  # Insert fragment inventory/wrapper helpers immediately before canonical playback helpers.
  canonical_helpers = '''function canonicalPlaybackElements(wordId) {\n'''
  if canonical_helpers not in text:
    raise RuntimeError("TranscriptView.cs: canonical playback helper marker missing")
  fragment_helpers = r'''function setSpeechFragments(fragments) {
  speechFragments = (fragments || []).map(fragment => ({
    fragmentId:Number(fragment.fragmentId ?? fragment.FragmentId ?? -1),
    wordIds:(fragment.wordIds ?? fragment.WordIds ?? [])
      .map(Number)
      .filter(id => Number.isSafeInteger(id) && id > 0)
  })).filter(fragment =>
    Number.isSafeInteger(fragment.fragmentId) && fragment.fragmentId >= 0 &&
    fragment.wordIds.length > 0);
  wrapSpeechFragments();
}

function fragmentBlockOwner(element) {
  return element?.closest(
    'p,li,pre,blockquote,h1,h2,h3,h4,h5,h6,td,th,dt,dd') ||
    element?.parentElement || null;
}

function wrapSpeechFragments() {
  for (const fragment of speechFragments) {
    const id = 'frag-' + fragment.fragmentId;
    if (document.getElementById(id)) continue;
    const owners = fragment.wordIds.map(wordId =>
      document.getElementById('word-' + wordId));
    if (owners.some(owner => !owner)) continue;
    const first = owners[0];
    const last = owners[owners.length - 1];
    if (fragmentBlockOwner(first) !== fragmentBlockOwner(last)) {
      chrome.webview.postMessage({
        type:'fragment-wrap-failed',
        fragmentId:fragment.fragmentId,
        reason:'fragment-crosses-rendered-blocks'
      });
      continue;
    }
    const range = document.createRange();
    range.setStartBefore(first);
    range.setEndAfter(last);
    const wrapper = document.createElement('span');
    wrapper.id = id;
    wrapper.className = 'speech-fragment';
    try {
      range.surroundContents(wrapper);
    } catch (_) {
      const contents = range.extractContents();
      wrapper.append(contents);
      range.insertNode(wrapper);
    }
  }
}

function retireFragmentPlayback(useFade) {
  if (!currentFragmentPlaybackElement) return;
  const element = currentFragmentPlaybackElement;
  currentFragmentPlaybackElement = null;
  element.classList.remove('active', 'paused');
  cancelFade(element);
  if (useFade && fadeMs > 0) {
    const highlight = getComputedStyle(document.documentElement)
      .getPropertyValue('--highlight').trim();
    const animation = element.animate(
      [
        {backgroundColor: highlight},
        {backgroundColor: 'transparent'}
      ],
      {duration: fadeMs, easing: 'linear'});
    fadingAnimations.set(element, animation);
    animation.onfinish = () => fadingAnimations.delete(element);
    animation.oncancel = () => fadingAnimations.delete(element);
  }
}

function setFragmentPlayback(state, fragmentId) {
  retireCurrentWord(true);
  retireCanonicalPlayback(true);
  retireFragmentPlayback(true);
  wrapSpeechFragments();
  const id = Number(fragmentId);
  const element = Number.isSafeInteger(id) && id >= 0
    ? document.getElementById('frag-' + id)
    : null;
  if (!element) {
    const descriptor = speechFragments.find(item => item.fragmentId === id);
    if (descriptor?.wordIds?.length) {
      requestCanonicalPlaybackWindow(descriptor.wordIds[0]);
    }
    return;
  }
  currentFragmentPlaybackElement = element;
  cancelFade(element);
  element.classList.add(state === 'paused' ? 'paused' : 'active');
  if (followSpeech) openAncestors(element);
  reveal(element);
  maybePrefetchVoiceCursor(element);
}

'''
  text = text.replace(canonical_helpers, fragment_helpers + canonical_helpers, 1)
  # Canonical playback accepts one or many WordIds.
  old_canonical = '''function canonicalPlaybackElements(wordId) {\n  const id = Number(wordId);\n  if (!Number.isSafeInteger(id) || id < 1) return [];\n  const result = [];\n  const owner = document.getElementById('word-' + id);\n  if (owner) result.push(owner);\n  const selector = '[data-word-id=\"' + CSS.escape(String(id)) + '\"]';\n  for (const piece of transcript.querySelectorAll(selector)) {\n    if (!result.includes(piece)) result.push(piece);\n  }\n  return result;\n}\n'''
  new_canonical = '''function canonicalPlaybackElements(wordIds) {\n  const ids = (Array.isArray(wordIds) ? wordIds : [wordIds])\n    .map(Number)\n    .filter(id => Number.isSafeInteger(id) && id > 0);\n  const result = [];\n  for (const id of ids) {\n    const owner = document.getElementById('word-' + id);\n    if (owner && !result.includes(owner)) result.push(owner);\n    const selector = '[data-word-id=\"' + CSS.escape(String(id)) + '\"]';\n    for (const piece of transcript.querySelectorAll(selector)) {\n      if (!result.includes(piece)) result.push(piece);\n    }\n  }\n  return result;\n}\n'''
  if old_canonical not in text:
    raise RuntimeError("TranscriptView.cs: canonicalPlaybackElements block missing")
  text = text.replace(old_canonical, new_canonical, 1)
  old_set = '''function setCanonicalPlayback(state, wordId) {\n  retireCurrentWord(true);\n  retireCanonicalPlayback(true);\n  currentCanonicalPlaybackWordId = wordId;\n  const elements = canonicalPlaybackElements(wordId);\n  if (!elements.length) {\n    requestCanonicalPlaybackWindow(wordId);\n    return;\n  }\n'''
  new_set = '''function setCanonicalPlayback(state, wordIds) {\n  retireCurrentWord(true);\n  retireFragmentPlayback(true);\n  retireCanonicalPlayback(true);\n  const ids = (Array.isArray(wordIds) ? wordIds : [wordIds])\n    .map(Number)\n    .filter(id => Number.isSafeInteger(id) && id > 0);\n  currentCanonicalPlaybackWordId = ids.length ? ids[0] : 0;\n  const elements = canonicalPlaybackElements(ids);\n  const represented = new Set(elements.map(element => canonicalWordId(element)));\n  if (!ids.length || ids.some(id => !represented.has(id))) {\n    if (ids.length) requestCanonicalPlaybackWindow(ids[0]);\n    return;\n  }\n'''
  if old_set not in text:
    raise RuntimeError("TranscriptView.cs: setCanonicalPlayback block anchor missing")
  text = text.replace(old_set, new_set, 1)
  # Retained playback signature and routing.
  old_restore = '''    retainedPlayback.wordId);\n'''
  new_restore = '''    retainedPlayback.wordId,\n    retainedPlayback.wordIds,\n    retainedPlayback.fragmentId,\n    retainedPlayback.highlightMode);\n'''
  if old_restore not in text:
    raise RuntimeError("TranscriptView.cs: retained playback call missing")
  text = text.replace(old_restore, new_restore, 1)
  old_signature = '''  follow,\n  wordId = null) {\n'''
  new_signature = '''  follow,\n  wordId = null,\n  wordIds = null,\n  fragmentId = null,\n  highlightMode = 'word') {\n'''
  if old_signature not in text:
    raise RuntimeError("TranscriptView.cs: setPlayback signature missing")
  text = text.replace(old_signature, new_signature, 1)
  # Retire fragment marker in non-content states.
  text = text.replace(
    '''    retireCurrentWord(true);\n    retireCanonicalPlayback(true);\n    return;\n''',
    '''    retireCurrentWord(true);\n    retireCanonicalPlayback(true);\n    retireFragmentPlayback(true);\n    return;\n''',
    1)
  text = text.replace(
    '''    retireCurrentWord(true);\n    retireCanonicalPlayback(true);\n    liveEndMarker.textContent = state === 'waiting-end'\n''',
    '''    retireCurrentWord(true);\n    retireCanonicalPlayback(true);\n    retireFragmentPlayback(true);\n    liveEndMarker.textContent = state === 'waiting-end'\n''',
    1)
  route_anchor = '''  const canonicalWordIdValue = Number(wordId ?? 0);\n  if (Number.isSafeInteger(canonicalWordIdValue) && canonicalWordIdValue > 0) {\n    setCanonicalPlayback(state, canonicalWordIdValue);\n    return;\n  }\n'''
  if route_anchor not in text:
    raise RuntimeError("TranscriptView.cs: canonical playback routing missing")
  text = text.replace(
    route_anchor,
    '''  if (highlightMode === 'fragment') {\n    setFragmentPlayback(state, fragmentId);\n    return;\n  }\n  const canonicalIds = (Array.isArray(wordIds) ? wordIds : [])\n    .map(Number)\n    .filter(id => Number.isSafeInteger(id) && id > 0);\n  const canonicalWordIdValue = Number(wordId ?? 0);\n  if (!canonicalIds.length &&\n      Number.isSafeInteger(canonicalWordIdValue) && canonicalWordIdValue > 0) {\n    canonicalIds.push(canonicalWordIdValue);\n  }\n  if (canonicalIds.length) {\n    setCanonicalPlayback(state, canonicalIds);\n    return;\n  }\n''',
    1)
  # Browser message handling: inventory + ownership fields.
  settings_handler = '''  if (data.type === 'seekable-voice-ranges') {\n'''
  if settings_handler not in text:
    raise RuntimeError("TranscriptView.cs: message handler anchor missing")
  text = text.replace(
    settings_handler,
    '''  if (data.type === 'speech-fragments') {\n    setSpeechFragments(data.fragments ?? data.Fragments ?? []);\n    return;\n  }\n''' + settings_handler,
    1)
  retained_anchor = '''    nodeId:data.nodeId,\n    wordId:data.wordId\n  };\n'''
  if retained_anchor not in text:
    raise RuntimeError("TranscriptView.cs: retainedPlayback object missing")
  text = text.replace(
    retained_anchor,
    '''    nodeId:data.nodeId,\n    wordId:data.wordId,\n    wordIds:data.wordIds,\n    fragmentId:data.fragmentId,\n    highlightMode:data.highlightMode\n  };\n''',
    1)
  call_anchor = '''    data.follow,\n    data.wordId);\n'''
  if call_anchor not in text:
    raise RuntimeError("TranscriptView.cs: setPlayback message call missing")
  text = text.replace(
    call_anchor,
    '''    data.follow,\n    data.wordId,\n    data.wordIds,\n    data.fragmentId,\n    data.highlightMode);\n''',
    1)
  target_anchor = '''  const appliedTarget = data.wordId\n    ? document.getElementById(`word-${data.wordId}`)\n    : document.querySelector('.word.speaking,.word.paused');\n'''
  if target_anchor not in text:
    raise RuntimeError("TranscriptView.cs: appliedTarget anchor missing")
  text = text.replace(
    target_anchor,
    '''  const appliedTarget = data.highlightMode === 'fragment' &&\n      Number(data.fragmentId) >= 0\n    ? document.getElementById(`frag-${data.fragmentId}`)\n    : data.wordId\n      ? document.getElementById(`word-${data.wordId}`)\n      : document.querySelector('.word.speaking,.word.paused');\n''',
    1)
  applied_fields = '''    wordId: data.wordId,\n    wordIndex: data.wordIndex,\n'''
  if applied_fields not in text:
    raise RuntimeError("TranscriptView.cs: playback-applied fields missing")
  text = text.replace(
    applied_fields,
    '''    wordId: data.wordId,\n    wordIds: data.wordIds,\n    fragmentId: data.fragmentId,\n    highlightMode: data.highlightMode,\n    wordIndex: data.wordIndex,\n''',
    1)
  marker_visibility = '''    markerVisible: currentCanonicalPlaybackElements.length > 0\n      ? currentCanonicalPlaybackElements.some(element => {\n'''
  if marker_visibility not in text:
    raise RuntimeError("TranscriptView.cs: markerVisible anchor missing")
  text = text.replace(
    marker_visibility,
    '''    markerVisible: currentFragmentPlaybackElement\n      ? (() => {\n          const rect = currentFragmentPlaybackElement.getBoundingClientRect();\n          return rect.bottom > 0 && rect.top < window.innerHeight;\n        })()\n      : currentCanonicalPlaybackElements.length > 0\n      ? currentCanonicalPlaybackElements.some(element => {\n''',
    1)
  write(rel, text)


def patch_main_form() -> None:
  rel = "AgentPanelSpeaker/MainForm.cs"
  text = read(rel)
  assignment = '''      _selectedSessionHistory = snapshot;\n      _selectedSessionHistoryPath = expectedPath;\n'''
  if assignment in text:
    text = text.replace(
      assignment,
      '''      _selectedSessionHistory = snapshot;\n      _selectedSessionHistoryPath = expectedPath;\n      _transcriptView.SetSpeechFragments(snapshot.Fragments);\n''',
      1)
  history = '''      _speech.LoadHistory(\n        snapshot.Fragments,\n'''
  if history not in text:
    raise RuntimeError("MainForm.cs: MonitorHistoryLoaded LoadHistory anchor missing")
  text = text.replace(
    history,
    '''      _transcriptView.SetSpeechFragments(snapshot.Fragments);\n      _speech.LoadHistory(\n        snapshot.Fragments,\n''',
    1)
  live = '''      _speech.SpeakLive(fragment);\n      RefreshTranscriptVoiceSelectability();\n'''
  if live not in text:
    raise RuntimeError("MainForm.cs: MonitorTextReady anchor missing")
  text = text.replace(
    live,
    '''      _transcriptView.AppendSpeechFragment(fragment);\n      _speech.SpeakLive(fragment);\n      RefreshTranscriptVoiceSelectability();\n''',
    1)
  write(rel, text)


def apply_green() -> None:
  patch_data_contracts()
  patch_fragment_ids()
  patch_sapi_engine()
  patch_speech_service()
  patch_transcript_view()
  patch_main_form()


if __name__ == "__main__":
  parser = argparse.ArgumentParser()
  parser.add_argument("stage", choices=("red", "green"))
  args = parser.parse_args()
  wire_program()
  if args.stage == "green":
    apply_green()
