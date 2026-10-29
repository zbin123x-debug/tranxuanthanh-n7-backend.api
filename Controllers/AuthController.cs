using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Models;
using TranXuanThanh_2380614806_N7_API.Services;

namespace TranXuanThanh_2380614806_N7_API.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IConfiguration config,
    IEmailSender email,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string SenderAddress = "zbin789x@gmail.com";
    private const string RegistrationPurpose = "Registration";
    private const string LoginPurpose = "Login";
    private const int OtpLength = 6;
    private const int MaxOtpAttempts = 5;
    private const int ResendCooldownSeconds = 60;
    private const int MaxOtpRequestsPer24Hours = 400;
    private static readonly PasswordHasher<AppUser> Hasher = new();

    public sealed record RegisterRequest(string FullName, string Username, string Email, string Password, string ConfirmPassword);
    public sealed record LoginRequest(string Username, string Password);
    public sealed record VerifyOtpRequest(string Username, string Code);
    public sealed record ResendOtpRequest(string Username);
    public sealed record BootstrapAdminRequest(string BootstrapKey, string FullName, string Username, string Email, string Password);

    private enum OtpSendStatus { Sent, UserCooldown, DailyLimit, NotConfigured, DeliveryFailed, LockUnavailable }

    [HttpGet("me"), Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(id, out var userId)) return Unauthorized();
        var user = await db.Users
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new { x.Id, x.FullName, x.Username, x.Email, x.Role })
            .SingleOrDefaultAsync(ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("bootstrap-admin")]
    public async Task<IActionResult> BootstrapAdmin(BootstrapAdminRequest request, CancellationToken ct)
    {
        var expected = config["AdminBootstrap:Key"] ?? Environment.GetEnvironmentVariable("AdminBootstrap__Key");
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32) return NotFound();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(request.BootstrapKey ?? ""))) return Unauthorized();
        if (await db.Users.AnyAsync(x => x.Role == "Admin", ct))
            return Conflict(new { message = "Admin đã được khởi tạo; bootstrap chỉ dùng một lần." });
        if (request.Password.Length < 12 || !IsValidEmail(request.Email))
            return BadRequest(new { message = "Email hoặc mật khẩu không hợp lệ." });

        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            Username = request.Username.Trim(),
            Email = request.Email.Trim(),
            Role = "Admin",
            EmailVerified = false
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { message = "Username hoặc email đã được sử dụng." }); }
        return Created("", new { message = "Admin đã tạo. Lần đăng nhập đầu tiên sẽ gửi OTP đến email đã đăng ký." });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var fullName = request.FullName?.Trim() ?? "";
        var username = request.Username?.Trim() ?? "";
        var emailAddress = request.Email?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 100 ||
            username.Length is < 3 or > 40 || !System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-zA-Z0-9._-]+$") ||
            !IsValidEmail(emailAddress) || emailAddress.Length > 254 ||
            request.Password.Length < 10 || request.Password.Length > 128 || request.Password != request.ConfirmPassword)
            return BadRequest(new { message = "Thông tin không hợp lệ. Mật khẩu cần tối thiểu 10 ký tự và xác nhận phải trùng khớp." });

        if (await db.Users.AnyAsync(u => u.Username == username || u.Email == emailAddress, ct))
            return Conflict(new { message = "Username hoặc email đã được sử dụng." });

        var user = new AppUser
        {
            FullName = fullName,
            Username = username,
            Email = emailAddress,
            Role = "Employee",
            IsActive = true,
            EmailVerified = false
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { message = "Username hoặc email đã được sử dụng." }); }

        var sendStatus = await SendOtpAsync(user, RegistrationPurpose, ct);
        if (sendStatus != OtpSendStatus.Sent)
        {
            return OtpFailure(sendStatus, accountCreated: true);
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Tài khoản Employee đã tạo. Mã OTP đã được gửi đến email cá nhân để xác minh.",
            expiresInSeconds = 300
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không đúng." });
        if (!JwtConfigured()) return StatusCode(503, new { message = "JWT chưa được cấu hình an toàn trên backend." });

        var purpose = user.EmailVerified ? LoginPurpose : RegistrationPurpose;
        var sendStatus = await SendOtpAsync(user, purpose, ct);
        if (sendStatus != OtpSendStatus.Sent) return OtpFailure(sendStatus);

        return Ok(new
        {
            message = "Mã OTP đã được gửi đến email cá nhân đã đăng ký.",
            expiresInSeconds = 300,
            resendAfterSeconds = ResendCooldownSeconds
        });
    }

    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(ResendOtpRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null) return NotFound(new { message = "Không tìm thấy tài khoản đang hoạt động." });
        if (!JwtConfigured()) return StatusCode(503, new { message = "JWT chưa được cấu hình an toàn trên backend." });

        var purpose = user.EmailVerified ? LoginPurpose : RegistrationPurpose;
        var sendStatus = await SendOtpAsync(user, purpose, ct);
        if (sendStatus != OtpSendStatus.Sent) return OtpFailure(sendStatus);
        return Ok(new
        {
            message = "Mã OTP mới đã được gửi đến email cá nhân đã đăng ký.",
            expiresInSeconds = 300,
            resendAfterSeconds = ResendCooldownSeconds
        });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var code = request.Code?.Trim() ?? "";
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null || !JwtConfigured() || code.Length != OtpLength || !code.All(char.IsDigit))
            return Unauthorized(new { message = "Mã OTP không hợp lệ hoặc đã hết hạn." });

        var purpose = user.EmailVerified ? LoginPurpose : RegistrationPurpose;
        var now = DateTime.UtcNow;
        var challenge = await db.OtpChallenges
            .Where(x => x.UserId == user.Id && x.Purpose == purpose && x.UsedAtUtc == null && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (challenge is null || challenge.Attempts >= MaxOtpAttempts)
            return Unauthorized(new { message = "Mã OTP không hợp lệ hoặc đã hết hạn. Hãy gửi lại mã." });

        challenge.Attempts++;
        var expectedHash = HashOtp(code);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(challenge.CodeHash),
                Encoding.ASCII.GetBytes(expectedHash)))
        {
            await db.SaveChangesAsync(ct);
            return Unauthorized(new { message = "Mã OTP không chính xác." });
        }

        challenge.UsedAtUtc = now;
        if (purpose == RegistrationPurpose) user.EmailVerified = true;
        var otherChallenges = await db.OtpChallenges
            .Where(x => x.UserId == user.Id && x.Id != challenge.Id && x.UsedAtUtc == null)
            .ToListAsync(ct);
        foreach (var other in otherChallenges) other.UsedAtUtc = now;

        var token = CreateToken(user);
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            accessToken = token,
            tokenType = "Bearer",
            expiresInSeconds = 3600,
            role = user.Role,
            fullName = user.FullName,
            username = user.Username
        });
    }

    private async Task<OtpSendStatus> SendOtpAsync(AppUser user, string purpose, CancellationToken ct)
    {
        if (!SmtpConfigured()) return OtpSendStatus.NotConfigured;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        OtpChallenge challenge;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await AcquireOtpQuotaLockAsync(transaction, ct))
                return OtpSendStatus.LockUnavailable;

            var now = DateTime.UtcNow;
            if (await db.OtpChallenges.AnyAsync(
                    x => x.UserId == user.Id && x.CreatedAtUtc > now.AddSeconds(-ResendCooldownSeconds), ct))
                return OtpSendStatus.UserCooldown;

            var sentOrAttemptedToday = await db.OtpChallenges.CountAsync(
                x => x.CreatedAtUtc >= now.AddHours(-24), ct);
            if (sentOrAttemptedToday >= MaxOtpRequestsPer24Hours)
                return OtpSendStatus.DailyLimit;

            var previousChallenges = await db.OtpChallenges
                .Where(x => x.UserId == user.Id && x.UsedAtUtc == null)
                .ToListAsync(ct);
            foreach (var previous in previousChallenges) previous.UsedAtUtc = now;

            challenge = new OtpChallenge
            {
                UserId = user.Id,
                Purpose = purpose,
                CodeHash = HashOtp(code),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(5)
            };
            db.OtpChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        try
        {
            await email.SendOtpAsync(user.Email, code, ct);
            return OtpSendStatus.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            challenge.UsedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "OTP delivery failed for user ID {UserId}.", user.Id);
            return OtpSendStatus.DeliveryFailed;
        }
    }

    private async Task<bool> AcquireOtpQuotaLockAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = """
                DECLARE @LockResult int;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = N'AttendanceN7:OtpDailyQuota',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 10000;
                SELECT @LockResult;
                """;
            var result = await command.ExecuteScalarAsync(ct);
            return result is not null && Convert.ToInt32(result) >= 0;
        }
    }

    private IActionResult OtpFailure(OtpSendStatus status, bool accountCreated = false)
    {
        var message = status switch
        {
            OtpSendStatus.UserCooldown => $"Vui lòng đợi {ResendCooldownSeconds} giây trước khi gửi mã mới.",
            OtpSendStatus.DailyLimit => "Đã đạt giới hạn OTP trong 24 giờ. Vui lòng thử lại sau.",
            OtpSendStatus.NotConfigured => "Dịch vụ gửi OTP chưa được cấu hình đầy đủ.",
            OtpSendStatus.DeliveryFailed => "Không gửi được email OTP. Hãy kiểm tra App Password và thử lại.",
            OtpSendStatus.LockUnavailable => "Dịch vụ OTP đang bận. Vui lòng thử lại sau.",
            _ => "Không thể gửi email OTP lúc này."
        };
        if (accountCreated)
            message += " Tài khoản đã được tạo nhưng email chưa xác minh; hãy chuyển sang đăng nhập để gửi lại OTP sau.";

        var statusCode = status is OtpSendStatus.UserCooldown or OtpSendStatus.DailyLimit
            ? StatusCodes.Status429TooManyRequests
            : StatusCodes.Status503ServiceUnavailable;
        return StatusCode(statusCode, new { message });
    }

    private bool SmtpConfigured() =>
        string.Equals(config["Smtp:Host"], "smtp.gmail.com", StringComparison.OrdinalIgnoreCase) &&
        config["Smtp:Port"] == "587" &&
        string.Equals(config["Smtp:Username"], SenderAddress, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(config["Smtp:From"], SenderAddress, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(config["Smtp:Password"]);

    private bool JwtConfigured() => (config["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("Jwt__Secret")) is { Length: >= 32 };

    private static bool IsValidEmail(string email) =>
        email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    private string HashOtp(string code)
    {
        var secret = config["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("Jwt__Secret")!;
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(code)));
    }

    private string CreateToken(AppUser user)
    {
        var secret = config["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("Jwt__Secret")!;
        var issuer = config["Jwt:Issuer"] ?? "AttendanceN7";
        var audience = config["Jwt:Audience"] ?? "AttendanceN7Mobile";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("username", user.Username),
            new Claim("role", user.Role),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: credentials));
    }
}
