using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Models;

namespace TranXuanThanh_2380614806_N7_API.Controllers;

[ApiController, Authorize, Route("api/attendance")]
public sealed class AttendanceController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private const double AttendanceRadiusMeters = 20d;
    private const int LateGraceMinutes = 5;
    public sealed record MarkRequest(double Latitude, double Longitude, string Action = "check-in", string? QrToken = null, double? MotionMagnitude = null);
    public sealed record AdjustmentRequestDto(int? AttendanceId, DateTime? ProposedCheckInUtc, DateTime? ProposedCheckOutUtc, string Reason);
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        var items = await db.Attendances.Where(x => x.UserId == userId && !x.IsCancelled).OrderByDescending(x => x.CheckInUtc).Take(60).ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] DateOnly? from, CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var end = start.AddDays(30);
        var items = await db.EmployeeShifts.Include(x => x.Shift).Where(x => x.UserId == userId && x.WorkDate >= start && x.WorkDate <= end)
            .OrderBy(x => x.WorkDate).Select(x => new { x.WorkDate, Shift = x.Shift.Name, x.Shift.StartTime, x.Shift.EndTime }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("qr-session/active")]
    public async Task<IActionResult> ActiveQrSession(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.AttendanceQrSessions
            .Where(x => x.StartsAtUtc <= now && x.EndsAtUtc > now)
            .OrderByDescending(x => x.StartsAtUtc)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
            return Ok(new { active = true, active.Id, active.StartsAtUtc, active.EndsAtUtc, token = CreateQrSessionToken(active.Id) });

        var next = await db.AttendanceQrSessions
            .Where(x => x.StartsAtUtc > now)
            .OrderBy(x => x.StartsAtUtc)
            .FirstOrDefaultAsync(ct);
        return Ok(new { active = false, nextStartsAtUtc = next?.StartsAtUtc, nextEndsAtUtc = next?.EndsAtUtc });
    }

    [HttpGet("mine/adjustments")]
    public async Task<IActionResult> MyAdjustmentRequests(CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        return Ok(await db.AttendanceAdjustmentRequests.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct));
    }

    [HttpPost("adjustment-requests")]
    public async Task<IActionResult> RequestAdjustment(AdjustmentRequestDto request, CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500 ||
            (request.ProposedCheckInUtc.HasValue && request.ProposedCheckOutUtc < request.ProposedCheckInUtc))
            return BadRequest(new { message = "Lý do hoặc thời gian đề nghị không hợp lệ." });
        if (request.AttendanceId.HasValue && !await db.Attendances.AnyAsync(x => x.Id == request.AttendanceId && x.UserId == userId && !x.IsCancelled, ct))
            return NotFound(new { message = "Không tìm thấy bản ghi chấm công của bạn." });
        var item = new AttendanceAdjustmentRequest { UserId = userId, AttendanceId = request.AttendanceId, ProposedCheckInUtc = request.ProposedCheckInUtc, ProposedCheckOutUtc = request.ProposedCheckOutUtc, Reason = request.Reason.Trim() };
        db.AttendanceAdjustmentRequests.Add(item);
        await db.SaveChangesAsync(ct);
        return Created("", new { item.Id, item.Status, item.CreatedAtUtc });
    }
    [HttpPost("mark")]
    public async Task<IActionResult> Mark(MarkRequest request, CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        if (!double.IsFinite(request.Latitude) || !double.IsFinite(request.Longitude) || Math.Abs(request.Latitude) > 90 || Math.Abs(request.Longitude) > 180)
            return BadRequest(new { message = "Tọa độ GPS không hợp lệ." });
        if (request.MotionMagnitude is { } movement && (!double.IsFinite(movement) || movement < 0 || movement > 100))
            return BadRequest(new { message = "Mẫu cảm biến chuyển động không hợp lệ." });
        var record = await db.Attendances.Where(x => x.UserId == userId && !x.IsCancelled && x.CheckOutUtc == null).OrderByDescending(x => x.CheckInUtc).FirstOrDefaultAsync(ct);
        if (request.Action == "check-out")
        {
            if (record is null)
            {
                db.AttendanceAnomalies.Add(new AttendanceAnomaly { UserId = userId, Reason = "Thử check-out nhưng không có check-in đang mở." });
                await db.SaveChangesAsync(ct);
                return Conflict(new { message = "Không có lượt check-in đang mở; cảnh báo đã được ghi nhận." });
            }
            var (checkOutDistance, checkOutLocationName) = await MeasureWorkLocationAsync(request.Latitude, request.Longitude, ct);
            record.CheckOutUtc = DateTime.UtcNow;
            record.CheckOutLatitude = request.Latitude;
            record.CheckOutLongitude = request.Longitude;
            record.CheckOutDistanceMeters = checkOutDistance;
            record.CheckOutWorkLocationName = checkOutLocationName;
            if (checkOutDistance is > AttendanceRadiusMeters)
                db.AttendanceAnomalies.Add(new AttendanceAnomaly
                {
                    UserId = userId,
                    AttendanceId = record.Id,
                    Reason = $"Check-out cách công ty {checkOutDistance:0} m, ngoài bán kính {AttendanceRadiusMeters:0} m."
                });
            await db.SaveChangesAsync(ct);
            var checkoutArea = checkOutDistance is null ? "chưa xác minh được vị trí công ty" :
                checkOutDistance <= AttendanceRadiusMeters ? "trong bán kính 20 m" : "ngoài bán kính 20 m";
            return Ok(new { message = $"Đã check-out; vị trí {checkoutArea}.", record });
        }
        if (request.Action != "check-in") return BadRequest(new { message = "Action phải là check-in hoặc check-out." });
        AttendanceQrToken? qr = null;
        AttendanceQrSession? qrSession = null;
        if (!string.IsNullOrWhiteSpace(request.QrToken))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.QrToken)));
            qr = await db.AttendanceQrTokens.SingleOrDefaultAsync(x => x.TokenHash == hash && x.UsedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow, ct);
            if (qr is null)
                qrSession = await db.AttendanceQrSessions.SingleOrDefaultAsync(
                    x => x.TokenHash == hash && x.StartsAtUtc <= DateTimeOffset.UtcNow && x.EndsAtUtc > DateTimeOffset.UtcNow, ct);
            if (qr is null && qrSession is null)
            {
                db.AttendanceAnomalies.Add(new AttendanceAnomaly { UserId = userId, Reason = "QR hết hạn, không hợp lệ hoặc đã được dùng." });
                await db.SaveChangesAsync(ct);
                return Conflict(new { message = "QR không hợp lệ, đã hết hạn hoặc đã được sử dụng." });
            }
        }
        if (record is not null)
        {
            db.AttendanceAnomalies.Add(new AttendanceAnomaly { UserId = userId, AttendanceId = record.Id, Reason = "Check-in trùng lặp khi lượt trước chưa check-out." });
            await db.SaveChangesAsync(ct); return Conflict(new { message = "Bạn đã check-in và chưa check-out. Cảnh báo đã được ghi nhận." });
        }
        var nowUtc = DateTime.UtcNow;
        var zoneId = config["WorkLocation:TimeZoneId"] ?? "Asia/Ho_Chi_Minh";
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var localDate = DateOnly.FromDateTime(localNow);
        var startTime = await db.EmployeeShifts.Where(x => x.UserId == userId && x.WorkDate == localDate && x.Shift.IsActive)
            .OrderBy(x => x.Shift.StartTime).Select(x => (TimeOnly?)x.Shift.StartTime).FirstOrDefaultAsync(ct);
        var isLateByTime = startTime.HasValue && TimeOnly.FromDateTime(localNow) > startTime.Value.AddMinutes(LateGraceMinutes);
        var activeWorkLocations = await db.CompanyWorkLocations.Where(x => x.IsActive).ToListAsync(ct);
        var hasManagedWorkLocations = activeWorkLocations.Count > 0 || await db.CompanyWorkLocations.AnyAsync(ct);
        var radius = AttendanceRadiusMeters;
        double? distance = null;
        string? workLocationName = null;
        if (activeWorkLocations.Count > 0)
        {
            var nearest = activeWorkLocations
                .Select(x => (Location: x, Distance: Distance(request.Latitude, request.Longitude, x.Latitude, x.Longitude)))
                .MinBy(x => x.Distance);
            distance = nearest.Distance;
            workLocationName = nearest.Location.Name;
        }
        else if (!hasManagedWorkLocations &&
                 double.TryParse(config["WorkLocation:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                 double.TryParse(config["WorkLocation:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
                 Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
        {
            distance = Distance(request.Latitude, request.Longitude, lat, lon);
            workLocationName = "Vị trí công ty (cấu hình hiện tại)";
        }
        var isOutsideGeofence = distance is > AttendanceRadiusMeters;
        var status = distance is null ? "Unconfigured" : isLateByTime || isOutsideGeofence ? "Late" : "OnTime";
        var item = new AttendanceRecord { UserId = userId, CheckInUtc = nowUtc, Latitude = request.Latitude, Longitude = request.Longitude, DistanceMeters = distance, WorkLocationName = workLocationName, MotionMagnitude = request.MotionMagnitude, Status = status };
        if (qr is not null) { qr.UsedAtUtc = DateTime.UtcNow; item.QrTokenHash = qr.TokenHash; }
        else if (qrSession is not null) item.QrTokenHash = qrSession.TokenHash;
        db.Attendances.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "QR vừa được sử dụng ở một lượt chấm công khác." }); }
        if (distance.HasValue && distance > radius) db.AttendanceAnomalies.Add(new AttendanceAnomaly { UserId = userId, AttendanceId = item.Id, Reason = $"Chấm công ngoài vùng cho phép ({distance:0} m; bán kính {radius:0} m)." });
        if (isLateByTime) db.AttendanceAnomalies.Add(new AttendanceAnomaly { UserId = userId, AttendanceId = item.Id, Reason = $"Check-in trễ so với ca được phân công (bắt đầu {startTime}; cho phép trễ {LateGraceMinutes} phút)." });
        var motionThreshold = double.TryParse(config["Attendance:MotionSpikeThreshold"], out var configuredThreshold) ? configuredThreshold : 20d;
        var motionSpike = request.MotionMagnitude is { } magnitude && magnitude > motionThreshold;
        if (motionSpike) db.AttendanceAnomalies.Add(new AttendanceAnomaly { UserId = userId, AttendanceId = item.Id, Reason = $"Mẫu gia tốc bất thường ({request.MotionMagnitude:0.0} m/s²; ngưỡng cảnh báo {motionThreshold:0.0} m/s²)." });
        if (distance.HasValue && distance > radius || isLateByTime || motionSpike) await db.SaveChangesAsync(ct);
        var message = distance is null ? "Đã ghi nhận GPS; cần cấu hình tọa độ nơi làm việc để xác minh phạm vi." : isOutsideGeofence ? "Đã ghi nhận đi làm trễ: vị trí ngoài bán kính 20 m." : isLateByTime ? "Đã check-in trễ so với ca; cảnh báo được ghi nhận." : "Đã check-in trong phạm vi.";
        return Ok(new { message, item.Status, distanceMeters = distance, distanceKilometers = distance / 1000d, workLocationName, item.Id });
    }

    private async Task<(double? DistanceMeters, string? WorkLocationName)> MeasureWorkLocationAsync(double latitude, double longitude, CancellationToken ct)
    {
        var locations = await db.CompanyWorkLocations.Where(x => x.IsActive).ToListAsync(ct);
        if (locations.Count > 0)
        {
            var nearest = locations
                .Select(x => (Location: x, Distance: Distance(latitude, longitude, x.Latitude, x.Longitude)))
                .MinBy(x => x.Distance);
            return (nearest.Distance, nearest.Location.Name);
        }

        if (await db.CompanyWorkLocations.AnyAsync(ct)) return (null, null);
        if (double.TryParse(config["WorkLocation:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(config["WorkLocation:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
            Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
            return (Distance(latitude, longitude, lat, lon), "Vị trí công ty (cấu hình hiện tại)");

        return (null, null);
    }

    private string CreateQrSessionToken(int id)
    {
        var secret = config["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("Jwt__Secret");
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("JWT signing key is unavailable.");
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"attendance-qr-session:{id}"));
        return Convert.ToHexString(digest);
    }
    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double earth = 6371000; var dLat = (lat2-lat1)*Math.PI/180; var dLon=(lon2-lon1)*Math.PI/180;
        var a=Math.Sin(dLat/2)*Math.Sin(dLat/2)+Math.Cos(lat1*Math.PI/180)*Math.Cos(lat2*Math.PI/180)*Math.Sin(dLon/2)*Math.Sin(dLon/2);
        return earth*2*Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
    }
}
