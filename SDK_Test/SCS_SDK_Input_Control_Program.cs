using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly Button pause = new() { Text = "PAUSE через ETS2 SDK", Dock = DockStyle.Top, Height = 55 };
    private readonly Button ping = new() { Text = "PING плагина", Dock = DockStyle.Top, Height = 40 };
    private readonly Button focus = new() { Text = "Найти ETS2 и показать окно", Dock = DockStyle.Top, Height = 40 };
    private readonly TextBox log = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

    public MainForm()
    {
        Text = "ETS2 Assist — SDK Pause Test";
        Width = 620;
        Height = 420;

        Controls.Add(log);
        Controls.Add(focus);
        Controls.Add(ping);
        Controls.Add(pause);

        pause.Click += async (_, _) => await SendAsync("PAUSE");
        ping.Click += async (_, _) => await SendAsync("PING");
        focus.Click += (_, _) => FocusEts2();

        Add("Готово. Запусти ETS2 и помести ets2_assist_input.dll в bin\\win_x64\\plugins.");
    }

    private void Add(string message)
        => log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");

    private async Task SendAsync(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                "ETS2AssistPause",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(1500);

            var bytes = Encoding.ASCII.GetBytes(command);
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();

            var buffer = new byte[64];
            int count = await pipe.ReadAsync(buffer);
            Add($"{command} -> {Encoding.ASCII.GetString(buffer, 0, count).Trim()}");
        }
        catch (Exception ex)
        {
            Add($"Ошибка: {ex.Message}");
        }
    }

    private void FocusEts2()
    {
        var process = Process.GetProcessesByName("eurotrucks2").FirstOrDefault();
        if (process?.MainWindowHandle != IntPtr.Zero)
        {
            Native.SetForegroundWindow(process.MainWindowHandle);
            Add("Окно ETS2 активировано.");
        }
        else
        {
            Add("Процесс eurotrucks2 не найден.");
        }
    }
}

internal static class Native
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
