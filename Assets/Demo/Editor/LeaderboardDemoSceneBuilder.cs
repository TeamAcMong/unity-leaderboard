using System;
using System.IO;
using DreamTech.Leaderboard.EditorTools;
using DreamTech.Leaderboard.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.Demo.EditorTools
{
    /// <summary>
    /// Dựng scene demo: một board, ba host (màn riêng / popup / khối trong màn Win) cùng lồng <c>LeaderboardWidget.prefab</c> của
    /// package. Scene là sản phẩm sinh ra — sửa bố cục ở đây rồi dựng lại, đừng sửa tay trong scene.
    ///
    /// <para>Chạy từ menu hoặc batchmode (KHÔNG kèm <c>-quit</c> — import unitypackage chạy bất đồng bộ nên hàm tự thoát khi xong):
    /// <c>Unity -batchmode -projectPath . -executeMethod DreamTech.Leaderboard.Demo.EditorTools.LeaderboardDemoSceneBuilder.BuildFromCommandLine</c></para>
    /// </summary>
    public static class LeaderboardDemoSceneBuilder
    {
        public const string ScenePath = "Assets/Demo/LeaderboardDemo.unity";
        private const string TextMeshProSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private static readonly string[] TextMeshProHostPackages = { "com.unity.ugui", "com.unity.textmeshpro" };
        private const double ImportTimeoutSeconds = 180d;

        private const int UserInterfaceLayer = 5;
        private static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);
        private const float ControlsHeight = 360f;
        private static readonly Color StageColor = new Color32(0x10, 0x1A, 0x33, 0xFF);
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color WinPanelColor = new Color32(0x2B, 0x3F, 0x7A, 0xFF);
        private static readonly Color ContinueColor = new Color32(0x3C, 0xC2, 0x5E, 0xFF);

        [MenuItem("Tools/DreamTech/Leaderboard/Demo/Build Demo Scene")]
        public static void Build()
        {
            RunWithTextMeshProEssentials(BuildScene, false);
        }

        public static void BuildFromCommandLine()
        {
            RunWithTextMeshProEssentials(BuildScene, true);
        }

        private static void BuildScene()
        {
            var widgetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath);
            if (widgetPrefab == null) throw new InvalidOperationException("Không thấy " + LeaderboardEditorPaths.WidgetPrefabPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = StageColor;
            cameraObject.tag = "MainCamera";

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = UserInterfaceLayer;
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            // Chừa dải đáy cho nút điều khiển (IMGUI) để bấm nút không rơi vào lớp chạm-để-skip của widget.
            RectTransform stage = CreateRect("Stage", canvasObject.transform);
            Stretch(stage, 0f, 0f, ControlsHeight, 0f);

            LeaderboardDemoHost screenHost = BuildScreenHost(stage, widgetPrefab);
            LeaderboardDemoHost popupHost = BuildPopupHost(stage, widgetPrefab);
            LeaderboardDemoHost winHost = BuildWinBlockHost(stage, widgetPrefab);

            var demoObject = new GameObject("LeaderboardDemo", typeof(LeaderboardDemo));
            var demo = new SerializedObject(demoObject.GetComponent<LeaderboardDemo>());
            SerializedProperty hosts = demo.FindProperty("hosts");
            hosts.arraySize = 3;
            hosts.GetArrayElementAtIndex(0).objectReferenceValue = screenHost;
            hosts.GetArrayElementAtIndex(1).objectReferenceValue = popupHost;
            hosts.GetArrayElementAtIndex(2).objectReferenceValue = winHost;
            demo.FindProperty("controlsHeight").floatValue = ControlsHeight;
            demo.ApplyModifiedPropertiesWithoutUndo();

            screenHost.gameObject.SetActive(false);
            popupHost.gameObject.SetActive(false);
            winHost.gameObject.SetActive(false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Leaderboard Demo] Đã dựng " + ScenePath);
        }

        /// <summary>
        /// Prefab của package dùng font mặc định của TMP (LiberationSans SDF trong Assets/TextMesh Pro), nên phải có TMP Essential
        /// Resources trước khi dựng. Import unitypackage chạy bất đồng bộ (batchmode thoát trước khi nó xong nếu gọi rồi đi tiếp),
        /// nên việc dựng được hoãn tới <c>importPackageCompleted</c>, có watchdog để batchmode không treo.
        /// </summary>
        private static void RunWithTextMeshProEssentials(Action action, bool exitWhenDone)
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(TextMeshProSettingsPath) != null)
            {
                Finish(action, exitWhenDone);
                return;
            }

            string unityPackage;
            try
            {
                unityPackage = FindTextMeshProEssentialsPackage();
            }
            catch (Exception exception)
            {
                Fail(exception, exitWhenDone);
                return;
            }

            double deadline = EditorApplication.timeSinceStartup + ImportTimeoutSeconds;
#pragma warning disable CS0618 // Unity 6.3+ khuyên UnityEditor.AssetPackage.Package; API cũ vẫn chạy và dùng được cả 2022.3.
            AssetDatabase.ImportPackageCallback onCompleted = null;
            AssetDatabase.ImportPackageFailedCallback onFailed = null;
            EditorApplication.CallbackFunction watchdog = null;

            void Unsubscribe()
            {
                AssetDatabase.importPackageCompleted -= onCompleted;
                AssetDatabase.importPackageFailed -= onFailed;
                EditorApplication.update -= watchdog;
            }

            onCompleted = packageName =>
            {
                Unsubscribe();
                Finish(action, exitWhenDone);
            };
            onFailed = (packageName, error) =>
            {
                Unsubscribe();
                Fail(new InvalidOperationException("Import TMP Essential Resources lỗi: " + error), exitWhenDone);
            };
            watchdog = () =>
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                Unsubscribe();
                Fail(new TimeoutException("Import TMP Essential Resources quá " + ImportTimeoutSeconds + " giây."), exitWhenDone);
            };

            AssetDatabase.importPackageCompleted += onCompleted;
            AssetDatabase.importPackageFailed += onFailed;
            EditorApplication.update += watchdog;
            AssetDatabase.ImportPackage(unityPackage, false);
