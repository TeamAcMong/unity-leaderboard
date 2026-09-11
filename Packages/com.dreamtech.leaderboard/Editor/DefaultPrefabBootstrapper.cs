using System;
using DreamTech.Leaderboard.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DreamTech.Leaderboard.EditorTools
{
    /// <summary>
    /// Tạo bộ asset mặc định của package (art, âm tạm, config, prefab widget + row) — CHỈ khi còn thiếu.
    /// Sau lần đầu, prefab là nguồn sự thật: sửa trực tiếp trong prefab, không chạy lại để "đè" (trừ khi chủ động overwrite).
    /// Dựng trong preview scene nên không làm bẩn scene đang mở.
    /// </summary>
    public static class DefaultPrefabBootstrapper
    {
        private const int UserInterfaceLayer = 5;
        private const float RowHeight = 124f;
        private const float RowSideMargin = 18f;
        private const float ListTopPadding = 52f;
        private const float ListBottomPadding = 56f;

        private static readonly Color PanelColor = new Color32(0x3B, 0x74, 0xE0, 0xFF);
        private static readonly Color ListBackgroundColor = new Color32(0x1E, 0x4A, 0xAE, 0xFF);
        private static readonly Color PillColor = new Color32(0x3C, 0xC2, 0x5E, 0xFF);

        private const string CreateMenuPath = "Tools/DreamTech/Leaderboard/Create Default Assets (if missing)";

        [MenuItem(CreateMenuPath)]
        private static void CreateFromMenu()
        {
            CreateDefaultAssets(false);
        }

        // Cài bằng git URL thì package chỉ đọc: asset mặc định đã đi kèm, không có gì để tạo.
        [MenuItem(CreateMenuPath, true)]
        private static bool CanCreateFromMenu()
        {
            return LeaderboardEditorPaths.IsWritable(LeaderboardEditorPaths.PackageRoot);
        }

        public static void CreateDefaultAssets(bool overwritePrefabs)
        {
            PlaceholderArtGenerator.ArtSet art = PlaceholderArtGenerator.Load(LeaderboardEditorPaths.ArtFolder);
            if (!art.IsComplete) art = PlaceholderArtGenerator.Regenerate(LeaderboardEditorPaths.ArtFolder);

            PlaceholderAudioBaker.AudioSet audio = PlaceholderAudioBaker.Load(LeaderboardEditorPaths.AudioFolder);
            if (!audio.IsComplete) audio = PlaceholderAudioBaker.Bake(LeaderboardEditorPaths.AudioFolder);

            string configFolder = LeaderboardEditorPaths.ConfigFolder;
            LeaderboardEditorPaths.EnsureFolder(configFolder);
            var motion = EnsureAsset<LeaderboardMotionConfig>(configFolder + "/DefaultLeaderboardMotionConfig.asset");
            var theme = EnsureAsset<LeaderboardThemeConfig>(configFolder + "/DefaultLeaderboardThemeConfig.asset");
            var text = EnsureAsset<LeaderboardTextConfig>(configFolder + "/DefaultLeaderboardTextConfig.asset");
            EnsureAsset<LeaderboardBoardConfig>(configFolder + "/DefaultLeaderboardBoardConfig.asset");
            EnsureAsset<MockLeaderboardConfig>(configFolder + "/DefaultMockLeaderboardConfig.asset");

            LeaderboardEditorPaths.EnsureFolder(LeaderboardEditorPaths.PrefabFolder);
            var rowObject = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.RowPrefabPath);
            if (rowObject == null || overwritePrefabs) rowObject = BuildInPreviewScene(scene => BuildRow(art, scene), LeaderboardEditorPaths.RowPrefabPath);
            var rowPrefab = rowObject.GetComponent<LeaderboardEntryView>();

            var widgetObject = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath);
            if (widgetObject == null || overwritePrefabs)
            {
                BuildInPreviewScene(scene => BuildWidget(art, audio, motion, theme, text, rowPrefab, scene), LeaderboardEditorPaths.WidgetPrefabPath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Leaderboard] Asset mặc định đã sẵn sàng trong " + LeaderboardEditorPaths.PackageRoot);
        }

        // ---------------------------------------------------------------- Row

        private static GameObject BuildRow(PlaceholderArtGenerator.ArtSet art, Scene scene)
        {
            RectTransform root = CreateObject("LeaderboardEntryRow", null, scene);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(-RowSideMargin * 2f, RowHeight);
            var canvasGroup = root.gameObject.AddComponent<CanvasGroup>();
            var view = root.gameObject.AddComponent<LeaderboardEntryView>();

            RectTransform shadow = CreateObject("Shadow", root);
            Stretch(shadow, -26, -26, -14, -34);
            Image shadowImage = AddImage(shadow, art.SoftShadow, new Color(0.02f, 0.05f, 0.18f, 0f), true, 1f);

            RectTransform glow = CreateObject("Glow", root);
            Stretch(glow, -9, -9, -9, -9);
            Image glowImage = AddImage(glow, art.Rounded, new Color(1f, 0.84f, 0.3f, 0.3f), true, 0.9f);
            glow.gameObject.SetActive(false);

            RectTransform background = CreateObject("Background", root);
            Stretch(background, 0, 0, 0, 0);
            Image backgroundImage = AddImage(background, art.Chunky, new Color32(0x5E, 0x95, 0xF2, 0xFF), true, 1f);

            RectTransform shineMask = CreateObject("ShineMask", root);
            Stretch(shineMask, 6, 6, 4, PlaceholderArtGenerator.ChunkyLip + 4);
            shineMask.gameObject.AddComponent<RectMask2D>();
            RectTransform shine = CreateObject("Shine", shineMask);
            SetAnchored(shine, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, RowHeight * 2.4f));
            shine.localRotation = Quaternion.Euler(0f, 0f, -20f);
            AddImage(shine, art.Shine, new Color(1f, 1f, 1f, 0.45f), false, 1f);
            shine.gameObject.SetActive(false);

            RectTransform content = CreateObject("Content", root);
            Stretch(content, 0, 0, 0, PlaceholderArtGenerator.ChunkyLip);

            RectTransform badge = CreateObject("RankBadge", content);
            SetAnchored(badge, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(70f, 0f), new Vector2(80f, 80f));
            Image badgeImage = AddImage(badge, art.Circle, new Color(0f, 0f, 0f, 0.22f), false, 1f);
            RectTransform rankClip = CreateObject("RankClip", badge);
            Stretch(rankClip, 0, 0, 0, 0);
            rankClip.gameObject.AddComponent<RectMask2D>();
            RectTransform rank = CreateObject("RankText", rankClip);
            Stretch(rank, 0, 0, 0, 0);
            TMP_Text rankText = AddText(rank, "1", 42f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            AutoSize(rankText, 22f, 42f);
            RectTransform rankRoll = CreateObject("RankRollText", rankClip);
            Stretch(rankRoll, 0, 0, 0, 0);
            TMP_Text rankRollText = AddText(rankRoll, string.Empty, 42f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            AutoSize(rankRollText, 22f, 42f);

            RectTransform frame = CreateObject("AvatarFrame", content);
            SetAnchored(frame, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(170f, 0f), new Vector2(90f, 90f));
            AddImage(frame, art.Circle, Color.white, false, 1f);
            RectTransform avatar = CreateObject("Avatar", frame);
            Stretch(avatar, 6, 6, 6, 6);
            Image avatarImage = AddImage(avatar, art.Circle, Color.gray, false, 1f);
            RectTransform initial = CreateObject("AvatarInitial", avatar);
            Stretch(initial, 0, 0, 0, 0);
            TMP_Text initialText = AddText(initial, "A", 38f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);

            RectTransform name = CreateObject("Name", content);
            Stretch(name, 232, 250, 0, 0);
            // Không Bold giả lập ở text Ellipsis (xem LeaderboardPrefabValidator).
            TMP_Text nameText = AddText(name, "Player", 40f, Color.white, TextAlignmentOptions.MidlineLeft, art.TextOutline, FontStyles.Normal);
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.richText = false;

            RectTransform score = CreateObject("Score", content);
            SetAnchored(score, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(230f, 80f));
            TMP_Text scoreText = AddText(score, "0", 42f, new Color32(0xFF, 0xF1, 0xB0, 0xFF), TextAlignmentOptions.MidlineRight, art.TextOutline, FontStyles.Bold);

            RectTransform pill = CreateObject("Pill", content);
            SetAnchored(pill, new Vector2(0f, 0.5f), new Vector2(0.5f, 0f), new Vector2(92f, 34f), new Vector2(136f, 58f));
            Image pillImage = AddImage(pill, art.Chunky, PillColor, true, 1.6f);
            var pillGroup = pill.gameObject.AddComponent<CanvasGroup>();
            pillGroup.blocksRaycasts = false;
            RectTransform arrow = CreateObject("Arrow", pill);
            SetAnchored(arrow, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 4f), new Vector2(28f, 24f));
            Image arrowImage = AddImage(arrow, art.Arrow, Color.white, false, 1f);
            RectTransform pillLabel = CreateObject("Text", pill);
            Stretch(pillLabel, 50, 14, 0, 5);
            TMP_Text pillText = AddText(pillLabel, "12", 34f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            AutoSize(pillText, 20f, 34f);
            pill.gameObject.SetActive(false);

            RectTransform flash = CreateObject("Flash", root);
            Stretch(flash, 0, 0, 0, 0);
            Image flashImage = AddImage(flash, art.Rounded, new Color(1f, 1f, 1f, 0f), true, 1f);
            flashImage.enabled = false;

            RectTransform gap = CreateObject("Gap", root);
            Stretch(gap, 0, 0, 0, 0);
            TMP_Text gapText = AddText(gap, ". . .", 46f, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.Center, null, FontStyles.Bold);
            gap.gameObject.SetActive(false);

            Wire(view, "canvasGroup", canvasGroup);
            Wire(view, "shadowImage", shadowImage);
            Wire(view, "glowImage", glowImage);
            Wire(view, "backgroundImage", backgroundImage);
            Wire(view, "flashImage", flashImage);
            Wire(view, "contentRoot", content.gameObject);
            Wire(view, "gapRoot", gap.gameObject);
            Wire(view, "gapText", gapText);
            Wire(view, "rankBadgeImage", badgeImage);
            Wire(view, "rankText", rankText);
            Wire(view, "rankRollText", rankRollText);
            Wire(view, "avatarImage", avatarImage);
            Wire(view, "avatarInitialText", initialText);
            Wire(view, "nameText", nameText);
            Wire(view, "scoreText", scoreText);
            Wire(view, "shineTransform", shine);
            Wire(view, "pillGroup", pillGroup);
            Wire(view, "pillBackgroundImage", pillImage);
            Wire(view, "pillArrowImage", arrowImage);
            Wire(view, "pillText", pillText);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- Widget

        private static GameObject BuildWidget(PlaceholderArtGenerator.ArtSet art, PlaceholderAudioBaker.AudioSet audio,
                                              LeaderboardMotionConfig motion, LeaderboardThemeConfig theme, LeaderboardTextConfig text,
                                              LeaderboardEntryView rowPrefab, Scene scene)
        {
            RectTransform root = CreateObject("LeaderboardWidget", null, scene);
            Stretch(root, 0, 0, 0, 0);
            var widget = root.gameObject.AddComponent<LeaderboardWidget>();

            RectTransform panel = CreateObject("Background", root);
            Stretch(panel, 0, 0, 0, 0);
            AddImage(panel, art.Chunky, PanelColor, true, 0.5f);

            RectTransform title = CreateObject("Title", root);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.offsetMin = new Vector2(40f, -150f);
            title.offsetMax = new Vector2(-40f, -10f);
            TMP_Text titleText = AddText(title, text.Title, 66f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);

            RectTransform listArea = CreateObject("ListArea", root);
            Stretch(listArea, 26, 26, 150, 38);
            AddImage(listArea, art.Rounded, ListBackgroundColor, true, 0.7f);

            RectTransform scrollObject = CreateObject("ScrollView", listArea);
            Stretch(scrollObject, 0, 0, 0, 0);
            var scrollRect = scrollObject.gameObject.AddComponent<ScrollRect>();
            var scrollView = scrollObject.gameObject.AddComponent<LeaderboardScrollView>();

            RectTransform viewport = CreateObject("Viewport", scrollObject);
            Stretch(viewport, 0, 0, 6, 6);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image viewportImage = AddImage(viewport, null, new Color(1f, 1f, 1f, 0f), false, 1f);
            viewportImage.raycastTarget = true;

            RectTransform content = CreateObject("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 1000f);

            scrollRect.content = content;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 40f;

            RectTransform rays = CreateObject("Sunburst", content);
            SetAnchored(rays, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(1000f, 1000f));
            Image raysImage = AddImage(rays, art.Rays, new Color(1f, 0.85f, 0.3f, 0.4f), false, 1f);
            var sunburst = rays.gameObject.AddComponent<RankSunburstView>();
            Wire(sunburst, "image", raysImage);
            rays.gameObject.SetActive(false);

            RectTransform sticky = CreateObject("StickyLocalRow", listArea);
            sticky.anchorMin = new Vector2(0f, 0f);
            sticky.anchorMax = new Vector2(1f, 0f);
            sticky.pivot = new Vector2(0.5f, 0f);
            sticky.sizeDelta = new Vector2(-RowSideMargin * 2f, RowHeight);
            sticky.anchoredPosition = new Vector2(0f, 12f);
            var stickyGroup = sticky.gameObject.AddComponent<CanvasGroup>();
            Image stickyImage = AddImage(sticky, null, new Color(1f, 1f, 1f, 0f), false, 1f);
            stickyImage.raycastTarget = true;
            var stickyButton = sticky.gameObject.AddComponent<Button>();
            stickyButton.targetGraphic = stickyImage;
            stickyButton.transition = Selectable.Transition.None;
            sticky.gameObject.SetActive(false);

            RectTransform status = CreateObject("Status", listArea);
            Stretch(status, 0, 0, 0, 0);
            var statusView = status.gameObject.AddComponent<LeaderboardStatusView>();
            RectTransform loading = CreateObject("Loading", status);
            Stretch(loading, 0, 0, 0, 0);
            TMP_Text loadingText = AddText(loading, text.Loading, 46f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            loading.gameObject.SetActive(false);
            RectTransform error = CreateObject("Error", status);
            Stretch(error, 0, 0, 0, 0);
            RectTransform errorMessage = CreateObject("Message", error);
            SetAnchored(errorMessage, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(800f, 100f));
            TMP_Text errorText = AddText(errorMessage, text.ErrorMessage, 40f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Normal);
            RectTransform retry = CreateObject("RetryButton", error);
            SetAnchored(retry, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(280f, 110f));
            Image retryImage = AddImage(retry, art.Chunky, PillColor, true, 1f);
            retryImage.raycastTarget = true;
            var retryButton = retry.gameObject.AddComponent<Button>();
            retryButton.targetGraphic = retryImage;
            RectTransform retryLabel = CreateObject("Label", retry);
            Stretch(retryLabel, 0, 0, 0, PlaceholderArtGenerator.ChunkyLip);
            TMP_Text retryText = AddText(retryLabel, text.Retry, 44f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            error.gameObject.SetActive(false);
            RectTransform empty = CreateObject("Empty", status);
            Stretch(empty, 0, 0, 0, 0);
            TMP_Text emptyText = AddText(empty, text.Empty, 42f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Normal);
            empty.gameObject.SetActive(false);

            RectTransform skip = CreateObject("SkipCatcher", listArea);
            Stretch(skip, 0, 0, 0, 0);
            Image skipImage = AddImage(skip, null, new Color(1f, 1f, 1f, 0f), false, 1f);
            skipImage.raycastTarget = true;
            var skipButton = skip.gameObject.AddComponent<Button>();
            skipButton.targetGraphic = skipImage;
            skipButton.transition = Selectable.Transition.None;
            skip.gameObject.SetActive(false);

            RectTransform overlay = CreateObject("Overlay", root);
            Stretch(overlay, 0, 0, 0, 0);

            RectTransform bannerRoot = CreateObject("Banner", overlay);
            Stretch(bannerRoot, 0, 0, 0, 0);
            var bannerGroup = bannerRoot.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;
            var bannerView = bannerRoot.gameObject.AddComponent<TopTierBannerView>();
            RectTransform body = CreateObject("Body", bannerRoot);
            SetAnchored(body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 190f));
            Image bodyImage = AddImage(body, art.Chunky, new Color32(0xFF, 0xC4, 0x2E, 0xFF), true, 0.6f);
            RectTransform bannerTitle = CreateObject("Title", body);
            Stretch(bannerTitle, 20, 20, 14, 72);
            TMP_Text bannerTitleText = AddText(bannerTitle, text.PodiumTitle, 76f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            RectTransform bannerSubtitle = CreateObject("Subtitle", body);
            SetAnchored(bannerSubtitle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(660f, 54f));
            TMP_Text bannerSubtitleText = AddText(bannerSubtitle, "Rank #3", 38f, Color.white, TextAlignmentOptions.Center, art.TextOutline, FontStyles.Bold);
            Wire(bannerView, "group", bannerGroup);
            Wire(bannerView, "body", body);
            Wire(bannerView, "backgroundImage", bodyImage);
            Wire(bannerView, "titleText", bannerTitleText);
            Wire(bannerView, "subtitleText", bannerSubtitleText);
            bannerRoot.gameObject.SetActive(false);

            RectTransform celebration = CreateObject("Celebration", overlay);
            Stretch(celebration, 0, 0, 0, 0);
            var celebrationView = celebration.gameObject.AddComponent<CelebrationBurstView>();
            RectTransform particle = CreateObject("ParticleTemplate", celebration);
            SetAnchored(particle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 12f));
            Image particleImage = AddImage(particle, art.Rounded, Color.white, false, 1f);
            particle.gameObject.SetActive(false);
            Wire(celebrationView, "particleTemplate", particleImage);
            Wire(celebrationView, "starSprite", art.Star);
            Wire(celebrationView, "confettiSprite", art.Rounded);

            RectTransform feedback = CreateObject("Feedback", root);
            var oneShotSource = feedback.gameObject.AddComponent<AudioSource>();
            oneShotSource.playOnAwake = false;
            var tickSource = feedback.gameObject.AddComponent<AudioSource>();
            tickSource.playOnAwake = false;
            var audioFeedback = feedback.gameObject.AddComponent<AudioSourceLeaderboardFeedback>();
            Wire(audioFeedback, "oneShotSource", oneShotSource);
            Wire(audioFeedback, "tickSource", tickSource);
            Wire(audioFeedback, "liftClip", audio.Lift);
            Wire(audioFeedback, "tickClip", audio.Tick);
            Wire(audioFeedback, "landClip", audio.Land);
            Wire(audioFeedback, "fanfareClip", audio.Fanfare);

            Wire(scrollView, "scrollRect", scrollRect);
            Wire(scrollView, "viewport", viewport);
            Wire(scrollView, "content", content);
            Wire(scrollView, "rowPrefab", rowPrefab);
            Wire(scrollView, "sunburst", sunburst);
            Wire(scrollView, "stickyAnchor", sticky);
            Wire(scrollView, "stickyGroup", stickyGroup);
            Wire(scrollView, "stickyButton", stickyButton);
            SetFloat(scrollView, "rowHeight", RowHeight);
            SetFloat(scrollView, "topPadding", ListTopPadding);
            SetFloat(scrollView, "bottomPadding", ListBottomPadding);

            Wire(statusView, "loadingRoot", loading.gameObject);
            Wire(statusView, "loadingText", loadingText);
            Wire(statusView, "errorRoot", error.gameObject);
            Wire(statusView, "errorText", errorText);
            Wire(statusView, "retryButton", retryButton);
            Wire(statusView, "retryLabel", retryText);
            Wire(statusView, "emptyRoot", empty.gameObject);
            Wire(statusView, "emptyText", emptyText);

            Wire(widget, "scrollView", scrollView);
            Wire(widget, "statusView", statusView);
            Wire(widget, "bannerView", bannerView);
            Wire(widget, "celebrationView", celebrationView);
            Wire(widget, "bannerBounds", viewport);
            Wire(widget, "skipCatcher", skipButton);
            Wire(widget, "titleText", titleText);
            Wire(widget, "motionConfig", motion);
            Wire(widget, "themeConfig", theme);
            Wire(widget, "textConfig", text);
            var serializedWidget = new SerializedObject(widget);
            SerializedProperty sinks = serializedWidget.FindProperty("feedbackSinkComponents");
            sinks.ClearArray();
            sinks.InsertArrayElementAtIndex(0);
            sinks.GetArrayElementAtIndex(0).objectReferenceValue = audioFeedback;
            serializedWidget.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        // ---------------------------------------------------------------- Tiện ích dựng

        private static GameObject BuildInPreviewScene(Func<Scene, GameObject> build, string prefabPath)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = build(preview);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Không lưu được prefab: " + prefabPath);
                return prefab;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static RectTransform CreateObject(string name, Transform parent, Scene scene = default)
        {
            var gameObject = new GameObject(name, typeof(RectTransform)) { layer = UserInterfaceLayer };
            var rectTransform = (RectTransform)gameObject.transform;
            if (parent != null) rectTransform.SetParent(parent, false);
            else if (scene.IsValid()) SceneManager.MoveGameObjectToScene(gameObject, scene);
            return rectTransform;
        }

        /// <summary>Kéo giãn full cha với lề trái/phải/trên/dưới (âm = tràn ra ngoài).</summary>
        private static void Stretch(RectTransform rectTransform, float left, float right, float top, float bottom)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = new Vector2(left, bottom);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }

        private static void SetAnchored(RectTransform rectTransform, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = pivot;
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = position;
        }

        private static Image AddImage(RectTransform rectTransform, Sprite sprite, Color color, bool sliced, float pixelsPerUnitMultiplier)
        {
            var image = rectTransform.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sliced && sprite != null)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            }
            return image;
        }

        private static TMP_Text AddText(RectTransform rectTransform, string value, float size, Color color, TextAlignmentOptions alignment,
                                        Material material, FontStyles style)
        {
            var text = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            if (material != null) text.fontSharedMaterial = material;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.raycastTarget = false;
#if DREAMTECH_LEADERBOARD_TMP_WRAPPING_MODE
            text.textWrappingMode = TextWrappingModes.NoWrap;
#else
            text.enableWordWrapping = false;
#endif
            return text;
        }

        private static void AutoSize(TMP_Text text, float minimum, float maximum)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = minimum;
            text.fontSizeMax = maximum;
        }

        private static void Wire(Object target, string propertyName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + " không có field '" + propertyName + "'.");
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + " không có field '" + propertyName + "'.");
            property.floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
