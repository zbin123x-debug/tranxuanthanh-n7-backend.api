using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TranXuanThanh_2380614806_N7_API.Controllers;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Models;
using TranXuanThanh_2380614806_N7_API.Services;
using Xunit;

namespace AttendanceN7.Api.Tests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task Registration_creates_unverified_employee_without_sending_otp()
    {
        await using var f = await Fixture.CreateAsync(seedUser: false);
        var result = await f.Controller.Register(
            new AuthController.RegisterRequest("Test User", "newuser", "new@example.test", "strong-password-123", "strong-password-123"), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var user = await f.Db.Users.SingleAsync();
        Assert.Equal("Employee", user.Role);
        Assert.False(user.EmailVerified);
        Assert.Empty(f.Db.OtpChallenges);
        Assert.Equal(0, f.Email.SendCount);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, "strong-password-123"));
    }

    [Fact]
    public async Task Swagger_admin_registration_allows_multiple_admins_without_otp()
    {
        await using var f = await Fixture.CreateAsync(seedUser: false);
        var first = await f.Controller.RegisterAdmin(
            new AuthController.RegisterRequest("Admin One", "admin1", "admin1@example.test", "strong-password-123", "strong-password-123"), CancellationToken.None);
        var second = await f.Controller.RegisterAdmin(
            new AuthController.RegisterRequest("Admin Two", "admin2", "admin2@example.test", "strong-password-456", "strong-password-456"), CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(first).StatusCode);
        Assert.Equal(201, Assert.IsType<ObjectResult>(second).StatusCode);
        var admins = await f.Db.Users.OrderBy(x => x.Username).ToListAsync();
        Assert.Equal(2, admins.Count);
        Assert.All(admins, admin =>
        {
            Assert.Equal("Admin", admin.Role);
            Assert.False(admin.EmailVerified);
        });
        Assert.Empty(f.Db.OtpChallenges);
        Assert.Equal(0, f.Email.SendCount);
    }

    [Fact]
    public async Task Swagger_admin_registration_rejects_duplicate_username_or_email()
    {
        await using var f = await Fixture.CreateAsync(seedUser: false);
        var request = new AuthController.RegisterRequest("Admin One", "admin1", "admin1@example.test", "strong-password-123", "strong-password-123");
        await f.Controller.RegisterAdmin(request, CancellationToken.None);

        var duplicateUsername = await f.Controller.RegisterAdmin(
            request with { Email = "other@example.test" }, CancellationToken.None);
        var duplicateEmail = await f.Controller.RegisterAdmin(
            request with { Username = "admin2" }, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(duplicateUsername);
        Assert.IsType<ConflictObjectResult>(duplicateEmail);
        Assert.Single(f.Db.Users);
        Assert.Equal(0, f.Email.SendCount);
    }

    [Fact]
    public async Task Registration_rejects_duplicate_username_or_email()
    {
        await using var f = await Fixture.CreateAsync();
        var sameUsername = await f.Controller.Register(
            new AuthController.RegisterRequest("Other", "thanh", "other@example.test", "strong-password-123", "strong-password-123"), CancellationToken.None);
        var sameEmail = await f.Controller.Register(
            new AuthController.RegisterRequest("Other", "other", "thanh@example.test", "strong-password-123", "strong-password-123"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(sameUsername);
        Assert.IsType<ConflictObjectResult>(sameEmail);
        Assert.Single(f.Db.Users);
        Assert.Empty(f.Db.OtpChallenges);
    }

    [Fact]
    public async Task Unverified_user_gets_login_otp_and_becomes_verified_only_after_correct_code()
    {
        await using var f = await Fixture.CreateAsync();
        var login = await f.Controller.Login(new AuthController.LoginRequest("thanh", "old-password-123"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(login);
        Assert.False(f.User.EmailVerified);
        Assert.Equal("thanh@example.test", f.Email.LastRecipient);
        Assert.Matches("^\\d{6}$", f.Email.LastCode);
        Assert.Equal("Login", (await f.Db.OtpChallenges.SingleAsync()).Purpose);

        var verified = await f.Controller.VerifyOtp(new AuthController.VerifyOtpRequest("thanh", f.Email.LastCode!), CancellationToken.None);
        var tokenResponse = Assert.IsType<OkObjectResult>(verified);
        Assert.NotNull(tokenResponse.Value!.GetType().GetProperty("accessToken")!.GetValue(tokenResponse.Value));
        Assert.True(f.User.EmailVerified);
    }

    [Fact]
    public async Task Expired_login_code_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Controller.Login(new AuthController.LoginRequest("thanh", "old-password-123"), CancellationToken.None);
        var challenge = await f.Db.OtpChallenges.SingleAsync();
        challenge.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();

        var result = await f.Controller.VerifyOtp(new AuthController.VerifyOtpRequest("thanh", f.Email.LastCode!), CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(f.User.EmailVerified);
    }

    [Fact]
    public async Task Wrong_password_does_not_send_an_otp()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.Controller.Login(new AuthController.LoginRequest("thanh", "wrong-password-123"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, f.Email.SendCount);
        Assert.Empty(f.Db.OtpChallenges);
    }

    [Fact]
    public async Task Direct_otp_verification_without_a_login_challenge_does_not_issue_token()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.Controller.VerifyOtp(new AuthController.VerifyOtpRequest("thanh", "123456"), CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(f.User.EmailVerified);
    }

    [Fact]
    public async Task Every_login_requires_a_fresh_otp_even_after_email_verification()
    {
        await using var f = await Fixture.CreateAsync(emailVerified: true);
        var firstLogin = await f.Controller.Login(new AuthController.LoginRequest("thanh", "old-password-123"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(firstLogin);
        Assert.Equal(1, f.Email.SendCount);
        var first = await f.Db.OtpChallenges.SingleAsync();
        var verified = await f.Controller.VerifyOtp(new AuthController.VerifyOtpRequest("thanh", f.Email.LastCode!), CancellationToken.None);
        Assert.IsType<OkObjectResult>(verified);

        first.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2);
        await f.Db.SaveChangesAsync();
        var secondLogin = await f.Controller.Login(new AuthController.LoginRequest("thanh", "old-password-123"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(secondLogin);
        Assert.Equal(2, f.Email.SendCount);
        Assert.Equal(2, await f.Db.OtpChallenges.CountAsync());
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_challenge()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Controller.Login(new AuthController.LoginRequest("thanh", "old-password-123"), CancellationToken.None);
        var wrong = f.Email.LastCode == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.IsType<UnauthorizedObjectResult>(await f.Controller.VerifyOtp(new AuthController.VerifyOtpRequest("thanh", wrong), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await f.Controller.VerifyOtp(new AuthController.VerifyOtpRequest("thanh", f.Email.LastCode!), CancellationToken.None));
        Assert.False(f.User.EmailVerified);
    }

    private sealed class Fixture(AppDbContext db, AuthController controller, CapturingEmailSender email, AppUser user) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AuthController Controller { get; } = controller;
        public CapturingEmailSender Email { get; } = email;
        public AppUser User { get; } = user;

        public static async Task<Fixture> CreateAsync(bool seedUser = true, bool emailVerified = false)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
            var db = new AppDbContext(options);
            var user = new AppUser { FullName = "Thanh Nguyen", Username = "thanh", Email = "thanh@example.test", Role = "Employee", IsActive = true, EmailVerified = emailVerified };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "old-password-123");
            if (seedUser) { db.Users.Add(user); await db.SaveChangesAsync(); }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
                ["Smtp:Host"] = "smtp.gmail.com", ["Smtp:Port"] = "587", ["Smtp:Username"] = "zbin789x@gmail.com",
                ["Smtp:From"] = "zbin789x@gmail.com", ["Smtp:Password"] = "test-only-placeholder"
            }).Build();
            var email = new CapturingEmailSender();
            var controller = new AuthController(db, config, email, NullLogger<AuthController>.Instance);
            return new Fixture(db, controller, email, user);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? LastRecipient { get; private set; }
        public string? LastCode { get; private set; }
        public int SendCount { get; private set; }
        public Task SendOtpAsync(string to, string code, CancellationToken ct) { LastRecipient = to; LastCode = code; SendCount++; return Task.CompletedTask; }
        public Task SendPasswordResetOtpAsync(string to, string code, CancellationToken ct) => Task.CompletedTask;
    }
}
