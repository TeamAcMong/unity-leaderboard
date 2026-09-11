using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DreamTech.Leaderboard.UI
{
    /// <summary>
    /// Hạt nhẹ chạy trong Canvas bằng Image, không cần Particle System.
    /// <list type="bullet">
    /// <item>Twinkles: vài ngôi sao nở rồi tắt quanh row — mọi lần lên hạng.</item>
    /// <item>Confetti: giấy lật — CHỈ top 3 / #1, để nó còn giá trị.</item>
    /// </list>
    /// Mảnh hạt được nhân bản từ template đã bake trong prefab (sprite + material nối sẵn).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CelebrationBurstView : MonoBehaviour
    {
        [Tooltip("Image mẫu (inactive) để nhân bản mảnh hạt.")]
        [SerializeField] private Image particleTemplate;
        [SerializeField] private Sprite starSprite;
        [SerializeField] private Sprite confettiSprite;
        [SerializeField, Min(1)] private int maximumPieces = 260;

        private enum PieceKind
        {
            Confetti,
            Twinkle,
        }

        private sealed class Piece
        {
            public RectTransform Transform;
            public Image Image;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Rotation;
            public float RotationSpeed;
            public float Flip;
            public float FlipSpeed;
            public float Lifetime;
            public float Age;
            public float Gravity;
            public float Drag;
            public PieceKind Kind;
            public Color Color;
        }

        private readonly List<Piece> _alivePieces = new List<Piece>();
        private readonly Stack<Piece> _pool = new Stack<Piece>();
        private int _createdCount;
        private LeaderboardVisualSettings _visuals;

        public float TimeScale { get; set; } = 1f;
        public RectTransform RectTransform => (RectTransform)transform;
        internal Sprite StarSprite => starSprite;
        internal Sprite ConfettiSprite => confettiSprite;
        internal Image ParticleTemplate => particleTemplate;
        internal int AliveCount => _alivePieces.Count;

        public void Twinkles(Rect bounds, int count, Color color, LeaderboardVisualSettings visuals)
        {
            _visuals = visuals;
            Vector2 center = bounds.center;
            float halfWidth = bounds.width * 0.5f;
            float halfHeight = bounds.height * 0.5f;
            for (int index = 0; index < count; index++)
            {
                Piece piece = Spawn(starSprite, PieceKind.Twinkle);
                if (piece == null) return;
                // Rải trên viền ngoài của row (trên/dưới/hai đầu) để không che chữ.
                Vector2 offset;
                if (Random.value < 0.7f)
                {
                    offset = new Vector2(Random.Range(-halfWidth, halfWidth), (Random.value < 0.5f ? -1f : 1f) * halfHeight * Random.Range(0.8f, 1.25f));
                }
                else
                {
                    offset = new Vector2((Random.value < 0.5f ? -1f : 1f) * halfWidth * Random.Range(0.92f, 1.04f), Random.Range(-halfHeight, halfHeight));
                }
                piece.Position = center + offset;
                piece.Velocity = new Vector2(Random.Range(-15f, 15f), Random.Range(20f, 60f));
                piece.Gravity = 0f;
                piece.Drag = 0.5f;
                piece.Lifetime = Random.Range(visuals.TwinkleLifetimeRange.x, visuals.TwinkleLifetimeRange.y);
                piece.Age = -Random.Range(0f, 0.25f);
                float size = Random.Range(visuals.TwinkleSizeRange.x, visuals.TwinkleSizeRange.y);
                piece.Transform.sizeDelta = new Vector2(size, size);
                piece.RotationSpeed = Random.Range(-60f, 60f);
                Activate(piece, color);
            }
        }

        public void Confetti(Rect bounds, int count, Color[] palette, LeaderboardVisualSettings visuals)
        {
            _visuals = visuals;
            Vector2 center = bounds.center;
            float halfWidth = bounds.width * 0.5f;
            for (int index = 0; index < count; index++)
            {
                Piece piece = Spawn(confettiSprite, PieceKind.Confetti);
                if (piece == null) return;
                float angle = (90f + Random.Range(-visuals.ConfettiSpreadAngle, visuals.ConfettiSpreadAngle)) * Mathf.Deg2Rad;
                piece.Position = center + new Vector2(Random.Range(-halfWidth, halfWidth) * 0.85f, Random.Range(-10f, 10f));
                piece.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(visuals.ConfettiSpeedRange.x, visuals.ConfettiSpeedRange.y);
                piece.Gravity = visuals.ConfettiGravity;
                piece.Drag = visuals.ConfettiDrag;
                piece.Lifetime = Random.Range(visuals.ConfettiLifetimeRange.x, visuals.ConfettiLifetimeRange.y);
                piece.Transform.sizeDelta = new Vector2(Random.Range(visuals.ConfettiSizeMinimum.x, visuals.ConfettiSizeMaximum.x),
                                                        Random.Range(visuals.ConfettiSizeMinimum.y, visuals.ConfettiSizeMaximum.y));
                piece.RotationSpeed = Random.Range(-480f, 480f);
                Color color = palette != null && palette.Length > 0 ? palette[Random.Range(0, palette.Length)] : Color.white;
                Activate(piece, color);
            }
        }

        public void Clear()
        {
            for (int index = _alivePieces.Count - 1; index >= 0; index--) Recycle(_alivePieces[index]);
            _alivePieces.Clear();
        }

        private void Update()
        {
            if (_alivePieces.Count == 0) return;
            float deltaTime = Time.unscaledDeltaTime * TimeScale;
            for (int index = _alivePieces.Count - 1; index >= 0; index--)
            {
                Piece piece = _alivePieces[index];
                piece.Age += deltaTime;
                if (piece.Age >= piece.Lifetime)
                {
                    Recycle(piece);
                    _alivePieces.RemoveAt(index);
                    continue;
                }
                if (piece.Age < 0f)
                {
                    piece.Transform.localScale = Vector3.zero;
                    continue;
                }

                piece.Velocity.y -= piece.Gravity * deltaTime;
                piece.Velocity *= Mathf.Max(0f, 1f - piece.Drag * deltaTime);
                piece.Position += piece.Velocity * deltaTime;
                piece.Rotation += piece.RotationSpeed * deltaTime;
                piece.Flip += piece.FlipSpeed * deltaTime;

                float progress = piece.Age / piece.Lifetime;
                piece.Transform.anchoredPosition = piece.Position;
                piece.Transform.localRotation = Quaternion.Euler(0f, 0f, piece.Rotation);
                Color color = piece.Color;
                if (piece.Kind == PieceKind.Twinkle)
                {
                    float bloom = Mathf.Sin(progress * Mathf.PI);
                    piece.Transform.localScale = new Vector3(bloom, bloom, 1f);
                    color.a = bloom;
                }
                else
                {
                    piece.Transform.localScale = new Vector3(Mathf.Cos(piece.Flip), 1f, 1f);
                    color.a = progress < 0.7f ? 1f : 1f - (progress - 0.7f) / 0.3f;
                }
                piece.Image.color = color;
            }
        }

        private Piece Spawn(Sprite sprite, PieceKind kind)
        {
            Piece piece;
            if (_pool.Count > 0)
            {
                piece = _pool.Pop();
            }
            else
            {
                if (_createdCount >= maximumPieces || particleTemplate == null) return null;
                _createdCount++;
                Image image = Instantiate(particleTemplate, transform, false);
                image.raycastTarget = false;
                var pieceTransform = (RectTransform)image.transform;
                pieceTransform.anchorMin = new Vector2(0.5f, 0.5f);
                pieceTransform.anchorMax = new Vector2(0.5f, 0.5f);
                piece = new Piece { Transform = pieceTransform, Image = image };
            }
            piece.Image.sprite = sprite;
            piece.Kind = kind;
            piece.Age = 0f;
            piece.Rotation = Random.Range(0f, 360f);
            piece.Flip = Random.Range(0f, Mathf.PI * 2f);
            piece.FlipSpeed = Random.Range(8f, 15f);
            return piece;
        }

        private void Activate(Piece piece, Color color)
        {
            piece.Color = color;
            piece.Image.color = color;
            piece.Transform.anchoredPosition = piece.Position;
            piece.Transform.localScale = Vector3.zero;
            piece.Transform.gameObject.SetActive(true);
            _alivePieces.Add(piece);
        }

        private void Recycle(Piece piece)
        {
            piece.Transform.gameObject.SetActive(false);
            _pool.Push(piece);
        }
    }
}
