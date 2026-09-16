namespace AgentPanelSpeaker;

/// <summary>
/// Regression coverage for the diagnostic WebView2 shutdown fault injector.
/// </summary>
internal static class Issue134WebViewFaultInjectionRegressionTestRunner
{
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      (
        "webview-fault-injection/control-is-activity-scoped",
        DiagnosticControlIsActivityScoped),
      (
        "webview-fault-injection/real-webview-owner-destroyed-before-dispose",
        RealWebViewOwnerDestroyedBeforeDispose)
    };

    Console.WriteLine(
      $"Issue #134 WebView fault-injection suite: {tests.Length} tests");
    int failures = 0;
    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}");
        Console.Error.WriteLine(
          $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    if (failures == 0)
    {
      Console.WriteLine();
      Console.WriteLine(
        $"PASS: {tests.Length}/{tests.Length} issue #134 regressions passed.");
      return 0;
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine(
      $"FAIL: {tests.Length - failures}/{tests.Length} issue #134 regressions passed.");
    return 1;
  }

  private static void DiagnosticControlIsActivityScoped()
  {
    using var lease = new MainFormTestLease();
    MainForm form = lease.Form;
    WebViewShutdownFaultDiagnostic.Install(form);

    Button[] buttons = EnumerateControls(form)
      .OfType<Button>()
      .Where(button => string.Equals(
        button.Name,
        WebViewShutdownFaultDiagnostic.ButtonName,
        StringComparison.Ordinal))
      .ToArray();
    Require(
      buttons.Length == 1,
      $"expected one diagnostic button, found {buttons.Length}.");

    Button button = buttons[0];
    Require(
      string.Equals(
        button.Text,
        WebViewShutdownFaultDiagnostic.ButtonText,
        StringComparison.Ordinal),
      "diagnostic button text does not match the production contract.");
    TabPage? activityPage = Ancestors(button)
      .OfType<TabPage>()
      .SingleOrDefault();
    Require(
      activityPage is not null && string.Equals(
        activityPage.Text,
        "Activity",
        StringComparison.Ordinal),
      "diagnostic button is not scoped to the Activity tab.");
  }

  private static void RealWebViewOwnerDestroyedBeforeDispose()
  {
    WebViewShutdownFaultScenario scenario =
      WebViewShutdownFaultScenario.CreateAsync().GetAwaiter().GetResult();
    Require(
      scenario.CoreInitialized,
      "diagnostic scenario did not initialize a real CoreWebView2.");
    Require(
      scenario.OwnerHandleCreated,
      "diagnostic owner handle was not alive after WebView2 initialization.");
    Require(
      !scenario.WebViewDisposed,
      "diagnostic WebView2 was already disposed before fault injection.");

    scenario.DestroyOwnerHandle();
    Require(
      !scenario.OwnerHandleCreated,
      "diagnostic owner handle remained alive after explicit destruction.");
    Require(
      !scenario.WebViewDisposed,
      "diagnostic WebView2 was disposed before the invalid disposal attempt.");

    Exception? providerFault = null;
    try
    {
      scenario.DisposeWebView();
    }
    catch (Exception exception)
    {
      providerFault = exception;
    }

    Require(
      scenario.DisposeAttempted,
      "diagnostic scenario never attempted managed WebView2 disposal.");

    // The exact provider failure is runtime-sensitive and is the real-machine
    // acceptance target. The automated invariant is the actual invalid order:
    // initialized WebView2 -> destroyed owner HWND -> WebView2.Dispose().
    if (providerFault is null)
    {
      scenario.DisposeHostAfterNonFaultingRun();
    }
  }

  private static IEnumerable<Control> EnumerateControls(Control root)
  {
    foreach (Control child in root.Controls)
    {
      yield return child;
      foreach (Control descendant in EnumerateControls(child))
      {
        yield return descendant;
      }
    }
  }

  private static IEnumerable<Control> Ancestors(Control control)
  {
    for (Control? current = control.Parent;
         current is not null;
         current = current.Parent)
    {
      yield return current;
    }
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
