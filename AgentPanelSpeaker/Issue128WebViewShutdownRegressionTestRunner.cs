using Microsoft.Web.WebView2.WinForms;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies that an initialized transcript WebView2 follows the owning WinForms
/// lifetime without touching CoreWebView2 after its controller is torn down.
/// </summary>
internal static class Issue128WebViewShutdownRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #128 WebView2 shutdown regression suite.
  /// </summary>
  /// <returns>Zero when all regressions pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("webview-shutdown/initialized-owner-close-is-clean",
        TestInitializedOwnerCloseIsClean)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #128 WebView shutdown suite: {tests.Length} test");
    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failures;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine(
          $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #128 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #128 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Reproduces the production ownership path: a top-level form owns a
  /// TranscriptView, the TranscriptView owns a real initialized WebView2, and
  /// closing the top-level owner tears the hierarchy down through WinForms.
  /// </summary>
  private static void TestInitializedOwnerCloseIsClean()
  {
    var threadExceptions = new List<Exception>();
    ThreadExceptionEventHandler handler = (_, eventArgs) =>
      threadExceptions.Add(eventArgs.Exception);
    Application.ThreadException += handler;

    Form? form = null;
    TranscriptView? view = null;
    WebView2? webView = null;
    Exception? closeFailure = null;
    try
    {
      form = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      webView = view.Controls.OfType<WebView2>().Single();
      PumpUntil(
        () => webView.CoreWebView2 is not null && webView.Visible,
        "initialized transcript WebView2");

      try
      {
        form.Close();
        Application.DoEvents();
      }
      catch (Exception exception)
      {
        closeFailure = exception;
      }

      Require(
        closeFailure is null,
        "Closing the initialized owner threw " +
        $"{closeFailure?.GetType().Name}: {closeFailure?.Message}");
      Require(
        threadExceptions.Count == 0,
        "Closing the initialized owner raised a Windows Forms thread " +
        "exception: " + string.Join(" | ", threadExceptions.Select(
          exception => $"{exception.GetType().Name}: {exception.Message}")));
      Require(form.IsDisposed, "The owner form was not disposed by Close().");
      Require(view.IsDisposed, "TranscriptView was not disposed with its owner.");
      Require(webView.IsDisposed, "WebView2 was not disposed with TranscriptView.");
    }
    finally
    {
      Application.ThreadException -= handler;
      if (form is not null && !form.IsDisposed)
      {
        form.Dispose();
      }
      else if (view is not null && !view.IsDisposed)
      {
        view.Dispose();
      }
    }
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
