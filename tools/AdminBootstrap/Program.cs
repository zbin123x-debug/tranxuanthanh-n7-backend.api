using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Models;

const string username = "admin";
const string email = "zbin123x@gmail.com";
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ConnectionStrings__DefaultConnection to the intended SQL Server before running this one-time tool.");
    return 2;
}

Console.WriteLine($"This tool checks the connected database and, only if safe, creates {username} ({email}).");
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
await using var db = new AppDbContext(options);
await db.Database.CanConnectAsync(); // Never apply migrations or schema changes here.

var matches = await db.Users.Where(u => u.Username == username || u.Email == email).ToListAsync();
if (matches.Count > 0)
{
    var exact = matches.Count == 1 && matches[0].Username.Equals(username, StringComparison.OrdinalIgnoreCase)
        && matches[0].Email.Equals(email, StringComparison.OrdinalIgnoreCase) && matches[0].Role == "Admin";
    Console.WriteLine(exact
        ? "The Admin account already exists. Nothing was changed; its password was not read back or overwritten."
        : "A username/email conflict exists. Nothing was changed; inspect the existing account manually.");
    return exact ? 0 : 3;
}

if (await db.Users.AnyAsync(u => u.Role == "Admin"))
{
    Console.Error.WriteLine("Another Admin already exists. Nothing was changed.");
    return 3;
}

Console.Write("Admin full name: ");
var fullName = Console.ReadLine()?.Trim();
Console.Write("New Admin password (input hidden): ");
var password = ReadPassword();
if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 100 || password.Length < 12 || password.Length > 128)
{
    Console.Error.WriteLine("Full name is required and the password must contain 12 to 128 characters.");
    return 2;
}

var user = new AppUser { FullName = fullName, Username = username, Email = email, Role = "Admin", IsActive = true, EmailVerified = false };
user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
db.Users.Add(user);
try
{
    await db.SaveChangesAsync();
    Console.WriteLine("Admin created. Its first and every later login requires the Gmail OTP flow.");
    return 0;
}
catch (DbUpdateException)
{
    Console.Error.WriteLine("SQL rejected the insert (possibly a concurrent duplicate). No existing row was updated.");
    return 3;
}

static string ReadPassword()
{
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return new string(chars.ToArray()); }
        if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
        if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
}
