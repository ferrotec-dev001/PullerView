using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PullerView
{
    /// <summary>カメラ接続・画像保存の設定。VisionView.ini の読み書きは VisionIni、受信・保存は CvCore40.cs（VisionView.cs から使用）。
    /// 項目は CvImageSaver v1.1.0 の設定（settings.json）に対応させている。</summary>
    public static class CameraCfg
    {
        // [Camera] — VB_FERROTEC との接続（PullerView.ini [Host]）とは別
        public static int[] Ip = { 192, 168, 0, 10 }; public static int Port = 8501, Delimiter = 0 /*0=CR 1=CR+LF*/, CameraNo = 1 /*0=全カメラ 1〜4*/, ReconnectSec = 5, TimeoutSec = 5;
        public static bool RejectChecksum = false, RawDump = false;
        // [Image]
        public static bool Save = true; public static int SaveMode = 0 /*0=全数 1=間引き*/, SaveEvery = 5, MaxFiles = 10, KeepDays = 30, CleanupHours = 1; public static string Folder = "Image";
        // [Playback]
        public static string PlayFolder = "Image\\sample"; public static int PlayMs = 600; public static bool PlayLoop = true;
    }

    /// <summary>設定タブ「カメラ設定」: カメラ接続（TCP/IP）、画像保存、ファイル再生。</summary>
    public class CameraSettingsPanel : DarkPanel
    {
        TextBox dirBox, playBox; bool loading;
        public CameraSettingsPanel()
        {
            dirBox = MakeBox(); playBox = MakeBox();
            dirBox.TextChanged += delegate { if (!loading) { string d = dirBox.Text.Trim(); if (d != "") CameraCfg.Folder = d; Touch(); } };
            playBox.TextChanged += delegate { if (!loading) { string d = playBox.Text.Trim(); if (d != "") CameraCfg.PlayFolder = d; Touch(); } };
        }
        TextBox MakeBox() { TextBox b = new TextBox(); b.BorderStyle = BorderStyle.None; b.BackColor = FIELD; b.ForeColor = WH; b.Font = new Font(UiFont, 11f); Controls.Add(b); return b; }
        protected override void OnTouched() { M.Fire(); }
        void Set(Action a) { a(); Touch(); }
        void Card(Graphics g, Rectangle r, string title) { Box(g, r, CARD, null, 10, 1); Txt(g, title, 10f, WH, r.X + 16, r.Y + 10, true); using (Pen p = new Pen(LINE)) g.DrawLine(p, r.X + 16, r.Y + 32, r.Right - 16, r.Y + 32); }
        void Label(Graphics g, string s, int x, int y) { Txt(g, s, 8.5f, GR, x, y, false); }
        void TxtW(Graphics g, string s, float pt, Color c, Rectangle r) { using (Font f = F(pt, false)) using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r); }
        void TextRow(Graphics g, TextBox box, Rectangle r, string text)
        {
            Box(g, r, FIELD, LINE, 7, 1);
            loading = true; if (box.Text != text) box.Text = text; loading = false;
            box.Bounds = new Rectangle(r.X + 10, r.Y + (r.Height - 20) / 2, r.Width - 20, 20);
        }
        void StepBtns(Graphics g, int x, int y, NumField f)
        {
            string[] t = { "−", "＋" }; double[] d = { -1, 1 };
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = new Rectangle(x + i * 40, y, 36, 34); Box(g, r, Hover(r) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 8, 1); TxtC(g, t[i], 12f, WH, r, false);
                double dd = d[i] * f.Step;
                Add(r, delegate { double v = Math.Round(f.Get() + dd, 3); if (v < f.Min || v > f.Max) return; if (active == f) buf = ""; f.Set(v); Touch(); }, null);
            }
        }
        NumField Num(Graphics g, string key, string label, Func<double> get, Action<double> set, Rectangle r, double min, double max, double step, string suffix, bool enabled)
        {
            if (!enabled) { Box(g, r, CARD, LINE, 7, 1); Txt(g, "—", 11f, GR, r.X + 10, r.Y + r.Height / 2 - 10, false); return null; }
            NumField f = Field(key, label, get, set, true, r, min, max, step); NumBox(g, r, f, suffix); return f;
        }
        void Quick(Graphics g, int x, int y, int[] q, Func<int> get, Action<int> set)
        {
            for (int i = 0; i < q.Length; i++) { Rectangle r = new Rectangle(x + (i % 2) * 56, y + (i / 2) * 18, 52, 15); int v = q[i]; bool on = get() == v; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, v.ToString(), 7.5f, on ? CY : WH, r, false); Add(r, delegate { Set(delegate { set(v); }); }, null); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; BeginPaint(g);
            int colW = 340, midX = 364, midW = 336; bool cam = VisionCfg.Source != 1, play = VisionCfg.Source == 1;
            // ===== 左列: カメラ接続（TCP/IP） =====
            Rectangle c1 = new Rectangle(12, 12, colW, Height - 24); Card(g, c1, Lx.T("カメラ接続（TCP/IP）", "Camera connection (TCP/IP)", "相机连接（TCP/IP）"));
            int x = c1.X + 16, y = c1.Y;
            Label(g, Lx.T("接続先 IP アドレス（画像処理コントローラ）", "IP address (vision controller)", "连接目标 IP 地址（图像处理控制器）"), x, y + 40);
            for (int i = 0; i < 4; i++)
            {
                int k = i; Num(g, "ip" + i, "IP " + (i + 1) + "/4", delegate { return CameraCfg.Ip[k]; }, delegate (double v) { CameraCfg.Ip[k] = (int)v; }, new Rectangle(x + i * 78, y + 58, 66, 34), 0, 255, 1, null, true);
                if (i < 3) Txt(g, ".", 12f, WH, x + i * 78 + 66, y + 66, true);
            }
            Label(g, Lx.T("画像出力ポート番号", "Image output port", "图像输出端口号"), x, y + 104); Label(g, Lx.T("デリミタ", "Delimiter", "分隔符"), x + 164, y + 104);
            Num(g, "port", Lx.T("画像出力ポート番号", "Image output port", "图像输出端口号"), delegate { return CameraCfg.Port; }, delegate (double v) { CameraCfg.Port = (int)v; }, new Rectangle(x, y + 122, 112, 34), 1, 65535, 1, null, true);
            Segment(g, x + 164, y + 123, 32, new[] { "CR", "CR+LF" }, CameraCfg.Delimiter, delegate (int i) { Set(delegate { CameraCfg.Delimiter = i; }); }, 9f, 22);
            Label(g, Lx.T("対象カメラ番号（全て＝どのカメラ番号の画像も読み込む）", "Camera No. (All = accept images from any camera)", "对象相机编号（全部＝任何相机编号的图像都读取）"), x, y + 170);
            Segment(g, x, y + 188, 32, new[] { Lx.T("全て", "All", "全部"), "1", "2", "3", "4" }, CameraCfg.CameraNo, delegate (int i) { Set(delegate { CameraCfg.CameraNo = i; }); }, 9f, 22);
            Label(g, Lx.T("再接続間隔 [秒]", "Reconnect interval [s]", "重连间隔 [秒]"), x, y + 234); Label(g, Lx.T("接続タイムアウト [秒]", "Connect timeout [s]", "连接超时 [秒]"), x + 192, y + 234);
            NumField fr = Num(g, "rc", Lx.T("再接続間隔 [秒]", "Reconnect interval [s]", "重连间隔 [秒]"), delegate { return CameraCfg.ReconnectSec; }, delegate (double v) { CameraCfg.ReconnectSec = (int)v; }, new Rectangle(x, y + 252, 96, 32), 1, 600, 1, "s", true);
            StepBtns(g, x + 102, y + 252, fr);
            Num(g, "to", Lx.T("接続タイムアウト [秒]", "Connect timeout [s]", "连接超时 [秒]"), delegate { return CameraCfg.TimeoutSec; }, delegate (double v) { CameraCfg.TimeoutSec = (int)v; }, new Rectangle(x + 192, y + 252, 112, 32), 1, 60, 1, "s", true);
            ToggleRow(g, x, y + 302, colW - 32, Lx.T("チェックサム不一致の画像を破棄する", "Discard images with checksum errors", "丢弃校验和不一致的图像"), CameraCfg.RejectChecksum, delegate (bool v) { Set(delegate { CameraCfg.RejectChecksum = v; }); });
            ToggleRow(g, x, y + 340, colW - 32, Lx.T("診断用：受信した生データを保存する", "Diagnostics: save raw received data", "诊断用：保存接收的原始数据"), CameraCfg.RawDump, delegate (bool v) { Set(delegate { CameraCfg.RawDump = v; }); });
            // 接続状態と接続テスト
            Rectangle st = new Rectangle(x, y + 384, colW - 32, 62); Box(g, st, FIELD, null, 8, 1);
            Txt(g, Lx.T("接続状態", "Status", "连接状态"), 8f, GR, st.X + 10, st.Y + 8, false);
            string live = VV.Running && cam ? VV.Status() : null; Color sc = !cam ? AMB : VV.CamConnected ? GraphSettingsPanel.GRN : GR; using (SolidBrush b = new SolidBrush(sc)) g.FillEllipse(b, st.X + 12, st.Y + 30, 12, 12);
            string ipS = CameraCfg.Ip[0] + "." + CameraCfg.Ip[1] + "." + CameraCfg.Ip[2] + "." + CameraCfg.Ip[3] + ":" + CameraCfg.Port;
            Txt(g, live != null ? live : cam ? Lx.T("未接続  ", "Not connected  ", "未连接  ") + ipS : Lx.T("使用しない（入力元≠カメラ）", "Not used (source ≠ camera)", "不使用（输入源≠相机）"), 9f, cam ? WH : AMB, st.X + 30, st.Y + 26, false);
            if (VV.TestMsg != "") Txt(g, VV.TestMsg, 7.5f, CY, st.X + 30, st.Y + 45, false);
            string tb = Lx.T("接続テスト", "Test", "连接测试"); int bw = (int)TW(g, tb, 9f) + 24; Rectangle bt = new Rectangle(st.Right - 8 - bw, st.Y + 8, bw, 26);
            Box(g, bt, Hover(bt) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 7, 1); TxtC(g, tb, 9f, WH, bt, false); Add(bt, delegate { VV.TestConnect(delegate { try { BeginInvoke(new Action(Invalidate)); } catch (Exception) { } }); Invalidate(); }, null);
            TxtW(g, Lx.T("VB_FERROTEC との接続（PullerView.ini [Host]）とは別の設定です。画像入力元が「カメラ（TCP）」のときに使います。デリミタはコントローラ側の Ethernet 設定に合わせます。同じ画像出力ポートに他のソフト（CV-H5N 等）が接続していると受信できません。VisionView.ini [Camera]（キー名は仮）",
                "Separate from the VB_FERROTEC connection (PullerView.ini [Host]). Used when the image source is “Camera (TCP)”. The delimiter must match the controller's Ethernet setting. Images cannot be received while other software (e.g. CV-H5N) is connected to the same image output port. VisionView.ini [Camera] (key names provisional)",
                "与 VB_FERROTEC 的连接（PullerView.ini [Host]）是不同的设定。图像输入源为「相机（TCP）」时使用。分隔符与控制器侧的 Ethernet 设定一致。其他软件（CV-H5N 等）连接到同一图像输出端口时无法接收。VisionView.ini [Camera]（键名暂定）"), 7.5f, GR, new Rectangle(x, y + 456, colW - 32, 120));
            // ===== 中列上: 画像保存 =====
            Rectangle c2 = new Rectangle(midX, 12, midW, 384); Card(g, c2, Lx.T("画像保存", "Image saving", "图像保存"));
            x = c2.X + 16; y = c2.Y; bool sv = CameraCfg.Save;
            Txt(g, Lx.T("受信した画像を保存する", "Save received images", "保存接收的图像"), 9.5f, WH, x, y + 44, false);
            Rectangle tg = new Rectangle(c2.Right - 16 - 56, y + 42, 56, 26); Toggle(g, tg, sv); Add(tg, delegate { Set(delegate { CameraCfg.Save = !CameraCfg.Save; }); }, null);
            Label(g, Lx.T("保存する画像", "Images to save", "保存的图像"), x, y + 80); Label(g, Lx.T("間引き [枚に 1 枚]", "Thinning [1 of N]", "抽取 [每 N 张 1 张]"), x + 192, y + 80);
            Segment(g, x, y + 98, 32, new[] { Lx.T("全数", "All", "全部"), Lx.T("間引き", "Thinned", "抽取") }, CameraCfg.SaveMode, delegate (int i) { Set(delegate { CameraCfg.SaveMode = i; }); }, 9f, 22);
            Num(g, "evr", Lx.T("間引き [枚に 1 枚]", "Thinning [1 of N]", "抽取 [每 N 张 1 张]"), delegate { return CameraCfg.SaveEvery; }, delegate (double v) { CameraCfg.SaveEvery = (int)v; }, new Rectangle(x + 192, y + 98, 112, 32), 2, 1000, 1, null, sv && CameraCfg.SaveMode == 1);
            Label(g, Lx.T("保存先フォルダ（相対は EXE 基準）", "Folder (relative to EXE)", "保存文件夹（相对 EXE）"), x, y + 144);
            TextRow(g, dirBox, new Rectangle(x, y + 162, midW - 32, 36), CameraCfg.Folder);
            Label(g, Lx.T("最大保持枚数 [枚]（0＝無制限。超過分は古い順に削除）", "Max files (0 = unlimited; oldest deleted first)", "最大保留张数 [张]（0＝无限制。超过时从最旧的删除）"), x, y + 210);
            NumField fm = Num(g, "mx", Lx.T("最大保持枚数 [枚]", "Max files", "最大保留张数 [张]"), delegate { return CameraCfg.MaxFiles; }, delegate (double v) { CameraCfg.MaxFiles = (int)v; }, new Rectangle(x, y + 228, 96, 32), 0, 1000000, 10, null, true);
            StepBtns(g, x + 102, y + 228, fm); Quick(g, x + 192, y + 227, new[] { 10, 30, 100, 1000 }, delegate { return CameraCfg.MaxFiles; }, delegate (int v) { CameraCfg.MaxFiles = v; });
            Label(g, Lx.T("保持日数 [日]（0＝無効）", "Keep days (0 = off)", "保留天数 [天]（0＝无效）"), x, y + 272); Label(g, Lx.T("自動削除の間隔 [時間]", "Cleanup interval [h]", "自动删除间隔 [小时]"), x + 192, y + 272);
            NumField fk = Num(g, "kd", Lx.T("保持日数 [日]", "Keep days", "保留天数 [天]"), delegate { return CameraCfg.KeepDays; }, delegate (double v) { CameraCfg.KeepDays = (int)v; }, new Rectangle(x, y + 290, 96, 32), 0, 3650, 1, Lx.T("日", "d", "天"), true);
            StepBtns(g, x + 102, y + 290, fk);
            Num(g, "ch", Lx.T("自動削除の間隔 [時間]", "Cleanup interval [h]", "自动删除间隔 [小时]"), delegate { return CameraCfg.CleanupHours; }, delegate (double v) { CameraCfg.CleanupHours = (int)v; }, new Rectangle(x + 192, y + 290, 112, 32), 1, 24, 1, "h", true);
            double gb = CameraCfg.MaxFiles * 1216.0 * 1025 * 3 / 1e9; string est = CameraCfg.MaxFiles == 0 ? Lx.T("無制限", "unlimited", "无限制") : CameraCfg.MaxFiles + Lx.T(" 枚 ≒ ", " files ≈ ", " 张 ≒ ") + (gb < 1 ? (gb * 1000).ToString("0") + " MB" : gb.ToString("0.0") + " GB");
            TxtW(g, Lx.T("OK／NG は判断せず 1 つのフォルダに保存。ファイル名: YYYYMMDDHHMMSS_工程名.bmp　容量の目安: " + est + "（1216×1025・24bit）。VisionView.ini [Image]（キー名は仮）", "One folder, no OK/NG sorting. File: YYYYMMDDHHMMSS_process.bmp   Estimate: " + est + " (1216×1025, 24-bit). VisionView.ini [Image] (keys provisional)", "不区分 OK／NG，保存到同一个文件夹。文件名: YYYYMMDDHHMMSS_工程名.bmp　容量估算: " + est + "（1216×1025・24bit）。VisionView.ini [Image]（键名暂定）"), 7.5f, GR, new Rectangle(x, y + 332, midW - 32, 46));
            // ===== 中列下: ファイル再生 =====
            Rectangle c3 = new Rectangle(midX, 408, midW, Height - 12 - 408); Card(g, c3, Lx.T("ファイル再生（カメラ未接続時の確認用）", "File playback (without camera)", "文件回放（相机未连接时确认用）"));
            y = c3.Y;
            Label(g, Lx.T("再生フォルダ", "Playback folder", "回放文件夹"), x, y + 40);
            TextRow(g, playBox, new Rectangle(x, y + 58, midW - 32, 34), CameraCfg.PlayFolder);
            Label(g, Lx.T("再生間隔 [ms]", "Interval [ms]", "回放间隔 [ms]"), x, y + 100);
            NumField fp = Num(g, "pm", Lx.T("再生間隔 [ms]", "Interval [ms]", "回放间隔 [ms]"), delegate { return CameraCfg.PlayMs; }, delegate (double v) { CameraCfg.PlayMs = (int)v; }, new Rectangle(x, y + 118, 96, 32), 100, 60000, 100, "ms", true);
            StepBtns(g, x + 102, y + 118, fp);
            string lp = Lx.T("繰り返し", "Loop", "循环"); Txt(g, lp, 9f, WH, x + 192, y + 124, false); Rectangle tl = new Rectangle(c3.Right - 16 - 56, y + 121, 56, 26); Toggle(g, tl, CameraCfg.PlayLoop); Add(tl, delegate { Set(delegate { CameraCfg.PlayLoop = !CameraCfg.PlayLoop; }); }, null);
            TxtW(g, Lx.T("画像入力元が「ファイル再生」のときに使います。VisionView.ini [Playback]（キー名は仮）", "Used when the image source is “File playback”. VisionView.ini [Playback] (key names provisional)", "图像输入源为「文件回放」时使用。VisionView.ini [Playback]（键名暂定）"), 7.5f, play ? GR : GR, new Rectangle(x, y + 158, midW - 32, 32));
            // ===== 右列: テンキー =====
            DrawNumPad(g, 712, Lx.T("・フォルダはキーボードで入力", "- Folders: keyboard", "・文件夹用键盘输入"));
        }
    }
}
