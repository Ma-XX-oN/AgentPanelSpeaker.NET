namespace AgentPanelSpeaker;

internal enum HotkeyAction
{
  None,
  PreviousSpeaker,
  PreviousNode,
  PreviousSentence,
  PlayPause,
  NextSentence,
  NextNode,
  NextSpeaker,
  ProcessingTime,
  ToggleTranscriptSize,
  ToggleFollow
}

internal sealed record HotkeySettings
{
  public static HotkeySettings Default { get; } = new();

  public string PreviousSpeaker { get; init; } = "U";
  public string PreviousNode { get; init; } = "H";
  public string PreviousSentence { get; init; } = "J";
  public string PlayPause { get; init; } = "K";
  public string NextSentence { get; init; } = "L";
  public string NextNode { get; init; } = ";";
  public string NextSpeaker { get; init; } = "O";
  public string ProcessingTime { get; init; } = "'";
  public string ToggleTranscriptSize { get; init; } = "M";
  public string ToggleFollow { get; init; } = "F";

  public HotkeySettings Normalize()
  {
    var used = new HashSet<Keys>();
    string NormalizeOne(string value, string fallback)
    {
      Keys key = ParseKey(value);
      if (key == Keys.None || !used.Add(key))
      {
        key = ParseKey(fallback);
        used.Add(key);
      }
      return FormatKey(key);
    }

    return this with
    {
      PreviousSpeaker = NormalizeOne(PreviousSpeaker, "U"),
      PreviousNode = NormalizeOne(PreviousNode, "H"),
      PreviousSentence = NormalizeOne(PreviousSentence, "J"),
      PlayPause = NormalizeOne(PlayPause, "K"),
      NextSentence = NormalizeOne(NextSentence, "L"),
      NextNode = NormalizeOne(NextNode, ";"),
      NextSpeaker = NormalizeOne(NextSpeaker, "O"),
      ProcessingTime = NormalizeOne(ProcessingTime, "'"),
      ToggleTranscriptSize = NormalizeOne(ToggleTranscriptSize, "M"),
      ToggleFollow = NormalizeOne(ToggleFollow, "F")
    };
  }

  public HotkeyAction GetAction(Keys key)
  {
    Keys code = key & Keys.KeyCode;
    foreach ((HotkeyAction action, string value) in Entries())
    {
      if (ParseKey(value) == code)
      {
        return action;
      }
    }
    return HotkeyAction.None;
  }

  public IEnumerable<(HotkeyAction Action, string Value)> Entries()
  {
    yield return (HotkeyAction.PreviousSpeaker, PreviousSpeaker);
    yield return (HotkeyAction.PreviousNode, PreviousNode);
    yield return (HotkeyAction.PreviousSentence, PreviousSentence);
    yield return (HotkeyAction.PlayPause, PlayPause);
    yield return (HotkeyAction.NextSentence, NextSentence);
    yield return (HotkeyAction.NextNode, NextNode);
    yield return (HotkeyAction.NextSpeaker, NextSpeaker);
    yield return (HotkeyAction.ProcessingTime, ProcessingTime);
    yield return (HotkeyAction.ToggleTranscriptSize, ToggleTranscriptSize);
    yield return (HotkeyAction.ToggleFollow, ToggleFollow);
  }

  public static Keys ParseKey(string? value)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length != 1)
    {
      return Keys.None;
    }
    char c = char.ToUpperInvariant(value[0]);
    if (c is >= 'A' and <= 'Z')
    {
      return (Keys)((int)Keys.A + (c - 'A'));
    }
    return c switch
    {
      ';' => Keys.OemSemicolon,
      '\'' => Keys.OemQuotes,
      ',' => Keys.Oemcomma,
      '.' => Keys.OemPeriod,
      '/' => Keys.OemQuestion,
      _ => Keys.None
    };
  }

  public static string FormatKey(Keys key)
  {
    if ((int)key >= (int)Keys.A && (int)key <= (int)Keys.Z)
    {
      return ((char)('A' + ((int)key - (int)Keys.A))).ToString();
    }
    return key switch
    {
      Keys.OemSemicolon => ";",
      Keys.OemQuotes => "'",
      Keys.Oemcomma => ",",
      Keys.OemPeriod => ".",
      Keys.OemQuestion => "/",
      _ => string.Empty
    };
  }
}
