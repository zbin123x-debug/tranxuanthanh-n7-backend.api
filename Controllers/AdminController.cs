using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Models;

namespace TranXuanThanh_2380614806_N7_API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin")]
public sealed class AdminController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private const double AttendanceRadiusMeters = 20d;
    public sealed record ShiftRequest(string Name, TimeOnly StartTime, TimeOnly EndTime);
    public sealed record CreateEmployeeRequest(string FullName, string Username, string Email, string Password);
    public sealed record EmployeeRequest(string FullName, string Email, bool IsActive);
    public sealed record AssignShiftRequest(int UserId, int ShiftId, DateOnly WorkDate);
    public sealed record AttendanceCorrectionRequest(DateTime CheckInUtc, DateTime? CheckOutUtc, string Reason);
    public sealed record AdjustmentReviewRequest(bool Approve, string? Comment);
    public sealed record QrSessionRequest(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
    public sealed record WorkLocationRequest(string Name, double Latitude, double Longitude, double RadiusMeters);
    public sealed record WorkLocationResponse(int Id, string Name, double Latitude, double Longitude, double RadiusMeters, bool IsActive, bool IsLegacyConfig);
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var zoneId = config["WorkLocation:TimeZoneId"] ?? "Asia/Ho_Chi_Minh";
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); } catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var localStart = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), zone);
        return Ok(new
        {
            activeEmployees = await db.Users.CountAsync(x => x.Role == "Employee" && x.IsActive, ct),
            activeShifts = await db.Shifts.CountAsync(x => x.IsActive, ct),
            attendancesToday = await db.Attendances.CountAsync(x => !x.IsCancelled && x.CheckInUtc >= startUtc && x.CheckInUtc < endUtc, ct),
            pendingAlerts = await db.AttendanceAnomalies.CountAsync(x => !x.Reviewed && x.CreatedAtUtc >= startUtc && x.CreatedAtUtc < endUtc, ct)
        });
    }
    [HttpGet("employees")]
    public async Task<IActionResult> Employees([FromQuery] string? search, [FromQuery] bool includeDeleted, CancellationToken ct)
    {
        var query = db.Users.Where(x => x.Role == "Employee" && (includeDeleted || !x.IsDeleted));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.FullName.Contains(term) || x.Username.Contains(term) || x.Email.Contains(term));
        }
        return Ok(await query.OrderBy(x => x.FullName)
            .Select(x => new { x.Id, x.FullName, x.Username, x.Email, x.IsActive, x.IsDeleted, x.CreatedAtUtc })
            .Take(500).ToListAsync(ct));
    }
    [HttpGet("employees/{id:int}")]
    public async Task<IActionResult> EmployeeDetail(int id, CancellationToken ct)
    {
        var x = await db.Users.Where(x => x.Id == id && x.Role == "Employee")
            .Select(x => new { x.Id, x.FullName, x.Username, x.Email, x.IsActive, x.IsDeleted, x.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);
        return x is null ? NotFound() : Ok(x);
    }
    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee(CreateEmployeeRequest request, CancellationToken ct)
    {
        var fullName = request.FullName?.Trim();
        var username = request.Username?.Trim();
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 120 ||
            string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 40 ||
            !System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-zA-Z0-9_.-]+$") ||
            string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email) ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
            return BadRequest(new { message = "Họ tên, username, email hoặc mật khẩu không hợp lệ (mật khẩu tối thiểu 10 ký tự)." });
        if (await db.Users.AnyAsync(u => u.Username == username || u.Email == email, ct))
            return Conflict(new { message = "Username hoặc email đã được sử dụng." });

        var employee = new AppUser { FullName = fullName, Username = username, Email = email!, Role = "Employee", IsActive = true };
        employee.PasswordHash = new PasswordHasher<AppUser>().HashPassword(employee, request.Password);
        db.Users.Add(employee);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { message = "Username hoặc email đã được sử dụng." }); }
        return CreatedAtAction(nameof(EmployeeDetail), new { id = employee.Id },
            new { employee.Id, employee.FullName, employee.Username, employee.Email, employee.IsActive, employee.CreatedAtUtc });
    }

    [HttpPut("employees/{id:int}")]
    public async Task<IActionResult> UpdateEmployee(int id, EmployeeRequest request, CancellationToken ct)
    {
        var x = await db.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == "Employee" && !x.IsDeleted, ct);
        if (x is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email)) return BadRequest(new { message = "Thông tin nhân viên không hợp lệ." });
        var email = request.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email == email, ct)) return Conflict(new { message = "Email đã được sử dụng." });
        if (!string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))
            x.EmailVerified = false;
        x.FullName = request.FullName.Trim(); x.Email = email; x.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct); return Ok(new { x.Id, x.FullName, x.Username, x.Email, x.IsActive });
    }
    [HttpPatch("employees/{id:int}/active")]
    public async Task<IActionResult> SetEmployeeActive(int id,[FromQuery]bool active,CancellationToken ct){var x=await db.Users.FirstOrDefaultAsync(x=>x.Id==id&&x.Role=="Employee"&&!x.IsDeleted,ct);if(x is null)return NotFound();x.IsActive=active;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpDelete("employees/{id:int}")]
    public async Task<IActionResult> DeleteEmployee(int id, CancellationToken ct)
    {
        var employee = await db.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == "Employee" && !x.IsDeleted, ct);
        if (employee is null) return NotFound();
        employee.IsActive = false;
        employee.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts(CancellationToken ct)=>Ok(await db.Shifts.OrderBy(x=>x.StartTime).ToListAsync(ct));
    [HttpPost("shifts")]
    public async Task<IActionResult> CreateShift(ShiftRequest request,CancellationToken ct){if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>100||request.StartTime==request.EndTime)return BadRequest(new{message="Tên và giờ ca không hợp lệ."});var s=new Shift{Name=request.Name.Trim(),StartTime=request.StartTime,EndTime=request.EndTime};db.Shifts.Add(s);await db.SaveChangesAsync(ct);return Created($"api/admin/shifts/{s.Id}",s);}
    [HttpPut("shifts/{id:int}")]
    public async Task<IActionResult> UpdateShift(int id,ShiftRequest request,CancellationToken ct){if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>100||request.StartTime==request.EndTime)return BadRequest(new{message="Tên và giờ ca không hợp lệ."});var s=await db.Shifts.FindAsync([id],ct);if(s is null)return NotFound();s.Name=request.Name.Trim();s.StartTime=request.StartTime;s.EndTime=request.EndTime;await db.SaveChangesAsync(ct);return Ok(s);}
    [HttpDelete("shifts/{id:int}")]
    public async Task<IActionResult> DisableShift(int id,CancellationToken ct){var s=await db.Shifts.FindAsync([id],ct);if(s is null)return NotFound();s.IsActive=false;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpPost("employee-shifts")]
    public async Task<IActionResult> AssignShift(AssignShiftRequest request, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == request.UserId && x.Role == "Employee" && x.IsActive, ct) || !await db.Shifts.AnyAsync(x => x.Id == request.ShiftId && x.IsActive, ct)) return BadRequest(new { message = "Nhân viên hoặc ca làm không hợp lệ." });
        var assignment = new EmployeeShift { UserId = request.UserId, ShiftId = request.ShiftId, WorkDate = request.WorkDate };
        db.EmployeeShifts.Add(assignment);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Conflict(new { message = "Nhân viên đã được xếp ca này trong ngày." }); }
        return Created("", assignment);
    }
    [HttpGet("employee-shifts")]
    public async Task<IActionResult> Assignments([FromQuery]DateOnly? date, CancellationToken ct) => Ok(await db.EmployeeShifts.Include(x=>x.User).Include(x=>x.Shift).Where(x=>date==null||x.WorkDate==date).OrderBy(x=>x.WorkDate).Select(x=>new{x.Id,x.UserId,x.User.FullName,x.ShiftId,ShiftName=x.Shift.Name,x.WorkDate,x.Shift.StartTime,x.Shift.EndTime}).ToListAsync(ct));
    [HttpGet("attendances")]
    public async Task<IActionResult> Attendances([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int? employeeId, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] bool includeCancelled = false, CancellationToken ct = default)
    {
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc >= toUtc)
            return BadRequest(new { message = "Khoảng thời gian lọc không hợp lệ." });
        var query = db.Attendances.AsNoTracking().Include(x => x.User).AsQueryable();
        if (!includeCancelled) query = query.Where(x => !x.IsCancelled);
        if (fromUtc.HasValue) query = query.Where(x => x.CheckInUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.CheckInUtc < toUtc.Value);
        if (employeeId.HasValue) query = query.Where(x => x.UserId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.User.FullName.Contains(term) || x.User.Username.Contains(term));
        }
        return Ok(await query.OrderByDescending(x => x.CheckInUtc).Take(500)
            .Select(x => new { x.Id, x.UserId, x.User.FullName, x.User.Username, x.CheckInUtc,
                x.CheckOutUtc, x.Latitude, x.Longitude, x.DistanceMeters, distanceKilometers = x.DistanceMeters / 1000d, x.WorkLocationName,
                x.CheckOutLatitude, x.CheckOutLongitude, x.CheckOutDistanceMeters, x.CheckOutWorkLocationName, x.MotionMagnitude,
                x.Status, x.IsCancelled }).ToListAsync(ct));
    }
    [HttpGet("attendances/{id:int}")]
    public async Task<IActionResult> AttendanceDetail(int id, CancellationToken ct)
    {
        var item = await db.Attendances.AsNoTracking().Include(x => x.User)
            .Where(x => x.Id == id).Select(x => new { x.Id, x.UserId, x.User.FullName,
                x.User.Username, x.CheckInUtc, x.CheckOutUtc, x.Latitude, x.Longitude,
                x.DistanceMeters, distanceKilometers = x.DistanceMeters / 1000d, x.WorkLocationName,
                x.CheckOutLatitude, x.CheckOutLongitude, x.CheckOutDistanceMeters, x.CheckOutWorkLocationName,
                x.MotionMagnitude, x.Status, x.IsCancelled }).FirstOrDefaultAsync(ct);
        if (item is null) return NotFound();
        var history = await db.AttendanceAuditLogs.AsNoTracking().Where(x => x.AttendanceId == id)
            .OrderByDescending(x => x.ChangedAtUtc)
            .Select(x => new { x.Id, x.ChangedAtUtc, x.ChangedBy, x.ChangeReason, x.BeforeJson, x.AfterJson })
            .ToListAsync(ct);
        return Ok(new { attendance = item, history });
    }
    [HttpGet("reports")]
    public async Task<IActionResult> Reports([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddDays(-6);
        if (from >= to || to - from > TimeSpan.FromDays(366))
            return BadRequest(new { message = "Khoảng báo cáo phải hợp lệ và không quá 366 ngày." });
        var records = await db.Attendances.AsNoTracking()
            .Where(x => x.CheckInUtc >= from && x.CheckInUtc < to)
            .GroupBy(x => new { Date = x.CheckInUtc.Date, x.Status })
            .Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count(), Cancelled = g.Count(x => x.IsCancelled) })
            .OrderBy(x => x.Date).ThenBy(x => x.Status).ToListAsync(ct);
        return Ok(new
        {
            fromUtc = from,
            toUtc = to,
            total = records.Sum(x => x.Count),
            active = records.Sum(x => x.Count - x.Cancelled),
            cancelled = records.Sum(x => x.Cancelled),
            onTime = records.Where(x => x.Status == "OnTime").Sum(x => x.Count - x.Cancelled),
            late = records.Where(x => x.Status == "Late").Sum(x => x.Count - x.Cancelled),
            byDay = records.GroupBy(x => x.Date).Select(g => new { date = g.Key, total = g.Sum(x => x.Count - x.Cancelled), cancelled = g.Sum(x => x.Cancelled) })
        });
    }
    [HttpPut("attendances/{id:int}")]
    public async Task<IActionResult> CorrectAttendance(int id, AttendanceCorrectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500 || request.CheckOutUtc < request.CheckInUtc) return BadRequest(new { message = "Thời gian hoặc lý do điều chỉnh không hợp lệ." });
        var item = await db.Attendances.FirstOrDefaultAsync(x=>x.Id==id&&!x.IsCancelled,ct); if(item is null)return NotFound();
        var before = System.Text.Json.JsonSerializer.Serialize(new { item.CheckInUtc, item.CheckOutUtc });
        item.CheckInUtc = request.CheckInUtc; item.CheckOutUtc = request.CheckOutUtc;
        db.AttendanceAuditLogs.Add(new AttendanceAuditLog { AttendanceId=id, ChangedBy=User.FindFirstValue("username") ?? "Admin", ChangeReason=request.Reason.Trim(), BeforeJson=before, AfterJson=System.Text.Json.JsonSerializer.Serialize(new { item.CheckInUtc, item.CheckOutUtc }) });
        await db.SaveChangesAsync(ct); return Ok(new { item.Id, item.CheckInUtc, item.CheckOutUtc });
    }
    [HttpDelete("attendances/{id:int}")]
    public async Task<IActionResult> CancelAttendance(int id,[FromQuery]string reason,CancellationToken ct){var item=await db.Attendances.FirstOrDefaultAsync(x=>x.Id==id&&!x.IsCancelled,ct);if(item is null)return NotFound();if(string.IsNullOrWhiteSpace(reason))return BadRequest(new{message="Cần ghi lý do hủy."});item.IsCancelled=true;db.AttendanceAuditLogs.Add(new AttendanceAuditLog{AttendanceId=id,ChangedBy=User.FindFirstValue("username")??"Admin",ChangeReason=reason.Trim(),BeforeJson="Active",AfterJson="Cancelled"});await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("anomalies")]
    public async Task<IActionResult> Anomalies(CancellationToken ct)=>Ok(await db.AttendanceAnomalies.OrderByDescending(x=>x.CreatedAtUtc).Take(500).ToListAsync(ct));

    [HttpGet("work-locations")]
    public async Task<IActionResult> WorkLocations(CancellationToken ct)
    {
        var hasDatabaseLocations = await db.CompanyWorkLocations.AnyAsync(ct);
        var locations = await db.CompanyWorkLocations.Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new WorkLocationResponse(x.Id, x.Name, x.Latitude, x.Longitude, 20d, x.IsActive, false))
            .ToListAsync(ct);
        if (!hasDatabaseLocations && locations.Count == 0 && TryGetLegacyLocation(out var legacy))
            locations.Add(legacy!);
        return Ok(locations);
    }

    [HttpPost("work-locations")]
    public async Task<IActionResult> CreateWorkLocation(WorkLocationRequest request, CancellationToken ct)
    {
        if (!ValidWorkLocation(request)) return BadRequest(new { message = "Tên, tọa độ GPS hoặc bán kính vị trí không hợp lệ." });
        var now = DateTime.UtcNow;
        var item = new CompanyWorkLocation
        {
            Name = request.Name.Trim(), Latitude = request.Latitude, Longitude = request.Longitude,
            RadiusMeters = 20d, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.CompanyWorkLocations.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(new WorkLocationResponse(item.Id, item.Name, item.Latitude, item.Longitude, 20d, item.IsActive, false));
    }

    [HttpPut("work-locations/{id:int}")]
    public async Task<IActionResult> UpdateWorkLocation(int id, WorkLocationRequest request, CancellationToken ct)
    {
        if (!ValidWorkLocation(request)) return BadRequest(new { message = "Tên, tọa độ GPS hoặc bán kính vị trí không hợp lệ." });
        if (id == 0)
        {
            if (!TryGetLegacyLocation(out _)) return NotFound();
            var now = DateTime.UtcNow;
            var migrated = new CompanyWorkLocation { Name = request.Name.Trim(), Latitude = request.Latitude, Longitude = request.Longitude, RadiusMeters = 20d, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.CompanyWorkLocations.Add(migrated);
            await db.SaveChangesAsync(ct);
            return Ok(new WorkLocationResponse(migrated.Id, migrated.Name, migrated.Latitude, migrated.Longitude, 20d, true, false));
        }
        var item = await db.CompanyWorkLocations.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (item is null) return NotFound();
        item.Name = request.Name.Trim(); item.Latitude = request.Latitude; item.Longitude = request.Longitude;
        item.RadiusMeters = 20d; item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new WorkLocationResponse(item.Id, item.Name, item.Latitude, item.Longitude, 20d, item.IsActive, false));
    }

    [HttpDelete("work-locations/{id:int}")]
    public async Task<IActionResult> DeleteWorkLocation(int id, CancellationToken ct)
    {
        if (id == 0)
        {
            if (!TryGetLegacyLocation(out var legacy)) return NotFound();
            db.CompanyWorkLocations.Add(new CompanyWorkLocation
            {
                Name = legacy!.Name, Latitude = legacy.Latitude, Longitude = legacy.Longitude,
                RadiusMeters = legacy.RadiusMeters, IsActive = false
            });
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        var item = await db.CompanyWorkLocations.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (item is null) return NotFound();
        item.IsActive = false; item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static bool ValidWorkLocation(WorkLocationRequest request) =>
        !string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 100 &&
        double.IsFinite(request.Latitude) && Math.Abs(request.Latitude) <= 90 &&
        double.IsFinite(request.Longitude) && Math.Abs(request.Longitude) <= 180 &&
        double.IsFinite(request.RadiusMeters) && request.RadiusMeters == 20d;

    private bool TryGetLegacyLocation(out WorkLocationResponse? location)
    {
        location = null;
        if (!double.TryParse(config["WorkLocation:Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
            !double.TryParse(config["WorkLocation:Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
            Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180) return false;
        location = new WorkLocationResponse(0, "Vị trí công ty (cấu hình hiện tại)", latitude, longitude, AttendanceRadiusMeters, true, true);
        return true;
    }
    [HttpPost("anomalies/{id:int}/review")]
    public async Task<IActionResult> Review(int id,CancellationToken ct){var x=await db.AttendanceAnomalies.FindAsync([id],ct);if(x is null)return NotFound();x.Reviewed=true;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("adjustment-requests")]
    public async Task<IActionResult> AdjustmentRequests([FromQuery]bool pendingOnly = true, CancellationToken ct = default) => Ok(await db.AttendanceAdjustmentRequests
        .Where(x => !pendingOnly || x.Status == "Pending")
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(500)
        .Join(db.Users, request => request.UserId, user => user.Id, (request, user) => new
        {
            request.Id, request.UserId, user.FullName, user.Username, user.Email,
            request.AttendanceId, request.ProposedCheckInUtc, request.ProposedCheckOutUtc,
            request.Reason, request.Status, request.AdminComment, request.CreatedAtUtc
        }).ToListAsync(ct));
    [HttpPost("adjustment-requests/{id:int}/review")]
    public async Task<IActionResult> ReviewAdjustmentRequest(int id, AdjustmentReviewRequest request, CancellationToken ct)
    {
        var item = await db.AttendanceAdjustmentRequests.FirstOrDefaultAsync(x=>x.Id==id,ct);
        if(item is null) return NotFound();
        if(item.Status!="Pending") return Conflict(new { message="Yêu cầu đã được xử lý." });
        if(request.Comment?.Length>500) return BadRequest(new { message="Ghi chú tối đa 500 ký tự." });
        if(request.Approve && item.AttendanceId.HasValue && (item.ProposedCheckInUtc.HasValue || item.ProposedCheckOutUtc.HasValue))
        {
            var attendance=await db.Attendances.FirstOrDefaultAsync(x=>x.Id==item.AttendanceId&&!x.IsCancelled,ct);
            if(attendance is null) return Conflict(new { message="Bản ghi chấm công không còn hoạt động." });
            var finalIn=item.ProposedCheckInUtc??attendance.CheckInUtc; var finalOut=item.ProposedCheckOutUtc??attendance.CheckOutUtc;
            if(finalOut.HasValue&&finalOut<finalIn) return BadRequest(new { message="Thời gian checkout không thể trước check-in." });
            var before=System.Text.Json.JsonSerializer.Serialize(new{attendance.CheckInUtc,attendance.CheckOutUtc});
            if(item.ProposedCheckInUtc.HasValue) attendance.CheckInUtc=item.ProposedCheckInUtc.Value;
            if(item.ProposedCheckOutUtc.HasValue) attendance.CheckOutUtc=item.ProposedCheckOutUtc.Value;
            db.AttendanceAuditLogs.Add(new AttendanceAuditLog{AttendanceId=attendance.Id,ChangedBy=User.FindFirstValue("username")??"Admin",ChangeReason=$"Duyệt yêu cầu #{item.Id}: {item.Reason}",BeforeJson=before,AfterJson=System.Text.Json.JsonSerializer.Serialize(new{attendance.CheckInUtc,attendance.CheckOutUtc})});
        }
        item.Status=request.Approve?"Approved":"Rejected"; item.AdminComment=request.Comment?.Trim(); item.ReviewedBy=User.FindFirstValue("username")??"Admin"; item.ReviewedAtUtc=DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { item.Id, item.Status, item.AdminComment, item.ReviewedAtUtc });
    }
    [HttpPost("qr-token")]
    public async Task<IActionResult> CreateQrToken(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)) return Unauthorized();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var expires = DateTime.UtcNow.AddMinutes(3);
        db.AttendanceQrTokens.Add(new AttendanceQrToken { TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), ExpiresAtUtc = expires, CreatedByUserId = adminId });
        await db.SaveChangesAsync(ct);
        return Ok(new { token, expiresAtUtc = expires });
    }

    [HttpPost("qr-sessions")]
    public async Task<IActionResult> CreateQrSession(QrSessionRequest request, CancellationToken ct)
    {
        var starts = request.StartsAtUtc.ToUniversalTime();
        var ends = request.EndsAtUtc.ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        if (starts < now.AddMinutes(-1) || starts > now.AddDays(30) || ends <= starts || ends > starts.AddHours(12))
            return BadRequest(new { message = "Thời gian QR không hợp lệ. Chọn lịch trong 30 ngày tới, thời lượng tối đa 12 giờ." });
        if (await db.AttendanceQrSessions.AnyAsync(x => x.StartsAtUtc < ends && x.EndsAtUtc > starts, ct))
            return Conflict(new { message = "Khung giờ này đã có lịch QR khác." });
        if (!int.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
            return Unauthorized();

        var session = new AttendanceQrSession
        {
            StartsAtUtc = starts,
            EndsAtUtc = ends,
            CreatedByUserId = adminId,
            TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        };
        db.AttendanceQrSessions.Add(session);
        await db.SaveChangesAsync(ct);
        var token = CreateQrSessionToken(session.Id);
        session.TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await db.SaveChangesAsync(ct);
        return Ok(new { session.Id, session.StartsAtUtc, session.EndsAtUtc, active = false });
    }

    [HttpGet("qr-sessions/active")]
    public async Task<IActionResult> ActiveQrSession(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.AttendanceQrSessions
            .Where(x => x.StartsAtUtc <= now && x.EndsAtUtc > now)
            .OrderByDescending(x => x.StartsAtUtc)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
            return Ok(new { active = true, active.Id, active.StartsAtUtc, active.EndsAtUtc, token = CreateQrSessionToken(active.Id) });

        var next = await db.AttendanceQrSessions.Where(x => x.StartsAtUtc > now).OrderBy(x => x.StartsAtUtc).FirstOrDefaultAsync(ct);
        return Ok(new { active = false, nextSessionId = next?.Id, nextStartsAtUtc = next?.StartsAtUtc, nextEndsAtUtc = next?.EndsAtUtc });
    }

    [HttpGet("qr-sessions/{id:int}")]
    public async Task<IActionResult> QrSession(int id, CancellationToken ct)
    {
        var session = await db.AttendanceQrSessions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (session is null) return NotFound(new { message = "Không tìm thấy lịch QR." });
        var active = session.StartsAtUtc <= DateTimeOffset.UtcNow && session.EndsAtUtc > DateTimeOffset.UtcNow;
        return Ok(new { active, session.Id, session.StartsAtUtc, session.EndsAtUtc, token = active ? CreateQrSessionToken(session.Id) : null });
    }

    private string CreateQrSessionToken(int id)
    {
        var secret = config["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("Jwt__Secret");
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("JWT signing key is unavailable.");
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"attendance-qr-session:{id}"));
        return Convert.ToHexString(digest);
    }
}
