using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamTech.Leaderboard.Demo.EditorTools
{
    /// <summary>
    /// Dựng scene bàn thử League. Bảng điều khiển vẽ bằng IMGUI nên scene chỉ cần một camera và một GameObject — không cần
    /// prefab, font hay TMP. Scene là sản phẩm sinh ra: sửa ở đây rồi dựng lại, đừng sửa tay trong scene.
    ///
    /// <para>Chạy từ menu hoặc batchmode:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod DreamTech.Leaderboard.Demo.EditorTools.LeagueDemoSceneBuilder.BuildFromCommandLine</c></para>
    /// </summary>
    public static class LeagueDemoSceneBuilder
    {
        public const string ScenePath = "Assets/Demo/LeagueDemo.unity";
        private static readonly Color StageColor = new Color32(0x10, 0x1A, 0x33, 0xFF);

        [MenuItem("Tools/DreamTech/Leaderboard/Demo/Build League Demo Scene")]
        public static void Build()
        {
            BuildScene();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                BuildScene();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        private static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = StageColor;
            cameraObject.tag = "MainCamera";

            new GameObject("LeagueDemo", typeof(LeagueDemo));

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterSceneInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[League Demo] Đã dựng " + ScenePath);
        }

        /// <summary>Thêm scene vào Build Settings mà KHÔNG xoá scene khác — test PlayMode nạp scene theo tên nên cả hai demo phải có mặt.</summary>
        public static void RegisterSceneInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int index = 0; index < scenes.Count; index++)
            {
                if (!string.Equals(scenes[index].path, scenePath, StringComparison.Ordinal)) continue;
                if (scenes[index].enabled) return;
                scenes[index] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
