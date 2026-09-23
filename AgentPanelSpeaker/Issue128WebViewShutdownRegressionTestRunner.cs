using System.Reflection;
using Microsoft.Web.WebView2.WinForms;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies that the production MainForm tears down its initialized transcript
/// WebView2 before destroying the native owner hierarchy.
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
      ("webview-shutdown/webview-precedes-owner-handle-destruction",
        TestWebViewPrecedesOwnerHandleDestruction)
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
  /// Exercises the real MainFormClosing path through MainFormTestLease and
  /// requires the transcript WebView2 to be disposed exactly once before the
  /// MainForm HWND is destroyed. The production #128 log shows the inverse
  /// order immediately before WebView2.Dispose accesses an already-invalid
  /// CoreWebView2 profile.
  /// </summary>
  private static void TestWebViewPrecedesOwnerHandleDestruction()
  {
    var lease = new MainFormTestLease();
    MainForm form = lease.Form;
    TranscriptView view = ReadField<TranscriptView>(form, "_transcriptView");
    WebView2 webView = view.Controls.OfType<WebView2>().Single();

    bool ownerHandleDestroyed = false;
    bool webViewDisposedBeforeOwnerHandle = false;
    int webViewDisposeCount = 0;
    Exception? disposeFailure = null;

    void WebViewDisposed(object? sender, EventArgs eventArgs) =>
      webViewDisposeCount++;
    void OwnerHandleDestroyed(object? sender, EventArgs eventArgs)
    {
      ownerHandleDestroyed = true;
      webViewDisposedBeforeOwnerHandle = webView.IsDisposed;
    }

    webView.Disposed += WebViewDisposed;
    form.HandleDestroyed += OwnerHandleDestroyed;
    try
    {
      PumpUntil(
        () => webView.CoreWebView2 is not null && webView.Visible,
        "initialized production transcript WebView2");

      try
      {
        lease.Dispose();
        Application.DoEvents();
      }
      catch (Exception exception)
      {
        disposeFailure = exception;
      }

      Require(
        disposeFailure is null,
        "Production MainForm teardown threw " +
        $"{disposeFailure?.GetType().Name}: {disposeFailure?.Message}");
      Require(
        ownerHandleDestroyed,
        "Production MainForm teardown did not destroy the owner handle.");
      Require(
        webViewDisposeCount == 1,
        "Production MainForm teardown disposed the transcript WebView2 " +
        $"{webViewDisposeCount} times instead of exactly once.");
      Require(
        webViewDisposedBeforeOwnerHandle,
        "Transcript WebView2 was still live when MainForm destroyed its native " +
        "owner handle; WebView2 must be disposed first.");
    }
    finally
    {
      webView.Disposed -= WebViewDisposed;
      form.HandleDestroyed -= OwnerHandleDestroyed;
      lease.Dispose();
    }
  }

  private static T ReadField<T>(object target, string name)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    object? value = field.GetValue(target);
    if (value is not T typed)
    {
      throw new InvalidOperationException(
        $"Field {name} was not {typeof(T).Name}.");
    }
    return typed;
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
