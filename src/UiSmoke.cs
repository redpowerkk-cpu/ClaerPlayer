using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClaerPlayer
{
    internal sealed partial class PlayerForm
    {
        async Task<bool> Until(Func<bool> predicate, int timeout)
        {
            Stopwatch watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < timeout) { if (predicate()) return true; await Task.Delay(50); } return predicate();
        }
        void RenderForm(string path) { using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path, ImageFormat.Png); } }
        public async Task RunUiSmoke(string media, string output)
        {
            Directory.CreateDirectory(output); var results = new List<object>(); int failures = 0;
            Action<string, bool, string> check = delegate(string name, bool pass, string detail) { results.Add(new { name = name, passed = pass, detail = detail }); if (!pass) failures++; };
            try
            {
                await Task.Delay(400); check("engine initialized with D3D11", engine != null, lastError);
                RenderForm(Path.Combine(output, "01-empty.png"));
                check("header layout", nameLabel.Right <= header.Width - 150 && nameLabel.Top < details.Top, nameLabel.Bounds.ToString());
                check("transport layout", timeLabel.Right < mute.Left, timeLabel.Bounds + " / " + mute.Bounds);
                using (var p = Process.GetCurrentProcess()) { p.Refresh(); check("idle memory observation", true, (p.WorkingSet64 / 1048576.0).ToString("F1") + " MiB working set"); }
                OpenPaths(new[] { Path.Combine(media, "h264-aac.mp4"), Path.Combine(media, "animated.gif") }, false);
                check("rendered H264 playback", await Until(delegate { return loaded && position > 0.4; }, 8000), lastError);
                check("GPU output", engine != null && engine.Get("current-vo") == "gpu-next", engine == null ? "" : engine.Get("current-vo"));
                Cmd("keypress", "SPACE"); check("video input bridge pauses UI", await Until(delegate { return isPaused; }, 2000), Property("pause"));
                Cmd("set", "sub-file-paths", "sub;subtitles");
                check("native array marshalling for dropped paths", await Until(delegate { return engine.GetStrings("options/sub-file-paths").Length == 2; }, 1000), string.Join(",", engine.GetStrings("options/sub-file-paths")));
                Cmd("seek", "3", "absolute+exact"); check("UI seek updates position", await Until(delegate { return Math.Abs(position - 3) < 0.2; }, 2500), position.ToString());
                string frame = Path.Combine(output, "02-gpu-frame.png"); Cmd("screenshot-to-file", frame, "window");
                check("GPU frame capture", await Until(delegate { return File.Exists(frame); }, 4000), frame);
                ToggleQueue(); check("queue visible", queuePanel.Visible && queue.Items.Count == 2, queue.Items.Count.ToString());
                RenderForm(Path.Combine(output, "03-queue.png"));
                Cmd("keypress", "F11"); check("video input enters fullscreen", await Until(delegate { return full && FormBorderStyle == System.Windows.Forms.FormBorderStyle.None && !header.Visible; }, 2000), Bounds.ToString());
                Cmd("keypress", "ESC"); check("video input exits fullscreen", await Until(delegate { return !full && header.Visible && FormBorderStyle == System.Windows.Forms.FormBorderStyle.Sizable; }, 2000), Bounds.ToString());
                Size = new Size(850, 520); await Task.Delay(150); RenderForm(Path.Combine(output, "04-compact.png"));
                check("compact controls do not overlap", timeLabel.Right < mute.Left && !volume.Visible, timeLabel.Right + " < " + mute.Left);
                Size = new Size(1120, 740); ToggleQueue();
                using (var p = Process.GetCurrentProcess()) { p.Refresh(); check("paused playback memory observation", true, (p.WorkingSet64 / 1048576.0).ToString("F1") + " MiB working set"); }
                Cmd("seek", "8.8", "absolute+exact"); Cmd("set", "pause", "no");
                check("queue advances at EOF", await Until(delegate { return current == 1 && loaded; }, 6000), current.ToString());
                check("GIF automatically loops", engine.Get("loop-file") == "inf", engine.Get("loop-file"));
                string gifFrame = Path.Combine(output, "05-gif-frame.png"); Cmd("screenshot-to-file", gifFrame, "window");
                await Until(delegate { return File.Exists(gifFrame); }, 2000);
                OpenPaths(new[] { Path.Combine(media, "broken.mp4") }, false);
                check("error state is recoverable", await Until(delegate { return empty.Visible && lastError != ""; }, 4000), lastError);
                RenderForm(Path.Combine(output, "06-error.png"));
                OpenPaths(new[] { Path.Combine(media, "hevc-10bit.mp4") }, false);
                check("HEVC renders after recovery", await Until(delegate { return loaded && position > 0.1; }, 6000), lastError);
                SetQuality("high"); await Task.Delay(250); check("high quality applied", engine.Get("deband") == "yes", engine.Get("scale"));
                SetQuality("balanced");
            }
            catch (Exception e) { check("unhandled", false, e.ToString()); }
            File.WriteAllText(Path.Combine(output, "results.json"), new JavaScriptSerializer().Serialize(new { failures = failures, tests = results }));
            Close();
        }
    }
}
