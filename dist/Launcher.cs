using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text;

// TraeRadio 启动器：把内嵌的 radio.ps1 释放到本地状态目录，再用 PowerShell 执行。
// 目的：在没有 MSVC / Windows SDK 的机器上，也能产出一个可双击运行的 Windows 软件版本。
class TraeRadioLauncher
{
    static int Main(string[] args)
    {
        string stateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraeRadio");
        Directory.CreateDirectory(stateDir);
        string scriptPath = Path.Combine(stateDir, "radio.ps1");

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("radio.ps1"))
            {
                if (s == null)
                {
                    Console.Error.WriteLine("[TraeRadio] 内嵌脚本缺失，无法启动。");
                    return 3;
                }
                using (var fs = new FileStream(scriptPath, FileMode.Create, FileAccess.Write))
                {
                    s.CopyTo(fs);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[TraeRadio] 释放脚本失败：" + ex.Message);
            return 4;
        }

        var sb = new StringBuilder();
        sb.Append("-NoProfile -ExecutionPolicy Bypass -File ");
        sb.Append(Quote(scriptPath));
        foreach (string a in args)
        {
            sb.Append(' ');
            sb.Append(Quote(a));
        }

        var psi = new ProcessStartInfo();
        psi.FileName = "powershell.exe";
        psi.Arguments = sb.ToString();
        psi.UseShellExecute = false;

        try
        {
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[TraeRadio] 启动 PowerShell 失败：" + ex.Message);
            return 5;
        }
    }

    // Windows 命令行参数转义（标准算法）
    static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return s;

        var sb = new StringBuilder();
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in s)
        {
            if (c == '\\') { backslashes++; }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                if (backslashes > 0) { sb.Append('\\', backslashes); backslashes = 0; }
                sb.Append(c);
            }
        }
        if (backslashes > 0) sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}