using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class UITestDriver
{
    private static readonly string FlagDir = @"C:\Users\win\AppData\Roaming\Tencent\Marvis\User\oAN1i2QIGJngwR5YGfGCgiD5u-lg\workspace\conv_9b3fdffd68f74884a29761aa31ec5c33\temp";
    private static readonly string TriggerPath = @"C:\Users\win\AppData\Roaming\Tencent\Marvis\User\oAN1i2QIGJngwR5YGfGCgiD5u-lg\workspace\conv_9b3fdffd68f74884a29761aa31ec5c33\temp\ui_test_trigger.txt";
    private static readonly string StatePath = @"C:\Users\win\AppData\Roaming\Tencent\Marvis\User\oAN1i2QIGJngwR5YGfGCgiD5u-lg\workspace\conv_9b3fdffd68f74884a29761aa31ec5c33\temp\ui_test_state.txt";

    private static double startT = 0;
    private static int stage = -1;
    private static InputField inputField = null;

    static UITestDriver()
    {
        EditorApplication.update += Tick;
    }

    private static void WriteFlag(string name)
    {
        try { File.WriteAllText(Path.Combine(FlagDir, name), DateTime.Now.ToString("HH:mm:ss.fff")); }
        catch (Exception e) { Debug.LogWarning("[UITest] flag " + name + " fail " + e.Message); }
    }

    private static void WriteState(int st)
    {
        try { File.WriteAllText(StatePath, st + "|" + DateTime.Now.Ticks.ToString()); }
        catch (Exception e) { Debug.LogWarning("[UITest] state write fail " + e.Message); }
    }

    private static bool ReadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return false;
            string[] p = File.ReadAllText(StatePath).Split('|');
            if (p.Length != 2) return false;
            stage = int.Parse(p[0]);
            startT = long.Parse(p[1]) / 1e7;
            return true;
        }
        catch (Exception e) { Debug.LogWarning("[UITest] state read fail " + e.Message); return false; }
    }

    private static double Elapsed()
    {
        return DateTime.Now.Ticks / 1e7 - startT;
    }

    private static void Tick()
    {
        if (stage < 0)
        {
            if (ReadState()) { }
            else if (File.Exists(TriggerPath))
            {
                try { File.Delete(TriggerPath); } catch { }
                WriteState(0);
                EditorApplication.isPlaying = true;
                WriteFlag("flow_started");
                Debug.Log("[UITest] flow started, state persisted");
            }
            else return;
        }

        double t = Elapsed();
        bool changed = false;
        switch (stage)
        {
            case 0:
                if (t >= 3.0) { WriteFlag("stage0_menu_ready"); Debug.Log("[UITest] stage0 menu"); stage = 1; changed = true; }
                break;
            case 1:
                if (t >= 4.5)
                {
                    MainMenuController mm = UnityEngine.Object.FindObjectOfType<MainMenuController>();
                    if (mm != null) { mm.StartLevel(3); Debug.Log("[UITest] StartLevel(3)"); }
                    else Debug.LogWarning("[UITest] MainMenuController not found");
                    stage = 2; changed = true;
                }
                break;
            case 2:
                if (t >= 8.0)
                {
                    CodeEditorController ed = UnityEngine.Object.FindObjectOfType<CodeEditorController>();
                    if (ed != null) { ed.Open(); Debug.Log("[UITest] Editor Open()"); }
                    else Debug.LogWarning("[UITest] CodeEditorController not found");
                    stage = 3; changed = true;
                }
                break;
            case 3:
                if (t >= 12.0) { WriteFlag("stage1_editor_open_ready"); Debug.Log("[UITest] stage1 editor open"); stage = 4; changed = true; }
                break;
            case 4:
                if (t >= 14.0)
                {
                    SetText("int a = 3 < 5 ? 1 : 0;\nint b = a > 0 ? 1 : 0;\n// 中文注释 < > 测试\nint c = a << 2;");
                    WriteFlag("text_pasted");
                    Debug.Log("[UITest] text pasted");
                    stage = 5; changed = true;
                }
                break;
            case 5:
                if (t >= 15.0)
                {
                    SetCaretAndSelection(0, 3);
                    WriteFlag("select_ready");
                    Debug.Log("[UITest] select 0..3");
                    stage = 6; changed = true;
                }
                break;
            case 6:
                if (t >= 18.5)
                {
                    SetCaretAndSelection(60, 60);
                    WriteFlag("caret1_ready");
                    Debug.Log("[UITest] caret line3 end");
                    stage = 7; changed = true;
                }
                break;
            case 7:
                if (t >= 19.1) { WriteFlag("caret2_ready"); Debug.Log("[UITest] caret2"); stage = 8; changed = true; }
                break;
            case 8:
                if (t >= 21.0) { WriteFlag("status_ready"); Debug.Log("[UITest] status"); stage = 9; changed = true; }
                break;
            case 9:
                if (t >= 23.0)
                {
                    EditorApplication.isPlaying = false;
                    WriteFlag("flow_done");
                    Debug.Log("[UITest] flow done, exit play");
                    stage = 10; changed = true;
                }
                break;
            case 10:
                break;
        }
        if (changed && stage < 10) WriteState(stage);
        if (stage == 10) { try { File.Delete(StatePath); } catch { } }
    }

    private static InputField GetInputField()
    {
        if (inputField != null) return inputField;
        CodeEditorController ed = UnityEngine.Object.FindObjectOfType<CodeEditorController>();
        if (ed == null) return null;
        FieldInfo fi = typeof(CodeEditorController).GetField("inputField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (fi == null) return null;
        inputField = fi.GetValue(ed) as InputField;
        return inputField;
    }

    private static void SetText(string text)
    {
        InputField ifd = GetInputField();
        if (ifd == null) { Debug.LogWarning("[UITest] no input field"); return; }
        ifd.text = text;
    }

    private static void SetCaretAndSelection(int caret, int focus)
    {
        InputField ifd = GetInputField();
        if (ifd == null) { Debug.LogWarning("[UITest] no input field"); return; }
        ifd.caretPosition = caret;
        ifd.selectionAnchorPosition = caret;
        ifd.selectionFocusPosition = focus;
    }
}