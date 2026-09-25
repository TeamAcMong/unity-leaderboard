# DreamTech Leaderboard

> Leaderboard lắp ráp kiểu Lego cho game mobile — uGUI + TextMeshPro, "premium smooth" kiểu Royal Match. Backend, chỉ số
> xếp hạng, âm thanh/haptic và nơi lưu trạng thái đều cắm qua port; cùng một widget đặt được vào popup, màn riêng hay một
> khối trong màn Win.

[![Version](https://img.shields.io/badge/version-0.4.0-blue.svg)](https://github.com/TeamAcMong/unity-leaderboard/tags)
[![Unity](https://img.shields.io/badge/unity-2022.3%20%E2%86%92%206000-black.svg)](https://unity.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## 📦 Cài đặt

**Package Manager → `+` → Add package from git URL:**

```
https://github.com/TeamAcMong/unity-leaderboard.git#0.4.0
```

Hoặc trong `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.dreamtech.leaderboard": "https://github.com/TeamAcMong/unity-leaderboard.git#0.4.0"
  }
}
```

> **Yêu cầu:** Unity 2022.3+ (đã kiểm 2022.3.62f2 và 6000.6.0f1), uGUI, TextMeshPro + **TMP Essential Resources** đã import.

## 🚀 Dùng

Toàn bộ hướng dẫn nằm trong package: [`Packages/com.dreamtech.leaderboard/README.md`](Packages/com.dreamtech.leaderboard/README.md)
— bốn port, composition root mẫu, hợp đồng host `Arm → PresentAsync(hostReady) → Disarm`, config, menu Editor, bẫy đã gặp.

Vì sao hệ thống được thiết kế như vậy (hướng thẩm mỹ, hiệu ứng đã bị từ chối, lỗi của bản tham khảo và cách sửa, kết quả kiểm
chứng): [`Documentation/DESIGN_NOTES.md`](Packages/com.dreamtech.leaderboard/Documentation/DESIGN_NOTES.md).

## 🗂 Repo này có gì

Nhánh `main` là **dev project Unity 6** để phát triển package; người dùng package chỉ tải tag (nội dung package, không kèm
project).

```
Packages/com.dreamtech.leaderboard/   ← package (thứ được phát hành)
  Runtime/Core        DreamTech.Leaderboard            C# thuần: domain, port, board, Mock
  Runtime/ViewModel   DreamTech.Leaderboard.ViewModel  C# thuần: RowState, RevealTimeline, toán
  Runtime/UI          DreamTech.Leaderboard.UI         LeaderboardWidget, list ảo hoá, hiệu ứng, config
  Runtime/League      DreamTech.Leaderboard.League     C# thuần: tier theo mùa, vùng lên/xuống, streak, rương, mô phỏng bot
  Runtime/League/Unity DreamTech.Leaderboard.League.Unity  adapter cần UnityEngine (PlayerPrefs)
  Editor              DreamTech.Leaderboard.Editor     sinh art/âm tạm, prefab mặc định, validator
  Tests/Editor        403 test EditMode (269 leaderboard + 134 League)
Assets/Demo/                          ← scene demo + test PlayMode (không đi theo package)
  LeaderboardDemo.unity               1 board, 3 host: màn riêng / popup / khối trong màn Win
  Scripts/LeaderboardDemo.cs          composition root + nút: +3, +12, +300, Top 3, #1, New, Best, Same, Fail next, Slow x0.1
  LeagueDemo.unity                    bàn thử League (IMGUI, chưa có art)
  Scripts/LeagueDemo.cs               composition root + nút: thắng/thoát/thua/hồi sinh, tua giờ, khép mùa, nhận rương
  Editor/*SceneBuilder                Tools/DreamTech/Leaderboard/Demo/Build (League) Demo Scene
  Tests/                              13 test PlayMode: scene thật + nút tua giờ / xoá dữ liệu của bảng thử League
deploy.sh, DEPLOY_UPM_SUBTREE.md      ← phát hành bằng git subtree split + tag
```

## 🛠 Phát triển

1. Mở repo bằng **Unity 6000.6.0f1** (hoặc Unity 6 mới hơn).
2. Mở `Assets/Demo/LeaderboardDemo.unity` → Play → bấm nút ở dải đáy. Chạm vào bảng trong lúc diễn = skip.
   Thử League: mở `Assets/Demo/LeagueDemo.unity` → Play (xem mục dưới).
3. Test:
   ```bash
   Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults TestResults/editmode.xml
   Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults TestResults/playmode.xml
   ```
4. Package hỗ trợ từ Unity 2022.3: trước khi phát hành, chạy thêm EditMode trên một project 2022.3 có TMP 3.0.x trỏ
   `"com.dreamtech.leaderboard": "file:<repo>/Packages/com.dreamtech.leaderboard"`.
5. Thử ngay trong game đang dùng package: tạm trỏ manifest của game sang đường dẫn `file:` ở trên, xong trả lại git URL.

Dựng lại scene demo sau khi đổi bố cục host:

```bash
Unity -batchmode -nographics -projectPath . -executeMethod DreamTech.Leaderboard.Demo.EditorTools.LeaderboardDemoSceneBuilder.BuildFromCommandLine
Unity -batchmode -nographics -quit -projectPath . -executeMethod DreamTech.Leaderboard.Demo.EditorTools.LeagueDemoSceneBuilder.BuildFromCommandLine
```

## 🏆 Bàn thử League

`Assets/Demo/LeagueDemo.unity` → Play. Chưa có art nên bảng vẽ bằng IMGUI: mục đích là thử **luật và luồng**, không phải giao diện.

| Nút | Thử điều gì |
|---|---|
| Thắng thường / khó / siêu khó | Cúp theo độ khó × hệ số streak, cộng ngay không chờ mạng |
| Thoát level / Thua hẳn / Hồi sinh | Luật streak: thoát và thua hẳn thì mất, hồi sinh thì giữ. Dòng "Thoát bây giờ có mất streak" là thứ popup cảnh báo sẽ hỏi |
| Tua +1h / +6h / → hết mùa | Đồng hồ có độ lệch: đếm ngược mùa, khép mùa, mùa mới |
| Lên hạng #1 / Giữ #1 hết mùa | "Lên hạng #1" chỉ đủ #1 lúc này (bot còn kiếm tiếp nên sẽ trôi); "Giữ #1 hết mùa" mới lên hạng được |
| Lỗi mạng lần sau | Cúp nằm lại hàng chờ, lần gọi sau gửi lại, không cộng trùng |
| Khoá League | Cổng tính năng: đang khoá thì thắng cũng không cộng gì |
| Xoá dữ liệu | Xoá PlayerPrefs của demo và đưa đồng hồ về giờ thật |

Bảng bên trái là nhóm: dải Promotion / Demotion chèn đúng chỗ design vẽ, `[chest.*]` là rương sẽ nhận nếu mùa kết thúc ngay
lúc đó, `➤` là dòng của bạn. Khi mùa kết thúc, hộp "KẾT QUẢ MÙA" hiện ra với hai nút **Xem xong** và **Nhận rương**; quà chạy
qua granter vào "Ví" ở cột phải.

Trạng thái lưu bằng PlayerPrefs nên tắt Play rồi Play lại vẫn còn streak, cúp và mùa đang chạy.

Chỉnh số ở Inspector của GameObject `LeagueDemo`: số người mỗi nhóm, số lên/xuống hạng, độ dài mùa (mặc định 24h cho dễ thử;
design dùng theo tuần), cúp theo độ khó, hệ số từng bậc streak, độ trễ mạng giả lập.

## 🚢 Phát hành

Xem [`DEPLOY_UPM_SUBTREE.md`](DEPLOY_UPM_SUBTREE.md). Tóm tắt: bump `package.json` → cập nhật hai CHANGELOG → commit + push
`main` → `./deploy.sh --semver X.Y.Z`.

## 📄 License

MIT — xem [LICENSE](LICENSE).
