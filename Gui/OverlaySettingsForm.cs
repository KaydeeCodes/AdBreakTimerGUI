using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;
using AdBreakTimerGUI.Server;

namespace AdBreakTimerGUI.Gui;

public class OverlaySettingsForm : Form
{
    private readonly BarState _barState;
    private readonly RadialState _radialState;

    private ComboBox _cmbBarDirection = null!;
    private NumericUpDown _numBarHeight = null!;
    private TextBox _txtBarWidth = null!;
    private TextBox _txtBarColor = null!;
    private TextBox _txtBarFinishColor = null!;
    private TextBox _txtBarBgColor = null!;
    private CheckBox _chkBarFlash = null!;
    private NumericUpDown _numBarFlashDuration = null!;

    private ComboBox _cmbRadialDirection = null!;
    private NumericUpDown _numRadialSize = null!;
    private NumericUpDown _numRadialThickness = null!;
    private TextBox _txtRadialTrackColor = null!;
    private ComboBox _cmbRadialRotation = null!;
    private TextBox _txtRadialColor = null!;
    private TextBox _txtRadialFinishColor = null!;
    private TextBox _txtRadialBgColor = null!;
    private CheckBox _chkRadialFlash = null!;
    private NumericUpDown _numRadialFlashDuration = null!;

    private const int LabelX = 16;
    private const int ControlX = 230;
    private const int ControlWidth = 180;

    private const int ColorBoxWidth = 120;
    private const int ColorPickButtonX = ControlX + ColorBoxWidth + 4;
    private const int ColorPickButtonWidth = 56;

    public OverlaySettingsForm()
    {
        _barState = JsonStore.Load<BarState>(Paths.BarFile) ?? new BarState();
        _radialState = JsonStore.Load<RadialState>(Paths.RadialFile) ?? new RadialState();

        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Overlay settings";
        ClientSize = new Size(460, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        var tabs = new TabControl { Location = new Point(12, 12), Size = new Size(436, 430) };

        var barTab = new TabPage("Bar");
        var radialTab = new TabPage("Radial");
        tabs.TabPages.Add(barTab);
        tabs.TabPages.Add(radialTab);

        BuildBarTab(barTab);
        BuildRadialTab(radialTab);

        Controls.Add(tabs);

        var btnSave = new Button { Location = new Point(12, 452), Size = new Size(220, 32), Text = "Save" };
        btnSave.Click += (_, _) => SaveAndClose();

        var btnCancel = new Button { Location = new Point(238, 452), Size = new Size(220, 32), Text = "Cancel" };
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

    private static TextBox NewColorRow(TabPage tab, string label, int y, string initialValue)
    {
        tab.Controls.Add(NewFieldLabel(label, y + 3));

        var textBox = new TextBox
        {
            Location = new Point(ControlX, y),
            Size = new Size(ColorBoxWidth, 23),
            Text = initialValue
        };
        tab.Controls.Add(textBox);

        var pickButton = new Button
        {
            Location = new Point(ColorPickButtonX, y - 1),
            Size = new Size(ColorPickButtonWidth, 25),
            Text = "Pick..."
        };
        pickButton.Click += (_, _) => OpenColorPicker(textBox);
        tab.Controls.Add(pickButton);

        return textBox;
    }

    private static void OpenColorPicker(TextBox target)
    {
        using var dlg = new ColorDialog { FullOpen = true };
        dlg.Color = TryParseToDrawingColor(target.Text, Color.White);
        if (dlg.ShowDialog() == DialogResult.OK)
            target.Text = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
    }

    private static Color TryParseToDrawingColor(string cssColor, Color fallback)
    {
        try { return ColorTranslator.FromHtml(cssColor.Trim()); }
        catch { return fallback; }
    }

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
            Size = new Size(420, 34),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "Width accepts a CSS value, e.g. 100% or 800px."
        };
        tab.Controls.Add(hint);

        _txtBarColor = NewColorRow(tab, "Running colour", 160, _barState.PreferredColor);
        _txtBarFinishColor = NewColorRow(tab, "Finish colour", 196, _barState.FinishColor);
        _txtBarBgColor = NewColorRow(tab, "Background colour", 232, _barState.BgColor);

        _chkBarFlash = new CheckBox { Location = new Point(LabelX, 268), AutoSize = true, Text = "Flash on finish", Checked = _barState.FlashOnFinish };
        tab.Controls.Add(_chkBarFlash);

        tab.Controls.Add(NewFieldLabel("Flash duration (s)", 300));
        _numBarFlashDuration = new NumericUpDown { Location = new Point(ControlX, 297), Size = new Size(ControlWidth, 23), Minimum = 0, Maximum = 300, Value = Math.Clamp(_barState.FlashDuration, 0, 300) };
        tab.Controls.Add(_numBarFlashDuration);
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

        _txtRadialTrackColor = NewColorRow(tab, "Track colour", 124, _radialState.TrackColor);

        var trackHint = new Label
        {
            Location = new Point(LabelX, 148),
            Size = new Size(420, 22),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5F),
            Text = "The picker only does solid colours, type rgba(...) here directly for transparency."
        };
        tab.Controls.Add(trackHint);

