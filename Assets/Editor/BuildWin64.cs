using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows 64 位独立包构建入口。
/// 批处理调用：Unity.exe -quit -batchmode -nographics -projectPath "E:\Sean\Projects\ABOVE THE VOID"
///            -executeMethod BuildWin64.Build -logFile Logs\build-win64.log
/// 输出目录：E:\Sean\Projects\ABOVE THE VOID\Build\v0.3.0\win64\ABOVE THE VOID.exe
/// </summary>
public static class BuildWin64
{
    private const string ProductName = "ABOVE THE VOID";
    private const string CompanyName = "SEAN MA";
    private const string BundleVersion = "0.3.0";
    private const string RelativeOutputDir = "Build/v0.3.0/win64";
    private const string ExeName = "ABOVE THE VOID.exe";

    [MenuItem("ABOVE THE VOID/打包 Windows 64 位")]
    public static void BuildFromMenu()
    {
        Build();
    }

    /// <summary>批处理模式主入口：无返回值，成功退出码 0，失败退出码 1。</summary>
    public static void Build()
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDir = Path.Combine(projectRoot, RelativeOutputDir.Replace('/', Path.DirectorySeparatorChar));
            string exePath = Path.Combine(outputDir, ExeName);

            LogHeader(projectRoot, outputDir, exePath);

            // ---------- 1. 场景列表校验 ----------
            string[] scenes = CollectScenes(projectRoot);
            if (scenes == null || scenes.Length == 0)
            {
                Debug.LogError("[BuildWin64] FAILED: EditorBuildSettings 中没有可用场景，且未找到 Assets/Scenes/Main.unity");
                EditorApplication.Exit(1);
                return;
            }
            for (int i = 0; i < scenes.Length; i++)
            {
                Debug.Log(string.Format("[BuildWin64] scene[{0}] = {1}", i, scenes[i]));
            }

            // ---------- 2. PlayerSettings / 平台设置校验与写入 ----------
            ApplyPlayerSettings();
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "Architecture", "x86_64");

            AssetDatabase.SaveAssets();

            // ---------- 3. 输出目录 ----------
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                Debug.Log("[BuildWin64] created output dir: " + outputDir);
            }

            // ---------- 4. 打包 ----------
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None // 显式关闭 Development Build
            };

            Debug.Log("[BuildWin64] starting BuildPipeline.BuildPlayer ...");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(string.Format(
                "[BuildWin64] RESULT={0} errors={1} warnings={2} sizeMB={3:F2} duration={4}",
                summary.result, summary.totalErrors, summary.totalWarnings,
                summary.totalSize / 1048576.0, summary.totalTime));

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[BuildWin64] FAILED: build result = " + summary.result);
                EditorApplication.Exit(1);
                return;
            }

            VerifyOutput(exePath, outputDir);
            Debug.Log("[BuildWin64] SUCCEEDED: " + exePath);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[BuildWin64] EXCEPTION: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void LogHeader(string projectRoot, string outputDir, string exePath)
    {
        Debug.Log("[BuildWin64] ================= Windows x86_64 build start =================");
        Debug.Log("[BuildWin64] Unity version : " + Application.unityVersion);
        Debug.Log("[BuildWin64] projectRoot   : " + projectRoot);
        Debug.Log("[BuildWin64] outputDir     : " + outputDir);
        Debug.Log("[BuildWin64] exePath       : " + exePath);
        Debug.Log("[BuildWin64] platform      : " + EditorUserBuildSettings.activeBuildTarget);
    }

    /// <summary>优先取 EditorBuildSettings 中已启用场景；为空时回退到约定主场景。</summary>
    private static string[] CollectScenes(string projectRoot)
    {
        List<string> result = new List<string>();
        EditorBuildSettingsScene[] registered = EditorBuildSettings.scenes;
        if (registered != null)
        {
            for (int i = 0; i < registered.Length; i++)
            {
                EditorBuildSettingsScene s = registered[i];
                if (s == null || !s.enabled || string.IsNullOrEmpty(s.path)) continue;
                if (File.Exists(Path.Combine(projectRoot, s.path.Replace('/', Path.DirectorySeparatorChar))))
                {
                    result.Add(s.path);
                }
                else
                {
                    Debug.LogWarning("[BuildWin64] registered scene file missing, skipped: " + s.path);
                }
            }
        }

        if (result.Count == 0)
        {
            const string fallback = "Assets/Scenes/Main.unity";
            if (File.Exists(Path.Combine(projectRoot, fallback.Replace('/', Path.DirectorySeparatorChar))))
            {
                result.Add(fallback);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(fallback, true) };
                Debug.LogWarning("[BuildWin64] EditorBuildSettings 为空，已自动登记主场景: " + fallback);
            }
        }

        return result.ToArray();
    }

    private static void ApplyPlayerSettings()
    {
        if (PlayerSettings.productName != ProductName) PlayerSettings.productName = ProductName;
        if (PlayerSettings.companyName != CompanyName) PlayerSettings.companyName = CompanyName;
        if (PlayerSettings.bundleVersion != BundleVersion) PlayerSettings.bundleVersion = BundleVersion;

        // 保持 Mono 后端，避免依赖 IL2CPP 附加模块
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        Debug.Log(string.Format(
            "[BuildWin64] PlayerSettings => productName='{0}' companyName='{1}' bundleVersion='{2}' backend={3}",
            PlayerSettings.productName, PlayerSettings.companyName, PlayerSettings.bundleVersion,
            PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)));
    }

    private static void VerifyOutput(string exePath, string outputDir)
    {
        Debug.Log("[BuildWin64] ---- output verification ----");
        if (!File.Exists(exePath))
        {
            Debug.LogWarning("[BuildWin64] exe missing: " + exePath);
        }
        else
        {
            FileInfo fi = new FileInfo(exePath);
            Debug.Log(string.Format("[BuildWin64] exe size = {0:F2} MB, lastWrite = {1}", fi.Length / 1048576.0, fi.LastWriteTime));
        }

        string dataDir = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(exePath) + "_Data");
        Debug.Log("[BuildWin64] Data dir exists = " + Directory.Exists(dataDir) + " :: " + dataDir);
        Debug.Log("[BuildWin64] UnityPlayer.dll exists = " + File.Exists(Path.Combine(outputDir, "UnityPlayer.dll")));

        string[] files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
        long total = 0;
        for (int i = 0; i < files.Length; i++)
        {
            total += new FileInfo(files[i]).Length;
        }
        Debug.Log(string.Format("[BuildWin64] package total = {0:F2} MB in {1} files", total / 1048576.0, files.Length));
    }
}
