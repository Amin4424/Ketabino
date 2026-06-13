using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Ketabino.Database;
using Ketabino.Services;

namespace Ketabino.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BookClubController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public BookClubController(SqliteDbHelper db, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _connectionProvider = connectionProvider;
        }

        public class CreateClubRequest
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public long BookId { get; set; }
        }

        public class PostMessageRequest
        {
            public string Content { get; set; } = "";
        }

        [HttpGet]
        public async Task<IActionResult> GetClubs()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT c.ID, c.NAME, c.DESCRIPTION, c.CREATED_AT, 
                       b.TITLE AS BOOK_TITLE, b.COVER_IMAGE AS BOOK_COVER,
                       (SELECT COUNT(*) FROM BOOK_CLUB_MEMBERS WHERE BOOK_CLUB_ID = c.ID) AS MEMBER_COUNT,
                       (SELECT COUNT(*) FROM BOOK_CLUB_MEMBERS WHERE BOOK_CLUB_ID = c.ID AND USER_ID = :userId) AS IS_MEMBER
                FROM BOOK_CLUBS c
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                ORDER BY c.CREATED_AT DESC";

            var clubs = await _db.QueryAsync(sql, new[] { new SqliteParameter("userId", userId) }, reader => new
            {
                Id = Convert.ToInt64(reader["ID"]),
                Name = reader["NAME"].ToString()!,
                Description = reader["DESCRIPTION"].ToString()!,
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]),
                BookTitle = reader["BOOK_TITLE"].ToString()!,
                BookCover = reader["BOOK_COVER"] == DBNull.Value ? null : reader["BOOK_COVER"].ToString(),
                MemberCount = Convert.ToInt32(reader["MEMBER_COUNT"]),
                IsMember = Convert.ToInt32(reader["IS_MEMBER"]) > 0
            });

            return Ok(clubs);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetClubDetails(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Fetch club info
            var sql = @"
                SELECT c.ID, c.NAME, c.DESCRIPTION, c.CREATED_AT, c.CREATOR_ID,
                       b.ID AS BOOK_ID, b.TITLE AS BOOK_TITLE, b.COVER_IMAGE AS BOOK_COVER
                FROM BOOK_CLUBS c
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                WHERE c.ID = :id";

            var club = await _db.QuerySingleOrDefaultAsync(sql, new[] { new SqliteParameter("id", id) }, reader => new
            {
                Id = Convert.ToInt64(reader["ID"]),
                Name = reader["NAME"].ToString()!,
                Description = reader["DESCRIPTION"].ToString()!,
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]),
                CreatorId = Convert.ToInt64(reader["CREATOR_ID"]),
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                BookTitle = reader["BOOK_TITLE"].ToString()!,
                BookCover = reader["BOOK_COVER"] == DBNull.Value ? null : reader["BOOK_COVER"].ToString()
            });

            if (club == null) return NotFound(new { Message = "گروه کتابخوانی یافت نشد." });

            // Fetch members
            var membersSql = @"
                SELECT u.ID, u.NAME, u.ROLE
                FROM BOOK_CLUB_MEMBERS m
                JOIN USERS u ON m.USER_ID = u.ID
                WHERE m.BOOK_CLUB_ID = :clubId";

            var members = await _db.QueryAsync(membersSql, new[] { new SqliteParameter("clubId", id) }, reader => new
            {
                Id = Convert.ToInt64(reader["ID"]),
                Name = reader["NAME"].ToString()!,
                Role = reader["ROLE"].ToString()!
            });

            // Fetch messages
            var messagesSql = @"
                SELECT msg.ID, msg.CONTENT, msg.CREATED_AT, u.ID AS USER_ID, u.NAME AS USER_NAME
                FROM BOOK_CLUB_MESSAGES msg
                JOIN USERS u ON msg.USER_ID = u.ID
                WHERE msg.BOOK_CLUB_ID = :clubId
                ORDER BY msg.CREATED_AT ASC";

            var messages = await _db.QueryAsync(messagesSql, new[] { new SqliteParameter("clubId", id) }, reader => new
            {
                Id = Convert.ToInt64(reader["ID"]),
                Content = reader["CONTENT"].ToString()!,
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                UserName = reader["USER_NAME"].ToString()!
            });

            bool isMember = false;
            foreach (var m in members)
            {
                if (m.Id == userId) isMember = true;
            }

            return Ok(new
            {
                Club = club,
                Members = members,
                Messages = messages,
                IsMember = isMember
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateClub([FromBody] CreateClubRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { Message = "نام گروه الزامی است." });

            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Create club
                var createSql = @"
                    INSERT INTO BOOK_CLUBS (CREATOR_ID, BOOK_ID, NAME, DESCRIPTION) 
                    VALUES (:creatorId, :bookId, :name, :description);
                    SELECT last_insert_rowid();";

                using var createCmd = new SqliteCommand(createSql, connection);
                createCmd.Transaction = transaction;
                createCmd.Parameters.Add(new SqliteParameter("creatorId", userId));
                createCmd.Parameters.Add(new SqliteParameter("bookId", request.BookId));
                createCmd.Parameters.Add(new SqliteParameter("name", request.Name));
                createCmd.Parameters.Add(new SqliteParameter("description", request.Description));

                var clubId = Convert.ToInt64(await createCmd.ExecuteScalarAsync());

                // Auto-join creator
                var joinSql = "INSERT INTO BOOK_CLUB_MEMBERS (BOOK_CLUB_ID, USER_ID) VALUES (:clubId, :userId)";
                using var joinCmd = new SqliteCommand(joinSql, connection);
                joinCmd.Transaction = transaction;
                joinCmd.Parameters.Add(new SqliteParameter("clubId", clubId));
                joinCmd.Parameters.Add(new SqliteParameter("userId", userId));
                await joinCmd.ExecuteNonQueryAsync();

                transaction.Commit();

                // Notify creator
                await NotificationHelper.SendAsync(_db, userId,
                    "📚 گروه کتابخوانی ایجاد شد",
                    $"گروه «{request.Name}» با موفقیت ایجاد شد. شما به عنوان سازنده گروه به صورت خودکار عضو شدید.");

                return Ok(new { Message = "گروه کتابخوانی با موفقیت ایجاد شد.", ClubId = clubId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در ایجاد گروه رخ داد.", Error = ex.Message });
            }
        }

        [HttpPost("{id}/join")]
        public async Task<IActionResult> JoinClub(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if club exists
            var checkSql = "SELECT COUNT(*) FROM BOOK_CLUBS WHERE ID = :id";
            var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[] { new SqliteParameter("id", id) }));
            if (exists == 0) return NotFound(new { Message = "گروه یافت نشد." });

            // Check if already a member
            var checkMemberSql = "SELECT COUNT(*) FROM BOOK_CLUB_MEMBERS WHERE BOOK_CLUB_ID = :clubId AND USER_ID = :userId";
            var memberCount = Convert.ToInt32(await _db.ExecuteScalarAsync(checkMemberSql, new[] {
                new SqliteParameter("clubId", id),
                new SqliteParameter("userId", userId)
            }));

            if (memberCount > 0) return BadRequest(new { Message = "شما در حال حاضر عضو این گروه هستید." });

            // Join
            var sql = "INSERT INTO BOOK_CLUB_MEMBERS (BOOK_CLUB_ID, USER_ID) VALUES (:clubId, :userId)";
            await _db.ExecuteNonQueryAsync(sql, new[] {
                new SqliteParameter("clubId", id),
                new SqliteParameter("userId", userId)
            });

            // Fetch club name for notification
            var clubNameSql = "SELECT NAME FROM BOOK_CLUBS WHERE ID = :id";
            var clubName = (await _db.ExecuteScalarAsync(clubNameSql, new[] { new SqliteParameter("id", id) }))?.ToString() ?? "گروه";

            await NotificationHelper.SendAsync(_db, userId,
                "👥 عضویت در گروه کتابخوانی",
                $"شما با موفقیت به گروه «{clubName}» پیوستید.");

            return Ok(new { Message = "شما با موفقیت عضو گروه شدید." });
        }

        [HttpPost("{id}/leave")]
        public async Task<IActionResult> LeaveClub(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if creator is leaving
            var checkCreatorSql = "SELECT CREATOR_ID FROM BOOK_CLUBS WHERE ID = :id";
            var creatorId = Convert.ToInt64(await _db.ExecuteScalarAsync(checkCreatorSql, new[] { new SqliteParameter("id", id) }));
            if (creatorId == userId) return BadRequest(new { Message = "سازنده گروه نمی‌تواند گروه را ترک کند." });

            // Leave
            var sql = "DELETE FROM BOOK_CLUB_MEMBERS WHERE BOOK_CLUB_ID = :clubId AND USER_ID = :userId";
            var rows = await _db.ExecuteNonQueryAsync(sql, new[] {
                new SqliteParameter("clubId", id),
                new SqliteParameter("userId", userId)
            });

            if (rows == 0) return BadRequest(new { Message = "شما عضو این گروه نیستید." });

            // Fetch club name for notification
            var clubNameSql2 = "SELECT NAME FROM BOOK_CLUBS WHERE ID = :id";
            var clubName2 = (await _db.ExecuteScalarAsync(clubNameSql2, new[] { new SqliteParameter("id", id) }))?.ToString() ?? "گروه";

            await NotificationHelper.SendAsync(_db, userId,
                "🚪 خروج از گروه کتابخوانی",
                $"شما گروه «{clubName2}» را ترک کردید.");

            return Ok(new { Message = "شما گروه را ترک کردید." });
        }

        [HttpPost("{id}/message")]
        public async Task<IActionResult> PostMessage(long id, [FromBody] PostMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest(new { Message = "محتوای پیام نمی‌تواند خالی باشد." });

            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check membership
            var checkMemberSql = "SELECT COUNT(*) FROM BOOK_CLUB_MEMBERS WHERE BOOK_CLUB_ID = :clubId AND USER_ID = :userId";
            var memberCount = Convert.ToInt32(await _db.ExecuteScalarAsync(checkMemberSql, new[] {
                new SqliteParameter("clubId", id),
                new SqliteParameter("userId", userId)
            }));

            if (memberCount == 0) return Unauthorized(new { Message = "برای ارسال پیام ابتدا باید عضو گروه شوید." });

            var sql = "INSERT INTO BOOK_CLUB_MESSAGES (BOOK_CLUB_ID, USER_ID, CONTENT) VALUES (:clubId, :userId, :content)";
            await _db.ExecuteNonQueryAsync(sql, new[] {
                new SqliteParameter("clubId", id),
                new SqliteParameter("userId", userId),
                new SqliteParameter("content", request.Content)
            });

            return Ok(new { Message = "پیام با موفقیت ارسال شد." });
        }
    }
}
