using System;
using System.Collections.Generic;
using System.Linq;
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
    public class BookController : ControllerBase
    {
        private readonly SqliteDbHelper _db;

        public BookController(SqliteDbHelper db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetBooks([FromQuery] int? genreId, [FromQuery] string? search)
        {
            long userId = 0;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var sql = @"
                SELECT b.ID, b.AUTHOR_ID, u.NAME AS AUTHOR_NAME, b.TITLE, b.DESCRIPTION, b.COVER_IMAGE, b.STATUS, b.CREATED_AT, b.UPDATED_AT,
                       (SELECT COUNT(*) FROM CHAPTERS c WHERE c.BOOK_ID = b.ID AND c.STATUS = 'Published') AS CHAPTERS_COUNT,
                       (SELECT COUNT(*) FROM LIKES l WHERE l.BOOK_ID = b.ID) AS LIKES_COUNT,
                       COALESCE((SELECT AVG(r.RATING) FROM REVIEWS r WHERE r.BOOK_ID = b.ID), 0.0) AS AVG_RATING,
                       EXISTS(SELECT 1 FROM LIKES l WHERE l.BOOK_ID = b.ID AND l.USER_ID = :userId) AS IS_LIKED
                FROM BOOKS b
                JOIN USERS u ON b.AUTHOR_ID = u.ID
                WHERE b.STATUS = 'Published'";

            var parameters = new List<SqliteParameter>();
            parameters.Add(new SqliteParameter("userId", userId));

            if (genreId.HasValue)
            {
                sql += " AND b.ID IN (SELECT BOOK_ID FROM BOOK_GENRES WHERE GENRE_ID = :genreId)";
                parameters.Add(new SqliteParameter("genreId", genreId.Value));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (b.TITLE LIKE :search OR u.NAME LIKE :search)";
                parameters.Add(new SqliteParameter("search", $"%{search}%"));
            }

            sql += " ORDER BY b.CREATED_AT DESC";

            var books = await _db.QueryAsync(sql, parameters.ToArray(), MapBookResponse);

            if (books.Count > 0)
            {
                // Fetch genres for all these books in one query to avoid N+1 queries
                var bookIdsStr = string.Join(",", books.Select(b => b.Id));
                var genresSql = $@"
                    SELECT bg.BOOK_ID, g.ID AS GENRE_ID, g.NAME AS GENRE_NAME 
                    FROM BOOK_GENRES bg 
                    JOIN GENRES g ON bg.GENRE_ID = g.ID 
                    WHERE bg.BOOK_ID IN ({bookIdsStr})";

                var bookGenres = await _db.QueryAsync(genresSql, null, reader => new
                {
                    BookId = Convert.ToInt64(reader["BOOK_ID"]),
                    Genre = new GenreResponse
                    {
                        Id = Convert.ToInt32(reader["GENRE_ID"]),
                        Name = reader["GENRE_NAME"].ToString()!
                    }
                });

                // Map genres to their respective books
                var genreDict = bookGenres.GroupBy(bg => bg.BookId)
                                          .ToDictionary(g => g.Key, g => g.Select(x => x.Genre).ToList());

                foreach (var book in books)
                {
                    if (genreDict.TryGetValue(book.Id, out var genresList))
                    {
                        book.Genres = genresList;
                    }
                }
            }

            return Ok(books);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBook(long id)
        {
            long userId = 0;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var sql = @"
                SELECT b.ID, b.AUTHOR_ID, u.NAME AS AUTHOR_NAME, b.TITLE, b.DESCRIPTION, b.COVER_IMAGE, b.STATUS, b.CREATED_AT, b.UPDATED_AT,
                       (SELECT COUNT(*) FROM CHAPTERS c WHERE c.BOOK_ID = b.ID AND (c.STATUS = 'Published' OR b.AUTHOR_ID = :userId2)) AS CHAPTERS_COUNT,
                       (SELECT COUNT(*) FROM LIKES l WHERE l.BOOK_ID = b.ID) AS LIKES_COUNT,
                       COALESCE((SELECT AVG(r.RATING) FROM REVIEWS r WHERE r.BOOK_ID = b.ID), 0.0) AS AVG_RATING,
                       EXISTS(SELECT 1 FROM LIKES l WHERE l.BOOK_ID = b.ID AND l.USER_ID = :userId) AS IS_LIKED
                FROM BOOKS b
                JOIN USERS u ON b.AUTHOR_ID = u.ID
                WHERE b.ID = :id AND (b.STATUS = 'Published' OR b.AUTHOR_ID = :userId3)";

            var book = await _db.QuerySingleOrDefaultAsync(sql, new[] { 
                new SqliteParameter("id", id),
                new SqliteParameter("userId", userId),
                new SqliteParameter("userId2", userId),
                new SqliteParameter("userId3", userId)
            }, MapBookResponse);

            if (book == null)
            {
                return NotFound(new { Message = "کتاب مورد نظر یافت نشد یا منتشر نشده است." });
            }

            // Fetch genres for this book
            var genresSql = @"
                SELECT g.ID, g.NAME 
                FROM BOOK_GENRES bg 
                JOIN GENRES g ON bg.GENRE_ID = g.ID 
                WHERE bg.BOOK_ID = :bookId";

            book.Genres = await _db.QueryAsync(genresSql, new[] { new SqliteParameter("bookId", id) }, reader => new GenreResponse
            {
                Id = Convert.ToInt32(reader["ID"]),
                Name = reader["NAME"].ToString()!
            });

            return Ok(book);
        }

        [Authorize]
        [HttpGet("library")]
        public async Task<IActionResult> GetMyLibrary()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT DISTINCT b.ID, b.AUTHOR_ID, u.NAME AS AUTHOR_NAME, b.TITLE, b.DESCRIPTION, b.COVER_IMAGE, b.STATUS, b.CREATED_AT, b.UPDATED_AT,
                       (SELECT COUNT(*) FROM CHAPTERS c WHERE c.BOOK_ID = b.ID AND c.STATUS = 'Published') AS CHAPTERS_COUNT,
                       (SELECT COUNT(*) FROM LIKES l WHERE l.BOOK_ID = b.ID) AS LIKES_COUNT,
                       COALESCE((SELECT AVG(r.RATING) FROM REVIEWS r WHERE r.BOOK_ID = b.ID), 0.0) AS AVG_RATING,
                       EXISTS(SELECT 1 FROM LIKES l WHERE l.BOOK_ID = b.ID AND l.USER_ID = :userId) AS IS_LIKED
                FROM BOOKS b
                JOIN USERS u ON b.AUTHOR_ID = u.ID
                LEFT JOIN LIKES l ON b.ID = l.BOOK_ID AND l.USER_ID = :userId
                LEFT JOIN READING_PROGRESS rp ON b.ID = rp.BOOK_ID AND rp.USER_ID = :userId
                WHERE b.STATUS = 'Published' AND (l.USER_ID IS NOT NULL OR rp.USER_ID IS NOT NULL)
                ORDER BY b.CREATED_AT DESC";

            var books = await _db.QueryAsync(sql, new[] { new SqliteParameter("userId", userId) }, MapBookResponse);
            return Ok(books);
        }

        [Authorize]
        [HttpPost("{id}/like")]
        public async Task<IActionResult> LikeBook(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if already liked
            var checkSql = "SELECT COUNT(*) FROM LIKES WHERE USER_ID = :userId AND BOOK_ID = :bookId";
            var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", id)
            }));

            if (exists > 0)
            {
                return BadRequest(new { Message = "شما قبلاً این کتاب را پسندیده‌اید." });
            }

            var insertSql = "INSERT INTO LIKES (USER_ID, BOOK_ID) VALUES (:userId, :bookId)";
            await _db.ExecuteNonQueryAsync(insertSql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", id)
            });

            // Fetch book info for notification
            var bookInfoSql = "SELECT b.TITLE, b.AUTHOR_ID FROM BOOKS b WHERE b.ID = :bookId";
            var bookInfo = await _db.QuerySingleOrDefaultAsync(bookInfoSql,
                new[] { new SqliteParameter("bookId", id) },
                r => new { Title = r["TITLE"].ToString()!, AuthorId = Convert.ToInt64(r["AUTHOR_ID"]) });

            // Notify the liker
            await NotificationHelper.SendAsync(_db, userId,
                "❤️ کتاب پسندیده شد",
                $"کتاب «{bookInfo?.Title ?? ""}» به لیست علاقه‌مندی‌های شما اضافه شد.");

            // Notify the author
            if (bookInfo != null)
                await NotificationHelper.SendAsync(_db, bookInfo.AuthorId,
                    "❤️ کتابت پسندیده شد!",
                    $"یک خواننده کتاب «{bookInfo.Title}» شما را پسندید.");

            return Ok(new { Message = "کتاب با موفقیت پسندیده شد." });
        }

        [Authorize]
        [HttpDelete("{id}/like")]
        public async Task<IActionResult> UnlikeBook(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var deleteSql = "DELETE FROM LIKES WHERE USER_ID = :userId AND BOOK_ID = :bookId";
            var deletedRows = await _db.ExecuteNonQueryAsync(deleteSql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", id)
            });

            if (deletedRows == 0)
            {
                return BadRequest(new { Message = "شما این کتاب را نپسندیده بودید." });
            }

            return Ok(new { Message = "کتاب از لیست علاقه‌مندی‌ها حذف شد." });
        }

        [HttpGet("{id}/review")]
        public async Task<IActionResult> GetReviews(long id)
        {
            var sql = @"
                SELECT r.ID, r.USER_ID, u.NAME AS USER_NAME, r.BOOK_ID, r.RATING, r.TITLE, r.CONTENT, r.CREATED_AT 
                FROM REVIEWS r
                JOIN USERS u ON r.USER_ID = u.ID
                WHERE r.BOOK_ID = :bookId
                ORDER BY r.CREATED_AT DESC";

            var reviews = await _db.QueryAsync(sql, new[] { new SqliteParameter("bookId", id) }, reader => new ReviewResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                UserName = reader["USER_NAME"].ToString()!,
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                Rating = Convert.ToInt32(reader["RATING"]),
                Title = reader["TITLE"] == DBNull.Value ? null : reader["TITLE"].ToString(),
                Content = reader["CONTENT"] == DBNull.Value ? null : reader["CONTENT"].ToString(),
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
            });

            return Ok(reviews);
        }

        [Authorize]
        [HttpPost("{id}/review")]
        public async Task<IActionResult> SubmitReview(long id, [FromBody] ReviewRequest request)
        {
            if (request.Rating < 1 || request.Rating > 5)
            {
                return BadRequest(new { Message = "امتیاز باید بین ۱ تا ۵ باشد." });
            }

            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if already reviewed
            var checkSql = "SELECT COUNT(*) FROM REVIEWS WHERE USER_ID = :userId AND BOOK_ID = :bookId";
            var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", id)
            }));

            if (exists > 0)
            {
                var updateSql = @"
                    UPDATE REVIEWS 
                    SET RATING = :rating, TITLE = :title, CONTENT = :content, CREATED_AT = CURRENT_TIMESTAMP
                    WHERE USER_ID = :userId AND BOOK_ID = :bookId";

                await _db.ExecuteNonQueryAsync(updateSql, new[]
                {
                    new SqliteParameter("userId", userId),
                    new SqliteParameter("bookId", id),
                    new SqliteParameter("rating", request.Rating),
                    new SqliteParameter("title", SqliteDbHelper.ToDbValue(request.Title)),
                    new SqliteParameter("content", SqliteDbHelper.ToDbValue(request.Content))
                });

                return Ok(new { Message = "نظر شما با موفقیت بروزرسانی شد." });
            }

            var insertSql = @"
                INSERT INTO REVIEWS (USER_ID, BOOK_ID, RATING, TITLE, CONTENT) 
                VALUES (:userId, :bookId, :rating, :title, :content)";

            await _db.ExecuteNonQueryAsync(insertSql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", id),
                new SqliteParameter("rating", request.Rating),
                new SqliteParameter("title", SqliteDbHelper.ToDbValue(request.Title)),
                new SqliteParameter("content", SqliteDbHelper.ToDbValue(request.Content))
            });

            // Fetch book info for notifications
            var rvBookSql = "SELECT TITLE, AUTHOR_ID FROM BOOKS WHERE ID = :bookId";
            var rvBook = await _db.QuerySingleOrDefaultAsync(rvBookSql, new[] { new SqliteParameter("bookId", id) },
                r => new { Title = r["TITLE"].ToString()!, AuthorId = Convert.ToInt64(r["AUTHOR_ID"]) });

            // Notify reviewer
            var stars = new string('★', request.Rating) + new string('☆', 5 - request.Rating);
            await NotificationHelper.SendAsync(_db, userId,
                "⭐ نظر شما ثبت شد",
                $"نظر شما ({stars}) برای کتاب «{rvBook?.Title ?? ""}» با موفقیت ثبت شد.");

            // Notify author
            if (rvBook != null)
                await NotificationHelper.SendAsync(_db, rvBook.AuthorId,
                    "📝 نظر جدید دریافت شد",
                    $"یک خواننده برای کتاب «{rvBook.Title}» نظر {stars} ثبت کرد.");

            return Ok(new { Message = "ثبت نظر با موفقیت انجام شد." });
        }

        [HttpGet("{id}/comment")]
        public async Task<IActionResult> GetComments(long id)
        {
            var sql = @"
                SELECT c.ID, c.USER_ID, u.NAME AS USER_NAME, c.BOOK_ID, c.CHAPTER_ID, c.PARENT_COMMENT_ID, c.CONTENT, c.CREATED_AT 
                FROM COMMENTS c
                JOIN USERS u ON c.USER_ID = u.ID
                WHERE c.BOOK_ID = :bookId AND c.PARENT_COMMENT_ID IS NULL
                ORDER BY c.CREATED_AT DESC";

            var parentComments = await _db.QueryAsync(sql, new[] { new SqliteParameter("bookId", id) }, MapComment);

            // Fetch replies for each comment
            foreach (var comment in parentComments)
            {
                var repliesSql = @"
                    SELECT c.ID, c.USER_ID, u.NAME AS USER_NAME, c.BOOK_ID, c.CHAPTER_ID, c.PARENT_COMMENT_ID, c.CONTENT, c.CREATED_AT 
                    FROM COMMENTS c
                    JOIN USERS u ON c.USER_ID = u.ID
                    WHERE c.PARENT_COMMENT_ID = :parentId
                    ORDER BY c.CREATED_AT ASC";

                comment.Replies = await _db.QueryAsync(repliesSql, new[] { new SqliteParameter("parentId", comment.Id) }, MapComment);
            }

            return Ok(parentComments);
        }

        [Authorize]
        [HttpPost("{id}/comment")]
        public async Task<IActionResult> SubmitComment(long id, [FromBody] CommentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { Message = "متن کامنت نمی‌تواند خالی باشد." });
            }

            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var insertSql = @"
                INSERT INTO COMMENTS (USER_ID, BOOK_ID, CHAPTER_ID, PARENT_COMMENT_ID, CONTENT) 
                VALUES (:userId, :bookId, :chapterId, :parentId, :content)";

            await _db.ExecuteNonQueryAsync(insertSql, new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("bookId", id),
                new SqliteParameter("chapterId", SqliteDbHelper.ToDbValue(request.ChapterId)),
                new SqliteParameter("parentId", SqliteDbHelper.ToDbValue(request.ParentCommentId)),
                new SqliteParameter("content", request.Content)
            });

            return Ok(new { Message = "کامنت شما با موفقیت ثبت شد." });
        }

        private BookResponse MapBookResponse(SqliteDataReader reader) => new()
        {
            Id = Convert.ToInt64(reader["ID"]),
            AuthorId = Convert.ToInt64(reader["AUTHOR_ID"]),
            AuthorName = reader["AUTHOR_NAME"].ToString()!,
            Title = reader["TITLE"].ToString()!,
            Description = reader["DESCRIPTION"] == DBNull.Value ? null : reader["DESCRIPTION"].ToString(),
            CoverImage = reader["COVER_IMAGE"] == DBNull.Value ? null : reader["COVER_IMAGE"].ToString(),
            Status = reader["STATUS"].ToString()!,
            CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]),
            UpdatedAt = SqliteDbHelper.GetUtcDateTime(reader["UPDATED_AT"]),
            ChaptersCount = Convert.ToInt32(reader["CHAPTERS_COUNT"]),
            LikesCount = Convert.ToInt32(reader["LIKES_COUNT"]),
            AverageRating = Convert.ToDouble(reader["AVG_RATING"]),
            IsLiked = Convert.ToBoolean(reader["IS_LIKED"])
        };

        private CommentResponse MapComment(SqliteDataReader reader) => new()
        {
            Id = Convert.ToInt64(reader["ID"]),
            UserId = Convert.ToInt64(reader["USER_ID"]),
            UserName = reader["USER_NAME"].ToString()!,
            BookId = Convert.ToInt64(reader["BOOK_ID"]),
            ChapterId = reader["CHAPTER_ID"] == DBNull.Value ? null : Convert.ToInt64(reader["CHAPTER_ID"]),
            ParentCommentId = reader["PARENT_COMMENT_ID"] == DBNull.Value ? null : Convert.ToInt64(reader["PARENT_COMMENT_ID"]),
            Content = reader["CONTENT"].ToString()!,
            CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
        };
    }
}
