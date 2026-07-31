# Speech progress harness

This console harness isolates System.Speech timing and transcript-token identity
from AgentPanelSpeaker's UI, WebView2, playback, Bluetooth wake audio, JSONL
monitoring, scrolling, fading, and playback mailbox.

It synthesizes the known failing transcript text to WAV files while inserting a
unique SSML `<mark>` immediately before every visible transcript token.  It
records both:

- `BookmarkReached`, which gives the exact token ID and WAV `AudioPosition`;
- `SpeakProgress`, retained for comparison with the older character-range
  approach.

The bookmark path does not infer identity from word order, repeated text,
character offsets, or the number of progress events.  A number such as `957`
has one bookmark even when System.Speech reports several spoken subwords.

## Run

```bat
build.cmd
run.cmd
```

The harness prefers `Microsoft Hazel Desktop`.  Specify a different installed
System.Speech voice as the first argument:

```bat
run.cmd "Microsoft Zira Desktop"
```

Specify an alternate Markdown fixture as the second argument:

```bat
run.cmd "Microsoft Hazel Desktop" C:\path\fixture.md
```

Results are written beneath `output\<timestamp>\`:

- one WAV per fragment;
- `bookmarks.csv`;
- `speak-progress.csv`;
- `summary.txt`;
- the exact generated SSML for every fragment.

The process exits with a failure code if bookmark IDs are missing, duplicated,
out of order, or have decreasing audio positions.
