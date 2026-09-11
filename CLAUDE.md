# unity-leaderboard — hướng dẫn cho Claude Code

Repo này là **dev project Unity 6** của một UPM package (`com.dreamtech.leaderboard`), không phải game. Thứ được phát hành là
thư mục `Packages/com.dreamtech.leaderboard/`; game cài bằng `https://github.com/TeamAcMong/unity-leaderboard.git#<tag>`.

**Luật của package (hướng thẩm mỹ, kiến trúc, bất biến, quy ước code) nằm ở
[`Packages/com.dreamtech.leaderboard/CLAUDE.md`](Packages/com.dreamtech.leaderboard/CLAUDE.md) — đọc file đó trước khi sửa
bất cứ gì trong package.**

## Bố cục

| Đường dẫn | Là gì | Đi theo package? |
|---|---|---|
| `Packages/com.dreamtech.leaderboard/` | Package | ✅ |
| `Assets/Demo/` | Scene demo (3 host), composition root mẫu, test PlayMode | ❌ |
| `Assets/TextMesh Pro/` | TMP Essential Resources — prefab của package cần font/shader trong đó | ❌ |
| `deploy.sh`, `DEPLOY_UPM_SUBTREE.md` | Phát hành bằng subtree split + tag | ❌ |
| `CHANGELOG.md` (gốc) | Bản sao CHANGELOG của package — sửa cả hai cùng lúc | ❌ |

## Luật riêng của repo

- **Unity:** dev project mở bằng 6000.6.0f1. Package phải chạy từ **Unity 2022.3**: code trong package không dùng API chỉ có từ
  2023.1+; khác biệt TMP 3.0 ↔ TMP 3.2 / uGUI 2.x tách bằng `versionDefines`. Code trong `Assets/Demo` chỉ chạy ở dev project
  nên được dùng API Unity 6 (vd `FindAnyObjectByType`).
- **Mọi file mới trong package phải có `.meta` được commit.** Package cài bằng git là chỉ đọc; thiếu `.meta` thì Unity của game
  bỏ qua file và GUID đổi giữa các máy. Mở Unity (hoặc chạy batchmode) một lần trước khi commit file mới.
- **Scene demo là sản phẩm sinh ra.** Sửa bố cục trong `Assets/Demo/Editor/LeaderboardDemoSceneBuilder.cs` rồi dựng lại, không
  sửa tay `LeaderboardDemo.unity`.
- **Không đổi GUID** của asset trong package (không xoá rồi tạo lại `.meta`): prefab variant trong game trỏ vào GUID đó.
- **Phát hành:** chỉ sau khi ma trận test ở dưới xanh. Tag là bất biến.
- **Commit:** conventional commits `type(scope): mô tả` (scope: `leaderboard`, `demo`, `editor`, `docs`, `release`).

## Ma trận test trước khi phát hành

```bash
# Unity 6 — dev project
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults TestResults/editmode.xml
Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults TestResults/playmode.xml

# Unity 2022.3 — project tạm có manifest:
#   "com.dreamtech.leaderboard": "file:<repo>/Packages/com.dreamtech.leaderboard",
#   "com.unity.textmeshpro": "3.0.6", "com.unity.ugui": "1.0.0", "com.unity.test-framework": "1.1.33",
#   "testables": ["com.dreamtech.leaderboard"]
# + TMP Essential Resources giải nén vào Assets/ của project tạm
Unity-2022.3 -batchmode -nographics -projectPath <project tạm> -runTests -testPlatform EditMode -testResults editmode-2022.xml
```

Kết quả lần phát hành 0.1.0: Unity 6000.6.0f1 99/99 EditMode + 5/5 PlayMode; Unity 2022.3.62f2 + TMP 3.0.7 99/99 EditMode;
Icon Match (2022.3 + TMP 3.2.0-pre.12) 99/99 + chạy thật.
