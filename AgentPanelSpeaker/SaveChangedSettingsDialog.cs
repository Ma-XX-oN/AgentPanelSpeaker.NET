namespace AgentPanelSpeaker;

/// <summary>
/// Confirms saving a selectable subset of changed settings.
/// </summary>
internal sealed class SaveChangedSettingsDialog : Form
{
  private readonly LinkLabel _changedSettingsLink = new();
  private readonly Button _okButton = new();
  private readonly Button _cancelButton = new();
  private readonly ChangedSettingsPopup _popup;
  private readonly HoverPopupController _popupController;

  public SaveChangedSettingsDialog(
    IReadOnlyList<SettingsChangeSet.Change> changes,
    AppTheme theme,
    bool closing)
  {
    ArgumentNullException.ThrowIfNull(changes);
    Text = closing ? "Unsaved settings" : "Select settings to save";
    AutoScaleMode = AutoScaleMode.Font;
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MaximizeBox = false;
    MinimizeBox = false;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.CenterParent;
    Padding = new Padding(14);

    var prompt = new FlowLayoutPanel
    {
      AutoSize = true,
      WrapContents = false,
      Dock = DockStyle.Fill,
      Margin = new Padding(0, 0, 0, 12)
    };
    prompt.Controls.Add(new Label
    {
      AutoSize = true,
      Text = closing ? "Save " : "Save "
    });
    _changedSettingsLink.AutoSize = true;
    _changedSettingsLink.Text = "changed settings";
    _changedSettingsLink.TabStop = true;
    _changedSettingsLink.TabIndex = 0;
    prompt.Controls.Add(_changedSettingsLink);
    prompt.Controls.Add(new Label
    {
      AutoSize = true,
      Text = closing ? " before closing?" : "?"
    });

    _okButton.AutoSize = true;
    _okButton.Text = "OK";
    _okButton.DialogResult = DialogResult.OK;
    _okButton.TabIndex = 1;
    _cancelButton.AutoSize = true;
    _cancelButton.Text = "Cancel";
    _cancelButton.DialogResult = DialogResult.Cancel;
    _cancelButton.TabIndex = 0;

    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.RightToLeft,
      WrapContents = false
    };
    buttons.TabIndex = 0;
    buttons.Controls.Add(_cancelButton);
    buttons.Controls.Add(_okButton);

    prompt.TabIndex = 1;

    var layout = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 1,
      RowCount = 2,
      Dock = DockStyle.Fill
    };
    layout.Controls.Add(prompt, 0, 0);
    layout.Controls.Add(buttons, 0, 1);
    Controls.Add(layout);

    AcceptButton = _cancelButton;
    CancelButton = _cancelButton;

    _popup = new ChangedSettingsPopup(changes, theme, showSaveButton: false);
    _popupController = new HoverPopupController(
      _changedSettingsLink,
      () => new[] { _popup },
      ShowPopup,
      HidePopup,
      _popup.FocusInitialControl,
      openDelayMilliseconds: 250,
      closeDelayMilliseconds: 1000);
    _changedSettingsLink.LinkClicked += (_, _) =>
      _popupController.OpenImmediately(focusPopup: true);

    ThemeManager.Apply(this, theme);
  }

  protected override void OnShown(EventArgs eventArgs)
  {
    base.OnShown(eventArgs);
    ActiveControl = _cancelButton;
    _cancelButton.Select();
  }

  public IReadOnlySet<string> SelectedKeys => _popup.SelectedKeys;

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _popupController.Dispose();
      _popup.Dispose();
    }
    base.Dispose(disposing);
  }

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (HoverPopupController.HandleGlobalDismissKey(keyData))
    {
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private void ShowPopup(bool focusPopup)
  {
    Point location = _changedSettingsLink.PointToScreen(
      new Point(0, _changedSettingsLink.Height + 4));
    _popup.Location = location;
    if (!_popup.Visible)
    {
      _popup.Show(this);
    }
    _popup.BringToFront();
    if (focusPopup)
    {
      _popup.FocusInitialControl();
    }
  }

  private void HidePopup(bool returnFocus)
  {
    _popup.Visible = false;
    if (returnFocus)
    {
      _changedSettingsLink.Focus();
    }
  }

}

/// <summary>
/// Displays the changed settings and lets the user select which to save.
/// </summary>
internal sealed class ChangedSettingsPopup : PopupFormBase
{
  private readonly CheckedListBox _list = new();

  public ChangedSettingsPopup(
    IReadOnlyList<SettingsChangeSet.Change> changes,
    AppTheme theme,
    bool showSaveButton)
  {
    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    Size = new Size(410, Math.Min(390, 135 + changes.Count * 24));
    Padding = new Padding(10);

    var explanation = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(385, 0),
      Text = showSaveButton
        ? "Choose which changed settings will be saved. " +
          "Unselected changes will remain unsaved."
        : "Choose which changed settings will be saved. " +
          "Unselected changes will be discarded."
    };
    _list.CheckOnClick = true;
    _list.Dock = DockStyle.Fill;
    _list.IntegralHeight = false;
    _list.DisplayMember = nameof(SettingsChangeSet.Change.DisplayName);
    foreach (SettingsChangeSet.Change change in changes)
    {
      _list.Items.Add(change, isChecked: true);
    }

    var selectAll = new Button { AutoSize = true, Text = "Select all" };
    var selectNone = new Button { AutoSize = true, Text = "Select none" };
    selectAll.Click += (_, _) => SetAll(true);
    selectNone.Click += (_, _) => SetAll(false);
    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    buttons.Controls.Add(selectAll);
    buttons.Controls.Add(selectNone);
    if (showSaveButton)
    {
      var saveSelected = new Button
      {
        AutoSize = true,
        Text = "Save selected"
      };
      saveSelected.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
      buttons.Controls.Add(saveSelected);
    }

    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3
    };
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.Controls.Add(explanation, 0, 0);
    layout.Controls.Add(_list, 0, 1);
    layout.Controls.Add(buttons, 0, 2);
    Controls.Add(layout);

    ThemeManager.Apply(this, theme);
  }

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (HoverPopupController.HandleGlobalPopupKey(keyData, this))
    {
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  public event EventHandler? SaveRequested;

  public IReadOnlySet<string> SelectedKeys => _list.CheckedItems
    .Cast<SettingsChangeSet.Change>()
    .Select(change => change.Key)
    .ToHashSet(StringComparer.Ordinal);

  public void SetChanges(IReadOnlyList<SettingsChangeSet.Change> changes)
  {
    _list.Items.Clear();
    foreach (SettingsChangeSet.Change change in changes)
    {
      _list.Items.Add(change, isChecked: true);
    }
    Height = Math.Min(390, 135 + changes.Count * 24);
  }

  public void FocusInitialControl()
  {
    _list.Focus();
  }

  private void SetAll(bool isChecked)
  {
    for (int index = 0; index < _list.Items.Count; index++)
    {
      _list.SetItemChecked(index, isChecked);
    }
  }
}
