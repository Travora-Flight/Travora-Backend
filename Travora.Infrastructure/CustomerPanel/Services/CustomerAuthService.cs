using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IUpstashRedisService _redis;
    private readonly IJwtTokenGenerator _jwt;
    private readonly JwtSettings _jwtSettings;
    private readonly IEmailService _emailService;
    private readonly ICloudinaryService _cloudinary;
    private readonly IPassportOcrService _ocrService;
    private readonly string? _fixedOtp;

    public CustomerAuthService(
        ApplicationDbContext db,
        IUpstashRedisService redis,
        IJwtTokenGenerator jwt,
        JwtSettings jwtSettings,
        IEmailService emailService,
        ICloudinaryService cloudinary,
        IPassportOcrService ocrService,
        IConfiguration configuration)
    {
        _db = db;
        _redis = redis;
        _jwt = jwt;
        _jwtSettings = jwtSettings;
        _emailService = emailService;
        _cloudinary = cloudinary;
        _ocrService = ocrService;
        _fixedOtp = configuration["Testing:FixedOtp"];
    }

    // ===== Step 1: Basic Info → Redis =====
    public async Task<object> RegisterStep1Async(RegisterStep1Request request)
    {
        // Check email uniqueness
        var emailExists = await _db.Customers.AnyAsync(c => c.Email == request.Email);
        if (emailExists)
            throw new InvalidOperationException("Email already registered");

        var normalizedPhone = request.PhoneNumber.Replace("+", "").Replace(" ", "").Replace("-", "");

        if (normalizedPhone.Length < 10 || normalizedPhone.Length > 15 || !normalizedPhone.All(char.IsDigit))
            throw new ArgumentException("Invalid phone number");
        var sessionId = Guid.NewGuid().ToString();

        var sessionData = JsonSerializer.Serialize(new
        {
            request.FirstName,
            request.LastName,
            request.Email,
            PhoneNumber = normalizedPhone,
            request.Nationality,
            request.Gender
        });

        await _redis.SetAsync(
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
        var step1Json = await _redis.GetAsync($"register:step1:{request.SessionId}");

        if (string.IsNullOrEmpty(step1Json))
            throw new InvalidOperationException("Session expired, please start over");

        var step1 = JsonSerializer.Deserialize<Step1Data>(step1Json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid session data");

        // 2) Validate
        if (request.Password != request.ConfirmPassword)
            throw new ArgumentException("Passwords do not match");

        ValidatePasswordStrength(request.Password);

        if (!DateTime.TryParse(request.DateOfBirth, out var dateOfBirth) || dateOfBirth > DateTime.UtcNow)
            throw new ArgumentException("Invalid date of birth. Please provide a valid date in YYYY-MM-DD format and ensure it is not in the future.");

        var age = DateTime.UtcNow.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > DateTime.UtcNow.Date.AddYears(-age)) age--;

        if (age < 16)
            throw new ArgumentException("You must be at least 16 years old to register.");

        if (!DateTime.TryParse(request.PassportExpiryDate, out var passportExpiry) || passportExpiry <= DateTime.UtcNow)
            throw new ArgumentException("Passport is expired");

        var usernameExists = await _db.Customers.AnyAsync(c => c.Username == request.Username);
        if (usernameExists)
            throw new InvalidOperationException("Username is already taken");

        // Double-check email (race condition protection)
        var emailExists = await _db.Customers.AnyAsync(c => c.Email == step1.Email);
        if (emailExists)
            throw new InvalidOperationException("Email already registered");

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
                throw new InvalidOperationException("Passport image is unclear or invalid. Please upload a clearer image.");

            bool passportVerified = ocrResult.Error == null && ocrResult.ValidScore >= 85;

            DateTime.TryParse(ocrResult.DateOfBirthFormatted, out var extractedDob);
            DateTime.TryParse(ocrResult.ExpirationDateFormatted, out var extractedExpiry);

            if (passportVerified)
            {
                if (extractedExpiry <= DateTime.UtcNow)
                    throw new InvalidOperationException("The uploaded passport data is expired");

                if (!string.IsNullOrWhiteSpace(ocrResult.Number))
                {
                    bool exists = await _db.Customers.AnyAsync(c => c.PassportNumber == ocrResult.Number);

                    if (exists)
                        throw new InvalidOperationException("The extracted passport number is already registered in the system.");
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
                        Title = "Manual Passport Review Required",
                        Message = $"Customer {step1.FirstName} {step1.LastName} needs a manual passport review",
                        NotificationChannel = NotificationChannel.InApp
                    });
                }
            }

            await _db.SaveChangesAsync();

            // Welcome notification for the customer (DB only — no SignalR since not yet logged in)
            _db.Notifications.Add(new Notification
            {
                UserId = customer.CustomerId,
                UserType = UserType.Customer,
                NotificationType = NotificationType.AccountAlert,
                Title = accountStatus == CustomerAccountStatus.Verified
                    ? "Welcome to Travora"
                    : "Account under review",
                Message = accountStatus == CustomerAccountStatus.Verified
                    ? "Your account has been created and verified successfully. Enjoy our services!"
                    : "Your account is under review. We'll notify you once verified.",
                NotificationChannel = NotificationChannel.InApp
            });
            await _db.SaveChangesAsync();

            // 10) Welcome email + Verify Email OTP (Only if verified)
            if (accountStatus == CustomerAccountStatus.Verified)
            {
                // When you stop the Fixed OTP, uncomment this line and delete the one below it
                var otp = Random.Shared.Next(100000, 999999).ToString();
                // var otp = !string.IsNullOrEmpty(_fixedOtp) ? _fixedOtp : Random.Shared.Next(100000, 999999).ToString();
                await _redis.SetAsync($"email_verify:{step1.Email}", otp, TimeSpan.FromHours(24));

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendEmailAsync(
                            step1.Email,
                            "Welcome to Travora - Account Activation",
                            $"<h2>Hello {step1.FirstName} 👋</h2><p>Your account has been created successfully.</p><p>Email activation code is: <b style='font-size:24px;letter-spacing:4px;'>{otp}</b></p>");
                    }
                    catch { /* Email failure should not block registration */ }
                });
            }

            // 11) Delete session from Redis
            await _redis.DeleteAsync($"register:step1:{request.SessionId}");

            var responseMessage = accountStatus == CustomerAccountStatus.PendingVerification 
                ? "Account is now under review. A message will be sent once the account is activated" 
                : "Account created successfully! Please check your email to activate the account.";

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
            await LogLogin(customer?.CustomerId, UserType.Customer, LoginStatus.Failed, "Incorrect data", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Incorrect data");
        }

        if (!customer.IsActive)
        {
            await LogLogin(customer.CustomerId, UserType.Customer, LoginStatus.Failed, "Account suspended", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Account suspended");
        }

        if (!customer.EmailVerified)
        {
            await LogLogin(customer.CustomerId, UserType.Customer, LoginStatus.Failed, "Email not verified", ipAddress, userAgent);
            throw new UnauthorizedAccessException("Please verify your email first.");
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
            throw new KeyNotFoundException("Email is not registered in the system");
        }

        // TODO: When you stop the Fixed OTP, uncomment this line and delete the one below it
        var otp = Random.Shared.Next(100000, 999999).ToString();
        // var otp = !string.IsNullOrEmpty(_fixedOtp) ? _fixedOtp : Random.Shared.Next(100000, 999999).ToString();

        await _redis.SetAsync($"otp:{email}", otp, TimeSpan.FromMinutes(10));

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendEmailAsync(
                    email,
                    "Verification Code - Travora",
                    $"<h2>Your Verification Code</h2><p style='font-size:24px;font-weight:bold;letter-spacing:8px'>{otp}</p><p>Code is valid for 10 minutes</p>");
            }
            catch { }
        });

        return new { success = true, message = "Verification code has been sent to your email" };
    }

    // ===== Verify OTP =====
    public async Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request)
    {
        var storedOtp = await _redis.GetAsync($"otp:{request.Email}");

        if (string.IsNullOrEmpty(storedOtp))
            throw new InvalidOperationException("Code has expired");

        if (storedOtp != request.Otp)
            throw new ArgumentException("Incorrect code");

        var resetToken = Guid.NewGuid().ToString();
        await _redis.SetAsync($"reset:{resetToken}", request.Email, TimeSpan.FromMinutes(15));
        await _redis.DeleteAsync($"otp:{request.Email}");

        return new VerifyOtpResponse
        {
            Success = true,
            ResetToken = resetToken
        };
    }

    // ===== Reset Password =====
    public async Task<object> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = await _redis.GetAsync($"reset:{request.ResetToken}");

        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Request has expired");

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("Passwords do not match");

        ValidatePasswordStrength(request.NewPassword);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email)
            ?? throw new KeyNotFoundException("Customer not found");

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, customer.PasswordHash))
            throw new ArgumentException("New password cannot be the same as the current password");

        customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _redis.DeleteAsync($"reset:{request.ResetToken}");

        return new { success = true, message = "Password changed successfully" };
    }

    // ===== Verify Email =====
    public async Task<object> VerifyEmailAsync(VerifyEmailRequest request)
    {
        var storedOtp = await _redis.GetAsync($"email_verify:{request.Email}");

        if (string.IsNullOrEmpty(storedOtp))
            throw new InvalidOperationException("Code has expired or email is incorrect");

        if (storedOtp != request.Otp)
            throw new ArgumentException("Incorrect code");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == request.Email)
            ?? throw new KeyNotFoundException("Customer not found");

        customer.EmailVerified = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _redis.DeleteAsync($"email_verify:{request.Email}");

        return new { success = true, message = "Email activated successfully" };
    }

    // ===== Resend Verify Email =====
    public async Task<object> ResendVerificationEmailAsync(ResendVerifyEmailRequest request)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == request.Email)
            ?? throw new KeyNotFoundException("Customer not found");

        if (customer.EmailVerified)
            throw new InvalidOperationException("Email already activated");

        // TODO: When you stop the Fixed OTP, uncomment this line and delete the one below it
        var otp = Random.Shared.Next(100000, 999999).ToString();
        // var otp = !string.IsNullOrEmpty(_fixedOtp) ? _fixedOtp : Random.Shared.Next(100000, 999999).ToString();
        await _redis.SetAsync($"email_verify:{request.Email}", otp, TimeSpan.FromHours(24));

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendEmailAsync(
                    request.Email,
                    "Email Activation Code - Travora",
                    $"<h2>Hello {customer.Firstname} 👋</h2><p>Email activation code is: <b style='font-size:24px;letter-spacing:4px;'>{otp}</b></p>");
            }
            catch { }
        });

        return new { success = true, message = "A new code has been sent to your email" };
    }

    // ===== Private Helpers =====

    private static void ValidatePasswordStrength(string password)
    {
        if (password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters");
        if (!password.Any(char.IsUpper))
            throw new ArgumentException("Password must contain an uppercase letter");
        if (!password.Any(char.IsLower))
            throw new ArgumentException("Password must contain a lowercase letter");
        if (!password.Any(char.IsDigit))
            throw new ArgumentException("Password must contain a number");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("Password must contain a symbol (!@#$%^&*)");
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
