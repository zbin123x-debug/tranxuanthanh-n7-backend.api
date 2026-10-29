using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TranXuanThanh_2380614806_N7_API.Controllers;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Models;
using Xunit;

namespace AttendanceN7.Api.Tests;

public sealed class AdminAndCheckoutTests
{
    [Fact]
    public async Task DeleteEmployee_SoftDeletesAccountAndPreservesAttendance()
    {
        await using var db = CreateDb();
        var user = new AppUser { FullName = "Employee One", Username = "employee.one", Email = "employee@example.com", Role = "Employee" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var attendance = new AttendanceRecord { UserId = user.Id, CheckInUtc = DateTime.UtcNow };
        db.Attendances.Add(attendance);
        await db.SaveChangesAsync();

        var controller = new AdminController(db, new ConfigurationBuilder().Build());
        var result = await controller.DeleteEmployee(user.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(user.IsActive);
        Assert.True(user.IsDeleted);
        Assert.True(await db.Attendances.AnyAsync(x => x.Id == attendance.Id));
    }

    [Fact]
    public async Task CheckOut_SavesGpsAndRecordsOutsideTwentyMeterAlert()
    {
        await using var db = CreateDb();
        var user = new AppUser { FullName = "Employee Two", Username = "employee.two", Email = "employee2@example.com", Role = "Employee" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var attendance = new AttendanceRecord { UserId = user.Id, CheckInUtc = DateTime.UtcNow.AddHours(-8), Latitude = 0, Longitude = 0 };
        db.Attendances.Add(attendance);
        db.CompanyWorkLocations.Add(new CompanyWorkLocation { Name = "Office", Latitude = 0, Longitude = 0, RadiusMeters = 20 });
        await db.SaveChangesAsync();

        var controller = new AttendanceController(db, new ConfigurationBuilder().Build());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString())], "test"))
            }
        };
        var result = await controller.Mark(new AttendanceController.MarkRequest(0.001, 0, "check-out"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0.001, attendance.CheckOutLatitude);
        Assert.Equal(0, attendance.CheckOutLongitude);
        Assert.True(attendance.CheckOutDistanceMeters > 20);
        Assert.Equal("Office", attendance.CheckOutWorkLocationName);
        Assert.Contains(await db.AttendanceAnomalies.ToListAsync(), x => x.Reason.Contains("ngoài bán kính 20 m"));
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }
}
