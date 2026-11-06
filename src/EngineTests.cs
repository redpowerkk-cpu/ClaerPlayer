using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace ClaerPlayer
{
    internal static class EngineTests
    {
        static bool Wait(Func<bool> predicate, int timeout)
        {
            Stopwatch watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < timeout) { if (predicate()) return true; Thread.Sleep(40); } return predicate();
        }
        static double Number(string s) { double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0; }
        public static int Run(string[] args)
        {
            if (args.Length < 3) return 2;
            string folder = args[1], report = args[2]; var results = new List<object>(); int failures = 0;
            Action<string, bool, string> check = delegate(string name, bool pass, string detail) { results.Add(new { name = name, passed = pass, detail = detail }); if (!pass) failures++; };
            try
            {
                using (var engine = new Engine(IntPtr.Zero, "balanced", true))
                {
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        if (Path.GetExtension(file) == ".srt" || Path.GetFileName(file) == "broken.mp4") continue;
                        engine.Set("loop-file", "no"); engine.Command("loadfile", Path.GetFullPath(file), "replace"); engine.Set("pause", "no");
                        bool decoded = Wait(delegate { return engine.Get("path") == Path.GetFullPath(file) && Number(engine.Get("time-pos")) > 0.15 && Number(engine.Get("video-params/w")) > 0; }, 10000);
                        check("decode " + Path.GetFileName(file), decoded, engine.Get("video-format") + " " + engine.Get("video-params/w") + "x" + engine.Get("video-params/h"));
                    }
                    string main = Path.Combine(folder, "h264-aac.mp4");
                    engine.Command("loadfile", main, "replace"); engine.Set("pause", "no"); Wait(delegate { return engine.Get("path") == main && Number(engine.Get("time-pos")) > 0.2; }, 7000);
                    engine.Set("pause", "yes"); check("pause", Wait(delegate { return engine.Get("pause") == "yes"; }, 2000), engine.Get("pause"));
                    double first = Number(engine.Get("time-pos")); Thread.Sleep(300); check("paused clock is stable", Math.Abs(Number(engine.Get("time-pos")) - first) < 0.08, engine.Get("time-pos"));
                    engine.Command("seek", "4", "absolute+exact"); check("exact seek", Wait(delegate { return Math.Abs(Number(engine.Get("time-pos")) - 4) < 0.2; }, 3000), engine.Get("time-pos"));
                    engine.Set("speed", "1.5"); check("speed", Wait(delegate { return Math.Abs(Number(engine.Get("speed")) - 1.5) < 0.01; }, 1000), engine.Get("speed"));
                    engine.Set("volume", "35"); engine.Set("mute", "yes"); check("volume and mute", Wait(delegate { return Number(engine.Get("volume")) == 35 && engine.Get("mute") == "yes"; }, 1000), engine.Get("volume"));
                    engine.Command("sub-add", Path.Combine(folder, "sample.srt"), "select"); check("external subtitle", Wait(delegate { return engine.Get("sid") != "no" && engine.Get("sid") != ""; }, 3000), engine.Get("sid"));
                    foreach (string mode in new[] { "low", "balanced", "high" }) { engine.Quality(mode); string wanted = mode == "high" ? "ewa_lanczossharp" : mode == "low" ? "bilinear" : "spline36"; check("quality " + mode, Wait(delegate { return engine.Get("scale") == wanted; }, 1000), engine.Get("scale")); }
                    engine.Set("speed", "1"); engine.Set("pause", "no"); engine.Set("loop-file", "inf");
                    string gif = Path.Combine(folder, "animated.gif"); engine.Command("loadfile", gif, "replace");
                    Wait(delegate { return engine.Get("path") == gif && Number(engine.Get("time-pos")) > 0.05; }, 4000);
                    double last = -1; bool wrapped = false; Stopwatch gifWatch = Stopwatch.StartNew();
                    while (gifWatch.ElapsedMilliseconds < 4000) { double now = Number(engine.Get("time-pos")); if (last > 0.4 && now < last - 0.3) wrapped = true; last = now; Thread.Sleep(40); }
                    check("GIF loops across boundary", wrapped, engine.Get("loop-file"));
                    int errors = 0; engine.Failed += delegate { Interlocked.Increment(ref errors); };
                    engine.Set("loop-file", "no"); engine.Command("loadfile", Path.Combine(folder, "broken.mp4"), "replace");
                    check("corrupt file raises an error", Wait(delegate { return errors > 0; }, 5000), errors.ToString());
                    engine.Command("loadfile", main, "replace"); engine.Set("pause", "no");
                    check("recover after bad file", Wait(delegate { return engine.Get("path") == main && Number(engine.Get("time-pos")) > 0.15; }, 7000), engine.Get("video-format"));
                    check("engine version", true, engine.Get("mpv-version"));
                }
            }
            catch (Exception e) { check("unhandled", false, e.ToString()); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            File.WriteAllText(report, new JavaScriptSerializer().Serialize(new { failures = failures, tests = results }));
            return failures == 0 ? 0 : 1;
        }
    }
}
