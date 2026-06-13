using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Ketabino.Database;
using Ketabino.Models;
using Ketabino.Services;

namespace Ketabino.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ReadingProgressController : ControllerBase
    {
        private readonly SqliteDbHelper _db;

        public ReadingProgressController(SqliteDbHelper db)
        {
            _db = db;
        }

        [HttpGet("{bookId}")]
        public async Task<IActionResult> GetProgress(long bookId)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT rp.ID, rp.USER_ID, rp.BOOK_ID, b.TITLE AS BOOK_TITLE, rp.LAST_READ_CHAPTER_ID, c.TITLE AS CHAPTER_TITLE, rp.LAST_READ_POSITION, rp.LAST_READ_AT 
                FROM READING_PROGRESS rp
                JOIN BOOKS b ON rp.BOOK_ID = b.ID
                LEFT JOIN CHAPTERS c ON rp.LAST_READ_CHAPTER_ID = c.ID
                WHERE rp.USER_ID = :userId AND rp.BOOK_ID = :bookId";

            var progress = await _db.QuerySingleOrDefaultAsync(sql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", bookId)
            }, reader => new ReadingProgressResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                BookTitle = reader["BOOK_TITLE"].ToString()!,
                LastReadChapterId = reader["LAST_READ_CHAPTER_ID"] == DBNull.Value ? null : Convert.ToInt64(reader["LAST_READ_CHAPTER_ID"]),
                LastReadChapterTitle = reader["CHAPTER_TITLE"] == DBNull.Value ? null : reader["CHAPTER_TITLE"].ToString(),
                LastReadPosition = Convert.ToInt32(reader["LAST_READ_POSITION"]),
                LastReadAt = SqliteDbHelper.GetUtcDateTime(reader["LAST_READ_AT"])
            });

            if (progress == null)
            {
                return Ok(new { Message = "پیشرفت مطالعه‌ای ثبت نشده است." });
            }

            return Ok(progress);
        }

        [HttpPost("{bookId}")]
        public async Task<IActionResult> UpdateProgress(long bookId, [FromBody] ReadingProgressRequest request)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // SQLite ON CONFLICT UPSERT statement
            var sql = @"
                INSERT INTO READING_PROGRESS (USER_ID, BOOK_ID, LAST_READ_CHAPTER_ID, LAST_READ_POSITION, LAST_READ_AT)
                VALUES (:userId, :bookId, :chapterId, :pos, CURRENT_TIMESTAMP)
                ON CONFLICT(USER_ID, BOOK_ID) DO UPDATE SET
                    LAST_READ_CHAPTER_ID = excluded.LAST_READ_CHAPTER_ID,
                    LAST_READ_POSITION = excluded.LAST_READ_POSITION,
                    LAST_READ_AT = CURRENT_TIMESTAMP";

            var parameters = new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", bookId),
                new SqliteParameter("chapterId", request.LastReadChapterId),
                new SqliteParameter("pos", request.LastReadPosition)
            };

            await _db.ExecuteNonQueryAsync(sql, parameters);
            return Ok(new { Message = "وضعیت پیشرفت مطالعه با موفقیت ثبت شد." });
        }

        // --- BOOKMARKS ---

        [HttpGet("bookmarks")]
        public async Task<IActionResult> GetBookmarks()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT bm.ID, bm.USER_ID, bm.CHAPTER_ID, c.TITLE AS CHAPTER_TITLE, c.BOOK_ID, b.TITLE AS BOOK_TITLE, bm.POSITION, bm.NOTE, bm.CREATED_AT 
                FROM BOOKMARKS bm
                JOIN CHAPTERS c ON bm.CHAPTER_ID = c.ID
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                WHERE bm.USER_ID = :userId
                ORDER BY bm.CREATED_AT DESC";

            var bookmarks = await _db.QueryAsync(sql, new[] { new SqliteParameter("userId", userId) }, reader => new BookmarkResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                ChapterId = Convert.ToInt64(reader["CHAPTER_ID"]),
                ChapterTitle = reader["CHAPTER_TITLE"].ToString()!,
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                BookTitle = reader["BOOK_TITLE"].ToString()!,
                Position = Convert.ToInt32(reader["POSITION"]),
                Note = reader["NOTE"] == DBNull.Value ? null : reader["NOTE"].ToString(),
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
            });

            return Ok(bookmarks);
        }

        [HttpPost("bookmark")]
        public async Task<IActionResult> CreateBookmark([FromBody] BookmarkRequest request)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                INSERT INTO BOOKMARKS (USER_ID, CHAPTER_ID, POSITION, NOTE) 
                VALUES (:userId, :chapterId, :pos, :note)";

            var parameters = new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("chapterId", request.ChapterId),
                new SqliteParameter("pos", request.Position),
                new SqliteParameter("note", SqliteDbHelper.ToDbValue(request.Note))
            };

            await _db.ExecuteNonQueryAsync(sql, parameters);

            // Get chapter title for notification
            var chTitleSql = "SELECT c.TITLE, b.TITLE AS BOOK_TITLE FROM CHAPTERS c JOIN BOOKS b ON c.BOOK_ID = b.ID WHERE c.ID = :chId";
            var chInfo = await _db.QuerySingleOrDefaultAsync(chTitleSql, new[] { new SqliteParameter("chId", request.ChapterId) }, r => new { ChapterTitle = r["TITLE"].ToString()!, BookTitle = r["BOOK_TITLE"].ToString()! });

            await NotificationHelper.SendAsync(_db, userId,
                "🔖 نشانک اضافه شد",
                chInfo != null ? $"نشانک در فصل «{chInfo.ChapterTitle}» از کتاب «{chInfo.BookTitle}» ثبت شد." : "نشانک جدید با موفقیت اضافه شد.");

            return Ok(new { Message = "نشانک با موفقیت ایجاد شد." });
        }

        [HttpDelete("bookmark/{id}")]
        public async Task<IActionResult> DeleteBookmark(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = "DELETE FROM BOOKMARKS WHERE ID = :id AND USER_ID = :userId";
            var deletedRows = await _db.ExecuteNonQueryAsync(sql, new[]
            {
                new SqliteParameter("id", id),
                new SqliteParameter("userId", userId)
            });

            if (deletedRows == 0) return NotFound(new { Message = "نشانک یافت نشد یا دسترسی حذف ندارید." });

            return Ok(new { Message = "نشانک حذف شد." });
        }

        // --- HIGHLIGHTS ---

        [HttpGet("highlights/{chapterId}")]
        public async Task<IActionResult> GetHighlights(long chapterId)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT h.ID, h.USER_ID, h.CHAPTER_ID, c.TITLE AS CHAPTER_TITLE, c.BOOK_ID, b.TITLE AS BOOK_TITLE, 
                       h.START_CHAR, h.END_CHAR, h.COLOR, h.TEXT_CONTENT, h.NOTE, h.CREATED_AT 
                FROM HIGHLIGHTS h
                JOIN CHAPTERS c ON h.CHAPTER_ID = c.ID
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                WHERE h.USER_ID = :userId AND h.CHAPTER_ID = :chapterId
                ORDER BY h.CREATED_AT DESC";

            var highlights = await _db.QueryAsync(sql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("chapterId", chapterId)
            }, reader => new HighlightResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                ChapterId = Convert.ToInt64(reader["CHAPTER_ID"]),
                ChapterTitle = reader["CHAPTER_TITLE"].ToString()!,
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                BookTitle = reader["BOOK_TITLE"].ToString()!,
                StartChar = Convert.ToInt32(reader["START_CHAR"]),
                EndChar = Convert.ToInt32(reader["END_CHAR"]),
                Color = reader["COLOR"].ToString()!,
                TextContent = reader["TEXT_CONTENT"].ToString()!,
                Note = reader["NOTE"] == DBNull.Value ? null : reader["NOTE"].ToString(),
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
            });

            return Ok(highlights);
        }

        [HttpPost("highlight")]
        public async Task<IActionResult> CreateHighlight([FromBody] HighlightRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TextContent))
            {
                return BadRequest(new { Message = "متن هایلایت نمی‌تواند خالی باشد." });
            }

            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                INSERT INTO HIGHLIGHTS (USER_ID, CHAPTER_ID, START_CHAR, END_CHAR, COLOR, TEXT_CONTENT, NOTE) 
                VALUES (:userId, :chapterId, :start, :end, :color, :content, :note)";

            var parameters = new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("chapterId", request.ChapterId),
                new SqliteParameter("start", request.StartChar),
                new SqliteParameter("end", request.EndChar),
                new SqliteParameter("color", request.Color),
                new SqliteParameter("content", request.TextContent),
                new SqliteParameter("note", SqliteDbHelper.ToDbValue(request.Note))
            };

            await _db.ExecuteNonQueryAsync(sql, parameters);

            await NotificationHelper.SendAsync(_db, userId,
                "📌 هایلایت اضافه شد",
                $"متن «{request.TextContent[..Math.Min(30, request.TextContent.Length)]}...» با رنگ {request.Color} هایلایت شد.");

            return Ok(new { Message = "متن با موفقیت هایلایت شد." });
        }

        [HttpDelete("highlight/{id}")]
        public async Task<IActionResult> DeleteHighlight(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = "DELETE FROM HIGHLIGHTS WHERE ID = :id AND USER_ID = :userId";
            var deletedRows = await _db.ExecuteNonQueryAsync(sql, new[]
            {
                new SqliteParameter("id", id),
                new SqliteParameter("userId", userId)
            });

            if (deletedRows == 0) return NotFound(new { Message = "هایلایت یافت نشد یا دسترسی حذف ندارید." });

            return Ok(new { Message = "هایلایت حذف شد." });
        }
    }
}
