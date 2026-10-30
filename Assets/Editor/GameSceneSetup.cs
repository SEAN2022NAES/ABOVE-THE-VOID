using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ABOVE THE VOID 场景搭建工具：
///   - 编辑器启动时自动检测，若 Assets/Scenes/Main.unity 不存在则自动创建并打开；
///   - 也可通过菜单「ABOVE THE VOID / 一键搭建游戏场景」手动执行。
/// 创建的场景自带：Main Camera（置于原点）、平行光、挂载 GameBootstrap 的空物体。
/// </summary>
public static class GameSceneSetup
{
    private const string SceneDir = "Assets/Scenes";
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [InitializeOnLoadMethod]
    private static void AutoCreateSceneOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(ScenePath))
            {
                BuildScene();
                if (File.Exists(ScenePath))
                {
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Debug.Log("[AVT] 未找到游戏场景，已自动生成并打开：Assets/Scenes/Main.unity");
                }
            }
        };
    }

    [MenuItem("ABOVE THE VOID/一键搭建游戏场景")]
    public static void BuildScene()
    {
        // 确保旧版 Input Manager 可用（脚本使用 Input.GetAxis）
        PlayerSettings.SetPropertyInt("activeInputHandler", 2); // 2 = Both

        Directory.CreateDirectory(SceneDir);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        // DefaultGameObjects 自带 Main Camera 和 Directional Light

        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.transform.position = Vector3.zero; // 玩家视角位于原点
        cam.transform.rotation = Quaternion.identity;
        cam.nearClipPlane = 0.01f;

        GameObject go = new GameObject("GameBootstrap");
        go.AddComponent<GameBootstrap>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log("[AVT] 游戏场景已生成：" + ScenePath);
    }
}
