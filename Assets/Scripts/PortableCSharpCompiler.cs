// PortableCSharpCompiler.cs
// 打包版（播放器）用的 C# 编译器桥：不依赖编辑器自带的 mcs/mono，直接调用 Windows 自带 .NET Framework 的 csc.exe。
//
// 背景：编辑器里 CSharpCodeProvider 会去启动 Unity 安装目录里的 mono.exe + mcs.exe，打包后这两个文件都不存在，
//      于是报 “Error running ...\mono\mini\mono.exe: 系统找不到指定的文件”。
// 方案：找系统自带的 csc.exe（Win10 1903+ 默认带 .NET Framework 4.8，必有 v4.0.30319\csc.exe），
//      把播放器自己的 Managed 目录当成引用库，编译出 dll 后 Assembly.LoadFile 加载。
//      一个都找不到时不静默失败，而是提示用户去微软官方页面装 .NET Framework 4.8。
//
// 关键坑（实测）：必须同时给 /noconfig 与 /nostdlib+，
//      否则 csc 自己的 csc.rsp 会把 Framework 目录下的 System.dll / System.Core.dll 一起导进来，
//      与播放器 Managed 目录下的同名程序集（标识完全一致）冲突，报 CS1703 已导入相同标识的程序集。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

public static class PortableCSharpCompiler
{
    /// <summary>编译器缺失时引导用户去的官方页面（微软 .NET Framework 下载入口，装完即带 csc.exe）。</summary>
    public const string DownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework";

    /// <summary>可选手动指定 csc.exe 路径的配置文件（放在 persistentDataPath 下，第一行写完整路径）。</summary>
    public const string ManualPathFileName = "compiler_path.txt";

    private const int CompileTimeoutMs = 25000;
    private const int SmokeTimeoutMs = 15000;

    /// <summary>编译诊断（一行 error / warning）。</summary>
    public sealed class Diag
    {
        public int Line;
        public int Column;
        public string Code;      // 形如 CS0103
        public string Message;
        public bool IsWarning;
    }

    private static bool located;
    private static string compilerPath;
    private static bool smokePassed;
    private static string lastDiagnostic = string.Empty;
    private static int compileSerial;

    public static string CompilerPathOf { get { return compilerPath; } }
    public static string LastDiagnostic { get { return lastDiagnostic; } }

    /// <summary>是否可用（找到文件且通过试编译）。结果在本进程内缓存。</summary>
    public static bool HasCompiler()
    {
        if (!located)
            Locate();
        return !string.IsNullOrEmpty(compilerPath) && smokePassed;
    }

    // ---------------- 探测 ----------------

