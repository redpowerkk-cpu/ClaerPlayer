using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaerPlayer
{
    internal static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [STAThread] static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test") return EngineTests.Run(args);
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show("작업을 완료하지 못했습니다.\n" + e.Exception.Message, "ClaerPlayer"); };
            try
            {
                bool uiTest = args.Length == 3 && args[0] == "--ui-smoke";
                var form = new PlayerForm(uiTest ? new string[0] : args);
                if (uiTest) form.Shown += async delegate { await form.RunUiSmoke(args[1], args[2]); };
                Application.Run(form); return 0;
            }
            catch (Exception ex) { MessageBox.Show("ClaerPlayer를 시작하지 못했습니다.\n" + ex.Message, "ClaerPlayer"); return 1; }
        }
    }
}
