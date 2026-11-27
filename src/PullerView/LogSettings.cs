using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PullerView
{
    /// <summary>設定タブ「ログ設定」（v48）: 装置名称、生データ CSV、ZIP／保存日数、イベントログ。</summary>
    public class LogSettingsPanel : DarkPanel
    {
        TextBox devBox, dirBox; bool loading;
        public LogSettingsPanel()
        {
            devBox = MakeBox(); dirBox = MakeBox();
            devBox.TextChanged += delegate { if (!loading) { M.S.Device = devBox.Text.Trim(); Touch(); } };
            dirBox.TextChanged += delegate { if (!loading) { string d = dirBox.Text.Trim(); if (d != "") M.S.CsvDir = d; Touch(); } };
        }
        TextBox MakeBox() { TextBox b = new TextBox(); b.BorderStyle = BorderStyle.None; b.BackColor = FIELD; b.ForeColor = WH; b.Font = new Font(UiFont, 11f); Controls.Add(b); return b; }
        protected override void OnTouched() { M.Fire(); }
        void Set(Action a) { a(); Touch(); }
        void Card(Graphics g, Rectangle r, string title) { Box(g, r, CARD, null, 10, 1); Txt(g, title, 10f, WH, r.X + 16, r.Y + 10, true); using (Pen p = new Pen(LINE)) g.DrawLine(p, r.X + 16, r.Y + 32, r.Right - 16, r.Y + 32); }
        void Label(Graphics g, string s, int x, int y) { Txt(g, s, 8.5f, GR, x, y, false); }
        void TextRow(Graphics g, TextBox box, Rectangle r, string text)
        {
            Box(g, r, FIELD, LINE, 7, 1);
            loading = true; if (box.Text != text) box.Text = text; loading = false;
            box.Bounds = new Rectangle(r.X + 10, r.Y + (r.Height - 20) / 2, r.Width - 20, 20);
        }
        /// <summary>V2-v3: フォルダ欄。入力欄の右に「フォルダを選択」ボタンを付ける。keepAbsolute＝選んだフォルダを常に絶対パスで入れる。</summary>
        void FolderRow(Graphics g, TextBox box, Rectangle r, string text, string title, bool keepAbsolute)
        {
            TextRow(g, box, new Rectangle(r.X, r.Y, FolderFieldW(r), r.Height), text); FolderBtn(g, r, box, title, keepAbsolute);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; BeginPaint(g);
            Settings s = M.S; int colW = 340, midX = 364, midW = 336;
            // ===== 左列: 装置 =====
            Rectangle c1 = new Rectangle(12, 12, colW, 170); Card(g, c1, Lx.T("装置", "Equipment", "设备"));
            int x = c1.X + 16, y = c1.Y;
            Label(g, Lx.T("装置名称", "Equipment name", "设备名称"), x, y + 40);
            TextRow(g, devBox, new Rectangle(x, y + 58, colW - 32, 36), s.Device);
            TxtW(g, Lx.T("イベント文の先頭に付けます（例: Y10 液面位置が UCL 50.5mm を超えました）  PullerView.ini [Device] name", "Prefixed to event messages (e.g. Y10 Melt level exceeded UCL 50.5mm).  PullerView.ini [Device] name", "加在事件文本开头（例: Y10 液面位置超过了 UCL 50.5mm）  PullerView.ini [Device] name"), 7.5f, GR, new Rectangle(x, y + 100, colW - 32, 36));
            Txt(g, Lx.T("※ 文字入力は欄をタップしてキーボードで入力", "※ Tap the field and type with the keyboard", "※ 点击输入栏后用键盘输入"), 7.5f, GR, x, y + 140, false);
            // ===== 左列: 生データ CSV =====
            Rectangle c2 = new Rectangle(12, 194, colW, Height - 12 - 194); Card(g, c2, Lx.T("生データ CSV（毎秒）", "Raw data CSV (every second)", "原始数据 CSV（每秒）"));
            y = c2.Y;
            Txt(g, Lx.T("CSV に記録する", "Record to CSV", "记录到 CSV"), 9.5f, WH, x, y + 44, false);
            Rectangle tg = new Rectangle(c2.Right - 16 - 56, y + 42, 56, 26); Toggle(g, tg, s.CsvLog); Add(tg, delegate { Set(delegate { s.CsvLog = !s.CsvLog; }); }, null);
            Label(g, Lx.T("保存フォルダ（相対は EXE 基準。全ログ・JPG・ZIP を保存）", "Folder (relative to EXE; all logs, JPG, ZIP)", "保存文件夹（相对 EXE。所有日志、JPG、ZIP）"), x, y + 80);
            FolderRow(g, dirBox, new Rectangle(x, y + 98, colW - 32, 36), s.CsvDir, Lx.T("保存フォルダ", "Log folder", "保存文件夹"), false);
            Label(g, Lx.T("文字コード", "Encoding", "编码"), x, y + 146);
            string[] encs = { "utf-8", "utf-16", "shift_jis" }; int es = 0; for (int i = 0; i < encs.Length; i++) if (s.CsvEnc.ToLower().Replace("-", "").Replace("_", "") == encs[i].Replace("-", "").Replace("_", "")) es = i;
            Segment(g, x, y + 164, 32, new[] { "UTF-8 (BOM)", "UTF-16", "Shift_JIS" }, es, delegate (int i) { Set(delegate { s.CsvEnc = encs[i]; }); }, 9f, 14);
            Txt(g, Lx.T("前日以前の CSV を ZIP 化", "Zip CSV of previous days", "将前一天以前的 CSV 压缩为 ZIP"), 9.5f, WH, x, y + 214, false);
            Rectangle tz = new Rectangle(c2.Right - 16 - 56, y + 212, 56, 26); Toggle(g, tz, s.CsvZip); Add(tz, delegate { Set(delegate { s.CsvZip = !s.CsvZip; }); }, null);
            Label(g, Lx.T("保存日数（超過分は CSV・ZIP・ログ・JPG とも自動削除）", "Keep days (older CSV/ZIP/logs/JPG are deleted)", "保存天数（超过的 CSV/ZIP/日志/JPG 自动删除）"), x, y + 252);
            NumField fk = Field("keep", Lx.T("保存日数 [日]", "Keep days", "保存天数 [天]"), delegate { return s.CsvKeepDays; }, delegate (double v) { s.CsvKeepDays = (int)v; }, true, new Rectangle(x, y + 270, 96, 32), 1, 3650, 1);
            NumBox(g, fk.R, fk, Lx.T("日", "days", "天")); StepBtns(g, x + 102, y + 270, fk);
            int[] qk = { 30, 90, 180, 365 }; for (int i = 0; i < qk.Length; i++) { Rectangle r = new Rectangle(x + 192 + (i % 2) * 56, y + 269 + (i / 2) * 18, 52, 15); int q = qk[i]; bool on = s.CsvKeepDays == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString(), 7.5f, on ? CY : WH, r, false); Add(r, delegate { Set(delegate { s.CsvKeepDays = q; }); }, null); }
            TxtW(g, Lx.T("ファイル: PV_yyyyMMdd.csv → 翌日 PV_yyyyMMdd.zip。動作ログ: PullerView.log → 日付が変わると PullerView_yyyyMMdd.zip", "Files: PV_yyyyMMdd.csv → next day PV_yyyyMMdd.zip. App log: PullerView.log → PullerView_yyyyMMdd.zip on date change", "文件: PV_yyyyMMdd.csv → 次日 PV_yyyyMMdd.zip。运行日志: PullerView.log → 日期变化时 PullerView_yyyyMMdd.zip"), 7.5f, GR, new Rectangle(x, y + 312, colW - 32, 48));
            // ===== 中列: イベントログ =====
            Rectangle c3 = new Rectangle(midX, 12, midW, 386); Card(g, c3, Lx.T("イベントログ", "Event log", "事件日志"));
            x = c3.X + 16; y = c3.Y;
            TxtW(g, Lx.T("監視項目の上下限超過・復帰・判定開始／停止、工程切替を口語文で記録します。メイン画面のタイトル行アイコン（グラフ／イベントログ）で表示を切り替えます。", "Limit violations, recoveries, judgement start/stop and process changes are recorded in plain sentences. Switch the main window with the title-row icons (Graph / Event log).", "以口语句记录监视项目的超限、恢复、判定开始/停止和工序切换。用主窗口标题行图标（图表/事件日志）切换显示。"), 7.5f, GR, new Rectangle(x, y + 40, midW - 32, 48));
            Label(g, Lx.T("1 ファイルの上限 [kB]（超過で _2, _3 … に分割）", "Max file size [kB] (then _2, _3 …)", "单个文件上限 [kB]（超过后 _2、_3 …）"), x, y + 90);
            NumField fe = Field("evkb", Lx.T("イベントログ上限 [kB]", "Event log max [kB]", "事件日志上限 [kB]"), delegate { return s.EventMaxKb; }, delegate (double v) { s.EventMaxKb = (int)v; }, true, new Rectangle(x, y + 108, 96, 32), 10, 100000, 10);
            NumBox(g, fe.R, fe, "kB"); StepBtns(g, x + 102, y + 108, fe);
            int[] qe = { 100, 200, 500, 1000 }; for (int i = 0; i < qe.Length; i++) { Rectangle r = new Rectangle(x + 192 + (i % 2) * 56, y + 107 + (i / 2) * 18, 52, 15); int q = qe[i]; bool on = s.EventMaxKb == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString(), 7.5f, on ? CY : WH, r, false); Add(r, delegate { Set(delegate { s.EventMaxKb = q; }); }, null); }
            TxtW(g, Lx.T("ファイル: Log\\yyyyMMdd_event.log（タブ区切り: 日時／本文／工程／PV／SV／結晶長さ or 経過分）。記録言語は表示言語と同じ。", "File: Log\\yyyyMMdd_event.log (tab: time / text / process / PV / SV / length or minutes). Language = display language.", "文件: Log\\yyyyMMdd_event.log（制表符: 时间/文本/工序/PV/SV/长度或分钟）。记录语言与显示语言相同。"), 7.5f, GR, new Rectangle(x, y + 150, midW - 32, 40));
            Txt(g, Lx.T("文例", "Examples", "示例"), 8.5f, WH, x, y + 198, true);
            string[] ex = {
                Lx.T("2027/10/31 12:14:45 Y10 液面位置がUCL50.5mmを超えました。現在は51.2mmです。", "2027/10/31 12:14:45 Y10 Melt level exceeded UCL 50.5mm. Now 51.2mm.", "2027/10/31 12:14:45 Y10 液面位置超过了UCL50.5mm。当前为51.2mm。"),
                Lx.T("2027/10/31 12:15:45 Y10 液面位置がUCL50.5mm以下に復帰しました。", "2027/10/31 12:15:45 Y10 Melt level returned below UCL 50.5mm.", "2027/10/31 12:15:45 Y10 液面位置已恢复到UCL50.5mm以下。"),
                Lx.T("2027/10/31 12:14:45 Y10 液面位置の判定条件に達しました。判定を開始します。", "2027/10/31 12:14:45 Y10 Melt level reached the judgement condition. Starting judgement.", "2027/10/31 12:14:45 Y10 液面位置已达到判定条件，开始判定。") };
            for (int i = 0; i < ex.Length; i++) { Rectangle r = new Rectangle(x, y + 216 + i * 44, midW - 32, 38); Box(g, r, FIELD, null, 8, 1); TxtW(g, ex[i], 7.5f, i == 0 ? AMB : i == 1 ? GRN : CY, new Rectangle(r.X + 8, r.Y + 4, r.Width - 16, r.Height - 8)); }
            Txt(g, Lx.T("メモリ上の表示件数: ", "Events in memory: ", "内存中的事件数: ") + EventLog.Items.Count + " / " + EventLog.Max, 7.5f, GR, x, y + 356, false);
            // ===== 中列下: 画像の保存 OK_SNAP／NG_SNAP（VisionView.ini [Snap]。V2-v10）=====
            Rectangle c4 = new Rectangle(midX, 410, midW, Height - 12 - 410); Card(g, c4, Lx.T("画像の保存（OK_SNAP／NG_SNAP）", "Image snapshots (OK_SNAP / NG_SNAP)", "图像保存（OK_SNAP／NG_SNAP）"));
            y = c4.Y;
            Label(g, Lx.T("フォルダごとの上限枚数 [枚]（0＝無制限）", "Max files per folder (0 = unlimited)", "每个文件夹的上限张数 [张]（0＝无限制）"), x, y + 40);
            NumField fs = Field("snapmax", Lx.T("上限枚数 [枚]", "Max files", "上限张数 [张]"), delegate { return SnapCfg.MaxFiles; }, delegate (double v) { SnapCfg.MaxFiles = (int)v; }, true, new Rectangle(x, y + 58, 96, 32), 0, 1000000, 100);
            NumBox(g, fs.R, fs, null); StepBtns(g, x + 102, y + 58, fs);
            int[] qs = { 200, 500, 1000, 2000 }; for (int i = 0; i < qs.Length; i++) { Rectangle r = new Rectangle(x + 192 + (i % 2) * 56, y + 57 + (i / 2) * 18, 52, 15); int q = qs[i]; bool on = SnapCfg.MaxFiles == q; Box(g, r, on ? SEL : Hover(r) ? BTN : FIELD, on ? CY : LINE, 5, 1); TxtC(g, q.ToString(), 7.5f, on ? CY : WH, r, false); Add(r, delegate { Set(delegate { SnapCfg.MaxFiles = q; }); }, null); }
            Label(g, Lx.T("上限に達したとき", "When the limit is reached", "达到上限时"), x, y + 100);
            Segment(g, x, y + 118, 32, new[] { Lx.T("保存しない", "Do not save", "不保存"), Lx.T("古い画像から削除して保存", "Delete oldest, then save", "删除最旧的图像后保存") }, SnapCfg.OnFull, delegate (int i) { Set(delegate { SnapCfg.OnFull = i; }); }, 9f, 14);
            double sgb = SnapCfg.MaxFiles * 1216.0 * 1025 * 3 / 1e9; string sest = SnapCfg.MaxFiles == 0 ? Lx.T("無制限", "unlimited", "无限制") : (sgb < 1 ? (sgb * 1000).ToString("0") + " MB" : sgb.ToString("0.0") + " GB");
            TxtW(g, Lx.T("容量の目安: 1 フォルダ " + sest + "（1/2 サイズ画像）。フルサイズ画像は約 4 倍。VisionView.ini [Snap]", "Estimate: " + sest + " per folder (half-size images); full-size images take about 4 times as much. VisionView.ini [Snap]", "容量估算: 每个文件夹 " + sest + "（1/2 尺寸图像）。全尺寸图像约为 4 倍。VisionView.ini [Snap]"), 7.5f, GR, new Rectangle(x, y + 158, midW - 32, 34));
            // ===== 右列: テンキー =====
            DrawNumPad(g, 712, Lx.T("・装置名称はキーボードで入力。フォルダは\n  欄の右のボタンで選択、またはキーボードで入力", "- Equipment name: keyboard. Folder: button\n  at right of the field, or keyboard", "・设备名称用键盘输入。文件夹用输入栏\n  右侧的按钮选择，或用键盘输入"));
        }
        void StepBtns(Graphics g, int x, int y, NumField f)
        {
            string[] t = { "−", "＋" }; double[] d = { -1, 1 };
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = new Rectangle(x + i * 40, y, 36, 34); Box(g, r, Hover(r) ? Color.FromArgb(0x5A, 0x5A, 0x66) : BTN, null, 8, 1); TxtC(g, t[i], 12f, WH, r, false);
                double dd = d[i] * Math.Max(1, f.Step);
                Add(r, delegate { double v = f.Get() + dd; if (v < f.Min || v > f.Max) return; if (active == f) buf = ""; f.Set(v); Touch(); }, null);
            }
        }
        /// <summary>折返しテキスト。</summary>
        void TxtW(Graphics g, string s, float pt, Color c, Rectangle r) { using (Font f = F(pt, false)) using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r); }
    }
}
