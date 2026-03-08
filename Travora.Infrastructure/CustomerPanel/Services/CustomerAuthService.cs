using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Travora.Application.DTOs.Customer.Auth;
using Travora.Application.Interfaces;
using Travora.Application.Interfaces.External.Communication;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Application.Interfaces.Services.Customer;
using Travora.Domain.Entities;
using Travora.Domain.Enums;
using Travora.Infrastructure.Data;
using Travora.Shared.Settings;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class CustomerAuthService : ICustomerAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly IJwtTokenGenerator _jwt;
    private readonly JwtSettings _jwtSettings;
    private readonly IEmailService _emailService;
    private readonly ICloudinaryService _cloudinary;
    private readonly IPassportOcrService _ocrService;

    public CustomerAuthService(
        ApplicationDbContext db,
        IConnectionMultiplexer redis,
        IJwtTokenGenerator jwt,
        JwtSettings jwtSettings,
        IEmailService emailService,
        ICloudinaryService cloudinary,
        IPassportOcrService ocrService)
    {
        _db = db;
        _redis = redis;
        _jwt = jwt;
        _jwtSettings = jwtSettings;
        _emailService = emailService;
        _cloudinary = cloudinary;
        _ocrService = ocrService;
    }

    // ===== Step 1: Basic Info → Redis =====
    public async Task<object> RegisterStep1Async(RegisterStep1Request request)
    {
        // Check email uniqueness
        var emailExists = await _db.Customers.AnyAsync(c => c.Email == request.Email);
        if (emailExists)
            throw new InvalidOperationException("الإيميل مسجل مسبقاً");

        var normalizedPhone = request.PhoneNumber.Replace("+", "").Replace(" ", "").Replace("-", "");

        if (normalizedPhone.Length < 10 || normalizedPhone.Length > 15 || !normalizedPhone.All(char.IsDigit))
            throw new ArgumentException("رقم الهاتف غير صحيح");
        var sessionId = Guid.NewGuid().ToString();
        var redisDb = _redis.GetDatabase();

        var sessionData = JsonSerializer.Serialize(new
        {
            request.FirstName,
            request.LastName,
            request.Email,
            PhoneNumber = normalizedPhone,
            request.Nationality,
            request.Gender
        });

        await redisDb.StringSetAsync(
            $"register:step1:{sessionId}",
            sessionData,
            TimeSpan.FromMinutes(30)
        );

        return new { success = true, sessionId };
    }

    // ===== Step 2: Account Creation + OCR =====
    public async Task<RegisterResponse> RegisterStep2Async(RegisterStep2Request request)
    {
        // 1) Get Step1 data from Redis
        var redisDb = _redis.GetDatabase();
        var step1Json = await redisDb.StringGetAsync($"register:step1:{request.SessionId}");

        if (!step1Json.HasValue)
            throw new InvalidOperationException("انتهت صلاحية الجلسة، ابدأ من الأول");

        var step1 = JsonSerializer.Deserialize<Step1Data>(step1Json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("بيانات الجلسة غير صالحة");

        // 2) Validate
        if (request.Password != request.ConfirmPassword)
            throw new ArgumentException("كلمات المرور غير متطابقة");

        ValidatePasswordStrength(request.Password);

        if (!DateTime.TryParse(request.DateOfBirth, out var dateOfBirth) || dateOfBirth > DateTime.UtcNow)
            throw new ArgumentException("تاريخ ميلاد غير صحيح");

        if (!DateTime.TryParse(request.PassportExpiryDate, out var passportExpiry) || passportExpiry <= DateTime.UtcNow)
            throw new ArgumentException("جواز السفر منتهي الصلاحية");

        var usernameExists = await _db.Customers.AnyAsync(c => c.Username == request.Username);
        if (usernameExists)
            throw new InvalidOperationException("اسم المستخدم مستخدم مسبقاً");

        // Double-check email (race condition protection)
        var emailExists = await _db.Customers.AnyAsync(c => c.Email == step1.Email);
        if (emailExists)
            throw new InvalidOperationException("الإيميل مسجل مسبقاً");

        // 3) Save temp file for OCR + Cloudinary upload
        var tempDir = Path.Combine(Path.GetTempPath(), "travora_ocr");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid()}{Path.GetExtension(request.PassportImage.FileName)}");

        try
        {
            // Save file to temp path first
            await using (var fileStream = File.Create(tempPath))
            {
                await request.PassportImage.CopyToAsync(fileStream);
            }

            // Upload to Cloudinary from temp file
            string cloudinaryUrl;
            await using (var uploadStream = File.OpenRead(tempPath))
            {
                cloudinaryUrl = await _cloudinary.UploadFileAsync(
                    uploadStream, request.PassportImage.FileName, "travora/passports");
            }

            // Run OCR on temp file
            var ocrResult = await _ocrService.ExtractPassportDataAsync(tempPath);

            // 5) Determine verification status
            if (ocrResult.ValidScore < 65)
                throw new InvalidOperationException("صورة جواز السفر غير واضحة الملامح أو غير صالحة. يرجى رفع صورة أكثر وضوحاً.");

            bool passportVerified = ocrResult.Error == null && ocrResult.ValidScore >= 85;

            DateTime.TryParse(ocrResult.DateOfBirthFormatted, out var extractedDob);
            DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var extractedExpiry);

            if (passportVerified)
            {
                if (extractedExpiry <= DateTime.UtcNow)
                    throw new InvalidOperationException("بيانات جواز السفر المرفوع منتهية الصلاحية");

                if (!string.IsNullOrWhiteSpace(ocrResult.Number))
                {
                    bool exists = await _db.Customers.AnyAsync(c => c.PassportNumber == ocrResult.Number) ||
                                  await _db.Companions.AnyAsync(c => c.PassportNumber == ocrResult.Number);

                    if (exists)
                        throw new InvalidOperationException("رقم جواز السفر المستخرج مسجل بالفعل في النظام.");
                }
            }

            var verificationStatus = passportVerified ? VerificationStatus.Approved : VerificationStatus.UnderReview;
            var validationStatus = passportVerified ? PassportValidationStatus.Passed : PassportValidationStatus.RequiresManualReview;
            var accountStatus = passportVerified ? CustomerAccountStatus.Verified : CustomerAccountStatus.PendingVerification;

            // 6) Create Customer
            var customer = new Domain.Entities.Customer
            {
                Firstname = step1.FirstName,
                Lastname = step1.LastName,
                Username = request.Username,
                Email = step1.Email,
                PhoneNumber = step1.PhoneNumber,
                Nationality = passportVerified && !string.IsNullOrWhiteSpace(ocrResult.Nationality) ? ocrResult.Nationality : step1.Nationality,
                Gender = passportVerified && !string.IsNullOrWhiteSpace(ocrResult.SexFormatted) ? ocrResult.SexFormatted : step1.Gender,
                PassportNumber = passportVerified && !string.IsNullOrWhiteSpace(ocrResult.Number) ? ocrResult.Number : "manual_review",
                PassportExpiryDate = passportVerified && extractedExpiry != default ? extractedExpiry : passportExpiry,
                DateOfBirth = passportVerified && extractedDob != default ? extractedDob : dateOfBirth,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsActive = true,
                AccountStatus = accountStatus,
                EmailVerified = false, 
                ProfileCompleted = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            // 7) Create Document
            var document = new Document
            {
                OwnerId = customer.CustomerId,
                OwnerType = DocumentOwnerType.Customer,
                DocumentType = DocumentType.Passport,
                FilePath = cloudinaryUrl,
                FileSizeKb = (int)(request.PassportImage.Length / 1024),
                MimeType = request.PassportImage.ContentType,
                VerificationStatus = verificationStatus,
                UploadedAt = DateTime.UtcNow,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Documents.Add(document);
            await _db.SaveChangesAsync();

            // 8) Create PassportValidation
            var validation = new PassportValidation
            {
                DocumentId = document.DocumentId,
                ExpiryCheckPassed = passportExpiry > DateTime.UtcNow,
                FormatCheckPassed = ocrResult.ValidComposite ?? false,
                NameMatchCheck = string.Equals(ocrResult.Surname, step1.LastName, StringComparison.OrdinalIgnoreCase),
                BirthDateMatchCheck = extractedDob.Date == dateOfBirth.Date,
                ValidationStatus = validationStatus,
                OcrConfidenceScore = ocrResult.ValidScore / 100.0m,
                ManualReviewRequired = !passportVerified,
                MrzType = ocrResult.MrzType,
                RawMrzText = ocrResult.RawText,
                ValidScore = ocrResult.ValidScore,
                MrzMethod = ocrResult.Method,
                CheckNumber = ocrResult.CheckNumber,
                CheckDateOfBirth = ocrResult.CheckDateOfBirth,
                CheckExpirationDate = ocrResult.CheckExpirationDate,
                CheckComposite = ocrResult.CheckComposite,
                CheckPersonalNumber = ocrResult.CheckPersonalNumber,
                ValidNumber = ocrResult.ValidNumber,
                ValidDateOfBirth = ocrResult.ValidDateOfBirth,
                ValidExpirationDate = ocrResult.ValidExpirationDate,
                ValidComposite = ocrResult.ValidComposite,
                ValidPersonalNumber = ocrResult.ValidPersonalNumber,
                ExtractedPassportNumber = ocrResult.Number,
                ExtractedSurname = ocrResult.Surname,
                ExtractedGivenNames = ocrResult.Names,
                ExtractedNationality = ocrResult.Nationality,
                ExtractedDateOfBirth = extractedDob != default ? extractedDob : null,
                ExtractedExpiryDate = extractedExpiry != default ? extractedExpiry : null,
                ExtractedGender = ocrResult.SexFormatted,
                ValidatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _db.Set<PassportValidation>().Add(validation);

            // 9) Admin notification if manual review needed
            if (!passportVerified)
            {
                var admin = await _db.Admins.FirstOrDefaultAsync(a => a.IsActive);
                if (admin != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        UserId = admin.AdminId,
                        UserType = UserType.Admin,
                        NotificationType = NotificationType.SystemAlert,
                        Title = "مراجعة جواز يدوية مطلوبة",
                        Message = $"العميل {step1.FirstName} {step1.LastName} يحتاج مراجعة جواز يدوية",
                        NotificationChannel = NotificationChannel.InApp
                    });
                }
            }

            await _db.SaveChangesAsync();

            // 10) Welcome email + Verify Email OTP (Only if verified)
            if (accountStatus == CustomerAccountStatus.Verified)
            {
                var otp = Random.Shared.Next(100000, 999999).ToString();
                await redisDb.StringSetAsync($"email_verify:{step1.Email}", otp, TimeSpan.FromHours(24));

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendEmailAsync(
                            step1.Email,
                            "مرحباً بك في Travora - تفعيل الحساب",
                            $"<h2>أهلاً {step1.FirstName} 👋</h2><p>تم إنشاء حسابك بنجاح.</p><p>كود تفعيل الإيميل هو: <b style='font-size:24px;letter-spacing:4px;'>{otp}</b></p>");
                    }
                    catch { /* Email failure should not block registration */ }
                });
            }

            // 11) Delete session from Redis
            await redisDb.KeyDeleteAsync($"register:step1:{request.SessionId}");

            var responseMessage = accountStatus == CustomerAccountStatus.PendingVerification 
                ? "الحساب الآن تحت المراجعة. سيتم إرسال رسالة عند تفعيل الحساب" 
                : "تم إنشاء الحساب بنجاح! يرجى مراجعة بريدك الإلكتروني لتفعيل الحساب.";

            return new RegisterResponse
            {
                Success = true,
                Message = responseMessage,
                CustomerId = customer.CustomerId,
                AccountStatus = accountStatus.ToString(),
                PassportVerified = passportVerified,
                RequiresManualReview = !passportVerified,
                PassportNumber = passportVerified ? customer.PassportNumber : null
            };
        }
        finally
        {
            // Cleanup temp file
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    // ===== Login =====
    public async Task<CustomerLoginResponse> LoginAsync(CustomerLoginRequest request, string ipAddress, string userAgent)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == request.Email);

        if (customer == null || !BCrypt.Net.BCrypt.Verify(request.Password, customer.PasswordHash))
        {
            await LogLogin(customer?.CustomerId, UserType.Customer, LoginStatus.Failed, "بيانات غير صحيحة", ipAddress, userAgent);
            throw new UnauthorizedAccessException("بيانات غير صحيحة");
        }

        if (!customer.IsActive)
        {
            await LogLogin(customer.CustomerId, UserType.Customer, LoginStatus.Failed, "الحساب موقوف", ipAddress, userAgent);
            throw new UnauthorizedAccessException("الحساب موقوف");
        }

        await LogLogin(customer.CustomerId, UserType.Customer, LoginStatus.Success, null, ipAddress, userAgent);

        customer.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var accessToken = _jwt.GenerateCustomerToken(customer);
        var refreshToken = await CreateRefreshToken(customer.CustomerId, UserType.Customer);

        return new CustomerLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            CustomerId = customer.CustomerId,
            FirstName = customer.Firstname,
            AccountStatus = customer.AccountStatus.ToString(),
            ProfileCompleted = customer.ProfileCompleted
        };
    }

    // ===== Refresh Token =====
    public async Task<object> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken && r.UserType == UserType.Customer);

        if (storedToken == null)
            throw new UnauthorizedAccessException("Invalid token");
        if (storedToken.IsRevoked)
            throw new UnauthorizedAccessException("Token revoked");
        if (storedToken.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Token expired");

        storedToken.IsRevoked = true;

        var customer = await _db.Customers.FindAsync(storedToken.UserId)
            ?? throw new UnauthorizedAccessException("User not found");

        var newAccessToken = _jwt.GenerateCustomerToken(customer);
        var newRefreshToken = await CreateRefreshToken(customer.CustomerId, UserType.Customer);

        return new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshToken
        };
    }

    // ===== Forgot Password =====
    public async Task<object> ForgotPasswordAsync(string email)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email);

        if (customer == null)
        {
            throw new KeyNotFoundException("الإيميل غير مسجل في النظام");
        }

        var otp = Random.Shared.Next(100000, 999999).ToString();
        var redisDb = _redis.GetDatabase();

        await redisDb.StringSetAsync($"otp:{email}", otp, TimeSpan.FromMinutes(10));

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendEmailAsync(
                    email,
                    "كود التحقق - Travora",
                    $"<h2>كود التحقق الخاص بك</h2><p style='font-size:24px;font-weight:bold;letter-spacing:8px'>{otp}</p><p>الكود صالح لمدة 10 دقائق</p>");
            }
            catch { }
        });

        return new { success = true, message = "تم إرسال كود التحقق على إيميلك" };
    }

    // ===== Verify OTP =====
    public async Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request)
    {
        var redisDb = _redis.GetDatabase();
        var storedOtp = await redisDb.StringGetAsync($"otp:{request.Email}");

        if (!storedOtp.HasValue)
            throw new InvalidOperationException("انتهت صلاحية الكود");

        if (storedOtp.ToString() != request.Otp)
            throw new ArgumentException("كود غير صحيح");

        var resetToken = Guid.NewGuid().ToString();
        await redisDb.StringSetAsync($"reset:{resetToken}", request.Email, TimeSpan.FromMinutes(15));
        await redisDb.KeyDeleteAsync($"otp:{request.Email}");

        return new VerifyOtpResponse
        {
            Success = true,
            ResetToken = resetToken
        };
    }

    // ===== Reset Password =====
    public async Task<object> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var redisDb = _redis.GetDatabase();
        var email = await redisDb.StringGetAsync($"reset:{request.ResetToken}");

        if (!email.HasValue)
            throw new InvalidOperationException("انتهت صلاحية الطلب");

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("كلمات المرور غير متطابقة");

        ValidatePasswordStrength(request.NewPassword);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email.ToString())
            ?? throw new KeyNotFoundException("العميل غير موجود");

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, customer.PasswordHash))
            throw new ArgumentException("كلمة المرور الجديدة لا يمكن أن تكون نفس كلمة المرور الحالية");

        customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await redisDb.KeyDeleteAsync($"reset:{request.ResetToken}");

        return new { success = true, message = "تم تغيير كلمة المرور بنجاح" };
    }

    // ===== Verify Email =====
    public async Task<object> VerifyEmailAsync(VerifyEmailRequest request)
    {
        var redisDb = _redis.GetDatabase();
        var storedOtp = await redisDb.StringGetAsync($"email_verify:{request.Email}");

        if (!storedOtp.HasValue)
            throw new InvalidOperationException("انتهت صلاحية الكود أو الإيميل غير صحيح");

        if (storedOtp.ToString() != request.Otp)
            throw new ArgumentException("الكود غير صحيح");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == request.Email)
            ?? throw new KeyNotFoundException("العميل غير موجود");

        customer.EmailVerified = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await redisDb.KeyDeleteAsync($"email_verify:{request.Email}");

        return new { success = true, message = "تم تفعيل الإيميل بنجاح" };
    }

    // ===== Resend Verify Email =====
    public async Task<object> ResendVerificationEmailAsync(ResendVerifyEmailRequest request)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == request.Email)
            ?? throw new KeyNotFoundException("العميل غير موجود");

        if (customer.EmailVerified)
            throw new InvalidOperationException("الإيميل مفعل بالفعل");

        var otp = Random.Shared.Next(100000, 999999).ToString();
        var redisDb = _redis.GetDatabase();
        await redisDb.StringSetAsync($"email_verify:{request.Email}", otp, TimeSpan.FromHours(24));

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendEmailAsync(
                    request.Email,
                    "كود تفعيل الإيميل - Travora",
                    $"<h2>أهلاً {customer.Firstname} 👋</h2><p>كود تفعيل الإيميل هو: <b style='font-size:24px;letter-spacing:4px;'>{otp}</b></p>");
            }
            catch { }
        });

        return new { success = true, message = "تم إرسال كود جديد على إيميلك" };
    }

    // ===== Private Helpers =====

    private static void ValidatePasswordStrength(string password)
    {
        if (password.Length < 8)
            throw new ArgumentException("كلمة المرور 8 أحرف على الأقل");
        if (!password.Any(char.IsUpper))
            throw new ArgumentException("كلمة المرور لازم تحتوي على حرف كبير");
        if (!password.Any(char.IsLower))
            throw new ArgumentException("كلمة المرور لازم تحتوي على حرف صغير");
        if (!password.Any(char.IsDigit))
            throw new ArgumentException("كلمة المرور لازم تحتوي على رقم");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("كلمة المرور لازم تحتوي على رمز (!@#$%^&*)");
    }

    private async Task<string> CreateRefreshToken(int userId, UserType userType)
    {
        var token = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
            UserId = userId,
            UserType = userType
        });

        await _db.SaveChangesAsync();
        return token;
    }

    private async Task LogLogin(int? customerId, UserType userType, LoginStatus status, string? failureReason, string ipAddress, string userAgent)
    {
        _db.LoginLogs.Add(new LoginLog
        {
            CustomerId = customerId,
            UserType = userType,
            LoginStatus = status,
            FailureReason = failureReason ?? string.Empty,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceType = DetectDeviceType(userAgent)
        });
        await _db.SaveChangesAsync();
    }

    private static string DetectDeviceType(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Unknown";
        var ua = userAgent.ToLower();
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone")) return "Mobile";
        if (ua.Contains("tablet") || ua.Contains("ipad")) return "Tablet";
        return "Desktop";
    }
}

// Internal DTO for Redis Step1 session
internal class Step1Data
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
}
