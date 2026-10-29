using System.ComponentModel.DataAnnotations;

namespace TranXuanThanh_2380614806_N7_API.Models;

public sealed class AppUser
{
    public int Id { get; set; }
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(40)] public string Username { get; set; } = "";
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(500)] public string PasswordHash { get; set; } = "";
    [MaxLength(20)] public string Role { get; set; } = "Employee";
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<OtpChallenge> OtpChallenges { get; set; } = [];
    public List<AttendanceRecord> Attendances { get; set; } = [];
}

public sealed class OtpChallenge
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    [MaxLength(64)] public string CodeHash { get; set; } = "";
    [MaxLength(20)] public string Purpose { get; set; } = "Login";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Shift
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class EmployeeShift
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
}

public sealed class AttendanceRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTime CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? DistanceMeters { get; set; }
    [MaxLength(120)] public string? WorkLocationName { get; set; }
    public double? MotionMagnitude { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "OnTime";
    [MaxLength(120)] public string? QrTokenHash { get; set; }
    public bool IsCancelled { get; set; }
}

public sealed class AttendanceAnomaly
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public int? AttendanceId { get; set; }
    [MaxLength(300)] public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Reviewed { get; set; }
}

public sealed class AttendanceAuditLog
{
    public int Id { get; set; }
    public int AttendanceId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(80)] public string ChangedBy { get; set; } = "";
    [MaxLength(1000)] public string ChangeReason { get; set; } = "";
    [MaxLength(1000)] public string BeforeJson { get; set; } = "";
    [MaxLength(1000)] public string AfterJson { get; set; } = "";
}

public sealed class AttendanceAdjustmentRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? AttendanceId { get; set; }
    public DateTime? ProposedCheckInUtc { get; set; }
    public DateTime? ProposedCheckOutUtc { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [MaxLength(500)] public string? AdminComment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(80)] public string? ReviewedBy { get; set; }
}

public sealed class AttendanceQrToken
{
    public int Id { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class AttendanceQrSession
{
    public int Id { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int CreatedByUserId { get; set; }
}

public sealed class CompanyWorkLocation
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 250;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
