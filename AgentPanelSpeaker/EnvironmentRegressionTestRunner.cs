using Microsoft.Web.WebView2.Core;

namespace AgentPanelSpeaker;

/// <summary>
/// Runs Windows-environment smoke tests for runtime dependencies and top-level
/// application construction.  These checks intentionally avoid playing audio.
/// </summary>
internal static class EnvironmentRegressionTestRunner
{
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("environment/webview2-runtime", TestWebView2Runtime),
      ("environment/speech-engine-initialization", TestSpeechEngineInitialization),
      ("environment/main-form-construction", TestMainFormConstruction)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Environment regression suite: {tests.Length} tests");
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
        Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} environment regression tests passed."
      : $"FAIL: {failures}/{tests.Length} environment regression tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestWebView2Runtime()
  {
    string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
    Require(!string.IsNullOrWhiteSpace(version),
      "WebView2 runtime version could not be resolved.");
    CoreWebView2Environment environment = CoreWebView2Environment
      .CreateAsync()
      .GetAwaiter()
      .GetResult();
    Require(environment is not null,
      "WebView2 environment could not be created.");
  }

  private static void TestSpeechEngineInitialization()
  {
    using var engine = new SapiSpeechEngine();
    Require(engine.Voices is not null,
      "Speech engine did not return a voice collection.");
  }

  private static void TestMainFormConstruction()
  {
    ApplicationConfiguration.Initialize();
    using var form = new MainForm();
    IntPtr handle = form.Handle;
    Require(handle != IntPtr.Zero,
      "Main window handle was not created.");
    Require(form.Controls.Count != 0,
      "Main form constructed without child controls.");
    form.PerformLayout();
    Application.DoEvents();
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
