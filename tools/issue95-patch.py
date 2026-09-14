from __future__ import annotations

import argparse
from pathlib import Path

TARGET = Path("AgentPanelSpeaker/RegressionTestRunner.cs")

TEST_REGISTRATION = '''      new TestCase("regression", "no-stdin-bom", TestNoBridgeBom),
      new TestCase("regression", "ci-report-routing", TestReportRouting),
      new TestCase("regression", "reasoning-disclosure-ownership", TestReasoningDisclosureOwnership)'''

OLD_TEST_REGISTRATION = '''      new TestCase("regression", "no-stdin-bom", TestNoBridgeBom),
      new TestCase("regression", "reasoning-disclosure-ownership", TestReasoningDisclosureOwnership)'''

TEST_METHOD = r'''
  private static void TestReportRouting()
  {
    string? originalRunnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
    string originalCurrentDirectory = Environment.CurrentDirectory;
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-report-routing-{Guid.NewGuid():N}");
    string currentDirectory = Path.Combine(root, "current");
    string runnerTemp = Path.Combine(root, "runner-temp");
    const string reportName = "AgentPanelSpeaker-test-results.txt";
    Directory.CreateDirectory(currentDirectory);
    Directory.CreateDirectory(runnerTemp);

    try
    {
      Environment.CurrentDirectory = currentDirectory;
      Environment.SetEnvironmentVariable("RUNNER_TEMP", runnerTemp);
      WriteReport(new[] { "ci-report" });
      string ciReport = Path.Combine(runnerTemp, reportName);
      string localReport = Path.Combine(currentDirectory, reportName);
      Require(File.Exists(ciReport),
        "CI regression report was not written into RUNNER_TEMP.");
      Require(!File.Exists(localReport),
        "CI regression report was written into the current directory.");

      Environment.SetEnvironmentVariable("RUNNER_TEMP", null);
      WriteReport(new[] { "local-report" });
      Require(File.Exists(localReport),
        "Local regression report was not written into the current directory.");

      File.Delete(localReport);
      Environment.SetEnvironmentVariable(
        "RUNNER_TEMP",
        Path.Combine(root, "missing-runner-temp"));
      WriteReport(new[] { "missing-runner-temp" });
      Require(File.Exists(localReport),
        "Missing RUNNER_TEMP directory did not fall back to the current directory.");
    }
    finally
    {
      Environment.SetEnvironmentVariable("RUNNER_TEMP", originalRunnerTemp);
      Environment.CurrentDirectory = originalCurrentDirectory;
      try { Directory.Delete(root, recursive: true); }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
'''

METHOD_ANCHOR = '''  private static string CreateTemporaryPath()
  {'''

OLD_WRITE_REPORT = '''  private static void WriteReport(IReadOnlyCollection<string> output)
  {
    string reportPath = Path.Combine(
      Environment.CurrentDirectory,
      "AgentPanelSpeaker-test-results.txt");
    File.WriteAllLines(reportPath, output, Utf8NoBom);
  }'''

NEW_WRITE_REPORT = '''  private static void WriteReport(IReadOnlyCollection<string> output)
  {
    string? runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
    string reportDirectory =
      !string.IsNullOrWhiteSpace(runnerTemp) && Directory.Exists(runnerTemp)
        ? runnerTemp
        : Environment.CurrentDirectory;
    string reportPath = Path.Combine(
      reportDirectory,
      "AgentPanelSpeaker-test-results.txt");
    File.WriteAllLines(reportPath, output, Utf8NoBom);
  }'''


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise SystemExit(f"{label}: expected exactly one match, found {count}")
  return text.replace(old, new, 1)


def patch_test(text: str) -> str:
  text = replace_once(
    text,
    OLD_TEST_REGISTRATION,
    TEST_REGISTRATION,
    "test registration")
  text = replace_once(
    text,
    METHOD_ANCHOR,
    TEST_METHOD + "\n" + METHOD_ANCHOR,
    "test method anchor")
  return text


def patch_production(text: str) -> str:
  return replace_once(
    text,
    OLD_WRITE_REPORT,
    NEW_WRITE_REPORT,
    "WriteReport implementation")


def main() -> None:
  parser = argparse.ArgumentParser()
  parser.add_argument("phase", choices=("test", "production"))
  args = parser.parse_args()
  text = TARGET.read_text(encoding="utf-8")
  if args.phase == "test":
    text = patch_test(text)
  else:
    text = patch_production(text)
  TARGET.write_text(text, encoding="utf-8", newline="\n")


if __name__ == "__main__":
  main()
