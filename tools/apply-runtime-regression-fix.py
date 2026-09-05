from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  file_path = Path(path)
  text = file_path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{path}: expected one replacement site, found {count}")
  file_path.write_text(
    text.replace(old, new, 1),
    encoding="utf-8",
    newline="\n")


replace_once(
  "AgentPanelSpeaker/TranscriptView.cs",
  """    if (generation != _renderGeneration)
    {
      return;
    }

    var cancellation = new CancellationTokenSource();
""",
  """    if (generation != _renderGeneration)
    {
      return;
    }

    // Record the exact file generation before preparation starts. If
    // preparation fails, the timer must not retry identical bytes in a tight
    // loop; a real file change or explicit forced refresh can retry.
    _lastWriteUtc = info.LastWriteTimeUtc;
    _lastLength = info.Length;

    var cancellation = new CancellationTokenSource();
""")

replace_once(
  ".github/workflows/core-integration-validation.yml",
  """      - name: Build display validation harness
        shell: pwsh
        run: dotnet build tools/AgentPanelSpeaker.DisplayParity/AgentPanelSpeaker.DisplayParity.csproj --configuration Release
""",
  """      - name: Verify bundled runtime from built output
        shell: pwsh
        run: |
          Remove-Item Env:AI_CONVERSATION_CORE -ErrorAction SilentlyContinue
          $outputTools = Join-Path $env:GITHUB_WORKSPACE 'AgentPanelSpeaker\\bin\\Release\\net10.0-windows10.0.22621.0\\tools'
          $worker = Join-Path $outputTools 'AIConversationCore-worker.mjs'
          $marker = Join-Path $outputTools 'AIConversationCore-runtime\\CORE_COMMIT'
          if (-not (Test-Path $worker)) { throw \"Built worker missing: $worker\" }
          if (-not (Test-Path $marker)) { throw \"Bundled core marker missing: $marker\" }
          $request = '{\"operation\":\"project\",\"provider\":\"claude\",\"records\":[{\"type\":\"user\",\"uuid\":\"bundled-ci\",\"timestamp\":\"2026-09-05T00:00:00Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Bundled runtime check\"}]}}]}'
          $response = $request | node $worker
          $decoded = $response | ConvertFrom-Json
          if (-not $decoded.ok) { throw \"Bundled worker projection failed: $($decoded.error)\" }
          if ($decoded.core_commit -ne 'a6fd322aece692cd0c90bc89f11228b3a4e83520') {
            throw \"Unexpected bundled core commit: $($decoded.core_commit)\"
          }
          if ($decoded.projection.units[0].block.text -ne 'Bundled runtime check') {
            throw 'Bundled projection returned unexpected text.'
          }

      - name: Build display validation harness
        shell: pwsh
        run: dotnet build tools/AgentPanelSpeaker.DisplayParity/AgentPanelSpeaker.DisplayParity.csproj --configuration Release
""")
