using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TranXuanThanh_2380614806_N7_API.Data;
using TranXuanThanh_2380614806_N7_API.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Attendance N7 API",
        Version = "v1",
        Description = "API chấm công Nhóm 7. Đăng nhập bằng OTP để lấy JWT, sau đó bấm Authorize và nhập token."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập JWT access token (không cần thêm chữ Bearer)."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});
builder.Services.AddProblemDetails();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString) && builder.Environment.IsDevelopment())
    connectionString = "Server=tranxuanthanh\\MSSQLSERVER01;Database=tranxuanthanh_nhom7;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;";
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("Jwt__Secret");
if (!builder.Environment.IsDevelopment())
{
    var missingProductionSettings = new List<string>();
    if (string.IsNullOrWhiteSpace(connectionString)) missingProductionSettings.Add("ConnectionStrings__DefaultConnection");
    if ((jwtSecret?.Length ?? 0) < 32) missingProductionSettings.Add("Jwt__Secret (minimum 32 characters)");
    if (missingProductionSettings.Count > 0)
        throw new InvalidOperationException($"Production startup blocked. Missing required settings: {string.Join(", ", missingProductionSettings)}. Set them in the hosting environment using double-underscore names such as ConnectionStrings__DefaultConnection and Jwt__Secret.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
var signingSecret = jwtSecret is { Length: >= 32 } ? jwtSecret : Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    // Keep standard JWT claim names (sub, role) intact for controllers and role authorization.
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "AttendanceN7",
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"] ?? "AttendanceN7Mobile",
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (builder.Environment.IsDevelopment()) policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
    else if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Attendance N7 API v1"));
}
if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}
app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy", database = "connected" }) : Results.Problem("Database unavailable", statusCode: 503); }
    catch { return Results.Problem("Database unavailable", statusCode: 503); }
});
app.MapControllers();
app.Run();
