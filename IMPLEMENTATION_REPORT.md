# Báo cáo tiến độ ứng dụng chấm công Nhóm 7

**Chủ đề:** Trần Xuân Thanh · 2380614806 · Nhóm 7  
**Phạm vi:** `tranxuanthanh_2380614806_n7_api` và `tranxuanthanh_2380614806_n7`  
**Cập nhật:** 29/10/2026

## Trạng thái tổng quan

Hai project chạy được ở môi trường phát triển. API là ASP.NET Core 8, kết nối SQL Server `tranxuanthanh_nhom7`; ứng dụng Flutter đang chạy trên điện thoại Android. Gmail SMTP được cấu hình trong User Secrets và xác thực STARTTLS thành công; chưa gửi email kiểm thử để tránh phát sinh OTP ngoài yêu cầu đăng nhập/đăng ký. Hệ thống chưa có API công khai, cấu hình production hoặc tọa độ nơi làm việc.

## Đối chiếu các nhóm chức năng

| Nhóm | Đã triển khai | Bằng chứng / giới hạn hiện tại |
|---|---|---|
| Tài khoản và bảo mật | Đăng ký Employee gửi OTP đến email cá nhân; login gửi OTP sau khi kiểm tra mật khẩu; mã 6 chữ số, hết hạn sau 5 phút, dùng một lần, HMAC hash, tối đa 5 lần nhập sai; gửi lại mỗi tài khoản sau 60 giây; trần 400 yêu cầu OTP/24 giờ; JWT chỉ sau xác minh; phân quyền từ SQL Server. | Build API sạch; Gmail SMTP STARTTLS xác thực thành công; chưa gửi OTP end-to-end tới hộp thư người dùng. |
| Admin và nhân sự | Tổng quan, danh sách/kích hoạt nhân viên, quản lý ca, phân ca, xem/hủy bản ghi có audit, duyệt cảnh báo, tạo QR một lần, duyệt/từ chối yêu cầu chỉnh công. | Source/API đã có; chưa kiểm thử toàn bộ thao tác bằng tài khoản Admin qua API công khai. |
| Nhân viên và lịch công | Dashboard, hồ sơ, lịch ca, lịch sử công, check-in/out; gửi yêu cầu chỉnh giờ kèm lý do. | API giới hạn dữ liệu theo người dùng. Màn yêu cầu điều chỉnh vừa được nối và có trong APK mới. |
| GPS | Lấy vị trí thiết bị, gửi tọa độ và tính khoảng cách; ngoài bán kính sinh cảnh báo. | SQL/API hoạt động. Cần latitude/longitude và bán kính để cấu hình geofence thật. |
| QR | Admin sinh token; nhân viên quét; token server được hash, hết hạn sau 3 phút, chỉ dùng một lần và có kiểm soát tranh chấp đồng thời. | Đã có màn/API; cần kiểm tra nghiệp vụ bằng QR với API production khi triển khai. |
| Cảm biến và bất thường | Đọc accelerometer ngắn khi check-out/QR check-in; backend so độ lớn chuyển động với ngưỡng; phát hiện đi trễ/ngoài vùng/QR sai hoặc lặp/check-out không có phiên. | Quy tắc ngưỡng minh bạch, không phải AI; cần đánh giá thực địa vì cảm biến có thể báo nhầm. |
| Giao diện | Flutter Material 3 tiếng Việt: auth/OTP, dashboard theo role, CRUD cơ bản, GPS, cảm biến, QR, lịch sử, lịch làm, hồ sơ, cảnh báo và yêu cầu điều chỉnh. | `flutter analyze` không có error/warning, còn 6 `info` về ngoặc nhọn trong nhánh `if`; widget test qua. |

## Database và migration

Đã xác nhận backend kết nối đúng database `tranxuanthanh_nhom7`; smoke check trả `healthy` và `database: connected`. Không reset database và không chạm các database khác. Các migration đã được áp dụng theo trạng thái phiên làm việc:

- `InitialAttendanceSchema`
- `AddSingleUseQrTokens`
- `AddShiftAssignmentsAndAttendanceAudit`
- `AddAttendanceMotionMagnitude`
- `AddAttendanceAdjustmentRequests`
- `AddEmailOtpVerification`
- `AddOtpRateLimitIndexes`

Các bảng gồm `Users`, `OtpChallenges`, `Shifts`, `EmployeeShifts`, `Attendances`, `AttendanceAnomalies`, `AttendanceQrTokens`, `AttendanceAuditLogs`, `AttendanceAdjustmentRequests`; EF theo dõi migration bằng `__EFMigrationsHistory`. `AddEmailOtpVerification` chỉ thêm cột xác minh email và mục đích OTP; giữ nguyên dữ liệu hiện có, đánh dấu tài khoản cũ đã xác minh và mã OTP cũ thuộc luồng đăng nhập. `AddOtpRateLimitIndexes` chỉ thêm index phục vụ giới hạn gửi. Script idempotent tại `artifacts/publish/migrations.sql` đã được cập nhật và cần review trước khi áp dụng production.

