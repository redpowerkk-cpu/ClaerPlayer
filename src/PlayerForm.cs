using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ClaerPlayer
{
    internal sealed class Preferences
    {
        public int Volume = 70;
        public string Quality = "balanced";
        public bool Resume = true;
        public Dictionary<string, double> Positions = new Dictionary<string, double>();
        public static string Folder { get { return Environment.GetEnvironmentVariable("CLAERPLAYER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaerPlayer"); } }
        public static Preferences Read()
        {
            try
            {
                var p = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder, "settings.json")));
                if (p == null) return new Preferences();
                p.Volume = Math.Max(0, Math.Min(100, p.Volume));
                if (p.Quality != "high" && p.Quality != "low") p.Quality = "balanced";
                if (p.Positions == null) p.Positions = new Dictionary<string, double>();
                return p;
            }
            catch { return new Preferences(); }
        }
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, "settings.json"), temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this));
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    internal sealed partial class PlayerForm : Form, IMessageFilter
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        readonly Preferences preferences = Preferences.Read();
        readonly List<string> files = new List<string>();
        readonly Dictionary<string, string> properties = new Dictionary<string, string>();
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 7000 };
        readonly Panel header = new Panel(), footer = new Panel(), queuePanel = new Panel(), empty = new Panel();
        readonly VideoPanel video = new VideoPanel();
        readonly Label nameLabel = new Label(), details = new Label(), timeLabel = new Label(), emptyTitle = new Label(), emptyText = new Label();
        readonly ListBox queue = new ListBox();
        readonly SeekBar seek = new SeekBar(), volume = new SeekBar();
        readonly IconButton play = new IconButton("play", "재생 또는 일시정지 (Space)"), mute = new IconButton("volume", "음소거 (M)"), repeat = new IconButton("loop", "한 파일 반복 (R)"), list = new IconButton("list", "재생목록 (Ctrl+L)");
        readonly Button quality = new Button(), speed = new Button();
        readonly Timer clock = new Timer();
        Engine engine;
        int current = -1;
        bool loaded, isPaused = true, isMuted, looping, full, closing, eofHandled;
        double position, duration, resumeAt;
        Rectangle normalBounds;
        FormWindowState normalState;
        readonly string[] initialFiles;
        string lastError = "";
        public PlayerForm(string[] args)
        {
            initialFiles = args; Text = "ClaerPlayer"; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Malgun Gothic", 9f); BackColor = Theme.Back; ForeColor = Theme.Text;
            Size = new Size(1120, 740); MinimumSize = new Size(850, 520); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true; AllowDrop = true;
            header.Dock = DockStyle.Top; header.Height = 60; header.BackColor = Theme.Panel;
            footer.Dock = DockStyle.Bottom; footer.Height = 112; footer.BackColor = Theme.Panel;
            video.Dock = DockStyle.Fill; video.BackColor = Color.Black;
            queuePanel.Dock = DockStyle.Right; queuePanel.Width = 260; queuePanel.BackColor = Theme.Panel; queuePanel.Visible = false;
            Controls.Add(video); Controls.Add(queuePanel); Controls.Add(footer); Controls.Add(header);
            BuildHeader(); BuildTransport(); BuildQueue(); BuildEmpty();
            DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += delegate(object s, DragEventArgs e) { OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop), false); };
            video.AllowDrop = true;
            video.DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            video.DragDrop += delegate(object s, DragEventArgs e) { OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop), false); };
            Shown += OnShown;
            FormClosing += delegate { closing = true; Remember(); preferences.Save(); clock.Stop(); Application.RemoveMessageFilter(this); if (engine != null) engine.Dispose(); };
            FormClosed += delegate { clock.Dispose(); tips.Dispose(); };
            Application.AddMessageFilter(this);
            clock.Interval = 250; clock.Tick += delegate { RefreshTransport(); }; clock.Start();
        }
        Button TextButton(string text, int width, Action action)
        {
            Button b = new Button { Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Cursor = Cursors.Hand, AccessibleName = text };
            b.FlatAppearance.BorderColor = Theme.Hover; b.FlatAppearance.MouseOverBackColor = Theme.Hover;
            b.Click += delegate { action(); }; return b;
        }
        void LabelStyle(Label l, Color color) { l.ForeColor = color; l.AutoEllipsis = true; l.TextAlign = ContentAlignment.MiddleLeft; }
        void AddIcon(Control parent, IconButton b, Action action) { parent.Controls.Add(b); tips.SetToolTip(b, b.AccessibleName); b.Click += delegate { action(); }; }
        void BuildHeader()
        {
            nameLabel.Text = "ClaerPlayer"; nameLabel.Font = new Font(Font, FontStyle.Bold); LabelStyle(nameLabel, Theme.Text);
            details.Text = "동영상 · GIF"; LabelStyle(details, Theme.Muted);
            var open = TextButton("파일 열기", 90, delegate { ChooseFiles(false); });
            var menu = new IconButton("menu", "설정 및 추가 기능"); AddIcon(header, menu, delegate { ShowMenu(menu); });
            header.Controls.Add(nameLabel); header.Controls.Add(details); header.Controls.Add(open);
            Action layout = delegate { nameLabel.SetBounds(24, 7, Math.Max(100, header.Width - 190), 24); details.SetBounds(24, 31, Math.Max(100, header.Width - 190), 21); open.SetBounds(header.Width - 148, 12, 90, 36); menu.SetBounds(header.Width - 51, 11, 38, 38); };
            header.Resize += delegate { layout(); }; layout();
        }
        void BuildTransport()
        {
            seek.BackColor = Theme.Panel; seek.Enabled = false; footer.Controls.Add(seek);
            seek.Seek += delegate(double ratio) { if (loaded && duration > 0) { Cmd("seek", (ratio * duration).ToString("F3", CultureInfo.InvariantCulture), "absolute+exact"); eofHandled = false; } };
            var previous = new IconButton("previous", "이전 파일 (Page Up)"); var next = new IconButton("next", "다음 파일 (Page Down)");
            var fullscreen = new IconButton("full", "전체화면 (F11 / 두 번 클릭)");
            AddIcon(footer, previous, delegate { PlayIndex(current - 1); }); AddIcon(footer, play, TogglePlay); AddIcon(footer, next, delegate { PlayIndex(current + 1); });
            AddIcon(footer, mute, delegate { Cmd("cycle", "mute"); }); AddIcon(footer, repeat, ToggleRepeat); AddIcon(footer, list, ToggleQueue); AddIcon(footer, fullscreen, ToggleFullscreen);
            LabelStyle(timeLabel, Theme.Muted); timeLabel.Text = "00:00 / 00:00"; footer.Controls.Add(timeLabel);
            volume.BackColor = Theme.Panel; volume.AccessibleName = "볼륨"; volume.Value = preferences.Volume / 100.0; footer.Controls.Add(volume);
            volume.Seek += delegate(double v) { preferences.Volume = (int)Math.Round(v * 100); Cmd("set", "volume", preferences.Volume.ToString()); Cmd("set", "mute", "no"); };
            StyleButton(quality, "균형", "화질 설정"); quality.Click += delegate { ShowQuality(); };
            StyleButton(speed, "1.0×", "재생 속도"); speed.Click += delegate { ShowSpeed(); };
            footer.Controls.Add(quality); footer.Controls.Add(speed); UpdateQualityLabel();
            Action layout = delegate
            {
                int w = footer.Width; seek.SetBounds(15, 8, w - 30, 26);
                previous.SetBounds(18, 44, 32, 38); play.SetBounds(54, 43, 42, 40); next.SetBounds(100, 44, 32, 38);
                timeLabel.SetBounds(146, 44, 145, 38);
                int right = w - 18; fullscreen.SetBounds(right - 38, 44, 38, 38); list.SetBounds(right - 80, 44, 38, 38);
                quality.SetBounds(right - 166, 45, 80, 36); speed.SetBounds(right - 236, 45, 64, 36); repeat.SetBounds(right - 278, 44, 38, 38);
                volume.SetBounds(right - 377, 50, 94, 28); mute.SetBounds(right - 418, 44, 36, 38);
                volume.Visible = w >= 900; mute.Left = w >= 900 ? right - 418 : right - 319;
            };
            footer.Resize += delegate { layout(); }; layout();
        }
        void StyleButton(Button b, string text, string accessible)
        {
            b.Text = text; b.AccessibleName = accessible; b.FlatStyle = FlatStyle.Flat; b.BackColor = Theme.Panel; b.ForeColor = Theme.Text; b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Theme.Hover; b.Cursor = Cursors.Hand; tips.SetToolTip(b, accessible);
        }
        void BuildQueue()
        {
            var caption = new Label { Text = "재생목록", Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
            var add = TextButton("파일 추가", 200, delegate { ChooseFiles(true); }); add.Dock = DockStyle.Bottom; add.Height = 44;
            queue.Dock = DockStyle.Fill; queue.BorderStyle = BorderStyle.None; queue.BackColor = Theme.Panel; queue.ForeColor = Theme.Text; queue.Font = Font; queue.DrawMode = DrawMode.OwnerDrawFixed; queue.ItemHeight = 42; queue.HorizontalScrollbar = true; queue.AccessibleName = "재생목록, 두 번 클릭하여 재생";
            queue.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                using (Brush background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Hover : Theme.Panel)) e.Graphics.FillRectangle(background, e.Bounds);
                var rect = new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 24, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, (e.Index + 1).ToString("00") + "  " + Path.GetFileName(files[e.Index]), Font, rect, e.Index == current ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            queue.DoubleClick += delegate { if (queue.SelectedIndex >= 0) PlayIndex(queue.SelectedIndex); };
            queue.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { PlayIndex(queue.SelectedIndex); e.Handled = true; } };
            queuePanel.Controls.Add(queue); queuePanel.Controls.Add(caption); queuePanel.Controls.Add(add);
        }
        void BuildEmpty()
        {
            empty.Dock = DockStyle.Fill; empty.BackColor = Theme.Back; video.Controls.Add(empty);
            emptyTitle.Text = "ClaerPlayer"; emptyTitle.Font = new Font("Segoe UI", 28, FontStyle.Regular); emptyTitle.ForeColor = Theme.Text; emptyTitle.TextAlign = ContentAlignment.MiddleCenter;
            emptyText.Text = "동영상과 GIF를 여기로 끌어 놓으세요."; emptyText.ForeColor = Theme.Muted; emptyText.TextAlign = ContentAlignment.MiddleCenter;
            var open = TextButton("파일 열기", 132, delegate { ChooseFiles(false); }); open.BackColor = Theme.Accent; open.ForeColor = Theme.Back; open.FlatAppearance.BorderSize = 0; open.Height = 42;
            var help = new Label { Text = "Ctrl+O  파일 열기     Space  재생 / 일시정지     F11  전체화면", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter };
            empty.Controls.Add(emptyTitle); empty.Controls.Add(emptyText); empty.Controls.Add(open); empty.Controls.Add(help);
            Action layout = delegate { int center = empty.Height / 2; emptyTitle.SetBounds(20, center - 105, empty.Width - 40, 58); emptyText.SetBounds(20, center - 42, empty.Width - 40, 50); open.SetBounds((empty.Width - 132) / 2, center + 27, 132, 42); help.SetBounds(10, Math.Max(center + 100, empty.Height - 56), empty.Width - 20, 30); };
            empty.Resize += delegate { layout(); }; layout();
        }
        void OnShown(object sender, EventArgs args)
        {
            int dark = 1; try { DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
            PerformLayout(); header.PerformLayout(); footer.PerformLayout();
            // Size events run after native handles and DPI have settled.
            header.Width = ClientSize.Width; footer.Width = ClientSize.Width;
            try
            {
                engine = new Engine(video.Handle, preferences.Quality, false);
                engine.PropertyChanged += delegate(string n, string v) { Dispatch(delegate { ChangeProperty(n, v); }); };
                engine.Loaded += delegate { Dispatch(OnLoaded); };
                engine.Failed += delegate(string error) { Dispatch(delegate { ShowError(error); }); };
                engine.OperationFailed += delegate(string error) { Dispatch(delegate { details.Text = "작업을 완료하지 못했습니다 · " + error; tips.SetToolTip(details, error); }); };
                engine.UserAction += delegate(string action) { Dispatch(delegate { HandleNativeInput(action); }); };
                engine.Ended += delegate { Dispatch(AtEnd); };
                engine.Set("volume", preferences.Volume.ToString());
                if (initialFiles.Length > 0) OpenPaths(initialFiles, false);
            }
            catch (Exception ex) { ShowError("재생 엔진을 시작하지 못했습니다. 프로그램을 다시 설치해 주세요.\n" + ex.Message); }
        }
        void Dispatch(Action action)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!closing && !IsDisposed) action(); }); } catch (InvalidOperationException) { }
        }
        void Cmd(params string[] args) { if (engine != null) engine.Command(args); }
        public static bool Subtitle(string path) { return new[] { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".smi" }.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        void ChooseFiles(bool append)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = append ? "재생목록에 추가" : "동영상 또는 GIF 열기", Multiselect = true, Filter = "영상 및 이미지|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.flv;*.m4v;*.ts;*.m2ts;*.mts;*.mpg;*.mpeg;*.vob;*.ogv;*.3gp;*.gif;*.webp;*.apng;*.png;*.jpg;*.jpeg;*.avif|모든 파일|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenPaths(dialog.FileNames, append);
        }
        void OpenPaths(IEnumerable<string> paths, bool append)
        {
            if (engine == null) { ShowError("재생 엔진이 준비되지 않았습니다. 프로그램을 다시 실행해 주세요."); return; }
            var valid = paths.Where(File.Exists).Select(Path.GetFullPath).ToList();
            var subtitles = valid.Where(Subtitle).ToList(); var media = valid.Where(p => !Subtitle(p)).ToList();
            if (media.Count > 0)
            {
                Remember();
                if (!append) { files.Clear(); queue.Items.Clear(); current = -1; }
                files.AddRange(media); foreach (string path in media) queue.Items.Add(Path.GetFileName(path));
                if (!append || current < 0) PlayIndex(append ? files.Count - media.Count : 0);
            }
            foreach (string sub in subtitles) { if (current >= 0) Cmd("sub-add", sub, "select"); }
            if (valid.Count == 0) MessageBox.Show(this, "파일을 찾을 수 없습니다. 파일 위치를 확인한 후 다시 열어 주세요.", "ClaerPlayer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        void Remember()
        {
            if (current < 0 || current >= files.Count || !loaded || IsGif) return;
            if (preferences.Resume && position > 5 && duration > position + 8) preferences.Positions[files[current]] = position;
            else preferences.Positions.Remove(files[current]);
            while (preferences.Positions.Count > 100) preferences.Positions.Remove(preferences.Positions.Keys.First());
        }
        bool IsGif { get { return current >= 0 && current < files.Count && Path.GetExtension(files[current]).Equals(".gif", StringComparison.OrdinalIgnoreCase); } }
        void PlayIndex(int index)
        {
            if (index < 0 || index >= files.Count || engine == null) return;
            Remember(); current = index; loaded = false; eofHandled = false; duration = 0; position = 0; properties.Clear(); lastError = "";
            resumeAt = 0; if (preferences.Resume && !IsGif) preferences.Positions.TryGetValue(files[index], out resumeAt);
            nameLabel.Text = Path.GetFileName(files[index]); Text = nameLabel.Text + " — ClaerPlayer"; details.Text = "파일을 여는 중…";
            emptyTitle.Text = "파일을 여는 중"; emptyText.Text = "잠시만 기다려 주세요."; empty.Visible = true;
            queue.SelectedIndex = index; queue.Invalidate();
            Cmd("set", "loop-file", looping || IsGif ? "inf" : "no");
            Cmd("loadfile", files[index], "replace"); Cmd("set", "pause", "no");
        }
        void OnLoaded()
        {
            loaded = true; eofHandled = false; empty.Visible = false;
            if (resumeAt > 5) { Cmd("seek", resumeAt.ToString("F3", CultureInfo.InvariantCulture), "absolute+exact"); resumeAt = 0; }
            UpdateDetails(); RefreshTransport();
        }
        void ChangeProperty(string name, string value)
        {
            properties[name] = value; double number;
            if (name == "dropped-files/time" && value != "" && value != "0") { OpenPaths(engine.GetStrings("dropped-files/files"), engine.Get("dropped-files/action") == "append"); return; }
            if (name == "time-pos" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) position = number;
            else if (name == "duration" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) duration = number;
            else if (name == "pause") isPaused = value == "yes";
            else if (name == "mute") isMuted = value == "yes";
            else if (name == "volume" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) { if (!volume.Dragging) volume.Value = number / 100; preferences.Volume = (int)number; }
            else if (name == "speed" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) speed.Text = number.ToString("0.##", CultureInfo.InvariantCulture) + "×";
            else if (name == "eof-reached" && value == "yes") AtEnd();
            if (loaded && (name.StartsWith("video-") || name == "hwdec-current")) UpdateDetails();
        }
        void AtEnd()
        {
            if (!loaded || looping || IsGif || eofHandled) return;
            eofHandled = true;
            if (current + 1 < files.Count) PlayIndex(current + 1);
            else { details.Text = "재생 완료 · Space로 다시 재생"; preferences.Positions.Remove(files[current]); }
        }
        string Property(string key) { string value; return properties.TryGetValue(key, out value) ? value : ""; }
        void UpdateDetails()
        {
            var parts = new List<string>(); string width = Property("video-params/w"), height = Property("video-params/h");
            if (width != "" && height != "") parts.Add(width + " × " + height);
            string codec = Property("video-format"); if (codec != "") parts.Add(codec.ToUpperInvariant());
            if (IsGif) parts.Add("GIF 자동 반복");
            string hw = Property("hwdec-current"); if (hw != "" && hw != "no") parts.Add("하드웨어 가속");
            if (Property("video-params/gamma") == "pq" || Property("video-params/gamma") == "hlg") parts.Add("HDR 소스");
            details.Text = parts.Count == 0 ? "재생 중" : string.Join("   ·   ", parts);
        }
        static string ClockText(double seconds) { if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0; TimeSpan t = TimeSpan.FromSeconds(seconds); return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString() + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00"); }
        void RefreshTransport()
        {
            seek.Enabled = loaded && duration > 0; if (!seek.Dragging) seek.Value = duration > 0 ? position / duration : 0;
            timeLabel.Text = ClockText(seek.Dragging ? seek.Value * duration : position) + " / " + ClockText(duration);
            string glyph = loaded && !isPaused ? "pause" : "play"; if (play.Glyph != glyph) { play.Glyph = glyph; play.Invalidate(); }
            glyph = isMuted ? "muted" : "volume"; if (mute.Glyph != glyph) { mute.Glyph = glyph; mute.Invalidate(); }
            bool active = looping || IsGif; if (repeat.Active != active) { repeat.Active = active; repeat.Invalidate(); }
        }
        void TogglePlay()
        {
            if (current < 0) { ChooseFiles(false); return; }
            if (!loaded) return;
            if (eofHandled || Property("eof-reached") == "yes") { eofHandled = false; Cmd("seek", "0", "absolute+exact"); Cmd("set", "pause", "no"); }
            else Cmd("cycle", "pause");
        }
        void ToggleRepeat() { looping = !looping; Cmd("set", "loop-file", looping || IsGif ? "inf" : "no"); RefreshTransport(); }
        void ToggleQueue() { queuePanel.Visible = !queuePanel.Visible; list.Active = queuePanel.Visible; list.Invalidate(); }
        void ToggleFullscreen()
        {
            if (!full) { normalBounds = Bounds; normalState = WindowState; WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None; Bounds = Screen.FromControl(this).Bounds; full = true; header.Visible = false; }
            else { full = false; FormBorderStyle = FormBorderStyle.Sizable; Bounds = normalBounds; WindowState = normalState; header.Visible = true; }
        }
        ContextMenuStrip PopupMenu() { return new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Font, ShowImageMargin = false }; }
        ToolStripMenuItem Item(ContextMenuStrip menu, string text, Action action, bool check)
        {
            var item = new ToolStripMenuItem(text) { Checked = check }; item.Click += delegate { action(); }; menu.Items.Add(item); return item;
        }
        void ShowPopup(ContextMenuStrip menu, Control anchor) { menu.Closed += delegate { menu.Dispose(); }; menu.Show(anchor, new Point(0, anchor.Height)); }
        void ShowQuality()
        {
            var menu = PopupMenu();
            Item(menu, "균형  ·  원본 비율 / 기본 확대", delegate { SetQuality("balanced"); }, preferences.Quality == "balanced");
            Item(menu, "고화질  ·  정교한 확대 / 계조 보정", delegate { SetQuality("high"); }, preferences.Quality == "high");
            Item(menu, "저전력  ·  단순 확대 / 보정 최소화", delegate { SetQuality("low"); }, preferences.Quality == "low");
            ShowPopup(menu, quality);
        }
        void SetQuality(string value) { preferences.Quality = value; if (engine != null) engine.Quality(value); UpdateQualityLabel(); preferences.Save(); }
        void UpdateQualityLabel() { quality.Text = preferences.Quality == "high" ? "고화질" : preferences.Quality == "low" ? "저전력" : "균형"; }
        void ShowSpeed()
        {
            var menu = PopupMenu(); foreach (string rate in new[] { "0.25", "0.5", "0.75", "1", "1.25", "1.5", "2", "3" }) { string r = rate; Item(menu, r + "×", delegate { Cmd("set", "speed", r); }, false); } ShowPopup(menu, speed);
        }
        void ShowMenu(Control anchor)
        {
            var menu = PopupMenu(); Item(menu, "파일 열기\tCtrl+O", delegate { ChooseFiles(false); }, false);
            Item(menu, "자막 파일 열기\tCtrl+S", ChooseSubtitle, false);
            Item(menu, "다음 자막 트랙\tJ", delegate { Cmd("cycle", "sid"); }, false);
            Item(menu, "다음 오디오 트랙\tA", delegate { Cmd("cycle", "aid"); }, false);
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "화면 캡처 저장…\tCtrl+P", Screenshot, false).Enabled = loaded;
            Item(menu, "항상 위에 표시", delegate { TopMost = !TopMost; }, TopMost);
            Item(menu, "마지막 시청 위치 기억", delegate { preferences.Resume = !preferences.Resume; if (!preferences.Resume) preferences.Positions.Clear(); preferences.Save(); }, preferences.Resume);
            Item(menu, "소프트웨어 디코딩", delegate { Cmd("set", "hwdec", engine != null && engine.Get("hwdec") == "no" ? "auto-safe" : "no"); }, engine != null && engine.Get("hwdec") == "no");
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "파일 정보", Info, false); Item(menu, "도움말 / 단축키", Help, false); ShowPopup(menu, anchor);
        }
        void ChooseSubtitle()
        {
            if (current < 0) { MessageBox.Show(this, "먼저 동영상을 열어 주세요.", "자막"); return; }
            using (var d = new OpenFileDialog { Filter = "자막|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.smi|모든 파일|*.*" }) if (d.ShowDialog(this) == DialogResult.OK) Cmd("sub-add", d.FileName, "select");
        }
        void Screenshot()
        {
            if (!loaded) return;
            using (var d = new SaveFileDialog { Filter = "PNG 이미지|*.png", DefaultExt = "png", FileName = "ClaerPlayer-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png" })
                if (d.ShowDialog(this) == DialogResult.OK) Cmd("screenshot-to-file", d.FileName, "video");
        }
        void Info()
        {
            string text = current < 0 ? "파일을 열면 영상 정보를 확인할 수 있습니다." : files[current] + "\n\n" + details.Text + "\n색 원색: " + Property("video-params/primaries") + "\n전달 함수: " + Property("video-params/gamma") + "\n디코더: " + Property("hwdec-current") + "\n재생 시간: " + ClockText(duration);
            MessageBox.Show(this, text, "파일 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        void Help() { MessageBox.Show(this, "ClaerPlayer 0.1.0 · Windows x64\n\nCtrl+O  파일 열기     Ctrl+L  재생목록\nSpace  재생 / 일시정지     ← / →  5초 이동\n↑ / ↓  볼륨     M  음소거     R  반복\nF11 / F  전체화면     Esc  전체화면 해제\nPage Up / Down  이전 / 다음 파일\nCtrl+S  자막 추가     J  자막 전환     A  오디오 전환\nCtrl+P  화면 캡처     .  다음 프레임\n\nGIF는 자동 반복합니다. 화질은 원본·모니터·GPU에 따라 달라집니다.\n고화질 모드는 확대와 계조 보정을 추가하며 GPU 사용량이 늘 수 있습니다.\nDRM 보호 파일과 손상 파일은 지원하지 않습니다.\n\nmpv / FFmpeg 기반 · 라이선스와 출처는 설치 폴더의 licenses를 참고하세요.", "ClaerPlayer 도움말", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void ShowError(string error)
        {
            if (lastError == error) return; lastError = error; loaded = false;
            emptyTitle.Text = "파일을 재생하지 못했습니다"; emptyText.Text = "파일이 손상되었거나 지원되지 않는 형식일 수 있습니다.\n다른 파일을 열어 보세요."; empty.Visible = true; empty.BringToFront();
            details.Text = "재생 오류 · " + error.Replace("\n", " "); tips.SetToolTip(details, error); RefreshTransport();
        }
        public bool PreFilterMessage(ref Message m)
        {
            if (closing || !ContainsFocus || Form.ActiveForm != this) return false;
            // libmpv owns a native child window; filter its mouse/keyboard messages too.
            if (m.Msg == 0x0203 && !empty.Visible && (m.HWnd == video.Handle || IsChild(video.Handle, m.HWnd))) { ToggleFullscreen(); return true; }
            if (m.Msg == 0x0201 && !empty.Visible && IsChild(video.Handle, m.HWnd)) { Focus(); return false; }
            if (m.Msg != 0x0100 && m.Msg != 0x0104) return false;
            Keys key = (Keys)m.WParam.ToInt32(); Keys modifiers = Control.ModifierKeys;
            if (modifiers == Keys.Control)
            {
                if (key == Keys.O) { ChooseFiles(false); return true; } if (key == Keys.L) { ToggleQueue(); return true; }
                if (key == Keys.S) { ChooseSubtitle(); return true; } if (key == Keys.P) { Screenshot(); return true; } return false;
            }
            if (modifiers != Keys.None) return false;
            if (key == Keys.F11 || key == Keys.F) { ToggleFullscreen(); return true; }
            if (key == Keys.Escape && full) { ToggleFullscreen(); return true; }
            if (queue.Focused && (key == Keys.Up || key == Keys.Down || key == Keys.Enter)) return false;
            if ((seek.Focused || volume.Focused) && (key == Keys.Left || key == Keys.Right)) return false;
            switch (key)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Left: Cmd("seek", "-5", "relative+exact"); return true;
                case Keys.Right: Cmd("seek", "5", "relative+exact"); return true;
                case Keys.Up: Cmd("add", "volume", "5"); return true;
                case Keys.Down: Cmd("add", "volume", "-5"); return true;
                case Keys.M: Cmd("cycle", "mute"); return true;
                case Keys.R: ToggleRepeat(); return true;
                case Keys.J: Cmd("cycle", "sid"); return true;
                case Keys.A: Cmd("cycle", "aid"); return true;
                case Keys.OemPeriod: Cmd("frame-step"); return true;
                case Keys.PageDown: PlayIndex(current + 1); return true;
                case Keys.PageUp: PlayIndex(current - 1); return true;
            }
            return false;
        }
        void HandleNativeInput(string action)
        {
            switch (action)
            {
                case "play": TogglePlay(); break;
                case "back": Cmd("seek", "-5", "relative+exact"); break;
                case "forward": Cmd("seek", "5", "relative+exact"); break;
                case "louder": Cmd("add", "volume", "5"); break;
                case "quieter": Cmd("add", "volume", "-5"); break;
                case "mute": Cmd("cycle", "mute"); break;
                case "repeat": ToggleRepeat(); break;
                case "full": ToggleFullscreen(); break;
                case "escape": if (full) ToggleFullscreen(); break;
                case "previous": PlayIndex(current - 1); break;
                case "next": PlayIndex(current + 1); break;
                case "open": ChooseFiles(false); break;
                case "queue": ToggleQueue(); break;
                case "subtitle": ChooseSubtitle(); break;
                case "screenshot": Screenshot(); break;
                case "subtrack": Cmd("cycle", "sid"); break;
                case "audiotrack": Cmd("cycle", "aid"); break;
                case "frame": Cmd("frame-step"); break;
            }
        }
        [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);
    }
}