#pragma warning restore CS0618
        }

        // TMP nằm trong com.unity.ugui từ Unity 6; Unity 2022.3 là package com.unity.textmeshpro riêng.
        private static string FindTextMeshProEssentialsPackage()
        {
            foreach (string packageName in TextMeshProHostPackages)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + packageName);
                if (info == null) continue;
                string unityPackage = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (File.Exists(unityPackage)) return unityPackage;
            }
            throw new InvalidOperationException("Không tìm thấy TMP Essential Resources.unitypackage trong com.unity.ugui hay com.unity.textmeshpro.");
        }

        private static void Finish(Action action, bool exitWhenDone)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Fail(exception, exitWhenDone);
                return;
            }
            if (exitWhenDone) EditorApplication.Exit(0);
        }

        private static void Fail(Exception exception, bool exitWhenDone)
        {
            Debug.LogException(exception);
            if (exitWhenDone) EditorApplication.Exit(1);
        }

        // ---------------------------------------------------------------- Ba host

        private static LeaderboardDemoHost BuildScreenHost(RectTransform stage, GameObject widgetPrefab)
        {
            RectTransform root = CreateRect("Host - Screen", stage);
            Stretch(root, 24f, 24f, 24f, 24f);
            LeaderboardWidget widget = InstantiateWidget(widgetPrefab, root, 0f, 0f, 0f, 0f);
            return AddHost(root, root, widget, "Screen");
        }

        private static LeaderboardDemoHost BuildPopupHost(RectTransform stage, GameObject widgetPrefab)
        {
            RectTransform root = CreateRect("Host - Popup", stage);
            Stretch(root, 0f, 0f, 0f, 0f);
            RectTransform dim = CreateRect("Dim", root);
            Stretch(dim, 0f, 0f, 0f, 0f);
            dim.gameObject.AddComponent<Image>().color = DimColor;

            RectTransform panel = CreateRect("Panel", root);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(920f, 1300f);
            LeaderboardWidget widget = InstantiateWidget(widgetPrefab, panel, 0f, 0f, 0f, 0f);
            return AddHost(root, panel, widget, "Popup");
        }

        private static LeaderboardDemoHost BuildWinBlockHost(RectTransform stage, GameObject widgetPrefab)
        {
            RectTransform root = CreateRect("Host - Win block", stage);
            Stretch(root, 0f, 0f, 0f, 0f);

            RectTransform panel = CreateRect("Panel", root);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(980f, 1440f);
            panel.gameObject.AddComponent<Image>().color = WinPanelColor;

            RectTransform title = CreateRect("Title", panel);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.sizeDelta = new Vector2(0f, 170f);
            AddLabel(title, "LEVEL COMPLETE", 84f);

            // Khối leaderboard chỉ là một phần layout của màn Win: nằm giữa tiêu đề và nút tiếp tục.
            LeaderboardWidget widget = InstantiateWidget(widgetPrefab, panel, 30f, 30f, 220f, 190f);

            RectTransform continueButton = CreateRect("Continue", panel);
            continueButton.anchorMin = continueButton.anchorMax = new Vector2(0.5f, 0f);
            continueButton.pivot = new Vector2(0.5f, 0f);
            continueButton.anchoredPosition = new Vector2(0f, 50f);
            continueButton.sizeDelta = new Vector2(460f, 130f);
            continueButton.gameObject.AddComponent<Image>().color = ContinueColor;
            // Một GameObject chỉ được có một Graphic: chữ nằm ở object con.
            RectTransform continueLabel = CreateRect("Label", continueButton);
            Stretch(continueLabel, 0f, 0f, 0f, 0f);
            AddLabel(continueLabel, "CONTINUE", 58f);

            return AddHost(root, panel, widget, "Win block");
        }

        // ---------------------------------------------------------------- Tiện ích

        private static LeaderboardDemoHost AddHost(RectTransform root, RectTransform panel, LeaderboardWidget widget, string displayName)
        {
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var host = root.gameObject.AddComponent<LeaderboardDemoHost>();
            var serialized = new SerializedObject(host);
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("group").objectReferenceValue = group;
            serialized.FindProperty("panel").objectReferenceValue = panel;
            serialized.FindProperty("widget").objectReferenceValue = widget;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return host;
        }

        private static LeaderboardWidget InstantiateWidget(GameObject prefab, RectTransform parent, float left, float right, float bottom, float top)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var rectTransform = (RectTransform)instance.transform;
            Stretch(rectTransform, left, right, bottom, top);
            return instance.GetComponent<LeaderboardWidget>();
        }

        private static void AddLabel(RectTransform parent, string value, float size)
        {
            var text = parent.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform)) { layer = UserInterfaceLayer };
            var rectTransform = (RectTransform)gameObject.transform;
            rectTransform.SetParent(parent, false);
            return rectTransform;
        }

        private static void Stretch(RectTransform rectTransform, float left, float right, float bottom, float top)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = new Vector2(left, bottom);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }
    }
}
