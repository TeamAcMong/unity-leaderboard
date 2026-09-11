namespace DreamTech.Leaderboard
{
    /// <summary>Nơi lưu <see cref="RevealSnapshot"/> theo từng board (PlayerPrefs, save system của game, bộ nhớ...).</summary>
    public interface ILeaderboardSnapshotStore
    {
        bool TryLoad(string boardId, out RevealSnapshot snapshot);
        void Save(string boardId, RevealSnapshot snapshot);
        void Clear(string boardId);
    }
}