        tab.Controls.Add(NewFieldLabel("Rotation", 179));
        _cmbRadialRotation = new ComboBox { Location = new Point(ControlX, 176), Size = new Size(ControlWidth, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbRadialRotation.Items.Add("0°");
        _cmbRadialRotation.Items.Add("90°");
        _cmbRadialRotation.Items.Add("180°");
        _cmbRadialRotation.Items.Add("270°");
        _cmbRadialRotation.SelectedIndex = (_radialState.RotationDegrees / 90) % 4;
        tab.Controls.Add(_cmbRadialRotation);

        _txtRadialColor = NewColorRow(tab, "Running colour", 215, _radialState.PreferredColor);
        _txtRadialFinishColor = NewColorRow(tab, "Finish colour", 251, _radialState.FinishColor);
        _txtRadialBgColor = NewColorRow(tab, "Background colour", 287, _radialState.BgColor);

        _chkRadialFlash = new CheckBox { Location = new Point(LabelX, 323), AutoSize = true, Text = "Flash on finish", Checked = _radialState.FlashOnFinish };
        tab.Controls.Add(_chkRadialFlash);

        tab.Controls.Add(NewFieldLabel("Flash duration (s)", 355));
        _numRadialFlashDuration = new NumericUpDown { Location = new Point(ControlX, 352), Size = new Size(ControlWidth, 23), Minimum = 0, Maximum = 300, Value = Math.Clamp(_radialState.FlashDuration, 0, 300) };
        tab.Controls.Add(_numRadialFlashDuration);
    }

    private bool TryParseColorField(TextBox box, string fieldName, out string parsed)
    {
        string? result = ColorParsing.ParseColor(box.Text);
        parsed = result ?? "";
        if (result == null)
        {
            MessageBox.Show(this,
                $"\"{box.Text}\" isn't a valid colour for {fieldName}. Try a hex code like #ffffff, or rgba(255,255,255,0.15).",
                "Overlay settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private void SaveAndClose()
    {
        if (!TryParseColorField(_txtBarColor, "Bar running colour", out string barColor)) return;
        if (!TryParseColorField(_txtBarFinishColor, "Bar finish colour", out string barFinishColor)) return;
        if (!TryParseColorField(_txtBarBgColor, "Bar background colour", out string barBgColor)) return;
        if (!TryParseColorField(_txtRadialTrackColor, "Radial track colour", out string radialTrackColor)) return;
        if (!TryParseColorField(_txtRadialColor, "Radial running colour", out string radialColor)) return;
        if (!TryParseColorField(_txtRadialFinishColor, "Radial finish colour", out string radialFinishColor)) return;
        if (!TryParseColorField(_txtRadialBgColor, "Radial background colour", out string radialBgColor)) return;

        string barDirection = (string)_cmbBarDirection.SelectedItem!;
        int barHeight = (int)_numBarHeight.Value;
        string barWidth = string.IsNullOrWhiteSpace(_txtBarWidth.Text) ? "100%" : _txtBarWidth.Text.Trim();
        bool barFlash = _chkBarFlash.Checked;
        int barFlashDuration = (int)_numBarFlashDuration.Value;

        string radialDirection = (string)_cmbRadialDirection.SelectedItem!;
        int radialSize = (int)_numRadialSize.Value;
        int radialThickness = (int)_numRadialThickness.Value;
        int radialRotation = _cmbRadialRotation.SelectedIndex * 90;
        bool radialFlash = _chkRadialFlash.Checked;
        int radialFlashDuration = (int)_numRadialFlashDuration.Value;

        OverlayCommandExecutor.UpdateBarAppearance(state =>
        {
            state.Direction = barDirection;
            state.BarHeight = barHeight;
            state.BarWidth = barWidth;
            state.PreferredColor = barColor;
            state.FinishColor = barFinishColor;
            state.BgColor = barBgColor;
            state.FlashOnFinish = barFlash;
            state.FlashDuration = barFlashDuration;
        });

        OverlayCommandExecutor.UpdateRadialAppearance(state =>
        {
            state.Direction = radialDirection;
            state.Size = radialSize;
            state.Thickness = radialThickness;
            state.TrackColor = radialTrackColor;
            state.RotationDegrees = radialRotation;
            state.PreferredColor = radialColor;
            state.FinishColor = radialFinishColor;
            state.BgColor = radialBgColor;
            state.FlashOnFinish = radialFlash;
            state.FlashDuration = radialFlashDuration;
        });

        DialogResult = DialogResult.OK;
        Close();
    }
}