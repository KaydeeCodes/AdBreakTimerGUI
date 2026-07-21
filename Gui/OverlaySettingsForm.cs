using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI.Gui;

public class OverlaySettingsForm : Form
{
    // Loaded once at open, purely to seed the form's starting values, never written back directly, see SaveAndClose.
    private readonly BarState _barState;
    private readonly RadialState _radialState;

    private ComboBox _cmbBarDirection = null!;
    private NumericUpDown _numBarHeight = null!;
    private TextBox _txtBarWidth = null!;

    private ComboBox _cmbRadialDirection = null!;
    private NumericUpDown _numRadialSize = null!;
    private NumericUpDown _numRadialThickness = null!;
    private TextBox _txtRadialTrackColor = null!;
    private ComboBox _cmbRadialRotation = null!;

    private const int LabelX = 16;
    private const int ControlX = 230;
    private const int ControlWidth = 180;

    public OverlaySettingsForm()
    {
        _barState = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();
        _radialState = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();

        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Overlay settings";
        ClientSize = new Size(440, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        var tabs = new TabControl { Location = new Point(12, 12), Size = new Size(416, 260) };

        var barTab = new TabPage("Bar");
        var radialTab = new TabPage("Radial");
        tabs.TabPages.Add(barTab);
        tabs.TabPages.Add(radialTab);

        BuildBarTab(barTab);
        BuildRadialTab(radialTab);

        Controls.Add(tabs);

        var btnSave = new Button { Location = new Point(12, 282), Size = new Size(200, 32), Text = "Save" };
        btnSave.Click += (_, _) => SaveAndClose();

        var btnCancel = new Button { Location = new Point(228, 282), Size = new Size(200, 32), Text = "Cancel" };
        btnCancel.Click += (_, _) => Close();

        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private static Label NewFieldLabel(string text, int y) => new()
    {
        Location = new Point(LabelX, y),
        Size = new Size(ControlX - LabelX - 8, 20),
        Text = text
    };

    private void BuildBarTab(TabPage tab)
    {
        tab.Controls.Add(NewFieldLabel("Fill direction", 19));
        _cmbBarDirection = new ComboBox { Location = new Point(ControlX, 16), Size = new Size(ControlWidth, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbBarDirection.Items.Add("drain");
        _cmbBarDirection.Items.Add("fill");
        _cmbBarDirection.SelectedItem = _barState.Direction is "drain" or "fill" ? _barState.Direction : "drain";
        tab.Controls.Add(_cmbBarDirection);

        tab.Controls.Add(NewFieldLabel("Bar height (px)", 55));
        _numBarHeight = new NumericUpDown { Location = new Point(ControlX, 52), Size = new Size(ControlWidth, 23), Minimum = 1, Maximum = 500, Value = Math.Clamp(_barState.BarHeight, 1, 500) };
        tab.Controls.Add(_numBarHeight);

        tab.Controls.Add(NewFieldLabel("Bar width", 91));
        _txtBarWidth = new TextBox { Location = new Point(ControlX, 88), Size = new Size(ControlWidth, 23), Text = _barState.BarWidth };
        tab.Controls.Add(_txtBarWidth);

        var hint = new Label
        {
            Location = new Point(LabelX, 118),
            Size = new Size(380, 34),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "Width accepts a CSS value, e.g. 100% or 800px."
        };
        tab.Controls.Add(hint);
    }

    private void BuildRadialTab(TabPage tab)
    {
        tab.Controls.Add(NewFieldLabel("Direction", 19));
        _cmbRadialDirection = new ComboBox { Location = new Point(ControlX, 16), Size = new Size(ControlWidth, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbRadialDirection.Items.Add("cw");
        _cmbRadialDirection.Items.Add("ccw");
        _cmbRadialDirection.SelectedItem = _radialState.Direction is "cw" or "ccw" ? _radialState.Direction : "cw";
        tab.Controls.Add(_cmbRadialDirection);

        tab.Controls.Add(NewFieldLabel("Size (% of viewport)", 55));
        _numRadialSize = new NumericUpDown { Location = new Point(ControlX, 52), Size = new Size(ControlWidth, 23), Minimum = 5, Maximum = 100, Value = Math.Clamp(_radialState.Size, 5, 100) };
        tab.Controls.Add(_numRadialSize);

        tab.Controls.Add(NewFieldLabel("Thickness (% of diameter)", 91));
        _numRadialThickness = new NumericUpDown { Location = new Point(ControlX, 88), Size = new Size(ControlWidth, 23), Minimum = 1, Maximum = 50, Value = Math.Clamp(_radialState.Thickness, 1, 50) };
        tab.Controls.Add(_numRadialThickness);

        tab.Controls.Add(NewFieldLabel("Track colour", 127));
        _txtRadialTrackColor = new TextBox { Location = new Point(ControlX, 124), Size = new Size(ControlWidth, 23), Text = _radialState.TrackColor };
        tab.Controls.Add(_txtRadialTrackColor);

        // Only four options, that's all a quarter-turn preset makes sense as. 360 isn't offered separately from 0, they're visually identical.
        tab.Controls.Add(NewFieldLabel("Rotation", 163));
        _cmbRadialRotation = new ComboBox { Location = new Point(ControlX, 160), Size = new Size(ControlWidth, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbRadialRotation.Items.Add("0°");
        _cmbRadialRotation.Items.Add("90°");
        _cmbRadialRotation.Items.Add("180°");
        _cmbRadialRotation.Items.Add("270°");
        _cmbRadialRotation.SelectedIndex = (_radialState.RotationDegrees / 90) % 4;
        tab.Controls.Add(_cmbRadialRotation);
    }

    private void SaveAndClose()
    {
        string? parsedTrackColor = ColorParsing.ParseColor(_txtRadialTrackColor.Text);
        if (parsedTrackColor == null)
        {
            MessageBox.Show(this,
                "That doesn't look like a valid colour. Try a hex code like #ffffff, or rgba(255,255,255,0.15).",
                "Overlay settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Reading the fields into locals first, then applying them against whatever's currently live via the mutator, not the stale snapshot loaded when this window opened. Fixes a real bug: saving used to overwrite the whole state object, including remaining/status/lastTick, which could roll back a countdown that was running while this window happened to be open.
        string barDirection = (string)_cmbBarDirection.SelectedItem!;
        int barHeight = (int)_numBarHeight.Value;
        string barWidth = string.IsNullOrWhiteSpace(_txtBarWidth.Text) ? "100%" : _txtBarWidth.Text.Trim();

        string radialDirection = (string)_cmbRadialDirection.SelectedItem!;
        int radialSize = (int)_numRadialSize.Value;
        int radialThickness = (int)_numRadialThickness.Value;
        int radialRotation = _cmbRadialRotation.SelectedIndex * 90;

        OverlayCommandExecutor.UpdateBarAppearance(state =>
        {
            state.Direction = barDirection;
            state.BarHeight = barHeight;
            state.BarWidth = barWidth;
        });

        OverlayCommandExecutor.UpdateRadialAppearance(state =>
        {
            state.Direction = radialDirection;
            state.Size = radialSize;
            state.Thickness = radialThickness;
            state.TrackColor = parsedTrackColor;
            state.RotationDegrees = radialRotation;
        });

        DialogResult = DialogResult.OK;
        Close();
    }
}