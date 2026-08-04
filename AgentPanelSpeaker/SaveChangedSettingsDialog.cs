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
    _popup.PositionBelow(_changedSettingsLink);
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
  private const int StateUnchecked = 0;
  private const int StateChecked = 1;
  private const int StateMixed = 2;

  private readonly TreeView _tree = new();
  private readonly Label _explanation = new();
  private readonly FlowLayoutPanel _buttons = new();
  private bool _updatingChecks;

  public ChangedSettingsPopup(
    IReadOnlyList<SettingsChangeSet.Change> changes,
    AppTheme theme,
    bool showSaveButton)
  {
    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    MinimumSize = new Size(440, 240);
    Padding = new Padding(10);

    _explanation.AutoSize = true;
    _explanation.MaximumSize = new Size(900, 0);
    _explanation.Text = showSaveButton
      ? "Choose which changed settings will be saved. " +
        "Unselected changes will remain unsaved."
      : "Choose which changed settings will be saved. " +
        "Unselected changes will be discarded.";

    _tree.Dock = DockStyle.Fill;
    _tree.HideSelection = false;
    _tree.FullRowSelect = true;
    _tree.ShowLines = true;
    _tree.ShowPlusMinus = true;
    _tree.ShowRootLines = true;
    _tree.StateImageList = CreateStateImages();
    _tree.NodeMouseClick += TreeNodeMouseClick;
    _tree.KeyDown += TreeKeyDown;

    var selectAll = new Button { AutoSize = true, Text = "Select all" };
    var selectNone = new Button { AutoSize = true, Text = "Select none" };
    selectAll.Click += (_, _) => SetAll(true);
    selectNone.Click += (_, _) => SetAll(false);
    _buttons.AutoSize = true;
    _buttons.Dock = DockStyle.Fill;
    _buttons.WrapContents = false;
    _buttons.Controls.Add(selectAll);
    _buttons.Controls.Add(selectNone);
    if (showSaveButton)
    {
      var saveSelected = new Button
      {
        AutoSize = true,
        Text = "Save selected"
      };
      saveSelected.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
      _buttons.Controls.Add(saveSelected);
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
    layout.Controls.Add(_explanation, 0, 0);
    layout.Controls.Add(_tree, 0, 1);
    layout.Controls.Add(_buttons, 0, 2);
    Controls.Add(layout);

    SetChanges(changes);
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

  public IReadOnlySet<string> SelectedKeys => EnumerateLeaves(_tree.Nodes)
    .Where(node => node.StateImageIndex == StateChecked)
    .Select(node => ((SettingsChangeSet.Change)node.Tag!).Key)
    .ToHashSet(StringComparer.Ordinal);

  public void SetChanges(IReadOnlyList<SettingsChangeSet.Change> changes)
  {
    _tree.BeginUpdate();
    try
    {
      _tree.Nodes.Clear();
      foreach (SettingsChangeSet.Change change in changes
        .OrderBy(change => change.Path.FirstOrDefault(), StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(change => change.Order))
      {
        TreeNodeCollection level = _tree.Nodes;
        TreeNode? node = null;
        for (int index = 0; index < change.Path.Count; index++)
        {
          string segment = change.Path[index];
          node = level.Cast<TreeNode>().FirstOrDefault(
            candidate => string.Equals(candidate.Text, segment, StringComparison.Ordinal));
          if (node is null)
          {
            node = new TreeNode(segment)
            {
              StateImageIndex = StateChecked
            };
            level.Add(node);
          }
          level = node.Nodes;
        }
        if (node is not null)
        {
          node.Tag = change;
        }
      }
      _tree.ExpandAll();
      UpdateParentStates(_tree.Nodes);
      SizeToContent(changes);
      if (_tree.Nodes.Count > 0)
      {
        _tree.SelectedNode = FirstLeaf(_tree.Nodes[0]);
      }
    }
    finally
    {
      _tree.EndUpdate();
    }
  }


  public void PositionBelow(Control anchor)
  {
    ArgumentNullException.ThrowIfNull(anchor);
    Point desired = anchor.PointToScreen(new Point(0, anchor.Height + 4));
    Rectangle workingArea = Screen.FromControl(anchor).WorkingArea;
    int x = Math.Clamp(
      desired.X,
      workingArea.Left,
      Math.Max(workingArea.Left, workingArea.Right - Width));
    int y = desired.Y + Height <= workingArea.Bottom
      ? desired.Y
      : Math.Max(workingArea.Top, anchor.PointToScreen(Point.Empty).Y - Height - 4);
    Location = new Point(x, y);
  }

  public void FocusInitialControl()
  {
    _tree.Focus();
  }

  private void TreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs eventArgs)
  {
    TreeNode? node = eventArgs.Node;
    if (node is null)
    {
      return;
    }

    _tree.SelectedNode = node;
    Rectangle stateBounds = new(
      node.Bounds.Left - 20,
      node.Bounds.Top,
      20,
      node.Bounds.Height);
    if (eventArgs.Button == MouseButtons.Left &&
        (stateBounds.Contains(eventArgs.Location) || node.Bounds.Contains(eventArgs.Location)))
    {
      ToggleNode(node);
    }
  }

  private void TreeKeyDown(object? sender, KeyEventArgs eventArgs)
  {
    if (eventArgs.KeyCode == Keys.Space && _tree.SelectedNode is not null)
    {
      ToggleNode(_tree.SelectedNode);
      eventArgs.Handled = true;
      eventArgs.SuppressKeyPress = true;
    }
  }

  private void ToggleNode(TreeNode node)
  {
    if (_updatingChecks)
    {
      return;
    }
    _updatingChecks = true;
    try
    {
      bool check = node.StateImageIndex != StateChecked;
      SetNodeAndDescendants(node, check ? StateChecked : StateUnchecked);
      UpdateAncestors(node.Parent);
    }
    finally
    {
      _updatingChecks = false;
    }
  }

  private void SetAll(bool isChecked)
  {
    _updatingChecks = true;
    try
    {
      foreach (TreeNode node in _tree.Nodes)
      {
        SetNodeAndDescendants(
          node,
          isChecked ? StateChecked : StateUnchecked);
      }
    }
    finally
    {
      _updatingChecks = false;
    }
  }

  private static void SetNodeAndDescendants(TreeNode node, int state)
  {
    node.StateImageIndex = state;
    foreach (TreeNode child in node.Nodes)
    {
      SetNodeAndDescendants(child, state);
    }
  }

  private static void UpdateAncestors(TreeNode? node)
  {
    while (node is not null)
    {
      int checkedCount = node.Nodes.Cast<TreeNode>()
        .Count(child => child.StateImageIndex == StateChecked);
      int uncheckedCount = node.Nodes.Cast<TreeNode>()
        .Count(child => child.StateImageIndex == StateUnchecked);
      node.StateImageIndex = checkedCount == node.Nodes.Count
        ? StateChecked
        : uncheckedCount == node.Nodes.Count
          ? StateUnchecked
          : StateMixed;
      node = node.Parent;
    }
  }

  private static void UpdateParentStates(TreeNodeCollection nodes)
  {
    foreach (TreeNode node in nodes)
    {
      UpdateParentStates(node.Nodes);
      if (node.Nodes.Count > 0)
      {
        node.StateImageIndex = node.Nodes.Cast<TreeNode>()
          .All(child => child.StateImageIndex == StateChecked)
            ? StateChecked
            : StateMixed;
      }
    }
  }

  private void SizeToContent(IReadOnlyList<SettingsChangeSet.Change> changes)
  {
    Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
    int visibleRows = CountVisibleNodes(_tree.Nodes);
    int deepest = changes.Count == 0 ? 1 : changes.Max(change => change.Path.Count);
    int longestText = changes
      .SelectMany(change => change.Path)
      .DefaultIfEmpty("Changed settings")
      .Max(text => TextRenderer.MeasureText(text, Font).Width);
    int desiredWidth = Math.Max(
      MinimumSize.Width,
      longestText + deepest * 22 + 95);
    int chromeHeight = _explanation.PreferredHeight +
      _buttons.PreferredSize.Height + Padding.Vertical + 34;
    int desiredHeight = Math.Max(
      MinimumSize.Height,
      chromeHeight + visibleRows * Math.Max(_tree.ItemHeight, Font.Height + 5));
    int maxWidth = Math.Max(MinimumSize.Width, workingArea.Width - 80);
    int maxHeight = Math.Max(MinimumSize.Height, workingArea.Height - 80);
    Size = new Size(
      Math.Min(desiredWidth, maxWidth),
      Math.Min(desiredHeight, maxHeight));
    _explanation.MaximumSize = new Size(ClientSize.Width - Padding.Horizontal, 0);
  }

  private static int CountVisibleNodes(TreeNodeCollection nodes)
  {
    int count = 0;
    foreach (TreeNode node in nodes)
    {
      count++;
      count += CountVisibleNodes(node.Nodes);
    }
    return count;
  }

  private static TreeNode FirstLeaf(TreeNode node)
  {
    while (node.Nodes.Count > 0)
    {
      node = node.Nodes[0];
    }
    return node;
  }

  private static IEnumerable<TreeNode> EnumerateLeaves(TreeNodeCollection nodes)
  {
    foreach (TreeNode node in nodes)
    {
      if (node.Nodes.Count == 0 && node.Tag is SettingsChangeSet.Change)
      {
        yield return node;
      }
      foreach (TreeNode child in EnumerateLeaves(node.Nodes))
      {
        yield return child;
      }
    }
  }

  private static ImageList CreateStateImages()
  {
    var images = new ImageList
    {
      ImageSize = new Size(16, 16),
      ColorDepth = ColorDepth.Depth32Bit
    };
    images.Images.Add(CreateStateImage(CheckState.Unchecked));
    images.Images.Add(CreateStateImage(CheckState.Checked));
    images.Images.Add(CreateStateImage(CheckState.Indeterminate));
    return images;
  }

  private static Bitmap CreateStateImage(CheckState state)
  {
    var bitmap = new Bitmap(16, 16);
    using Graphics graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Transparent);
    CheckBoxRenderer.DrawCheckBox(
      graphics,
      new Point(0, 0),
      state switch
      {
        CheckState.Checked => System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal,
        CheckState.Indeterminate => System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal,
        _ => System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal
      });
    return bitmap;
  }
}
