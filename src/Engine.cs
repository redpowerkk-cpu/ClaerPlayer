using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClaerPlayer
{
    internal sealed class Engine : IDisposable
    {
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_create();
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_initialize(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] value);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_command_async(IntPtr ctx, ulong id, IntPtr args);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_observe_property(IntPtr ctx, ulong id, byte[] name, int format);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_error_string(int error);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern void mpv_terminate_destroy(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern void mpv_free(IntPtr ptr);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern int mpv_get_property(IntPtr ctx, byte[] name, int format, out Node value);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] static extern void mpv_free_node_contents(ref Node value);

        [StructLayout(LayoutKind.Sequential)] struct MpvEvent { public int Id; public int Error; public ulong Reply; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)] struct PropertyEvent { public IntPtr Name; public int Format; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)] struct ClientMessage { public int Count; public IntPtr Args; }
        [StructLayout(LayoutKind.Sequential)] struct Node { public IntPtr Value; public int Format; }
        [StructLayout(LayoutKind.Sequential)] struct NodeList { public int Count; public IntPtr Values; public IntPtr Keys; }
        IntPtr handle;
        Thread events;
        volatile bool stopping;
        ulong request;
        readonly object gate = new object();
        public event Action<string, string> PropertyChanged;
        public event Action Loaded;
        public event Action<string> Failed;
        public event Action<string> OperationFailed;
        public event Action<string> UserAction;
        public event Action Ended;

        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }
        static string Text(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            byte[] b = new byte[n]; Marshal.Copy(p, b, 0, n); return Encoding.UTF8.GetString(b);
        }
        public Engine(IntPtr window, string quality, bool headless)
        {
            handle = mpv_create();
            if (handle == IntPtr.Zero) throw new InvalidOperationException("재생 엔진을 만들 수 없습니다.");
            try
            {
                Option("config", "no"); Option("load-scripts", "no"); Option("osc", "no");
                Option("input-default-bindings", "no"); Option("input-vo-keyboard", "yes");
                Option("input-builtin-drag-and-drop", "no"); Option("input-builtin-dragging", "no");
                Option("input-cursor", "yes"); Option("terminal", "no"); Option("osd-level", "0");
                Option("idle", "yes"); Option("keep-open", "yes"); Option("keep-open-pause", "yes");
                Option("audio-display", "no"); Option("volume-max", "100");
                Option("hwdec", headless ? "no" : "auto-safe");
                Option("vo", headless ? "null" : "gpu-next");
                if (headless) Option("ao", "null");
                else
                {
                    Option("wid", window.ToInt64().ToString(CultureInfo.InvariantCulture));
                    Option("gpu-api", "d3d11"); Option("gpu-context", "d3d11");
                    Option("target-colorspace-hint", "auto"); Option("icc-profile-auto", "yes");
                }
                Option("sub-auto", "fuzzy"); Option("sub-codepage", "auto");
                Option("demuxer-max-bytes", "64MiB"); Option("demuxer-max-back-bytes", "16MiB");
                int result = mpv_initialize(handle);
                if (result < 0) throw new InvalidOperationException(Text(mpv_error_string(result)));
                string[] names = { "time-pos", "duration", "pause", "eof-reached", "volume", "mute", "speed", "video-params/w", "video-params/h", "video-format", "hwdec-current", "estimated-vf-fps", "video-params/gamma", "video-params/primaries", "sid", "aid", "dropped-files/time" };
                for (int i = 0; i < names.Length; i++) mpv_observe_property(handle, (ulong)(i + 1), Utf8(names[i]), 1);
                string[,] bindings = { {"SPACE","play"}, {"LEFT","back"}, {"RIGHT","forward"}, {"UP","louder"}, {"DOWN","quieter"}, {"m","mute"}, {"r","repeat"}, {"f","full"}, {"F11","full"}, {"ESC","escape"}, {"PGUP","previous"}, {"PGDWN","next"}, {"Ctrl+o","open"}, {"Ctrl+l","queue"}, {"Ctrl+s","subtitle"}, {"Ctrl+p","screenshot"}, {"j","subtrack"}, {"a","audiotrack"}, {".","frame"}, {"MBTN_LEFT_DBL","full"} };
                for (int i = 0; i < bindings.GetLength(0); i++) Command("keybind", bindings[i,0], "script-message claerplayer " + bindings[i,1]);
                Quality(quality);
                events = new Thread(ReadEvents) { IsBackground = true, Name = "ClaerPlayer media events" };
                events.Start();
            }
            catch { mpv_terminate_destroy(handle); handle = IntPtr.Zero; throw; }
        }
        void Option(string name, string value)
        {
            int result = mpv_set_option_string(handle, Utf8(name), Utf8(value));
            if (result < 0) throw new InvalidOperationException(name + ": " + Text(mpv_error_string(result)));
        }
        public void Command(params string[] args)
        {
            lock (gate)
            {
                if (stopping || handle == IntPtr.Zero) return;
                IntPtr[] parts = new IntPtr[args.Length]; IntPtr array = Marshal.AllocHGlobal((args.Length + 1) * IntPtr.Size);
                try
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        byte[] b = Utf8(args[i]); parts[i] = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, parts[i], b.Length); Marshal.WriteIntPtr(array, i * IntPtr.Size, parts[i]);
                    }
                    Marshal.WriteIntPtr(array, args.Length * IntPtr.Size, IntPtr.Zero);
                    int r = mpv_command_async(handle, ++request, array);
                    if (r < 0 && Failed != null) Failed(Text(mpv_error_string(r)));
                }
                finally { foreach (IntPtr p in parts) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(array); }
            }
        }
        public string Get(string name)
        {
            lock (gate)
            {
                if (stopping || handle == IntPtr.Zero) return "";
                IntPtr p = mpv_get_property_string(handle, Utf8(name));
                try { return Text(p); } finally { if (p != IntPtr.Zero) mpv_free(p); }
            }
        }
        public void Set(string name, string value) { Command("set", name, value); }
        public string[] GetStrings(string name)
        {
            lock (gate)
            {
                if (stopping || handle == IntPtr.Zero) return new string[0];
                Node node; if (mpv_get_property(handle, Utf8(name), 6, out node) < 0) return new string[0];
                try
                {
                    if (node.Format != 7 || node.Value == IntPtr.Zero) return new string[0];
                    NodeList list = (NodeList)Marshal.PtrToStructure(node.Value, typeof(NodeList)); var values = new List<string>();
                    for (int i = 0; i < list.Count; i++) { Node entry = (Node)Marshal.PtrToStructure(IntPtr.Add(list.Values, i * Marshal.SizeOf(typeof(Node))), typeof(Node)); if (entry.Format == 1) values.Add(Text(entry.Value)); }
                    return values.ToArray();
                }
                finally { mpv_free_node_contents(ref node); }
            }
        }
        public void Quality(string mode)
        {
            Set("scale", mode == "high" ? "ewa_lanczossharp" : mode == "low" ? "bilinear" : "spline36");
            Set("cscale", mode == "high" ? "spline36" : "bilinear");
            Set("dscale", mode == "low" ? "bilinear" : "mitchell");
            Set("deband", mode == "high" ? "yes" : "no");
            Set("interpolation", "no");
        }
        void ReadEvents()
        {
            while (!stopping)
            {
                MpvEvent ev = (MpvEvent)Marshal.PtrToStructure(mpv_wait_event(handle, 0.1), typeof(MpvEvent));
                if (stopping) break;
                if (ev.Id == 22 && ev.Data != IntPtr.Zero)
                {
                    PropertyEvent p = (PropertyEvent)Marshal.PtrToStructure(ev.Data, typeof(PropertyEvent));
                    string value = p.Format == 1 && p.Data != IntPtr.Zero ? Text(Marshal.ReadIntPtr(p.Data)) : "";
                    if (PropertyChanged != null) PropertyChanged(Text(p.Name), value);
                }
                else if (ev.Id == 8) { if (Loaded != null) Loaded(); }
                else if (ev.Id == 16 && ev.Data != IntPtr.Zero)
                {
                    ClientMessage message = (ClientMessage)Marshal.PtrToStructure(ev.Data, typeof(ClientMessage));
                    if (message.Count == 2 && Text(Marshal.ReadIntPtr(message.Args)) == "claerplayer" && UserAction != null) UserAction(Text(Marshal.ReadIntPtr(message.Args, IntPtr.Size)));
                }
                else if (ev.Id == 7 && ev.Data != IntPtr.Zero)
                {
                    int reason = Marshal.ReadInt32(ev.Data); int error = Marshal.ReadInt32(ev.Data, 4);
                    if (reason == 4 && Failed != null) Failed(Text(mpv_error_string(error)));
                    else if (reason == 0 && Ended != null) Ended();
                }
                else if (ev.Id == 5 && ev.Error < 0 && OperationFailed != null) OperationFailed(Text(mpv_error_string(ev.Error)));
            }
        }
        public void Dispose()
        {
            lock (gate) { if (stopping) return; stopping = true; }
            if (events != null && events != Thread.CurrentThread) events.Join();
            if (handle != IntPtr.Zero) { mpv_terminate_destroy(handle); handle = IntPtr.Zero; }
        }
    }
}
