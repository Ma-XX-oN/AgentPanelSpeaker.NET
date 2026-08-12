namespace AgentPanelSpeaker;

/// <summary>
/// Traces native messages and handle lifetime for theme-sensitive child controls.
/// Intended only for diagnosing rapid theme-switch crashes.
/// </summary>
internal sealed class ThemeNativeMessageTracer : IDisposable
{
  private readonly Dictionary<Control, ControlProbe> _probes = new();
  private readonly HashSet<Control> _subscribedControls = new();
  private Control? _root;
  private bool _started;
  private bool _disposed;

  /// <summary>
  /// Records the root control. Native child tracing is started separately after
  /// the initial form HWND tree has finished constructing.
  /// </summary>
  public void Attach(Control root)
  {
    if (_disposed)
    {
      return;
    }
    _root = root;
  }

  /// <summary>
  /// Starts tracing an already-created control tree. This must run after the
  /// initial form construction path so diagnostics never participate in HWND
  /// creation.
  /// </summary>
  public void Start()
  {
    Control? root = _root;
    if (_disposed || _started || root is null || root.IsDisposed)
    {
      return;
    }

    _started = true;
    AttachRecursive(root);
    DiagnosticLog.Write("theme.native_child_tracer_started", new
    {
      rootType = root.GetType().FullName,
      root.Name,
      root.IsHandleCreated
    });
  }

  private void AttachRecursive(Control control)
  {
    if (_disposed || control.IsDisposed)
    {
      return;
    }

    if (_subscribedControls.Add(control))
    {
      control.HandleCreated += ControlHandleCreated;
      control.HandleDestroyed += ControlHandleDestroyed;
      control.ControlAdded += ControlAdded;
    }

    if (ShouldProbe(control) && control.IsHandleCreated)
    {
      AttachProbe(control);
    }

    foreach (Control child in control.Controls)
    {
      AttachRecursive(child);
    }
  }

  private static bool ShouldProbe(Control control)
  {
    return control is ComboBox or
      TextBoxBase or
      ButtonBase or
      TrackBar or
      TabControl ||
      control.GetType().FullName?.Contains("WebView2", StringComparison.Ordinal) == true;
  }

  private void ControlAdded(object? sender, ControlEventArgs eventArgs)
  {
    if (_started && eventArgs.Control is Control control)
    {
      AttachRecursive(control);
    }
  }

  private void ControlHandleCreated(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control)
    {
      return;
    }

    LogHandleEvent(control, "created");
    if (!_started || !ShouldProbe(control) || !control.IsHandleCreated)
    {
      return;
    }

    IntPtr createdHandle = control.Handle;
    QueueProbeAttach(control, createdHandle);
  }

  private void QueueProbeAttach(Control control, IntPtr expectedHandle)
  {
    try
    {
      control.BeginInvoke(new Action(() =>
      {
        if (_disposed || !_started || control.IsDisposed || control.Disposing ||
            !control.IsHandleCreated || control.Handle != expectedHandle)
        {
          LogProbeAttach(control, expectedHandle, "skipped");
          return;
        }

        LogProbeAttach(control, expectedHandle, "begin");
        AttachProbe(control);
        LogProbeAttach(control, expectedHandle, "end");
      }));
    }
    catch (InvalidOperationException)
    {
      LogProbeAttach(control, expectedHandle, "queue-failed");
    }
  }

  private static void LogProbeAttach(
    Control control,
    IntPtr expectedHandle,
    string phase)
  {
    int generation = ThemeManager.GetNativeMessageTraceGeneration();
    if (generation <= 0)
    {
      return;
    }

    DiagnosticLog.Write("theme.child_probe_attach", new
    {
      generation,
      phase,
      expectedHandle = expectedHandle.ToInt64(),
      controlType = control.GetType().FullName,
      control.Name,
      control.Text,
      control.IsDisposed,
      control.Disposing,
      control.IsHandleCreated,
      handle = control.IsHandleCreated ? control.Handle.ToInt64() : 0
    });
  }

  private void ControlHandleDestroyed(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control)
    {
      return;
    }

    LogHandleEvent(control, "destroyed");
    if (_probes.Remove(control, out ControlProbe? probe))
    {
      probe.Release();
    }
  }

  private static void LogHandleEvent(Control control, string phase)
  {
    int generation = ThemeManager.GetNativeMessageTraceGeneration();
    if (generation <= 0)
    {
      return;
    }

    DiagnosticLog.Write("theme.child_handle", new
    {
      generation,
      phase,
      controlType = control.GetType().FullName,
      control.Name,
      control.Text,
      control.IsDisposed,
      control.Disposing,
      control.IsHandleCreated,
      handle = control.IsHandleCreated ? control.Handle.ToInt64() : 0,
      parentType = control.Parent?.GetType().FullName,
      parentName = control.Parent?.Name
    });
  }

  private void AttachProbe(Control control)
  {
    if (_disposed || control.IsDisposed || !control.IsHandleCreated ||
        _probes.ContainsKey(control))
    {
      return;
    }

    var probe = new ControlProbe(control);
    _probes.Add(control, probe);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    _started = false;
    _root = null;

    foreach (ControlProbe probe in _probes.Values)
    {
      probe.Release();
    }
    _probes.Clear();

    foreach (Control control in _subscribedControls)
    {
      if (control.IsDisposed)
      {
        continue;
      }
      control.HandleCreated -= ControlHandleCreated;
      control.HandleDestroyed -= ControlHandleDestroyed;
      control.ControlAdded -= ControlAdded;
    }
    _subscribedControls.Clear();
  }

  private sealed class ControlProbe : NativeWindow
  {
    private readonly string? _controlType;
    private readonly string _controlName;
    private readonly string _controlText;
    private int _wndProcDepth;

    public ControlProbe(Control control)
    {
      // Cache every descriptive value before subclassing the HWND. WndProc must
      // remain passive: querying live Control properties while inside that
      // control's native window procedure can itself send native/accessibility
      // messages and recursively perturb the HWND being observed.
      _controlType = control.GetType().FullName;
      _controlName = control.Name;
      _controlText = control.Text;
      IntPtr handle = control.Handle;
      AssignHandle(handle);
    }

    public void Release()
    {
      if (Handle != IntPtr.Zero)
      {
        ReleaseHandle();
      }
    }

    protected override void WndProc(ref Message message)
    {
      int generation = ThemeManager.GetNativeMessageTraceGeneration();
      if (generation <= 0)
      {
        base.WndProc(ref message);
        return;
      }

      int depth = ++_wndProcDepth;
      try
      {
        // Do not touch the Control object here. Only cached metadata and the
        // Message itself are safe for a passive native-message diagnostic.
        DiagnosticLog.Write("theme.child_wndproc", new
        {
          generation,
          phase = "begin",
          depth,
          message = $"0x{message.Msg:X4}",
          hwnd = message.HWnd.ToInt64(),
          wParam = message.WParam.ToInt64(),
          lParam = message.LParam.ToInt64(),
          controlType = _controlType,
          controlName = _controlName,
          controlText = _controlText
        });

        base.WndProc(ref message);

        DiagnosticLog.Write("theme.child_wndproc", new
        {
          generation,
          phase = "end",
          depth,
          message = $"0x{message.Msg:X4}",
          hwnd = message.HWnd.ToInt64(),
          result = message.Result.ToInt64(),
          controlType = _controlType,
          controlName = _controlName,
          controlText = _controlText
        });
      }
      finally
      {
        --_wndProcDepth;
      }
    }
  }
}
