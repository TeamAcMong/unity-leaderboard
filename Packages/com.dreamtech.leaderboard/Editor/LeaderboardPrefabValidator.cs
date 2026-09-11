using System.Collections.Generic;
using DreamTech.Leaderboard.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DreamTech.Leaderboard.EditorTools
{
    /// <summary>
    /// Hợp đồng của prefab leaderboard (package lẫn prefab variant của game). Chạy bằng menu hoặc từ test.
    /// Mỗi luật chặn một lỗi đã từng xảy ra ở bản tham khảo.
    /// </summary>
    public static class LeaderboardPrefabValidator
    {
        /// <summary>Đỉnh của OutBack(overshoot 1.8) khi pill bật ra, cộng biên an toàn.</summary>
        private const float PillPopPeakScale = 1.12f;

        private static readonly HashSet<string> OptionalWidgetFields = new HashSet<string> { "titleText", "motionConfig", "themeConfig", "textConfig" };
        private static readonly HashSet<string> OptionalScrollFields = new HashSet<string> { "sunburst", "stickyAnchor", "stickyGroup", "stickyButton" };

        [MenuItem("Tools/DreamTech/Leaderboard/Validate Default Prefabs")]
        private static void ValidateFromMenu()
        {
            var widgetObject = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.WidgetPrefabPath);
            var rowObject = AssetDatabase.LoadAssetAtPath<GameObject>(LeaderboardEditorPaths.RowPrefabPath);
            var issues = new List<string>();
            if (rowObject) issues.AddRange(ValidateRow(rowObject.GetComponent<LeaderboardEntryView>()));
            else issues.Add("Thiếu prefab row: " + LeaderboardEditorPaths.RowPrefabPath);
            if (widgetObject) issues.AddRange(ValidateWidget(widgetObject.GetComponent<LeaderboardWidget>(), 1.05f));
            else issues.Add("Thiếu prefab widget: " + LeaderboardEditorPaths.WidgetPrefabPath);
            if (!PlaceholderArtGenerator.IsCurrentVersion(LeaderboardEditorPaths.ArtFolder, out string staleAsset)) issues.Add("Art sinh từ phiên bản cũ: " + staleAsset);

            if (issues.Count == 0) Debug.Log("[Leaderboard] Prefab hợp lệ.");
            else Debug.LogWarning("[Leaderboard] " + issues.Count + " vấn đề:\n- " + string.Join("\n- ", issues));
        }

        public static List<string> ValidateRow(LeaderboardEntryView row)
        {
            var issues = new List<string>();
            if (row == null)
            {
                issues.Add("Không có LeaderboardEntryView trên prefab row.");
                return issues;
            }

            CollectMissingReferences(row, null, issues);
            TMP_Text nameText = row.NameText;
            if (nameText != null)
            {
                // TMP tra ký tự "…" theo style/weight đang dùng; tra hụt là nó tự đổi sang Truncate và serialize luôn. Bold giả lập là
                // nghi phạm chính nên cấm hẳn.
                if (nameText.overflowMode != TextOverflowModes.Ellipsis) issues.Add("Name phải dùng Overflow = Ellipsis (đang " + nameText.overflowMode + ").");
                if ((nameText.fontStyle & FontStyles.Bold) != 0) issues.Add("Name không được dùng Bold giả lập (dùng font/material đậm thay thế).");
                if (nameText.richText) issues.Add("Name phải tắt Rich Text (tên do backend trả về có thể chứa thẻ).");
                if (!IsNoWrap(nameText)) issues.Add("Name phải NoWrap.");
                if (nameText.font == null) issues.Add("Name chưa có font.");
            }
            return issues;
        }

        public static List<string> ValidateWidget(LeaderboardWidget widget, float liftScale)
        {
            var issues = new List<string>();
            if (widget == null)
            {
                issues.Add("Không có LeaderboardWidget trên prefab.");
                return issues;
            }

            CollectMissingReferences(widget, OptionalWidgetFields, issues);
            LeaderboardScrollView scrollView = widget.ScrollView;
            if (scrollView == null) return issues;

            CollectMissingReferences(scrollView, OptionalScrollFields, issues);
            if (scrollView.RowPrefab != null)
            {
                issues.AddRange(ValidateRow(scrollView.RowPrefab));
                float overhang = TopOverhang(scrollView.RowPrefab, scrollView.RowHeight, liftScale);
                if (scrollView.TopPadding + 0.5f < overhang)
                {
                    issues.Add("topPadding (" + scrollView.TopPadding + ") nhỏ hơn phần row tràn lên khi nhấc (" + overhang.ToString("F1") +
                               ") — row hạng #1 sẽ bị mask cắt.");
                }
            }

            if (widget.CelebrationView != null)
            {
                CollectMissingReferences(widget.CelebrationView, null, issues);
            }
            if (widget.BannerView != null) CollectMissingReferences(widget.BannerView, null, issues);
            if (widget.StatusView != null) CollectMissingReferences(widget.StatusView, null, issues);
            return issues;
        }

        /// <summary>Mọi dependency của prefab phải nằm trong các thư mục cho phép (dùng cho prefab của package).</summary>
        public static List<string> ValidateDependencies(string prefabPath, IReadOnlyList<string> allowedPrefixes)
        {
            var issues = new List<string>();
            foreach (string dependency in AssetDatabase.GetDependencies(prefabPath, true))
            {
                bool isAllowed = false;
                for (int index = 0; index < allowedPrefixes.Count; index++)
                {
                    if (dependency.StartsWith(allowedPrefixes[index])) isAllowed = true;
                }
                if (!isAllowed) issues.Add(prefabPath + " phụ thuộc asset ngoài package: " + dependency);
            }
            return issues;
        }

        /// <summary>Phần row tràn lên trên mép trên của nó khi nhấc lên và pill bật ra (tính trong hệ toạ độ của row).</summary>
        public static float TopOverhang(LeaderboardEntryView row, float rowHeight, float liftScale)
        {
            float halfHeight = rowHeight * 0.5f;
            float highest = halfHeight;
            var root = row.RectTransform;
            if (row.PillGroup != null)
            {
                var pill = (RectTransform)row.PillGroup.transform;
                float pillBottom = LocalBottom(root, pill);
                float pillHeight = pill.rect.height;
                highest = Mathf.Max(highest, pillBottom + pillHeight * PillPopPeakScale);
            }
            if (row.GlowImage != null) highest = Mathf.Max(highest, LocalTop(root, row.GlowImage.rectTransform));
            return Mathf.Max(0f, highest * liftScale - halfHeight);
        }

        // TMP 3.2 / uGUI 2.0 đổi bool enableWordWrapping thành enum textWrappingMode; TMP 3.0.x của Unity 2022.3 chỉ có bool.
        private static bool IsNoWrap(TMP_Text text)
        {
#if DREAMTECH_LEADERBOARD_TMP_WRAPPING_MODE
            return text.textWrappingMode == TextWrappingModes.NoWrap;
#else
            return !text.enableWordWrapping;
#endif
        }

        private static float LocalTop(RectTransform root, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            return root.InverseTransformPoint(corners[1]).y;
        }

        private static float LocalBottom(RectTransform root, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            return root.InverseTransformPoint(corners[0]).y;
        }

        private static void CollectMissingReferences(Object component, HashSet<string> optionalFields, List<string> issues)
        {
            var serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.name == "m_Script") continue;
                if (optionalFields != null && optionalFields.Contains(property.name)) continue;
                if (property.objectReferenceValue == null) issues.Add(component.GetType().Name + "." + property.name + " chưa được nối.");
            }
        }
    }
}
