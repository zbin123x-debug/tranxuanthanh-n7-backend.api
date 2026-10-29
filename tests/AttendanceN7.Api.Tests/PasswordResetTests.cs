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

public sealed class PasswordResetTests
{
    private const string OldPassword = "old-password-123";
    private const string NewPassword = "new-password-456";

    private static string GetMessage(OkObjectResult response) =>
        response.Value!.GetType().GetProperty("message")!.GetValue(response.Value)!.ToString()!;

    [Fact]
    public async Task Request_sends_code_to_stored_email_and_hides_it_from_response()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Controller.RequestPasswordReset(
            new AuthController.RequestPasswordResetRequest("thanh"), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("thanh@example.test", fixture.Email.LastRecipient);
        Assert.Matches("^\\d{6}$", fixture.Email.LastCode);
        Assert.Equal("PasswordReset", (await fixture.Db.OtpChallenges.SingleAsync()).Purpose);
        var unknownResult = await fixture.Controller.RequestPasswordReset(
            new AuthController.RequestPasswordResetRequest("missing"), CancellationToken.None);
        Assert.Equal(GetMessage(response), GetMessage(Assert.IsType<OkObjectResult>(unknownResult)));
        Assert.DoesNotContain(fixture.Email.LastCode!, System.Text.Json.JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task Unknown_username_gets_same_generic_response_without_email()
    {
        await using var fixture = await Fixture.CreateAsync(seedUser: false);

        var result = await fixture.Controller.RequestPasswordReset(
            new AuthController.RequestPasswordResetRequest("missing"), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Null(fixture.Email.LastCode);
        Assert.Empty(fixture.Db.OtpChallenges);
    }

    [Fact]
    public async Task Confirm_changes_password_once_and_preserves_account_and_email_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalId = fixture.User.Id;
        await fixture.RequestOtpAsync();

        var result = await fixture.Controller.ConfirmPasswordReset(
            new AuthController.ConfirmPasswordResetRequest("thanh", fixture.Email.LastCode, NewPassword, NewPassword),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var hasher = new PasswordHasher<AppUser>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(fixture.User, fixture.User.PasswordHash, NewPassword));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(fixture.User, fixture.User.PasswordHash, OldPassword));
        Assert.Equal(originalId, fixture.User.Id);
        Assert.False(fixture.User.EmailVerified);
        Assert.Equal("Employee", fixture.User.Role);

        var reuse = await fixture.Controller.ConfirmPasswordReset(
            new AuthController.ConfirmPasswordResetRequest("thanh", fixture.Email.LastCode, "another-new-password", "another-new-password"),
            CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(reuse);
    }

    [Fact]
    public async Task Fifth_wrong_code_attempt_prevents_later_correct_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RequestOtpAsync();
        var wrongCode = fixture.Email.LastCode == "000000" ? "000001" : "000000";

        for (var i = 0; i < 5; i++)
        {
            var result = await fixture.Controller.ConfirmPasswordReset(
                new AuthController.ConfirmPasswordResetRequest("thanh", wrongCode, NewPassword, NewPassword),
                CancellationToken.None);
            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        var correctAfterLimit = await fixture.Controller.ConfirmPasswordReset(
            new AuthController.ConfirmPasswordResetRequest("thanh", fixture.Email.LastCode, NewPassword, NewPassword),
            CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(correctAfterLimit);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<AppUser>().VerifyHashedPassword(fixture.User, fixture.User.PasswordHash, OldPassword));
        Assert.Equal(5, (await fixture.Db.OtpChallenges.SingleAsync()).Attempts);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RequestOtpAsync();
        var challenge = await fixture.Db.OtpChallenges.SingleAsync();
        challenge.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.ConfirmPasswordReset(
            new AuthController.ConfirmPasswordResetRequest("thanh", fixture.Email.LastCode, NewPassword, NewPassword),
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Resend_cooldown_returns_generic_response_and_does_not_send_another_email()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Controller.RequestPasswordReset(
            new AuthController.RequestPasswordResetRequest("thanh"), CancellationToken.None);
        var second = await fixture.Controller.RequestPasswordReset(
            new AuthController.RequestPasswordResetRequest("thanh"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(second);
        Assert.Equal(1, fixture.Email.SendCount);
    }

    private sealed class Fixture(AppDbContext db, AuthController controller, CapturingEmailSender email, AppUser user) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AuthController Controller { get; } = controller;
        public CapturingEmailSender Email { get; } = email;
        public AppUser User { get; } = user;

        public static async Task<Fixture> CreateAsync(bool seedUser = true)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var db = new AppDbContext(options);
            var user = new AppUser
            {
                FullName = "Thanh Nguyen",
                Username = "thanh",
                Email = "thanh@example.test",
                Role = "Employee",
                IsActive = true,
                EmailVerified = false
            };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, OldPassword);
            if (seedUser)
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
                ["Smtp:Host"] = "smtp.gmail.com",
                ["Smtp:Port"] = "587",
                ["Smtp:Username"] = "zbin789x@gmail.com",
                ["Smtp:From"] = "zbin789x@gmail.com",
                ["Smtp:Password"] = "test-only-placeholder"
            }).Build();
            var email = new CapturingEmailSender();
            var controller = new AuthController(db, configuration, email, NullLogger<AuthController>.Instance);
            return new Fixture(db, controller, email, user);
        }

        public Task RequestOtpAsync() => Controller.RequestPasswordReset(
            new AuthController.RequestPasswordResetRequest("thanh"), CancellationToken.None);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? LastRecipient { get; private set; }
        public string? LastCode { get; private set; }
        public int SendCount { get; private set; }

        public Task SendOtpAsync(string to, string code, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendPasswordResetOtpAsync(string to, string code, CancellationToken cancellationToken)
        {
            LastRecipient = to;
            LastCode = code;
            SendCount++;
            return Task.CompletedTask;
        }
    }
}
