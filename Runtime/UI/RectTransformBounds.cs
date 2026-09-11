using UnityEngine;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>Đổi hình chữ nhật của một RectTransform sang hệ toạ độ local của RectTransform khác.</summary>
    public static class RectTransformBounds
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        public static bool TryGetBounds(RectTransform source, RectTransform space, out Rect bounds)
        {
            bounds = default;
            if (source == null || space == null) return false;
            source.GetWorldCorners(Corners);
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int index = 0; index < 4; index++)
            {
                Vector3 local = space.InverseTransformPoint(Corners[index]);
                minimum = Vector2.Min(minimum, local);
                maximum = Vector2.Max(maximum, local);
            }
            bounds = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            return true;
        }
    }
}
