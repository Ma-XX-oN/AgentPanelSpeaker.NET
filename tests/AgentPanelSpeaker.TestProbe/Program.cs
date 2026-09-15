using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPanelSpeaker.TestProbe;

/// <summary>
/// Provides stable generic probes that data-driven Robot tests can use without
/// adding issue-specific executable test code.
/// </summary>
internal static class Program
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    Converters = { new JsonStringEnumConverter() }
  };

  private static readonly Assembly ApplicationAssembly =
    Assembly.LoadFrom(GetApplicationAssemblyPath());

  private static int Main(string[] args)
  {
    try
    {
      if (args.Length == 0)
      {
        throw new ArgumentException("A probe command is required.");
      }

      return args[0] switch
      {
        "static-method" => RunStaticMethod(args),
        "setting-contract" => RunSettingContract(args),
        "ui-text" => RunUiText(args),
        _ => throw new ArgumentException($"Unknown probe command '{args[0]}'.")
      };
    }
    catch (TargetInvocationException exception)
      when (exception.InnerException is not null)
    {
      Console.Error.WriteLine(
        $"{exception.InnerException.GetType().Name}: " +
        exception.InnerException.Message);
      return 1;
    }
    catch (Exception exception)
    {
      Console.Error.WriteLine(
        $"{exception.GetType().Name}: {exception.Message}");
      return 1;
    }
  }

  private static int RunStaticMethod(string[] args)
  {
    if (args.Length != 4)
    {
      throw new ArgumentException(
        "static-method requires: <type> <method> <arguments-json-array>.");
    }

    Type type = GetApplicationType(args[1]);
    using JsonDocument document = JsonDocument.Parse(args[3]);
    if (document.RootElement.ValueKind != JsonValueKind.Array)
    {
      throw new ArgumentException("Static-method arguments must be a JSON array.");
    }

    JsonElement[] elements = document.RootElement.EnumerateArray().ToArray();
    MethodInfo[] candidates = type
      .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
      .Where(method =>
        string.Equals(method.Name, args[2], StringComparison.Ordinal) &&
        method.GetParameters().Length == elements.Length)
      .ToArray();
    if (candidates.Length != 1)
    {
      throw new InvalidOperationException(
        $"Expected one static method {type.FullName}.{args[2]} with " +
        $"{elements.Length} parameters; found {candidates.Length}.");
    }

    MethodInfo method = candidates[0];
    ParameterInfo[] parameters = method.GetParameters();
    object?[] values = new object?[parameters.Length];
    for (int index = 0; index < parameters.Length; index++)
    {
      values[index] = ConvertJson(elements[index], parameters[index].ParameterType);
    }

    object? result = method.Invoke(null, values);
    WriteJson(result);
    return 0;
  }

  private static int RunSettingContract(string[] args)
  {
    if (args.Length != 4)
    {
      throw new ArgumentException(
        "setting-contract requires: <property> <alternate-json> <change-key>.");
    }

    Type settingsType = GetApplicationType("AgentPanelSpeaker.UserSettings");
    PropertyInfo property = settingsType.GetProperty(
      args[1],
      BindingFlags.Instance | BindingFlags.Public) ??
      throw new InvalidOperationException(
        $"UserSettings property '{args[1]}' was not found.");
    if (property.SetMethod is null)
    {
      throw new InvalidOperationException(
        $"UserSettings property '{args[1]}' is not writable.");
    }

    MethodInfo createDefault = GetSingleMethod(
      settingsType,
      "CreateDefault",
      BindingFlags.Static | BindingFlags.Public,
      parameterCount: 1);
    object defaults = createDefault.Invoke(null, new object[] { Array.Empty<string>() }) ??
      throw new InvalidOperationException("UserSettings.CreateDefault returned null.");
    object working = createDefault.Invoke(null, new object[] { Array.Empty<string>() }) ??
      throw new InvalidOperationException("UserSettings.CreateDefault returned null.");

    object? alternateValue = ConvertJson(args[2], property.PropertyType);
    property.SetValue(working, alternateValue);

    Type documentType = GetApplicationType("AgentPanelSpeaker.SettingsDocument");
    MethodInfo fromRuntime = GetSingleMethod(
      documentType,
      "FromRuntime",
      BindingFlags.Static | BindingFlags.Public,
      parameterCount: 1);
    object document = fromRuntime.Invoke(null, new[] { working }) ??
      throw new InvalidOperationException("SettingsDocument.FromRuntime returned null.");
    MethodInfo toRuntime = GetSingleMethod(
      documentType,
      "ToRuntime",
      BindingFlags.Instance | BindingFlags.Public,
      parameterCount: 1);
    object roundTrip = toRuntime.Invoke(
      document,
      new object[] { Array.Empty<string>() }) ??
      throw new InvalidOperationException("SettingsDocument.ToRuntime returned null.");

    Type changeSetType = GetApplicationType("AgentPanelSpeaker.SettingsChangeSet");
    MethodInfo getChanges = GetSingleMethod(
      changeSetType,
      "GetChanges",
      BindingFlags.Static | BindingFlags.Public,
      parameterCount: 2);
    IEnumerable changes = getChanges.Invoke(null, new[] { defaults, working }) as IEnumerable ??
      throw new InvalidOperationException("SettingsChangeSet.GetChanges returned no collection.");
    bool changeKeyPresent = changes.Cast<object>().Any(change =>
    {
      PropertyInfo keyProperty = change.GetType().GetProperty("Key") ??
        throw new InvalidOperationException("Settings change has no Key property.");
      return string.Equals(
        keyProperty.GetValue(change) as string,
        args[3],
        StringComparison.Ordinal);
    });

    MethodInfo mergeSelected = GetSingleMethod(
      changeSetType,
      "MergeSelected",
      BindingFlags.Static | BindingFlags.Public,
      parameterCount: 3);
    var selected = new HashSet<string>(StringComparer.Ordinal) { args[3] };
    object merged = mergeSelected.Invoke(
      null,
      new object[] { defaults, working, selected }) ??
      throw new InvalidOperationException("SettingsChangeSet.MergeSelected returned null.");

    WriteJson(new
    {
      defaultValue = property.GetValue(defaults),
      roundTripValue = property.GetValue(roundTrip),
      changeKeyPresent,
      selectedMergeValue = property.GetValue(merged)
    });
    return 0;
  }

  private static int RunUiText(string[] args)
  {
    if (args.Length != 2)
    {
      throw new ArgumentException("ui-text requires: <resource-key>.");
    }

    Type type = GetApplicationType("AgentPanelSpeaker.UiText");
    MethodInfo method = GetSingleMethod(
      type,
      "Get",
      BindingFlags.Static | BindingFlags.Public,
      parameterCount: 1);
    object? result = method.Invoke(null, new object[] { args[1] });
    WriteJson(result);
    return 0;
  }

  private static string GetApplicationAssemblyPath()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      string candidate = Path.Combine(
        directory.FullName,
        "AgentPanelSpeaker",
        "bin",
        "Release",
        "net10.0-windows10.0.22621.0",
        "AgentPanelSpeaker.dll");
      if (File.Exists(candidate))
      {
        return candidate;
      }
      directory = directory.Parent;
    }

    throw new FileNotFoundException(
      "The Release AgentPanelSpeaker assembly was not found above the test probe output directory.");
  }

  private static Type GetApplicationType(string name) =>
    ApplicationAssembly.GetType(name, throwOnError: false) ??
    throw new InvalidOperationException($"Application type '{name}' was not found.");

  private static MethodInfo GetSingleMethod(
    Type type,
    string name,
    BindingFlags flags,
    int parameterCount)
  {
    MethodInfo[] methods = type.GetMethods(flags)
      .Where(method =>
        string.Equals(method.Name, name, StringComparison.Ordinal) &&
        method.GetParameters().Length == parameterCount)
      .ToArray();
    return methods.Length == 1
      ? methods[0]
      : throw new InvalidOperationException(
          $"Expected one {type.FullName}.{name} method with {parameterCount} " +
          $"parameters; found {methods.Length}.");
  }

  private static object? ConvertJson(string json, Type targetType)
  {
    using JsonDocument document = JsonDocument.Parse(json);
    return ConvertJson(document.RootElement, targetType);
  }

  private static object? ConvertJson(JsonElement element, Type targetType) =>
    JsonSerializer.Deserialize(element.GetRawText(), targetType, JsonOptions);

  private static void WriteJson(object? value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}
