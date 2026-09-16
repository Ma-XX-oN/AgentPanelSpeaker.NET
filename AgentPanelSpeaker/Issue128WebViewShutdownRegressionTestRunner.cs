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
      ("webview-shutdown/ancestor-handle-destroy-before-managed-dispose-is-clean",
        TestAncestorHandleDestroyBeforeManagedDisposeIsClean)
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
  /// Reproduces the ordering captured in the production failure: Form disposal
  /// first destroys the ancestor native window tree, then managed child-control
  /// disposal reaches TranscriptView and its initialized WebView2.
  /// </summary>
  private static void TestAncestorHandleDestroyBeforeManagedDisposeIsClean()
  {
    var threadExceptions = new List<Exception>();
    ThreadExceptionEventHandler handler = (_, eventArgs) =>
      threadExceptions.Add(eventArgs.Exception);
    Application.ThreadException += handler;

    var form = new ShutdownOwnerForm
    {
      Width = 900,
      Height = 700,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-30000, -30000)
    };
    var layout = new TableLayoutPanel { Dock = DockStyle.Fill };
    var tabs = new TabControl { Dock = DockStyle.Fill };
    var page = new TabPage("Transcript");
    var view = new TranscriptView { Dock = DockStyle.Fill };
    WebView2 webView = view.Controls.OfType<WebView2>().Single();
    page.Controls.Add(view);
    tabs.TabPages.Add(page);
    layout.Controls.Add(tabs);
    form.Controls.Add(layout);

    Exception? disposeFailure = null;
    try
    {
      form.Show();
      Application.DoEvents();
      PumpUntil(
        () => webView.CoreWebView2 is not null && webView.Visible,
        "initialized transcript WebView2");

      form.DestroyNativeOwnerHandle();
      Application.DoEvents();
      Require(
        !form.IsHandleCreated,
        "The owner handle remained alive after the production-order teardown step.");

      try
      {
        view.Dispose();
        Application.DoEvents();
      }
      catch (Exception exception)
      {
        disposeFailure = exception;
      }

      Require(
        disposeFailure is null,
        "Disposing TranscriptView after ancestor handle destruction threw " +
        $"{disposeFailure?.GetType().Name}: {disposeFailure?.Message}");
      Require(
        threadExceptions.Count == 0,
        "The production-order teardown raised a Windows Forms thread " +
        "exception: " + string.Join(" | ", threadExceptions.Select(
          exception => $"{exception.GetType().Name}: {exception.Message}")));
      Require(view.IsDisposed, "TranscriptView was not disposed.");
      Require(webView.IsDisposed, "WebView2 was not disposed with TranscriptView.");
    }
    finally
    {
      Application.ThreadException -= handler;
      if (disposeFailure is null)
      {
        form.Dispose();
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

  private sealed class ShutdownOwnerForm : Form
  {
    public void DestroyNativeOwnerHandle()
    {
      DestroyHandle();
    }
  }
}
