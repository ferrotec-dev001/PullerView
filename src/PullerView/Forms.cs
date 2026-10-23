using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PullerView
{

public class BackdropForm : Form
{
    Image shot;
    public BackdropForm()
    {
        Text = "VB_FERROTEC.exe - frmAutoStatus（画面イメージ・モック 1280×1024）"; DoubleBuffered = true; BackColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = true; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        try { shot = Image.FromStream(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PullerView.Resources.frmAutoStatus.png")); } catch (Exception) { }
        ClientSize = new Size(1280, 1024); Rectangle wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.Left, wa.Top);
        FormClosing += delegate(object s, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; M.AutoStatusShown = false; M.Log("frmAutoStatus を閉じました（非表示）→ PullerView も非表示"); M.Fire(); } };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (shot != null) e.Graphics.DrawImage(shot, 0, 0, 1280, 1024);
        using (Font f = new Font("Yu Gothic UI", 9f)) e.Graphics.DrawString("※ VB_FERROTEC 実画面のスクリーンショット（式样書より）。ダミー表示。", f, Brushes.Red, 300, 1000);
    }
}

// ---- モック操作パネル（画面遷移の確認用。実機には存在しない） ----
public class ControlPanel : Form
{
    BackdropForm back = new BackdropForm(); PullerViewForm pv = new PullerViewForm();
    ComboBox procBox; CheckBox cRun, cShown, cComm, cPv, cAuto, cPause; NumericUpDown nSec, nSpd; ComboBox[] inj = new ComboBox[4]; TextBox log; bool busy;
    public ControlPanel()
    {
        Text = "PullerView モック操作パネル"; Font = new Font("Yu Gothic UI", 9f); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        StartPosition = FormStartPosition.Manual; Rectangle wa = Screen.PrimaryScreen.WorkingArea; ClientSize = new Size(380, Math.Min(760, wa.Height - 60)); Location = new Point(Math.Min(wa.Left + 1290, wa.Right - 400), wa.Top);
        int y = 8;
        Label t = new Label(); t.Text = "※ 擬似データによる画面確認用です（VB_FERROTEC とは通信しません）"; t.SetBounds(10, y, 360, 32); t.ForeColor = Color.DimGray; Controls.Add(t); y += 34;
        GroupBox g1 = G("装置・接続状態の模擬", y, 120); cRun = Chk(g1, "VB_FERROTEC.exe 実行中", 12, 22, true); cShown = Chk(g1, "frmAutoStatus 表示中", 12, 46, true); cComm = Chk(g1, "Remoting 通信 正常（OFF=通信断）", 12, 70, true); cPv = Chk(g1, "PullerView 表示（ユーザー操作）", 12, 94, true); y += 126;
        GroupBox g2 = G("工程", y, 150); procBox = new ComboBox(); procBox.DropDownStyle = ComboBoxStyle.DropDownList; procBox.SetBounds(12, 22, 340, 24);
        for (int i = 0; i < M.Procs.Length; i++) procBox.Items.Add(M.Abbrs[i] + "  " + M.Procs[i] + (i == M.OTHER ? "  (AUTO/MANUAL/WAITMATERIALS/MAINTENANCE)" : "")); procBox.SelectedIndex = M.Proc; g2.Controls.Add(procBox);
        cAuto = Chk(g2, "NECK→…→TAIL→ANEEL を自動進行  工程時間(秒)", 12, 54, false); nSec = N(g2, 300, 52, 5, 300, M.ProcSec);
        cPause = Chk(g2, "長さの進行を一時停止", 12, 80, false); Label l = new Label(); l.Text = "長さ進行 mm/秒"; l.AutoSize = true; l.Location = new Point(12, 112); g2.Controls.Add(l); nSpd = N(g2, 120, 108, 0, 500, M.SpeedMm); y += 156;
        GroupBox g3 = G("異常の注入（グラフ別 = DataSetting.ini の [工程n]）", y, 170);
        string[] kinds = { "正常", "管理上限超過", "管理下限超過", "規格上限超過", "規格下限超過", "判定不能(---)" };
        for (int i = 0; i < 4; i++) { Label gl = new Label(); gl.Text = "グラフ" + (i + 1); gl.AutoSize = true; gl.Location = new Point(12, 28 + i * 28); g3.Controls.Add(gl); inj[i] = new ComboBox(); inj[i].DropDownStyle = ComboBoxStyle.DropDownList; inj[i].SetBounds(80, 24 + i * 28, 150, 24); inj[i].Items.AddRange(kinds); inj[i].SelectedIndex = 0; g3.Controls.Add(inj[i]); int k = i; inj[i].SelectedIndexChanged += delegate { M.Inject[k] = inj[k].SelectedIndex; }; }
        Button ra = new Button(); ra.Text = "全て正常に戻す"; ra.SetBounds(240, 24, 112, 28); ra.Click += delegate { foreach (ComboBox c in inj) c.SelectedIndex = 0; }; g3.Controls.Add(ra);
        Button demo = new Button(); demo.Text = "デモ（複数異常）"; demo.SetBounds(240, 58, 112, 28); demo.Click += delegate { inj[0].SelectedIndex = 3; inj[1].SelectedIndex = 2; inj[2].SelectedIndex = 1; inj[3].SelectedIndex = 4; }; g3.Controls.Add(demo); y += 176;
        Button b1 = new Button(); b1.Text = "最小化／最大化 切替"; b1.SetBounds(10, y, 130, 28); b1.Click += delegate { M.Maximized = !M.Maximized; M.Fire(); }; Controls.Add(b1);
        Button b2 = new Button(); b2.Text = "DataSetting.ini 再読込"; b2.SetBounds(146, y, 130, 28); b2.Click += delegate { M.LoadIni(M.IniPath); }; Controls.Add(b2);
        Button b3 = new Button(); b3.Text = "終了"; b3.SetBounds(282, y, 88, 28); b3.Click += delegate { Close(); }; Controls.Add(b3); y += 36;
        log = new TextBox(); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.SetBounds(10, y, 360, ClientSize.Height - y - 8); log.Font = new Font("Yu Gothic UI", 8.5f); Controls.Add(log);

        EventHandler any = delegate { if (busy) return; M.Running = cRun.Checked; M.AutoStatusShown = cShown.Checked; M.Comm = cComm.Checked; M.PvVisible = cPv.Checked; M.AutoAdvance = cAuto.Checked; M.Paused = cPause.Checked; M.ProcSec = (int)nSec.Value; M.SpeedMm = (int)nSpd.Value; M.Fire(); };
        foreach (CheckBox c in new CheckBox[] { cRun, cShown, cComm, cPv, cAuto, cPause }) c.CheckedChanged += any; nSec.ValueChanged += any; nSpd.ValueChanged += any;
        cComm.CheckedChanged += delegate { M.Log("Remoting 通信: " + (cComm.Checked ? "回復" : "断")); };
        cRun.CheckedChanged += delegate { M.Log("VB_FERROTEC.exe: " + (cRun.Checked ? "実行中" : (M.S.Standalone ? "未実行 → 単体起動状態（データなし表示）" : "未実行 → PullerView 非表示"))); };
        procBox.SelectedIndexChanged += delegate { if (!busy) M.SetProc(procBox.SelectedIndex, "手動"); };
        M.LogEvent += delegate(string s) { if (IsHandleCreated) log.AppendText(DateTime.Now.ToString("HH:mm:ss ") + s + "\r\n"); };
        M.Changed += Sync;
        Timer tm = new Timer(); tm.Interval = 1000; tm.Tick += delegate { M.Tick(); }; tm.Start();
        Load += delegate
        {
            string ini = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataSetting.ini");
            M.LoadIni(ini);
            pv.Anchor0 = back; back.LocationChanged += delegate { pv.FollowAnchor(); }; back.SizeChanged += delegate { pv.FollowAnchor(); };
            back.Show(); pv.Surf.GearClicked += OpenSettings; pv.SettingsRequested += OpenSettings; pv.Show(); pv.FollowAnchor(); pv.ApplyState();
            string tst = Environment.GetEnvironmentVariable("PVMOCK_TEST"); if (tst != null) { string[] a = tst.Split(','); M.SetProc(int.Parse(a[0]), "test"); for (int i = 1; i < a.Length && i <= 4; i++) { inj[i - 1].SelectedIndex = int.Parse(a[i]); } if (a.Length > 5) M.Maximized = a[5] == "1"; }
            M.Log("起動: PullerView モック（既定位置=式样 r2 図のとおり frmAutoStatus 左上から (300,67)（式样 r3「ウィンドウデフォルト位置」図））。歯車=設定 / 右上ボタン=最小化・最大化 / タイトル・コメント部ドラッグ=移動");
        };
        FormClosed += delegate { Application.Exit(); };
    }
    void OpenSettings() { using (SettingsForm f = new SettingsForm()) { f.Location = SettingsForm.PlaceNear(pv); f.ShowDialog(this); } M.Fire(); }
    void Sync()
    {
        busy = true; if (procBox.SelectedIndex != M.Proc) procBox.SelectedIndex = M.Proc; busy = false;
        back.Visible = M.Running && M.AutoStatusShown;
        pv.ApplyState(); back.Invalidate();
    }
    GroupBox G(string text, int y, int h) { GroupBox g = new GroupBox(); g.Text = text; g.SetBounds(10, y, 360, h); Controls.Add(g); return g; }
    CheckBox Chk(Control p, string t, int x, int y, bool v) { CheckBox c = new CheckBox(); c.Text = t; c.AutoSize = true; c.Location = new Point(x, y); c.Checked = v; p.Controls.Add(c); return c; }
    NumericUpDown N(Control p, int x, int y, int mn, int mx, int v) { NumericUpDown n = new NumericUpDown(); n.SetBounds(x, y, 56, 24); n.Minimum = mn; n.Maximum = mx; n.Value = v; p.Controls.Add(n); return n; }
}
}
