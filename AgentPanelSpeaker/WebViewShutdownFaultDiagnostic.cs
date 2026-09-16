using Microsoft.Web.WebView2.WinForms;

namespace AgentPanelSpeaker;

/// <summary>
/// Installs the Activity-tab control that deliberately reproduces the invalid
/// WebView2 owner/disposal sequence fixed by issue #128.
/// </summary>
internal static class WebViewShutdownFaultDiagnostic
{
  internal const string ButtonName = "WebViewShutdownFaultButton";
  internal const string ButtonText = "Test WebView2 shutdown fault";
  internal const string RequestedEventName =
    "diagnostic.webview_shutdown_fault_requested";
  internal const string ReadyEventName =
    "diagnostic.webview_shutdown_fault_ready";
  internal const string OwnerDestroyedEventName =
    "diagnostic.webview_shutdown_fault_owner_destroyed";
  internal const string NotReproducedEventName =
    "diagnostic.webview_shutdown_fault_not_reproduced";

  /// <summary>
  /// Adds the fault-injection control to the existing Activity diagnostic tab.
  /// </summary>
  public static void Install(Form mainForm)
  {
    ArgumentNullException.ThrowIfNull(mainForm);

    TabPage[] activityPages = EnumerateControls(mainForm)
      .OfType<TabPage>()
      .Where(page => string.Equals(
        page.Text,
        "Activity",
        StringComparison.Ordinal))
      .ToArray();
    if (activityPages.Length != 1)
    {
      throw new InvalidOperationException(
        "Expected exactly one Activity diagnostics tab.");
    }

    TabPage activityPage = activityPages[0];
    if (EnumerateControls(activityPage).Any(control => string.Equals(
          control.Name,
          ButtonName,
          StringComparison.Ordinal)))
    {
      throw new InvalidOperationException(
        "The WebView2 shutdown fault diagnostic is already installed.");
    }

    var button = new Button
    {
      Name = ButtonName,
      Text = ButtonText,
      AccessibleName = ButtonText,
      AccessibleDescription =
        "Intentionally reproduce the invalid WebView2 shutdown lifetime " +
        "sequence for diagnostics.",
      AutoSize = true,
      Dock = DockStyle.Bottom,
      TabStop = true
    };
    button.Click += FaultButtonClicked;
    activityPage.Controls.Add(button);
    activityPage.Controls.SetChildIndex(button, 0);

    ThemeManager.Apply(button, ResolveAppliedDarkTheme(activityPage));
  }

  /// <summary>
  /// Runs the diagnostic on the UI thread and intentionally lets the provider
  /// exception escape to the application's normal UI-thread exception handler.
  /// </summary>
  private static async void FaultButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (sender is not Button button)
    {
      return;
    }

    button.Enabled = false;
    DiagnosticLog.Write(RequestedEventName, new
    {
      control = ButtonName,
      threadId = Environment.CurrentManagedThreadId
    });

    WebViewShutdownFaultScenario scenario =
      await WebViewShutdownFaultScenario.CreateAsync();
    DiagnosticLog.Write(ReadyEventName, new
    {
      coreInitialized = scenario.CoreInitialized,
      ownerHandleCreated = scenario.OwnerHandleCreated,
      webViewDisposed = scenario.WebViewDisposed
    });

    scenario.DestroyOwnerHandle();
    DiagnosticLog.Write(OwnerDestroyedEventName, new
    {
      ownerHandleCreated = scenario.OwnerHandleCreated,
      webViewDisposed = scenario.WebViewDisposed
    });

    // This is the intentionally invalid operation under test.  Do not catch
    // the provider failure here: Application.ThreadException is the production
    // diagnostic path that this control exists to exercise.
    scenario.DisposeWebView();

    DiagnosticLog.Write(NotReproducedEventName, new
    {
      message =
        "WebView2 disposal completed without reproducing the disposed-state " +
        "provider exception."
    });
    scenario.DisposeHostAfterNonFaultingRun();
    button.Enabled = true;
  }

  private static bool ResolveAppliedDarkTheme(Control control)
  {
    if (control.ForeColor.ToArgb() ==
        ThemeManager.GetForeground(dark: true).ToArgb())
    {
      return true;
    }
    if (control.ForeColor.ToArgb() == SystemColors.ControlText.ToArgb())
    {
      return false;
    }
    throw new InvalidOperationException(
      "The Activity tab does not have a recognized applied application theme.");
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
}

/// <summary>
/// Owns an isolated real WebView2 used solely to reproduce the invalid lifetime
/// ordering that issue #128 removed from the production transcript path.
/// </summary>
internal sealed class WebViewShutdownFaultScenario
{
  private readonly FaultHostForm _host;
  private readonly WebView2 _webView;

  private WebViewShutdownFaultScenario(
    FaultHostForm host,
    WebView2 webView)
  {
    _host = host;
    _webView = webView;
  }

  public bool CoreInitialized => _webView.CoreWebView2 is not null;

  public bool OwnerHandleCreated => _host.IsHandleCreated;

  public bool WebViewDisposed => _webView.IsDisposed;

  public bool DisposeAttempted { get; private set; }

  /// <summary>
  /// Creates and initializes a real WebView2 on an isolated off-screen owner.
  /// </summary>
  public static async Task<WebViewShutdownFaultScenario> CreateAsync()
  {
    var host = new FaultHostForm
    {
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000),
      ClientSize = new Size(320, 240),
      Text = "WebView2 shutdown fault diagnostic"
    };
    var webView = new WebView2
    {
      Dock = DockStyle.Fill
    };
    host.Controls.Add(webView);
    host.Show();
    _ = host.Handle;

    await webView.EnsureCoreWebView2Async();
    if (webView.CoreWebView2 is null)
    {
      webView.Dispose();
      host.Dispose();
      throw new InvalidOperationException(
        "The diagnostic WebView2 did not initialize CoreWebView2.");
    }

    return new WebViewShutdownFaultScenario(host, webView);
  }

  /// <summary>
  /// Deliberately destroys the native owner before managed WebView2 disposal.
  /// </summary>
  public void DestroyOwnerHandle()
  {
    if (!_host.IsHandleCreated)
    {
      throw new InvalidOperationException(
        "The diagnostic WebView2 owner handle is not alive.");
    }

    _host.DestroyOwnerHandleForFaultInjection();
    if (_host.IsHandleCreated)
    {
      throw new InvalidOperationException(
        "The diagnostic WebView2 owner handle was not destroyed.");
    }
  }

  /// <summary>
  /// Attempts the same managed WebView2 disposal that failed after owner-handle
  /// destruction in the original issue #128 shutdown sequence.
  /// </summary>
  public void DisposeWebView()
  {
    DisposeAttempted = true;
    _webView.Dispose();
  }

  /// <summary>
  /// Cleans up only when the invalid sequence unexpectedly does not fault.
  /// </summary>
  public void DisposeHostAfterNonFaultingRun()
  {
    _host.Dispose();
  }

  private sealed class FaultHostForm : Form
  {
    public void DestroyOwnerHandleForFaultInjection()
    {
      DestroyHandle();
    }
  }
}
