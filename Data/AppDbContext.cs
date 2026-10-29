using Microsoft.EntityFrameworkCore;
using TranXuanThanh_2380614806_N7_API.Models;

namespace TranXuanThanh_2380614806_N7_API.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<EmployeeShift> EmployeeShifts => Set<EmployeeShift>();
    public DbSet<AttendanceRecord> Attendances => Set<AttendanceRecord>();
    public DbSet<AttendanceAnomaly> AttendanceAnomalies => Set<AttendanceAnomaly>();
    public DbSet<AttendanceQrToken> AttendanceQrTokens => Set<AttendanceQrToken>();
    public DbSet<AttendanceQrSession> AttendanceQrSessions => Set<AttendanceQrSession>();
    public DbSet<CompanyWorkLocation> CompanyWorkLocations => Set<CompanyWorkLocation>();
    public DbSet<AttendanceAuditLog> AttendanceAuditLogs => Set<AttendanceAuditLog>();
    public DbSet<AttendanceAdjustmentRequest> AttendanceAdjustmentRequests => Set<AttendanceAdjustmentRequest>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        model.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        model.Entity<AppUser>().Property(x => x.Username).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        model.Entity<AppUser>().Property(x => x.Email).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        model.Entity<OtpChallenge>().HasOne(x => x.User).WithMany(x => x.OtpChallenges).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<OtpChallenge>().HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        model.Entity<OtpChallenge>().HasIndex(x => x.CreatedAtUtc);
        model.Entity<AttendanceRecord>().HasOne(x => x.User).WithMany(x => x.Attendances).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttendanceRecord>().HasIndex(x => new { x.UserId, x.CheckInUtc });
        model.Entity<AttendanceAnomaly>().HasIndex(x => new { x.Reviewed, x.CreatedAtUtc });
        model.Entity<AttendanceQrToken>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<AttendanceQrSession>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<AttendanceQrSession>().HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
        model.Entity<CompanyWorkLocation>().HasIndex(x => new { x.IsActive, x.Name });
        model.Entity<EmployeeShift>().HasIndex(x => new { x.UserId, x.WorkDate, x.ShiftId }).IsUnique();
        model.Entity<EmployeeShift>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EmployeeShift>().HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttendanceAnomaly>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<AttendanceAnomaly>().HasOne<AttendanceRecord>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<AttendanceAuditLog>().HasOne<AttendanceRecord>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttendanceAdjustmentRequest>().HasIndex(x => new { x.Status, x.CreatedAtUtc });
        model.Entity<AttendanceAdjustmentRequest>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttendanceAdjustmentRequest>().HasOne<AttendanceRecord>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.SetNull);
    }
}
