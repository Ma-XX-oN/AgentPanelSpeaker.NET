using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;

namespace SpeechProgressHarness;

internal static partial class Program
{
  private const string PreferredVoice = "Microsoft Hazel Desktop";

  private static int Main(string[] args)
  {
    try
    {
      string fixturePath = args.Length >= 2
        ? Path.GetFullPath(args[1])
        : Path.Combine(AppContext.BaseDirectory, "fixture.md");
      if (!File.Exists(fixturePath))
      {
        throw new FileNotFoundException("The fixture file was not found.", fixturePath);
      }

      string root = Path.Combine(
        AppContext.BaseDirectory,
        "output",
        DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
      Directory.CreateDirectory(root);

      using var synthesizer = new SpeechSynthesizer();
      string voice = SelectVoice(synthesizer, args.FirstOrDefault());
      synthesizer.SelectVoice(voice);
      synthesizer.Rate = 0;
      synthesizer.Volume = 100;

      string markdown = File.ReadAllText(fixturePath);
      IReadOnlyList<string> fragments = ExtractFragments(markdown);
      var bookmarkRows = new List<BookmarkRow>();
      var progressRows = new List<ProgressRow>();
      var failures = new List<string>();

      Console.WriteLine($"Voice: {voice}");
      Console.WriteLine($"Fixture: {fixturePath}");
      Console.WriteLine($"Fragments: {fragments.Count}");
      Console.WriteLine();

      for (int fragmentIndex = 0; fragmentIndex < fragments.Count; ++fragmentIndex)
      {
        string fragmentId = $"f{fragmentIndex:D4}";
        string text = fragments[fragmentIndex];
        IReadOnlyList<Token> tokens = Tokenize(fragmentId, text);
        string ssml = BuildSsml(synthesizer.Voice.Culture, text, tokens);
        File.WriteAllText(
          Path.Combine(root, $"{fragmentId}.ssml.xml"),
          ssml,
          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var reached = new List<BookmarkEvent>();
        var progress = new List<ProgressEvent>();
        EventHandler<BookmarkReachedEventArgs> bookmarkHandler =
          (_, eventArgs) => reached.Add(new BookmarkEvent(
            eventArgs.Bookmark,
            eventArgs.AudioPosition));
        EventHandler<SpeakProgressEventArgs> progressHandler =
          (_, eventArgs) => progress.Add(new ProgressEvent(
            eventArgs.AudioPosition,
            eventArgs.CharacterPosition,
            eventArgs.CharacterCount,
            eventArgs.Text));

        synthesizer.BookmarkReached += bookmarkHandler;
        synthesizer.SpeakProgress += progressHandler;
        try
        {
          string wavPath = Path.Combine(root, $"{fragmentId}.wav");
          synthesizer.SetOutputToWaveFile(wavPath);
          synthesizer.SpeakSsml(ssml);
        }
        finally
        {
          synthesizer.SetOutputToNull();
          synthesizer.BookmarkReached -= bookmarkHandler;
          synthesizer.SpeakProgress -= progressHandler;
        }

        ValidateFragment(fragmentId, tokens, reached, failures);
        AppendRows(fragmentId, text, tokens, reached, progress, bookmarkRows, progressRows);
        Console.WriteLine(
          $"{fragmentId}: {tokens.Count} tokens, " +
          $"{reached.Count} bookmarks, {progress.Count} progress events");
      }

      WriteBookmarkCsv(Path.Combine(root, "bookmarks.csv"), bookmarkRows);
      WriteProgressCsv(Path.Combine(root, "speak-progress.csv"), progressRows);
      WriteSummary(
        Path.Combine(root, "summary.txt"),
        voice,
        fixturePath,
        fragments.Count,
        bookmarkRows.Count,
        progressRows.Count,
        failures);

      Console.WriteLine();
      Console.WriteLine($"Output: {root}");
      if (failures.Count == 0)
      {
        Console.WriteLine("PASS: every rendered token received one ordered bookmark.");
        return 0;
      }

      Console.Error.WriteLine("FAIL:");
      foreach (string failure in failures)
      {
        Console.Error.WriteLine($"  {failure}");
      }
      return 1;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine(exception);
      return 2;
    }
  }

  private static string SelectVoice(
    SpeechSynthesizer synthesizer,
    string? requestedVoice)
  {
    string[] names = synthesizer.GetInstalledVoices()
      .Where(item => item.Enabled)
      .Select(item => item.VoiceInfo.Name)
      .ToArray();
    if (names.Length == 0)
    {
      throw new InvalidOperationException("No enabled System.Speech voices are installed.");
    }

    string preferred = string.IsNullOrWhiteSpace(requestedVoice)
      ? PreferredVoice
      : requestedVoice.Trim();
    return names.FirstOrDefault(name => string.Equals(
      name,
      preferred,
      StringComparison.OrdinalIgnoreCase)) ??
      (requestedVoice is null
        ? names[0]
        : throw new ArgumentException(
          $"The requested System.Speech voice is not installed: {preferred}"));
  }

  private static IReadOnlyList<string> ExtractFragments(string markdown)
  {
    string normalized = markdown
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n')
      .Replace('\u00A0', ' ');
    var fragments = new List<string>();
    var paragraph = new StringBuilder();

    void FlushParagraph()
    {
      string value = CleanMarkdown(paragraph.ToString());
      paragraph.Clear();
      if (value.Length != 0)
      {
        fragments.Add(value);
      }
    }

    foreach (string originalLine in normalized.Split('\n'))
    {
      string line = originalLine.Trim();
      if (line.Length == 0)
      {
        FlushParagraph();
        continue;
      }

      if (line.StartsWith("- ", StringComparison.Ordinal))
      {
        FlushParagraph();
        string item = CleanMarkdown(line[2..]);
        if (item.Length != 0)
        {
          fragments.Add(item);
        }
        continue;
      }

      if (paragraph.Length != 0)
      {
        paragraph.Append(' ');
      }
      paragraph.Append(line);
    }
    FlushParagraph();
    return fragments;
  }

  private static string CleanMarkdown(string text)
  {
    return WhitespaceRegex().Replace(
      text.Replace("**", string.Empty, StringComparison.Ordinal),
      " ").Trim();
  }

  private static IReadOnlyList<Token> Tokenize(string fragmentId, string text)
  {
    var tokens = new List<Token>();
    MatchCollection matches = TokenRegex().Matches(text);
    for (int index = 0; index < matches.Count; ++index)
    {
      Match match = matches[index];
      tokens.Add(new Token(
        $"{fragmentId}-t{index:D4}",
        index,
        match.Index,
        match.Length,
        match.Value));
    }
    return tokens;
  }

  private static string BuildSsml(
    CultureInfo culture,
    string text,
    IReadOnlyList<Token> tokens)
  {
    var body = new StringBuilder();
    int position = 0;
    foreach (Token token in tokens)
    {
      AppendEscaped(body, text[position..token.Start]);
      body.Append("<mark name=\"");
      body.Append(token.Id);
      body.Append("\"/>");
      AppendEscaped(body, text.Substring(token.Start, token.Length));
      position = token.Start + token.Length;
    }
    AppendEscaped(body, text[position..]);

    string language = SecurityElement.Escape(culture.Name) ?? "en-US";
    return
      $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" " +
      $"xml:lang=\"{language}\"><prosody pitch=\"0%\">" +
      body +
      "</prosody></speak>";
  }

  private static void AppendEscaped(StringBuilder output, string text)
  {
    output.Append(SecurityElement.Escape(text));
  }

  private static void ValidateFragment(
    string fragmentId,
    IReadOnlyList<Token> tokens,
    IReadOnlyList<BookmarkEvent> events,
    ICollection<string> failures)
  {
    if (events.Count != tokens.Count)
    {
      failures.Add(
        $"{fragmentId}: expected {tokens.Count} bookmarks but received {events.Count}.");
    }

    var seen = new HashSet<string>(StringComparer.Ordinal);
    TimeSpan previous = TimeSpan.MinValue;
    int count = Math.Min(tokens.Count, events.Count);
    for (int index = 0; index < count; ++index)
    {
      Token expected = tokens[index];
      BookmarkEvent actual = events[index];
      if (!string.Equals(expected.Id, actual.Id, StringComparison.Ordinal))
      {
        failures.Add(
          $"{fragmentId}: bookmark {index} was {actual.Id}; expected {expected.Id}.");
      }
      if (!seen.Add(actual.Id))
      {
        failures.Add($"{fragmentId}: duplicate bookmark {actual.Id}.");
      }
      if (actual.AudioPosition < previous)
      {
        failures.Add(
          $"{fragmentId}: bookmark {actual.Id} moved backwards from " +
          $"{previous.TotalMilliseconds:F3} ms to " +
          $"{actual.AudioPosition.TotalMilliseconds:F3} ms.");
      }
      previous = actual.AudioPosition;
    }
  }

  private static void AppendRows(
    string fragmentId,
    string fragmentText,
    IReadOnlyList<Token> tokens,
    IReadOnlyList<BookmarkEvent> bookmarks,
    IReadOnlyList<ProgressEvent> progress,
    ICollection<BookmarkRow> bookmarkRows,
    ICollection<ProgressRow> progressRows)
  {
    var tokenById = tokens.ToDictionary(token => token.Id, StringComparer.Ordinal);
    foreach (BookmarkEvent item in bookmarks)
    {
      tokenById.TryGetValue(item.Id, out Token? token);
      bookmarkRows.Add(new BookmarkRow(
        fragmentId,
        fragmentText,
        item.Id,
        token?.Index ?? -1,
        token?.Start ?? -1,
        token?.Length ?? 0,
        token?.Text ?? string.Empty,
        item.AudioPosition.TotalMilliseconds));
    }

    foreach (ProgressEvent item in progress)
    {
      progressRows.Add(new ProgressRow(
        fragmentId,
        fragmentText,
        item.AudioPosition.TotalMilliseconds,
        item.CharacterPosition,
        item.CharacterCount,
        item.Text));
    }
  }

  private static void WriteBookmarkCsv(
    string path,
    IEnumerable<BookmarkRow> rows)
  {
    using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
    writer.WriteLine(
      "FragmentId,TokenId,TokenIndex,DisplayStart,DisplayLength," +
      "DisplayText,AudioMilliseconds,FragmentText");
    foreach (BookmarkRow row in rows)
    {
      writer.WriteLine(string.Join(",", new[]
      {
        Csv(row.FragmentId),
        Csv(row.TokenId),
        row.TokenIndex.ToString(CultureInfo.InvariantCulture),
        row.DisplayStart.ToString(CultureInfo.InvariantCulture),
        row.DisplayLength.ToString(CultureInfo.InvariantCulture),
        Csv(row.DisplayText),
        row.AudioMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
        Csv(row.FragmentText)
      }));
    }
  }

  private static void WriteProgressCsv(
    string path,
    IEnumerable<ProgressRow> rows)
  {
    using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
    writer.WriteLine(
      "FragmentId,AudioMilliseconds,CharacterPosition,CharacterCount," +
      "SpokenText,FragmentText");
    foreach (ProgressRow row in rows)
    {
      writer.WriteLine(string.Join(",", new[]
      {
        Csv(row.FragmentId),
        row.AudioMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
        row.CharacterPosition.ToString(CultureInfo.InvariantCulture),
        row.CharacterCount.ToString(CultureInfo.InvariantCulture),
        Csv(row.SpokenText),
        Csv(row.FragmentText)
      }));
    }
  }

  private static void WriteSummary(
    string path,
    string voice,
    string fixturePath,
    int fragmentCount,
    int bookmarkCount,
    int progressCount,
    IReadOnlyList<string> failures)
  {
    using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
    writer.WriteLine($"Voice: {voice}");
    writer.WriteLine($"Fixture: {fixturePath}");
    writer.WriteLine($"Fragments: {fragmentCount}");
    writer.WriteLine($"Bookmarks: {bookmarkCount}");
    writer.WriteLine($"SpeakProgress events: {progressCount}");
    writer.WriteLine($"Result: {(failures.Count == 0 ? "PASS" : "FAIL")}");
    foreach (string failure in failures)
    {
      writer.WriteLine($"Failure: {failure}");
    }
  }

  private static string Csv(string value)
  {
    return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
  }

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();

  [GeneratedRegex(
    @"[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*|[^\s\p{L}\p{N}_]")]
  private static partial Regex TokenRegex();

  private sealed record Token(
    string Id,
    int Index,
    int Start,
    int Length,
    string Text);

  private sealed record BookmarkEvent(string Id, TimeSpan AudioPosition);

  private sealed record ProgressEvent(
    TimeSpan AudioPosition,
    int CharacterPosition,
    int CharacterCount,
    string Text);

  private sealed record BookmarkRow(
    string FragmentId,
    string FragmentText,
    string TokenId,
    int TokenIndex,
    int DisplayStart,
    int DisplayLength,
    string DisplayText,
    double AudioMilliseconds);

  private sealed record ProgressRow(
    string FragmentId,
    string FragmentText,
    double AudioMilliseconds,
    int CharacterPosition,
    int CharacterCount,
    string SpokenText);
}
