# Phát hành bằng Git Subtree — DreamTech Leaderboard

## Vì sao Git Subtree?

| Cách | Dung lượng tải về | Tốc độ | Dùng khi |
|--------|--------------|-------|----------|
| Git URL trỏ thẳng `main` | Cả Unity project | Chậm | Thử nghiệm |
| **Git Subtree (cách này)** | Chỉ nội dung package | Nhanh | Production |

Game chỉ tải `Packages/com.dreamtech.leaderboard/` qua tag được tách bằng subtree — nhỏ và nhanh hơn nhiều.

## Checklist trước khi phát hành

- [ ] Mọi thay đổi đã commit vào `main`
- [ ] Đã bump version trong `Packages/com.dreamtech.leaderboard/package.json`
- [ ] Đã cập nhật `Packages/com.dreamtech.leaderboard/CHANGELOG.md` **và** `CHANGELOG.md` ở gốc repo
- [ ] EditMode + PlayMode xanh trên dev project (Unity 6)
- [ ] EditMode xanh trên một project Unity 2022.3 có TMP 3.0.x trỏ `file:` vào package
- [ ] Mọi file mới trong package đều có `.meta` (package cài bằng git là chỉ đọc — thiếu `.meta` thì Unity bỏ qua file đó)
- [ ] `main` đã push lên GitHub

## Các bước

### Bước 1 — Bump version

`Packages/com.dreamtech.leaderboard/package.json`:
```json
{
  "version": "0.2.0"
}
```

### Bước 2 — Cập nhật CHANGELOG

```markdown
## [0.2.0] - 2026-XX-XX

### Added
- Tính năng X

### Fixed
- Lỗi Y
```

### Bước 3 — Commit & push

```bash
git add .
git commit -m "chore(release): 0.2.0"
git push origin main
```

### Bước 4 — Chạy deploy

```bash
./deploy.sh --semver "0.2.0"
```

**Script làm gì:**
0. Chặn nếu version trong `package.json` khác `--semver`, hoặc thư mục package còn thay đổi chưa commit
1. `git subtree split --prefix="Packages/com.dreamtech.leaderboard" --branch upm` — tách thư mục package thành nhánh riêng
2. `git tag 0.2.0 upm` — gắn tag
3. `git push origin upm --tags` — đẩy tag lên (tag giữ commit chỉ chứa package)
4. Xoá nhánh `upm` tạm trên remote và local (tag giữ lại mãi)

### Bước 5 — Kiểm

Mở `https://github.com/TeamAcMong/unity-leaderboard/tags` — phải thấy tag mới. Bấm vào tag → gốc chỉ có nội dung package.

## URL cài cho game

```
https://github.com/TeamAcMong/unity-leaderboard.git#0.2.0
```

## Xử lý sự cố

### "tag already exists"
Tag là bất biến. Chỉ xoá tag khi **chưa có game nào cài** bản đó:
```bash
git tag -d 0.2.0
git push origin :refs/tags/0.2.0
./deploy.sh --semver "0.2.0"
```
Nếu đã có game cài → phát hành số mới (0.2.1).

### "refusing to update checked out branch"
Đang đứng ở nhánh `upm` — `git checkout main` rồi chạy lại.

### Unity không tìm thấy package
- Tag có thật không: `git ls-remote --tags origin`
- Đúng định dạng: `https://github.com/TeamAcMong/unity-leaderboard.git#X.Y.Z` (dấu `#` trước version)

## Versioning

Theo [Semantic Versioning](https://semver.org/):
- **MAJOR** (1.0.0) — đổi API không tương thích (đổi chữ ký port, xoá/đổi số `LeaderboardBeat`)
- **MINOR** (0.X.0) — thêm tính năng, tương thích ngược
- **PATCH** (0.0.X) — chỉ sửa lỗi

## Làm tay (không dùng deploy.sh)

```bash
git subtree split --prefix="Packages/com.dreamtech.leaderboard" --branch upm
git tag 0.2.0 upm
git push origin upm --tags
git push origin --delete upm
git branch -D upm
```

## Không được

- ❌ Dùng lại số version — đã tag thì coi như bất biến
- ❌ Đổi `name` trong `package.json` sau lần phát hành đầu — mọi game đang cài sẽ gãy
- ❌ Sửa tay nhánh `upm` — bị ghi đè ở lần deploy sau
- ❌ Xoá tag đã có game cài
