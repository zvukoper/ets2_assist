using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        private TableLayoutPanel? _mainButtonGrid;
        private Button? _btnMapEditor2;

        static MainForm()
        {
            // Do not depend on Application.Idle: while ETS2 is being detected/started,
            // the UI can be busy enough that Idle is delayed. Bootstrap the Map Editor 2
            // button from a short-lived background waiter instead.
            var thread = new Thread(BootstrapMapEditor2Button)
            {
                IsBackground = true,
                Name = "ETS2_Assist_MapEditor2Bootstrap"
            };
            thread.Start();
        }

        private static void BootstrapMapEditor2Button()
        {
            while (true)
            {
                try
                {
                    var form = Current;
                    if (form == null)
                    {
                        Thread.Sleep(40);
                        continue;
                    }

                    // Component.Disposed is an event and cannot be read as a boolean.
                    if (form.IsDisposed || form.Disposing)
                        return;

                    if (!form.IsHandleCreated || form.btnMapEditor == null || form.btnMapEditor.IsDisposed)
                    {
                        Thread.Sleep(40);
                        continue;
                    }

                    form.BeginInvoke(new Action(form.AddMapEditor2Button));
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(40);
                }
                catch
                {
                    Thread.Sleep(80);
                }
            }
        }

        private void AddMapEditor2Button()
        {
            if (_mainButtonGrid != null || btnMapEditor == null || IsDisposed || Disposing)
                return;

            SuspendLayout();
            try
            {
                var parent = btnMapEditor.Parent ?? this;
                var location = btnMapEditor.Location;

                _mainButtonGrid = new TableLayoutPanel
                {
                    Name = "mainButtonGrid",
                    ColumnCount = 2,
                    RowCount = 0,
                    Location = location,
                    Size = new Size(230, 0),
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    BackColor = Color.Transparent,
                    GrowStyle = TableLayoutPanelGrowStyle.AddRows
                };
                _mainButtonGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
                _mainButtonGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));

                parent.Controls.Add(_mainButtonGrid);

                var controls = new Control[]
                {
                    btnStart, btnStop, btnRestartOverlay, btnMinimize, btnExit,
                    btnRefreshTracks, btnRandomTarget, btnRandomTarget2, btnRandomTarget3,
                    btnRandomTarget4, btnCheckTargets, btnShowMap, btnShowHybrid,
                    btnTestPause, btnResetRecordingOrigin, btnMapEditor, btnLaunchAR
                };

                int row = 0;
                foreach (var control in controls)
                {
                    if (control == null || control.IsDisposed) continue;
                    if (control.Parent != null) control.Parent.Controls.Remove(control);
                    control.Dock = DockStyle.Fill;
                    control.Margin = new Padding(0, 0, 0, 6);
                    _mainButtonGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _mainButtonGrid.Controls.Add(control, 0, row);
                    _mainButtonGrid.SetColumnSpan(control, 2);
                    row++;
                }

                // AR v2 row: button and its checkbox share one grid row.
                if (btnAr2 != null && !btnAr2.IsDisposed)
                {
                    if (btnAr2.Parent != null) btnAr2.Parent.Controls.Remove(btnAr2);
                    btnAr2.Dock = DockStyle.Fill;
                    btnAr2.Margin = new Padding(0, 0, 0, 6);
                    _mainButtonGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _mainButtonGrid.Controls.Add(btnAr2, 0, row);

                    if (chkAr2Grid != null && !chkAr2Grid.IsDisposed)
                    {
                        if (chkAr2Grid.Parent != null) chkAr2Grid.Parent.Controls.Remove(chkAr2Grid);
                        chkAr2Grid.AutoSize = true;
                        chkAr2Grid.Anchor = AnchorStyles.Left;
                        chkAr2Grid.Margin = new Padding(0, 0, 0, 6);
                        _mainButtonGrid.Controls.Add(chkAr2Grid, 1, row);
                    }
                    row++;
                }

                _btnMapEditor2 = new Button
                {
                    Name = "btnMapEditor2",
                    Text = "Редактор карты 2",
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.FromArgb(166, 166, 166),
                    BackColor = Color.FromArgb(60, 60, 60),
                    UseVisualStyleBackColor = false,
                    Margin = new Padding(0, 0, 0, 6),
                    TabStop = true
                };
                _btnMapEditor2.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
                _btnMapEditor2.Click += (_, _) => OpenMapEditor2();
                _mainButtonGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _mainButtonGrid.Controls.Add(_btnMapEditor2, 0, row);
                _mainButtonGrid.SetColumnSpan(_btnMapEditor2, 2);

                parent.Controls.SetChildIndex(_mainButtonGrid, 0);

                int requiredHeight = _mainButtonGrid.GetPreferredSize(new Size(230, 0)).Height + location.Y + 16;
                this.AutoScroll = true;
                this.MinimumSize = new Size(MinimumSize.Width, Math.Max(MinimumSize.Height, Math.Min(requiredHeight, Screen.FromControl(this).WorkingArea.Height)));
                if (ClientSize.Height < requiredHeight && requiredHeight <= Screen.FromControl(this).WorkingArea.Height)
                    ClientSize = new Size(ClientSize.Width, requiredHeight);
            }
            finally
            {
                ResumeLayout(true);
                PerformLayout();
            }
        }

        private void OpenMapEditor2()
        {
            try
            {
                using var form = new MapEditor2Form();
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                AppendLog($"Map Editor 2 failed to open: {ex.Message}");
            }
        }
    }
}
