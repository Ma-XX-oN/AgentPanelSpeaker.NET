using System.Diagnostics;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regression coverage for bounded isolated test children.
/// </summary>
internal static class Issue140IsolationTimeoutRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #140 test-isolation regressions.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("test-isolation/hanging-child-is-bounded", TestHangingChildIsBounded),
      ("test-isolation/zero-exit-without-marker-is-rejected",
        TestZeroExitWithoutMarkerIsRejected),
      ("test-isolation/successful-marker-is-accepted",
        TestSuccessfulMarkerIsAccepted)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #140 test-isolation suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #140 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #140 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestHangingChildIsBounded()
  {
    var error = new StringWriter();
    TextWriter originalError = Console.Error;
    var stopwatch = Stopwatch.StartNew();
    int result;
    try
    {
      Console.SetError(error);
      result = Program.RunIsolatedTestSuiteForTest(
        "isolation-timeout-probe",
        TimeSpan.FromMilliseconds(300));
    }
    finally
    {
      stopwatch.Stop();
      Console.SetError(originalError);
    }

    Require(result == 1, "A timed-out child was not reported as a failure.");
    Require(
      stopwatch.Elapsed < TimeSpan.FromSeconds(5),
      $"Timed-out child took {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    Require(
      error.ToString().Contains(
        "test-isolation/isolation-timeout-probe: child exceeded 300 ms",
        StringComparison.Ordinal),
      "Timeout diagnostic did not identify the child suite and bound.");
  }

  private static void TestZeroExitWithoutMarkerIsRejected()
  {
    var error = new StringWriter();
    TextWriter originalError = Console.Error;
    int result;
    try
    {
      Console.SetError(error);
      result = Program.RunIsolatedTestSuiteForTest(
        "isolation-no-marker-probe",
        TimeSpan.FromSeconds(5));
    }
    finally
    {
      Console.SetError(originalError);
    }

    Require(
      result == 1,
      "A zero-exit child without a completion marker was accepted.");
    Require(
      error.ToString().Contains(
        "without the required successful completion marker",
        StringComparison.Ordinal),
      "Missing-marker diagnostic was not emitted.");
  }

  private static void TestSuccessfulMarkerIsAccepted()
  {
    int result = Program.RunIsolatedTestSuiteForTest(
      "isolation-success-probe",
      TimeSpan.FromSeconds(5));
    Require(
      result == 0,
      "A zero-exit child with the exact completion marker was rejected.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