    /// <summary>按优先级找编译器：手动指定 > 系统 64 位 > 系统 32 位 > 扫描其他 4.x 版本目录；找到后必须通过试编译才算数。</summary>
    private static void Locate()
    {
        located = true;
        compilerPath = null;
        smokePassed = false;
        lastDiagnostic = string.Empty;

        List<string> candidates = new List<string>();

        string manual = ReadManualPath();
        if (!string.IsNullOrEmpty(manual))
            candidates.Add(manual);

        string envPath = null;
        try { envPath = Environment.GetEnvironmentVariable("CSC_PATH"); }
        catch (Exception) { envPath = null; }
        if (!string.IsNullOrEmpty(envPath))
            candidates.Add(envPath);

        string windows = null;
        try { windows = Environment.GetEnvironmentVariable("WINDIR"); }
        catch (Exception) { windows = null; }
        if (string.IsNullOrEmpty(windows))
            windows = @"C:\Windows";

        string netRoot64 = Path.Combine(windows, "Microsoft.NET", "Framework64");
        string netRoot32 = Path.Combine(windows, "Microsoft.NET", "Framework");

        candidates.Add(Path.Combine(netRoot64, "v4.0.30319", "csc.exe"));
        candidates.Add(Path.Combine(netRoot32, "v4.0.30319", "csc.exe"));

        try
        {
            if (Directory.Exists(netRoot64))
            {
                string[] dirs = Directory.GetDirectories(netRoot64, "v4.*");
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (int i = dirs.Length - 1; i >= 0; i--)
                    candidates.Add(Path.Combine(dirs[i], "csc.exe"));
            }
            if (Directory.Exists(netRoot32))
            {
                string[] dirs = Directory.GetDirectories(netRoot32, "v4.*");
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (int i = dirs.Length - 1; i >= 0; i--)
                    candidates.Add(Path.Combine(dirs[i], "csc.exe"));
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[Compiler] 扫描 .NET Framework 目录失败：" + e.Message);
        }

        List<string> tried = new List<string>();
        for (int i = 0; i < candidates.Count; i++)
        {
            string path = candidates[i];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                continue;
            if (tried.Contains(path))
                continue;
            tried.Add(path);

            if (SmokeTest(path, out string why))
            {
                compilerPath = path;
                smokePassed = true;
                UnityEngine.Debug.Log("[Compiler] 使用 C# 编译器：" + path);
                return;
            }
            lastDiagnostic = why;
            UnityEngine.Debug.LogWarning("[Compiler] 该 csc 未通过试编译：" + path + " —— " + why);
        }

        compilerPath = null;
        smokePassed = false;
        if (lastDiagnostic.Length == 0)
            lastDiagnostic = "未找到 csc.exe（系统缺少 .NET Framework 4.x）";
        UnityEngine.Debug.LogWarning("[Compiler] 无可用 C# 编译器：" + lastDiagnostic);
    }

    /// <summary>试编译：用 3 行代码编成 dll，能出文件才认这台机器上的编译器可用（防「文件在但依赖坏了」）。</summary>
    private static bool SmokeTest(string cscPath, out string why)
    {
        why = string.Empty;
        string dir = CacheDir();
        if (string.IsNullOrEmpty(dir))
        {
            why = "无法创建编译缓存目录";
            return false;
        }

        string src = Path.Combine(dir, "__smoke.cs");
        string dll = Path.Combine(dir, "__smoke.dll");
        try
        {
            File.WriteAllText(src, "public class __Smoke { public static int V = 1; }\n", new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            why = "写入试编译源码失败：" + e.Message;
            return false;
        }

        try { if (File.Exists(dll)) File.Delete(dll); }
        catch (Exception) { /* 忽略 */ }

        string managed = ManagedDir();
        List<string> refs = new List<string>();
        AddRef(refs, managed, "mscorlib.dll");
        AddRef(refs, managed, "System.dll");

        List<Diag> diags;
        string fatal;
        bool ok = RunCsc(cscPath, dir, src, dll, refs, SmokeTimeoutMs, out diags, out fatal);
        if (!ok)
        {
            why = fatal ?? "试编译未通过";
            if (diags != null && diags.Count > 0)
                why = diags[0].Code + " " + diags[0].Message;
            return false;
        }
        if (!File.Exists(dll))
        {
            why = "试编译未产出 dll";
            return false;
        }
        return true;
    }

    private static string ReadManualPath()
    {
        try
        {
            string file = Path.Combine(Application.persistentDataPath, ManualPathFileName);
            if (!File.Exists(file))
                return null;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().Trim('"');
                if (line.Length > 0 && File.Exists(line))
                    return line;
            }
        }
        catch (Exception)
        {
            // 配置文件坏掉就当没有
        }
        return null;
    }

    // ---------------- 路径 ----------------

    /// <summary>播放器的托管程序集目录（<游戏>_Data\Managed）。编辑器里不存在，返回 null。</summary>
    public static string ManagedDir()
    {
        try
        {
            string dir = Path.Combine(Application.dataPath, "Managed");
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "mscorlib.dll")))
                return dir;
        }
        catch (Exception)
        {
            // 忽略
        }
        return null;
    }

    private static string CacheDir()
    {
        try
        {
            string cacheRoot = Application.persistentDataPath;
            if (string.IsNullOrEmpty(cacheRoot))
                return null;
            string dir = Path.Combine(cacheRoot, "CompileCache");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AddRef(List<string> refs, string dir, string file)
    {
        if (string.IsNullOrEmpty(dir))
            return;
        string full = Path.Combine(dir, file);
        if (File.Exists(full) && !refs.Contains(full))
            refs.Add(full);
    }

    /// <summary>播放器编译用户代码所需的引用库（全部来自 Managed 目录，与运行时同源，避免版本错配）。</summary>
    public static List<string> PlayerReferences(out string why)
    {
        why = null;
        string managed = ManagedDir();
        if (managed == null)
        {
            why = "找不到播放器托管目录（_Data\\Managed）";
            return new List<string>();
        }

        List<string> refs = new List<string>();
        // 基础框架程序集：/nostdlib+ 之后必须自己给 mscorlib，否则连 object 都找不到
        AddRef(refs, managed, "mscorlib.dll");
        AddRef(refs, managed, "netstandard.dll");
        AddRef(refs, managed, "System.dll");
        AddRef(refs, managed, "System.Core.dll");

        // 游戏与 Unity 常用模块：用户代码基本只碰这些
        AddRef(refs, managed, "UnityEngine.CoreModule.dll");
        AddRef(refs, managed, "UnityEngine.UI.dll");
        AddRef(refs, managed, "UnityEngine.UIModule.dll");
        AddRef(refs, managed, "UnityEngine.TextRenderingModule.dll");
        AddRef(refs, managed, "UnityEngine.InputLegacyModule.dll");
        AddRef(refs, managed, "UnityEngine.PhysicsModule.dll");
        AddRef(refs, managed, "UnityEngine.IMGUIModule.dll");
        AddRef(refs, managed, "UnityEngine.AudioModule.dll");
        AddRef(refs, managed, "Assembly-CSharp.dll"); // CodeTower / UIFontHelper 等游戏类型

        if (refs.Count == 0)
            why = "Managed 目录里没有任何可用的引用程序集";
        return refs;
    }

    // ---------------- 编译 ----------------

    /// <summary>编译用户代码。成功返回 true 并通过 <paramref name="asm"/> 给出已加载程序集。</summary>
    public static bool TryCompile(string source, out Assembly asm, out List<Diag> diags, out string fatal)
    {
        asm = null;
        diags = new List<Diag>();
        fatal = null;

        if (!HasCompiler())
        {
            fatal = lastDiagnostic.Length > 0 ? lastDiagnostic : "本机缺少 C# 编译器";
            return false;
        }

        string dir = CacheDir();
        if (dir == null)
        {
            fatal = "无法创建编译缓存目录（persistentDataPath 不可写）";
            return false;
        }

        string why;
        List<string> refs = PlayerReferences(out why);
        if (why != null)
        {
            fatal = why;
            return false;
        }

        compileSerial++;
        string stamp = DateTime.Now.ToString("HHmmss") + "_" + compileSerial.ToString();
        string src = Path.Combine(dir, "UserCode_" + stamp + ".cs");
        string dll = Path.Combine(dir, "UserCode_" + stamp + ".dll");

        try
        {
            File.WriteAllText(src, source, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            fatal = "写入用户代码失败：" + e.Message;
            return false;
        }

        CleanOldCache(dir);

        if (!RunCsc(compilerPath, dir, src, dll, refs, CompileTimeoutMs, out diags, out fatal))
            return false;

        if (!File.Exists(dll))
        {
            fatal = "编译未产出 dll";
            return false;
        }

        try
        {
            asm = Assembly.LoadFile(dll);
        }
        catch (Exception e)
        {
            fatal = "加载编译结果失败：" + e.Message;
            return false;
        }

        if (asm == null)
        {
            fatal = "加载编译结果失败：程序集为空";
            return false;
        }
        return true;
    }

    /// <summary>启动 csc.exe 编译一个源码文件。返回 true 表示 EXIT=0。</summary>
    private static bool RunCsc(string cscPath, string workDir, string sourceFile, string outDll,
        List<string> refs, int timeoutMs, out List<Diag> diags, out string fatal)
    {
        diags = new List<Diag>();
        fatal = null;

        StringBuilder args = new StringBuilder();
        args.Append("/nologo /noconfig /target:library /nostdlib+ /utf8output /optimize- ");
        args.Append("/out:\"").Append(outDll).Append("\" ");
        for (int i = 0; i < refs.Count; i++)
            args.Append("/r:\"").Append(refs[i]).Append("\" ");
        args.Append('"').Append(sourceFile).Append('"');

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = cscPath;
        psi.Arguments = args.ToString();
        psi.WorkingDirectory = workDir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        // csc 加了 /utf8output，输出统一按 UTF-8 读，避免中文系统上乱码
        try
        {
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
        }
        catch (Exception)
        {
            // 个别环境不支持设置编码，退回默认
        }

        Process proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception e)
        {
            fatal = "无法启动编译器：" + e.Message;
            return false;
        }
        if (proc == null)
        {
            fatal = "无法启动编译器：进程为空";
            return false;
        }

        StringBuilder stdout = new StringBuilder();
        StringBuilder stderr = new StringBuilder();
        // 两个流必须并行读，否则其中一个写满管道缓冲区就会互相卡死
        Thread outThread = new Thread(delegate ()
        {
            try { stdout.Append(proc.StandardOutput.ReadToEnd()); }
            catch (Exception) { }
        });
        Thread errThread = new Thread(delegate ()
        {
            try { stderr.Append(proc.StandardError.ReadToEnd()); }
            catch (Exception) { }
        });
        outThread.IsBackground = true;
        errThread.IsBackground = true;
        outThread.Start();
        errThread.Start();

        bool exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(); }
            catch (Exception) { }
            fatal = "编译器超时（" + (timeoutMs / 1000) + " 秒未结束）";
            return false;
        }
        outThread.Join(2000);
        errThread.Join(2000);

        int exitCode = proc.ExitCode;
        try { proc.Dispose(); }
        catch (Exception) { }

        string combined = stderr.ToString();
        if (combined.Length == 0)
            combined = stdout.ToString();
        diags = ParseDiagnostics(combined);

        if (exitCode != 0)
        {
            bool hasError = false;
            for (int i = 0; i < diags.Count; i++)
            {
                if (!diags[i].IsWarning) { hasError = true; break; }
            }
            if (!hasError)
            {
                string first = FirstNonEmptyLine(combined);
                fatal = first != null ? first : ("编译器返回 " + exitCode);
            }
            return false;
        }
        return true;
    }

    private static readonly Regex DiagRegex = new Regex(
        @"^(?<file>.*?)\((?<line>\d+),(?<col>\d+)\)\s*:\s*(?<kind>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<msg>.*)$",
        RegexOptions.Compiled);

    private static List<Diag> ParseDiagnostics(string text)
    {
        List<Diag> list = new List<Diag>();
        if (string.IsNullOrEmpty(text))
            return list;

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
                continue;
            Match m = DiagRegex.Match(line);
            if (!m.Success)
                continue;
            Diag d = new Diag();
            d.Line = ParseInt(m.Groups["line"].Value);
            d.Column = ParseInt(m.Groups["col"].Value);
            d.Code = m.Groups["code"].Value.ToUpperInvariant();
            d.Message = m.Groups["msg"].Value.Trim();
            d.IsWarning = string.Equals(m.Groups["kind"].Value, "warning", StringComparison.OrdinalIgnoreCase);
            list.Add(d);
        }
        return list;
    }

    private static int ParseInt(string s)
    {
        int v;
        if (int.TryParse(s, out v))
            return v;
        return 0;
    }

    private static string FirstNonEmptyLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length > 0)
                return line;
        }
        return null;
    }

    /// <summary>清理一天前的编译缓存（失败无所谓，不能影响编译）。</summary>
    private static void CleanOldCache(string dir)
    {
        try
        {
            DateTime cutoff = DateTime.Now.AddDays(-1);
            string[] files = Directory.GetFiles(dir, "UserCode_*.*");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    if (File.GetLastWriteTime(files[i]) < cutoff)
                        File.Delete(files[i]);
                }
                catch (Exception)
                {
                    // 被占用就留着
                }
            }
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    // ---------------- 引导 ----------------

    /// <summary>用系统默认浏览器打开编译器官方下载页。</summary>
    public static void OpenDownloadPage()
    {
        try
        {
            Application.OpenURL(DownloadUrl);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[Compiler] 打开下载页失败：" + e.Message);
        }
    }

    /// <summary>把下载链接复制到剪贴板（玩家可以自己粘到浏览器）。</summary>
    public static void CopyDownloadUrl()
    {
        try
        {
            GUIUtility.systemCopyBuffer = DownloadUrl;
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
