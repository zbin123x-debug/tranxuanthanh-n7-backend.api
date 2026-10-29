# Attendance N7 API

ASP.NET Core 8 Web API for the Flutter attendance application. This project keeps credentials outside source control.

## Local development

The development connection defaults to `tranxuanthanh\\MSSQLSERVER01`, database `tranxuanthanh_nhom7`, using Windows authentication. EF Core migrations create `Users`, `OtpChallenges`, `Shifts`, `EmployeeShifts`, `Attendances`, `AttendanceAnomalies`, `AttendanceQrTokens`, `AttendanceQrSessions`, `AttendanceAuditLogs`, `AttendanceAdjustmentRequests`, and `__EFMigrationsHistory`.

Set secrets before running the API. Use User Secrets in Visual Studio Code's terminal, or environment variables:

```powershell
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "<random secret with at least 32 characters>"
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set "Smtp:Port" "587"
dotnet user-secrets set "Smtp:Username" "zbin789x@gmail.com"
dotnet user-secrets set "Smtp:From" "zbin789x@gmail.com"
.\scripts\Set-GmailAppPassword.ps1
dotnet ef database update
dotnet run
```

Run the isolated API tests with `dotnet test tests/AttendanceN7.Api.Tests/AttendanceN7.Api.Tests.csproj`. The tests use an EF Core InMemory database and do not run migrations or connect to SQL Server.

### Gmail App Password

1. Sign in to the Gmail account that will send OTP messages and enable Google 2-Step Verification.
2. Open the Google Account **Security** page and find **App passwords** (Google only shows this option for eligible accounts with 2-Step Verification enabled).
3. Create an app password for this backend, copy the generated value once, and store it as `Smtp:Password` using User Secrets locally or `Smtp__Password` in the deployed host's secret settings.
4. Set `Smtp:Username` and `Smtp:From` to `zbin789x@gmail.com`. Run `pwsh -File scripts/Set-GmailAppPassword.ps1`; it prompts without echoing input and stores the App Password in this Windows user's .NET User Secrets file. Never commit it or include it in the APK, and do not send it in chat.

Google may change the account settings flow. If App passwords is not available, use an approved transactional mail provider and adapt the SMTP configuration rather than disabling OTP.

Environment variable names use double underscores, e.g. `Jwt__Secret`, `Smtp__Password`, and `ConnectionStrings__DefaultConnection`. If no JWT secret is configured in Development, the API still starts, but login intentionally returns 503. SMTP credentials are required to send OTP email.

### Admin and Employee registration in Swagger

- `POST /api/auth/register` creates an Employee.
- `POST /api/auth/register-admin` creates an Admin. It is public and has no one-time limit; each request still requires a unique username and email and a password of at least 10 characters.
- Both registration endpoints hash passwords with ASP.NET `PasswordHasher`, set `EmailVerified=false`, and send no OTP during account creation. Login continues to require Gmail OTP before issuing a JWT.
- Because `register-admin` is public, anyone who can reach the production API can create an Admin account. Use it only while the course demo is open to public registration; disable the route after grading if the API will hold real attendance records.

### Central Gmail OTP flow

