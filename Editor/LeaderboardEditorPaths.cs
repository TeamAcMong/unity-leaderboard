using System.IO;
using UnityEditor;

namespace DreamTech.Leaderboard.EditorTools
{
    /// <summary>Đường dẫn asset của package, tìm qua PackageInfo để vẫn đúng khi package được cài bằng git URL.</summary>
    public static class LeaderboardEditorPaths
    {
        public const string PackageName = "com.dreamtech.leaderboard";

        public static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LeaderboardEditorPaths).Assembly);
                return info != null ? info.assetPath : "Packages/" + PackageName;
            }
        }

        public static string ArtFolder => PackageRoot + "/Art";
        public static string AudioFolder => PackageRoot + "/Audio/Placeholder";
        public static string ConfigFolder => PackageRoot + "/Config";
        public static string PrefabFolder => PackageRoot + "/Prefabs";
        public static string WidgetPrefabPath => PrefabFolder + "/LeaderboardWidget.prefab";
        public static string RowPrefabPath => PrefabFolder + "/LeaderboardEntryRow.prefab";

        /// <summary>Package cài bằng git URL là chỉ đọc: công cụ sinh asset phải nhận thư mục đích từ ngoài.</summary>
        public static bool IsWritable(string assetFolder)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetFolder);
            return info == null || info.source == UnityEditor.PackageManager.PackageSource.Embedded ||
                   info.source == UnityEditor.PackageManager.PackageSource.Local;
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            Directory.CreateDirectory(assetFolder);
            AssetDatabase.ImportAsset(assetFolder, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
