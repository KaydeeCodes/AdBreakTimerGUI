using AdBreakTimerGUI.Config;
using AdBreakTimerGUI.Engine;

namespace AdBreakTimerGUI.Gui;

// The window that opens when I click "Bar / Radial settings..." on the
// main window. This is for the overlay's visual appearance, direction,
// size, thickness, that sort of thing, not for the day to day ad break
// timing itself (that stays on the main window, since it's what I
// actually touch often).
//
// Built the same way as MainForm, laid out in code rather than the
// visual designer, so I can read the whole thing top to bottom without
// hunting through a hidden .designer.cs file.
public class OverlaySettingsForm : Form
{
    // Loaded once when the window opens, only written back to disk if
    // Save is clicked. Closing with the X or clicking Cancel leaves
    // the files untouched, since nothing writes until SaveAndClose runs.
    private readonly BarState _barState;
    private readonly RadialState _radialState;

    private ComboBox _cmbBarDirection = null!;
    private NumericUpDown _numBarHeight = null!;
    private TextBox _txtBarWidth = null!;

    private ComboBox _cmbRadialDirection = null!;
    private NumericUpDown _numRadialSize = null!;
    private NumericUpDown _numRadialThickness = null!;
    private TextBox _txtRadialTrackColor = null!;

    // The x position where every control column starts, and how wide
    // each control is. Pulled out as constants rather than repeated
    // magic numbers, since I widened this once already after the
    // longer labels (like "Thickness (% of diameter)") were getting
    // squeezed against the control next to them.
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
        ClientSize = new Size(440, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        var tabs = new TabControl
        {
            Location = new Point(12, 12),
            Size = new Size(416, 220)
        };

        var barTab = new TabPage("Bar");
        var radialTab = new TabPage("Radial");
        tabs.TabPages.Add(barTab);
        tabs.TabPages.Add(radialTab);

        BuildBarTab(barTab);
        BuildRadialTab(radialTab);

        Controls.Add(tabs);

        // ---- Save / Cancel ----
        var btnSave = new Button
        {
            Location = new Point(12, 242),
            Size = new Size(200, 32),
            Text = "Save"
        };
        btnSave.Click += (_, _) => SaveAndClose();

        var btnCancel = new Button
        {
            Location = new Point(228, 242),
            Size = new Size(200, 32),
            Text = "Cancel"
        };
        btnCancel.Click += (_, _) => Close();

        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        // Lets Enter and Escape trigger these without me having to
        // click directly on them.
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    // A small helper so every label on both tabs gets a fixed width
    // rather than AutoSize, which is what was letting the longer ones
    // (like "Thickness (% of diameter)") run into the control next to
    // them instead of wrapping or getting cut off cleanly.
    private static Label NewFieldLabel(string text, int y) => new()
    {
        Location = new Point(LabelX, y),
        Size = new Size(ControlX - LabelX - 8, 20),
        Text = text
    };

    private void BuildBarTab(TabPage tab)
    {
        tab.Controls.Add(NewFieldLabel("Fill direction", 19));
        _cmbBarDirection = new ComboBox
        {
            Location = new Point(ControlX, 16),
            Size = new Size(ControlWidth, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbBarDirection.Items.Add("drain");
        _cmbBarDirection.Items.Add("fill");
        _cmbBarDirection.SelectedItem = _barState.Direction is "drain" or "fill" ? _barState.Direction : "drain";
        tab.Controls.Add(_cmbBarDirection);

        tab.Controls.Add(NewFieldLabel("Bar height (px)", 55));
        _numBarHeight = new NumericUpDown
        {
            Location = new Point(ControlX, 52),
            Size = new Size(ControlWidth, 23),
            Minimum = 1,
            Maximum = 500,
            Value = Math.Clamp(_barState.BarHeight, 1, 500)
        };
        tab.Controls.Add(_numBarHeight);

        tab.Controls.Add(NewFieldLabel("Bar width", 91));
        _txtBarWidth = new TextBox
        {
            Location = new Point(ControlX, 88),
            Size = new Size(ControlWidth, 23),
            Text = _barState.BarWidth
        };
        tab.Controls.Add(_txtBarWidth);

        // A note for future me on why this one's text rather than a
        // number. BarWidth is a raw CSS value (e.g. "100%" or "800px"),
        // not a plain pixel count like BarHeight, since I wanted the
        // option of a bar that doesn't stretch the full width of the
        // Browser Source.
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
        _cmbRadialDirection = new ComboBox
        {
            Location = new Point(ControlX, 16),
            Size = new Size(ControlWidth, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbRadialDirection.Items.Add("cw");
        _cmbRadialDirection.Items.Add("ccw");
        _cmbRadialDirection.SelectedItem = _radialState.Direction is "cw" or "ccw" ? _radialState.Direction : "cw";
        tab.Controls.Add(_cmbRadialDirection);

        tab.Controls.Add(NewFieldLabel("Size (% of viewport)", 55));
        _numRadialSize = new NumericUpDown
        {
            Location = new Point(ControlX, 52),
            Size = new Size(ControlWidth, 23),
            Minimum = 5,
            Maximum = 100,
            Value = Math.Clamp(_radialState.Size, 5, 100)
        };
        tab.Controls.Add(_numRadialSize);

        tab.Controls.Add(NewFieldLabel("Thickness (% of diameter)", 91));
        _numRadialThickness = new NumericUpDown
        {
            Location = new Point(ControlX, 88),
            Size = new Size(ControlWidth, 23),
            Minimum = 1,
            Maximum = 50,
            Value = Math.Clamp(_radialState.Thickness, 1, 50)
        };
        tab.Controls.Add(_numRadialThickness);

        tab.Controls.Add(NewFieldLabel("Track colour", 127));
        _txtRadialTrackColor = new TextBox
        {
            Location = new Point(ControlX, 124),
            Size = new Size(ControlWidth, 23),
            Text = _radialState.TrackColor
        };
        tab.Controls.Add(_txtRadialTrackColor);
    }

    private void SaveAndClose()
    {
        // Track colour is the only free text field here that actually
        // needs validating, everything else is either a dropdown or a
        // number already constrained by its NumericUpDown's Minimum
        // and Maximum.
        string? parsedTrackColor = ColorParsing.ParseColor(_txtRadialTrackColor.Text);
        if (parsedTrackColor == null)
        {
            MessageBox.Show(this,
                "That doesn't look like a valid colour. Try a hex code like #ffffff, or rgba(255,255,255,0.15).",
                "Overlay settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _barState.Direction = (string)_cmbBarDirection.SelectedItem!;
        _barState.BarHeight = (int)_numBarHeight.Value;
        _barState.BarWidth = string.IsNullOrWhiteSpace(_txtBarWidth.Text) ? "100%" : _txtBarWidth.Text.Trim();

        _radialState.Direction = (string)_cmbRadialDirection.SelectedItem!;
        _radialState.Size = (int)_numRadialSize.Value;
        _radialState.Thickness = (int)_numRadialThickness.Value;
        _radialState.TrackColor = parsedTrackColor;

        JsonStore.Save(_barState, Paths.BarFile);
        JsonStore.Save(_radialState, Paths.RadialFile);

        DialogResult = DialogResult.OK;
        Close();
    }
}