- Every OTP email is sent **from** `zbin789x@gmail.com` through Gmail SMTP STARTTLS on port 587. The recipient is taken from that user's unique `Users.Email` value, so each employee or admin receives their own code.
- Registration creates only an Employee with `EmailVerified=false`, stores the password with `PasswordHasher<AppUser>`, sends no OTP, and returns HTTP 201. The first successful login OTP marks the email verified and returns a JWT.
- Login sends a fresh OTP only after the username and password are valid. JWTs are returned only after OTP verification. The account role is read from SQL Server and placed in the signed token.
- Codes are six digits, expire after five minutes, are stored as an HMAC hash, and become invalid after one successful use or five failed entries. A new code invalidates any earlier unused code.
- Resending is limited to once per user per 60 seconds. A SQL Server application lock enforces a shared cap of 400 OTP requests in a rolling 24-hour window across API instances; failed delivery attempts count toward the cap. Google documents 500 outgoing messages per day for standard Gmail accounts, so the app cap leaves a 100-message buffer for other mail sent from the account. Gmail can still apply additional account-specific limits ([Gmail sending limits](https://support.google.com/mail/answer/22839)).
- `POST /api/auth/resend-otp` accepts `{ "username": "..." }`; it sends to the email already stored for that account and never accepts a recipient address from the client.

### Forgot password with email OTP

In Swagger, use `POST /api/auth/password-reset/request` with `{ "username": "thanh" }`. The API sends a password-reset OTP only to the active account's email already stored in `Users.Email`; it does not accept a destination email from the request. The response is intentionally identical for existing, inactive, unknown, or throttled accounts.

Then use `POST /api/auth/password-reset/confirm` with `{ "username": "thanh", "code": "123456", "newPassword": "a-new-strong-password", "confirmPassword": "a-new-strong-password" }`. The password must be 10–128 characters. A valid OTP is single-use, expires after five minutes, and allows at most five attempts. Sending is limited to once per account every 60 seconds and shares the 400-requests-per-24-hours OTP cap. The new password is stored with the same ASP.NET `PasswordHasher<AppUser>` used by registration. The reset does not change the account ID, role, email-verification flag, or other records. Because the OTP proves control of the registered email, an existing account with `EmailVerified=false` can reset its password; its normal login OTP flow can verify that email afterward.

These endpoints use the existing `OtpChallenges` table and require no database migration. Tests use an isolated EF Core InMemory database and never connect to a local or production SQL Server database.

## Production deployment

Build a container with `docker build -t attendance-n7-api:latest .` or publish with `dotnet publish -c Release`. Put it behind a TLS reverse proxy (for example, Caddy or Nginx) and expose only HTTPS to clients. Configure these values in the hosting provider's secret/environment settings before startup:

The project production API is `https://zbin123xgmailc-001-site1.ktempurl.com` and the online database is `db_ad06d2_tranxuanthanhn7`. Flutter defaults to that HTTPS API; it can still be overridden at build time with `--dart-define=API_BASE_URL=...`. The IIS publish profile is configured for out-of-process hosting. To prepare the SmarterASP.NET package locally, run `./scripts/Publish-IIS.ps1`; this creates `artifacts/deployment/api-iis-win-x64.zip` and does not upload or apply database changes.

- `ASPNETCORE_ENVIRONMENT=Production`
- Optional `Swagger__Enabled=true` to expose interactive API documentation at `/swagger` for a class demo. Leave unset/false for normal production use.
- `ReverseProxy__TrustForwardedHeaders=true` only when the app listener is private and reachable solely through the trusted HTTPS proxy (the forwarded-header values are trusted)
- `ConnectionStrings__DefaultConnection` (TLS-enabled SQL Server credentials and network access from the host)
- `Jwt__Secret` (at least 32 random characters), optionally `Jwt__Issuer` and `Jwt__Audience`
- `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__From`
- `WorkLocation__Latitude`, `WorkLocation__Longitude`, `WorkLocation__RadiusMeters`
- Optional `WorkLocation__TimeZoneId` (defaults to `Asia/Ho_Chi_Minh`), `Attendance__LateGraceMinutes` (defaults to 10), and `Attendance__MotionSpikeThreshold` (defaults to 20 m/s²)
- Optional browser origins as `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc. (native Android does not need CORS)

Production startup fails closed if the SQL connection string or JWT secret is missing. These authentication changes use the existing `Users` and `OtpChallenges` columns and indexes, so they require no database migration. Do not run migrations during deployment for this change. The health endpoint is `GET /health`; it confirms both process availability and SQL Server connectivity. Publishing prepares a ZIP only; hosting and online SQL remain untouched.

In Development, Swagger UI is available at `http://localhost:5110/swagger` (HTTP profile) or `https://localhost:7205/swagger` (HTTPS profile). The UI includes a JWT Bearer Authorize control for trying protected endpoints. On a production host it stays disabled unless `Swagger__Enabled=true` is explicitly configured.

## Endpoints

- `POST /api/auth/register`, `/api/auth/login`, `/api/auth/verify-otp`, `/api/auth/password-reset/request`, `/api/auth/password-reset/confirm`; authenticated `GET /api/auth/me`
- `GET /api/attendance/mine`, `GET /api/attendance/schedule`, `POST /api/attendance/mark`
- Employee: `GET /api/attendance/mine/adjustments`, `POST /api/attendance/adjustment-requests`
- Admin only: `/api/admin/overview`, `/api/admin/employees`, `/api/admin/shifts`, `/api/admin/employee-shifts`, `/api/admin/attendances`, `/api/admin/anomalies`, `/api/admin/adjustment-requests`, `POST /api/admin/qr-token`, `POST /api/admin/qr-sessions`, `GET /api/admin/qr-sessions/active`, `GET /api/admin/qr-sessions/{id}`
- `GET /health`

Admin shift deletion deactivates records to preserve history. Employee records can be deactivated. Legacy one-time QR tokens are stored hashed and expire after three minutes. Scheduled QR sessions are hashed and valid only within the Admin-selected time window; one session can be scanned by multiple employees.

See [IMPLEMENTATION_REPORT.md](IMPLEMENTATION_REPORT.md) for the current completion and deployment status, verification evidence, and remaining setup inputs.
