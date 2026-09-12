from pathlib import Path

path = Path('AgentPanelSpeaker/Program.cs')
text = path.read_text(encoding='utf-8')

old = '''      if (args.Length == 2 &&
          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))
'''
new = '''      if (args.Length == 2 &&
          string.Equals(
            args[1],
            "legacy-word-mapping-debt",
            StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "legacy-word-mapping-debt",
          Issue76LegacyWordMappingDebtRegressionTestRunner.Run);
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))
'''
if text.count(old) != 1:
    raise RuntimeError('named-suite insertion anchor mismatch')
text = text.replace(old, new, 1)

old = '''      int coreWordIdMigration = RunIsolatedTestSuite(
        "core-word-id-migration");

      Environment.ExitCode = primary == 0 &&
'''
new = '''      int coreWordIdMigration = RunIsolatedTestSuite(
        "core-word-id-migration");
      int legacyWordMappingDebt = RunIsolatedTestSuite(
        "legacy-word-mapping-debt");

      Environment.ExitCode = primary == 0 &&
'''
if text.count(old) != 1:
    raise RuntimeError('aggregate suite insertion anchor mismatch')
text = text.replace(old, new, 1)

old = '''                             ctrlClickVoicePointer == 0 &&
                             coreWordIdMigration == 0
'''
new = '''                             ctrlClickVoicePointer == 0 &&
                             coreWordIdMigration == 0 &&
                             legacyWordMappingDebt == 0
'''
if text.count(old) != 1:
    raise RuntimeError('aggregate condition insertion anchor mismatch')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8', newline='\n')
