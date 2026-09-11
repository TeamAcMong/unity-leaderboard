using System.Runtime.CompilerServices;

// Test được nhìn internal thay vì nới rộng API công khai: người dùng package không thấy gì thay đổi.
[assembly: InternalsVisibleTo("DreamTech.Leaderboard.Tests")]
[assembly: InternalsVisibleTo("DreamTech.Leaderboard.UI.Tests")]
