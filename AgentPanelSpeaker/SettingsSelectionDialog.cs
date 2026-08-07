namespace AgentPanelSpeaker;

internal enum SettingsSelectionMode
{
  Save,
  Reset
}

/// <summary>
/// Selects a subset of settings for saving or resetting to defaults.
/// </summary>
internal sealed class SettingsSelectionDialog : Form
{
  private const int StateUnchecked = 0;
  private const int StateChecked = 1;
  private const int StateMixed = 2;

  private readonly ThemedThreeStateTreeView _tree = new();
  private readonly Label _explanation = new();
  private readonly Button _applyButton = new();
  private bool _updatingChecks;

  public SettingsSelectionDialog(
    IReadOnlyList<SettingsChangeSet.Change> changes,
    AppTheme theme,
    SettingsSelectionMode mode)
  {
    ArgumentNullException.ThrowIfNull(changes);

    string prefix = mode == SettingsSelectionMode.Save
      ? "SettingsSelection.Save"
      : "SettingsSelection.Reset";
    Text = UiText.Get($"{prefix}.Title");
    AutoScaleMode = AutoScaleMode.Font;
    StartPosition = FormStartPosition.CenterParent;
    FormBorderStyle = FormBorderStyle.SizableToolWindow;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    MinimumSize = new Size(620, 480);
    Size = new Size(780, 720);
    Padding = new Padding(12);
    KeyPreview = true;

    _explanation.AutoSize = true;
    _explanation.Dock = DockStyle.Top;
    _explanation.MaximumSize = new Size(720, 0);
    _explanation.Text = UiText.Get($"{prefix}.Explanation");

    _tree.Dock = DockStyle.Fill;
    _tree.HideSelection = false;
    _tree.FullRowSelect = true;
    _tree.ShowLines = true;
    _tree.ShowPlusMinus = true;
    _tree.ShowRootLines = true;
    _tree.MouseDown += TreeMouseDown;
    _tree.KeyDown += TreeKeyDown;
    UiText.Apply(_tree, $"{prefix}.Tree");

    _applyButton.AutoSize = true;
    _applyButton.DialogResult = DialogResult.OK;
    UiText.Apply(_applyButton, $"{prefix}.Apply");

    var buttons = new FlowLayoutPanel
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.RightToLeft,
      WrapContents = false,
      Padding = new Padding(0, 4, 0, 0)
    };
    buttons.Controls.Add(_applyButton);

    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3
    };
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42.0f));
    layout.Controls.Add(_explanation, 0, 0);
    layout.Controls.Add(_tree, 0, 1);
    layout.Controls.Add(buttons, 0, 2);
    Controls.Add(layout);

    AcceptButton = _applyButton;
    SetChanges(changes);
    Shown += (_, _) => FitToWorkingArea();
    Resize += (_, _) => UpdateExplanationWidth();
    ThemeManager.Apply(this, theme);
    AccessibilityAudit.ReportMissing(this);
  }

  public IReadOnlySet<string> SelectedKeys => EnumerateLeaves(_tree.Nodes)
    .Where(node => node.StateImageIndex == StateChecked)
    .Select(node => ((SettingsChangeSet.Change)node.Tag!).Key)
    .ToHashSet(StringComparer.Ordinal);

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (keyData == Keys.Escape)
    {
      DialogResult = DialogResult.Cancel;
      Close();
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private void SetChanges(IReadOnlyList<SettingsChangeSet.Change> changes)
  {
    _tree.BeginUpdate();
    try
    {
      _tree.Nodes.Clear();
      var allNode = new TreeNode(UiText.Get("SettingsSelection.All"))
      {
        StateImageIndex = StateChecked
      };
      _tree.Nodes.Add(allNode);
      foreach (SettingsChangeSet.Change change in changes
        .OrderBy(change => change.Path.FirstOrDefault(),
          StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(change => change.Order))
      {
        TreeNodeCollection level = allNode.Nodes;
        TreeNode? node = null;
        for (int index = 0; index < change.Path.Count; index++)
        {
          string segment = change.Path[index];
          node = level.Cast<TreeNode>().FirstOrDefault(candidate =>
            string.Equals(candidate.Text, segment, StringComparison.Ordinal));
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
      allNode.ExpandAll();
      _tree.SelectedNode = allNode;
    }
    finally
    {
      _tree.EndUpdate();
    }
    UpdateApplyState();
  }

  private void TreeMouseDown(object? sender, MouseEventArgs e)
  {
    if (e.Button != MouseButtons.Left)
    {
      return;
    }

    TreeViewHitTestInfo hit = _tree.HitTest(e.Location);
    if (hit.Node is not TreeNode node ||
        (hit.Location & TreeViewHitTestLocations.PlusMinus) != 0)
    {
      return;
    }

    _tree.SelectedNode = node;
    ToggleNode(node);
  }

  private void FitToWorkingArea()
  {
    Rectangle workingArea = Screen.FromControl(Owner ?? this).WorkingArea;
    int margin = 24;
    int availableWidth = Math.Max(1, workingArea.Width - margin * 2);
    int availableHeight = Math.Max(1, workingArea.Height - margin * 2);
    Size = new Size(
      Math.Min(780, availableWidth),
      Math.Min(720, availableHeight));

    Rectangle ownerBounds = Owner?.Bounds ?? workingArea;
    int x = ownerBounds.Left + (ownerBounds.Width - Width) / 2;
    int y = ownerBounds.Top + (ownerBounds.Height - Height) / 2;
    x = Math.Clamp(x, workingArea.Left, workingArea.Right - Width);
    y = Math.Clamp(y, workingArea.Top, workingArea.Bottom - Height);
    Location = new Point(x, y);
    UpdateExplanationWidth();
  }

  private void UpdateExplanationWidth()
  {
    int width = Math.Max(200, ClientSize.Width - Padding.Horizontal);
    _explanation.MaximumSize = new Size(width, 0);
  }

  private void TreeKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.KeyCode is not (Keys.Space or Keys.Enter) ||
        _tree.SelectedNode is null)
    {
      return;
    }
    ToggleNode(_tree.SelectedNode);
    e.Handled = true;
    e.SuppressKeyPress = true;
  }

  private void ToggleNode(TreeNode node)
  {
    if (_updatingChecks)
    {
      return;
    }

    bool check = node.StateImageIndex != StateChecked;
    _updatingChecks = true;
    try
    {
      SetNodeAndDescendants(node, check ? StateChecked : StateUnchecked);
      UpdateAncestors(node.Parent);
    }
    finally
    {
      _updatingChecks = false;
    }
    UpdateApplyState();
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
      int[] states = node.Nodes.Cast<TreeNode>()
        .Select(child => child.StateImageIndex)
        .Distinct()
        .ToArray();
      node.StateImageIndex = states.Length == 1 ? states[0] : StateMixed;
      node = node.Parent;
    }
  }

  private void UpdateApplyState()
  {
    _applyButton.Enabled = SelectedKeys.Count > 0;
  }

  private static IEnumerable<TreeNode> EnumerateLeaves(
    TreeNodeCollection nodes)
  {
    foreach (TreeNode node in nodes)
    {
      if (node.Tag is SettingsChangeSet.Change)
      {
        yield return node;
      }
      foreach (TreeNode child in EnumerateLeaves(node.Nodes))
      {
        yield return child;
      }
    }
  }
}