## Endpoint chính

- Auth: `POST /api/auth/register`, `/login`, `/verify-otp`, `/resend-otp`, `/bootstrap-admin`; `GET /api/auth/me`.
- Nhân viên: `GET /api/attendance/mine`, `/schedule`, `/mine/adjustments`; `POST /api/attendance/mark`, `/adjustment-requests`.
- Admin: `/api/admin/overview`, `/employees`, `/shifts`, `/employee-shifts`, `/attendances`, `/anomalies`, `/adjustment-requests`; `POST /api/admin/qr-token`.
- Health: `GET /health` kiểm tra tiến trình và SQL Server.
- Swagger UI: `/swagger` trong Development; Production có thể bật bằng `Swagger__Enabled=true`. OpenAPI khai báo Bearer JWT để thử endpoint cần đăng nhập.

## Kiểm tra đã chạy

- Backend: `dotnet build --no-restore` thành công, 0 warning / 0 error; đã áp dụng migration cộng thêm cột, không xóa bảng/dữ liệu.
- API: `/health` trả healthy và SQL Server connected; login username không tồn tại trả `401`, resend username không tồn tại trả `404`.
- SMTP: Gmail STARTTLS trên cổng 587 xác thực bằng User Secrets thành công; bí mật không được in hoặc đưa vào APK. Chưa phát email kiểm thử tới tài khoản cá nhân.
- Swagger: UI và OpenAPI JSON trả HTTP 200 ở môi trường Development; định nghĩa Bearer JWT hiện trong tài liệu.
- Flutter: `dart format`; `flutter analyze` — không có vấn đề; `flutter test` — 1 widget test passed.
- Android thật: APK release cài đè thành công lên thiết bị đang kết nối và ứng dụng được mở. Đã kiểm tra khởi động, chưa thực hiện toàn bộ luồng OTP/GPS/QR/CRUD trên điện thoại với API production.

## Artifact và file hướng dẫn

- API publish: `artifacts/publish/`.
- Migration script: `artifacts/publish/migrations.sql`.
- APK hiện tại: `C:\Users\Xuan Thanh\AndroidStudioProjects\tranxuanthanh_2380614806_n7\build\app\outputs\flutter-apk\app-release.apk` (khoảng 66.6 MB).
- APK vừa build vẫn dùng URL mẫu `https://api.example.com`; ký bằng cấu hình debug hiện tại, chưa phải APK ký release bằng keystore cá nhân.
- Hướng dẫn secrets, Gmail App Password, deployment và API ở [README.md](README.md).

## Việc cần hoàn tất trước khi nộp / chạy thật

1. Cung cấp nơi host/VPS và domain HTTPS công khai; chuẩn bị SQL Server production mà host có thể truy cập an toàn.
2. Cấu hình connection string, JWT secret, SMTP và bootstrap key trong secret store của host. Không gửi bí mật qua chat hoặc commit vào git.
3. Local Gmail App Password đã có và SMTP auth đã xác nhận. Khi triển khai production, cấu hình cùng sender trong secret store của host; dùng `scripts/Set-GmailAppPassword.ps1` nếu cần nhập lại an toàn trên Windows.
4. Cung cấp tọa độ latitude/longitude của nơi làm việc và bán kính mong muốn.
5. Tạo Admin đầu tiên một lần bằng `/api/auth/bootstrap-admin`, tự đặt thông tin và gỡ bootstrap key sau đó.
6. Review rồi chạy migration script trên production DB, deploy API HTTPS và kiểm tra `/health` từ Internet.
7. Rebuild Flutter với URL thật bằng `flutter build apk --release --dart-define=API_BASE_URL=https://<domain-api>`; cấu hình keystore release trước khi phát hành APK cuối.
8. Kiểm thử end-to-end trên điện thoại: đăng ký → OTP → login/JWT → Admin CRUD/phân ca → check-in/out → GPS → QR → cảnh báo → xin chỉnh công/duyệt → đăng xuất.

## Chạy local và kịch bản demo

- Backend: cấu hình User Secrets theo README, xác nhận SQL Server instance, chạy migration đã review rồi `dotnet run` trong thư mục API.
- Flutter: mở folder Android Studio project, chạy `flutter pub get`, sau đó `flutter run --dart-define=API_BASE_URL=http://<địa-chỉ-máy-dev>:<port>`; điện thoại thật không gọi được `localhost` của máy tính.
- GPS: cấp quyền vị trí, check-in, trình bày khoảng cách/bán kính và cảnh báo ngoài vùng.
- Chuyển động: xem cảm biến trực tiếp rồi check-out/QR để gửi mẫu accelerometer; giải thích ngưỡng cảnh báo, không khẳng định chống gian lận tuyệt đối.
- QR: Admin tạo mã dùng một lần, nhân viên quét; quét lại để minh họa token hết hạn/đã dùng bị từ chối.
