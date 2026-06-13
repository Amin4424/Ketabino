using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Ketabino.Database;
using Ketabino.Models;
using Ketabino.Services;

namespace Ketabino.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly JwtService _jwt;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public AuthController(SqliteDbHelper db, JwtService jwt, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _jwt = jwt;
            _connectionProvider = connectionProvider;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { Message = "شماره موبایل و نام الزامی است." });
            }

            if (request.Role != "Reader" && request.Role != "Author" && request.Role != "Admin")
            {
                return BadRequest(new { Message = "نقش وارد شده نامعتبر است." });
            }

            // Check if phone number already exists
            var checkSql = "SELECT COUNT(*) FROM USERS WHERE PHONE_NUMBER = :phone";
            var checkParam = new SqliteParameter("phone", request.PhoneNumber);
            var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[] { checkParam }));

            if (exists > 0)
            {
                return Conflict(new { Message = "کاربر با این شماره موبایل قبلاً ثبت‌نام کرده است." });
            }

            // Start an atomic transaction to create user, wallet, and profile
            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Insert User (returning generated ID)
                var insertUserSql = @"
                    INSERT INTO USERS (PHONE_NUMBER, NAME, ROLE) 
                    VALUES (:phone, :name, :role) 
                    RETURNING ID";
                
                using var userCmd = new SqliteCommand(insertUserSql, connection);
                userCmd.Transaction = transaction;
                userCmd.Parameters.Add(new SqliteParameter("phone", request.PhoneNumber));
                userCmd.Parameters.Add(new SqliteParameter("name", request.Name));
                userCmd.Parameters.Add(new SqliteParameter("role", request.Role));
                
                
                long userId = Convert.ToInt64(await userCmd.ExecuteScalarAsync());

                // Create Wallet
                var createWalletSql = "INSERT INTO WALLETS (USER_ID, BALANCE) VALUES (:userId, 0.00)";
                using var walletCmd = new SqliteCommand(createWalletSql, connection);
                walletCmd.Transaction = transaction;
                walletCmd.Parameters.Add(new SqliteParameter("userId", userId));
                await walletCmd.ExecuteNonQueryAsync();

                // If role is Author, create Author Profile
                if (request.Role == "Author")
                {
                    var createProfileSql = "INSERT INTO AUTHOR_PROFILES (USER_ID, BIO, PROFILE_IMAGE) VALUES (:userId, NULL, NULL)";
                    using var profileCmd = new SqliteCommand(createProfileSql, connection);
                    profileCmd.Transaction = transaction;
                    profileCmd.Parameters.Add(new SqliteParameter("userId", userId));
                    await profileCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();

                // Retrieve and return the created user details
                var getUserSql = "SELECT ID, PHONE_NUMBER, NAME, ROLE, CREATED_AT FROM USERS WHERE ID = :id";
                var userObj = await _db.QuerySingleOrDefaultAsync(getUserSql, new[] { new SqliteParameter("id", userId) }, MapUser);

                return CreatedAtAction(nameof(GetProfile), null, userObj);
            }
            catch (Exception)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در هنگام ثبت‌نام رخ داد." });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                return BadRequest(new { Message = "شماره موبایل الزامی است." });
            }

            // Check if mock verification code matches (12345)
            if (request.VerificationCode != "12345")
            {
                return BadRequest(new { Message = "کد تایید اشتباه است. از کد نمونه 12345 استفاده کنید." });
            }

            var getUserSql = "SELECT ID, PHONE_NUMBER, NAME, ROLE, CREATED_AT FROM USERS WHERE PHONE_NUMBER = :phone";
            var user = await _db.QuerySingleOrDefaultAsync(getUserSql, new[] { new SqliteParameter("phone", request.PhoneNumber) }, MapUser);

            if (user == null)
            {
                return NotFound(new { Message = "کاربری با این شماره موبایل یافت نشد. ابتدا ثبت‌نام کنید." });
            }

            // Send login notification
            await NotificationHelper.SendAsync(_db, user.Id,
                "✅ ورود موفق",
                $"خوش آمدید {user.Name}! ورود شما به کتابینو با موفقیت انجام شد.");

            var token = _jwt.GenerateToken(user.Id, user.PhoneNumber, user.Role);

            return Ok(new AuthResponse
            {
                Token = token,
                User = user
            });
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            
            var getUserSql = "SELECT ID, PHONE_NUMBER, NAME, ROLE, CREATED_AT FROM USERS WHERE ID = :id";
            var user = await _db.QuerySingleOrDefaultAsync(getUserSql, new[] { new SqliteParameter("id", userId) }, MapUser);

            if (user == null) return NotFound();

            if (user.Role == "Author")
            {
                var getProfileSql = "SELECT USER_ID, BIO, PROFILE_IMAGE, SOCIAL_LINKS, UPDATED_AT FROM AUTHOR_PROFILES WHERE USER_ID = :userId";
                var profile = await _db.QuerySingleOrDefaultAsync(getProfileSql, new[] { new SqliteParameter("userId", userId) }, MapAuthorProfile);
                
                return Ok(new { User = user, Profile = profile });
            }

            return Ok(new { User = user });
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] AuthorProfileRequest request)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            string role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

            if (role != "Author")
            {
                return BadRequest(new { Message = "فقط کاربران با نقش نویسنده می‌توانند پروفایل تکمیلی داشته باشند." });
            }

            var updateSql = @"
                UPDATE AUTHOR_PROFILES 
                SET BIO = :bio, PROFILE_IMAGE = :img, SOCIAL_LINKS = :social, UPDATED_AT = CURRENT_TIMESTAMP 
                WHERE USER_ID = :userId";

            var parameters = new[]
            {
                new SqliteParameter("bio", SqliteDbHelper.ToDbValue(request.Bio)),
                new SqliteParameter("img", SqliteDbHelper.ToDbValue(request.ProfileImage)),
                new SqliteParameter("social", SqliteDbHelper.ToDbValue(request.SocialLinks)),
                new SqliteParameter("userId", userId)
            };

            await _db.ExecuteNonQueryAsync(updateSql, parameters);

            return Ok(new { Message = "پروفایل نویسنده با موفقیت بروزرسانی شد." });
        }

        private UserResponse MapUser(SqliteDataReader reader) => new()
        {
            Id = Convert.ToInt64(reader["ID"]),
            PhoneNumber = reader["PHONE_NUMBER"].ToString()!,
            Name = reader["NAME"].ToString()!,
            Role = reader["ROLE"].ToString()!,
            CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
        };

        private AuthorProfileResponse MapAuthorProfile(SqliteDataReader reader) => new()
        {
            UserId = Convert.ToInt64(reader["USER_ID"]),
            Bio = reader["BIO"] == DBNull.Value ? null : reader["BIO"].ToString(),
            ProfileImage = reader["PROFILE_IMAGE"] == DBNull.Value ? null : reader["PROFILE_IMAGE"].ToString(),
            SocialLinks = reader["SOCIAL_LINKS"] == DBNull.Value ? null : reader["SOCIAL_LINKS"].ToString(),
            UpdatedAt = SqliteDbHelper.GetUtcDateTime(reader["UPDATED_AT"])
        };
    }
}
