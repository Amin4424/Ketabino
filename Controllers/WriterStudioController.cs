using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Ketabino.Database;
using Ketabino.Models;

namespace Ketabino.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class WriterStudioController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public WriterStudioController(SqliteDbHelper db, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _connectionProvider = connectionProvider;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        private string CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

        private IActionResult CheckAuthorRole()
        {
            if (CurrentUserRole != "Author" && CurrentUserRole != "Admin")
            {
                return StatusCode(403, new { Message = "دسترسی فقط برای نویسندگان مجاز است." });
            }
            return null!;
        }

        [HttpGet("books")]
        public async Task<IActionResult> GetAuthorBooks()
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            var sql = @"
                SELECT b.ID, b.AUTHOR_ID, u.NAME AS AUTHOR_NAME, b.TITLE, b.DESCRIPTION, b.COVER_IMAGE, b.STATUS, b.CREATED_AT, b.UPDATED_AT,
                       (SELECT COUNT(*) FROM CHAPTERS c WHERE c.BOOK_ID = b.ID) AS CHAPTERS_COUNT,
                       (SELECT COUNT(*) FROM LIKES l WHERE l.BOOK_ID = b.ID) AS LIKES_COUNT,
                       COALESCE((SELECT AVG(r.RATING) FROM REVIEWS r WHERE r.BOOK_ID = b.ID), 0.0) AS AVG_RATING
                FROM BOOKS b
                JOIN USERS u ON b.AUTHOR_ID = u.ID
                WHERE b.AUTHOR_ID = :authorId
                ORDER BY b.CREATED_AT DESC";

            var books = await _db.QueryAsync(sql, new[] { new SqliteParameter("authorId", CurrentUserId) }, MapBookResponse);

            if (books.Count > 0)
            {
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

        [HttpPost("book")]
        public async Task<IActionResult> CreateBook([FromBody] BookRequest request)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { Message = "عنوان کتاب الزامی است." });
            }

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var insertSql = @"
                    INSERT INTO BOOKS (AUTHOR_ID, TITLE, DESCRIPTION, COVER_IMAGE, STATUS) 
                    VALUES (:authorId, :title, :desc, :cover, :status) 
                    RETURNING ID";

                using var cmd = new SqliteCommand(insertSql, connection);
                cmd.Transaction = transaction;
                cmd.Parameters.Add(new SqliteParameter("authorId", CurrentUserId));
                cmd.Parameters.Add(new SqliteParameter("title", request.Title));
                cmd.Parameters.Add(new SqliteParameter("desc", SqliteDbHelper.ToDbValue(request.Description)));
                cmd.Parameters.Add(new SqliteParameter("cover", SqliteDbHelper.ToDbValue(request.CoverImage)));
                cmd.Parameters.Add(new SqliteParameter("status", request.Status));

                var scalarResult = await cmd.ExecuteScalarAsync();
                long bookId = Convert.ToInt64(scalarResult);

                // Insert Genres
                if (request.GenreIds != null && request.GenreIds.Count > 0)
                {
                    foreach (var genreId in request.GenreIds)
                    {
                        var insertGenreSql = "INSERT INTO BOOK_GENRES (BOOK_ID, GENRE_ID) VALUES (:bookId, :genreId)";
                        using var genreCmd = new SqliteCommand(insertGenreSql, connection);
                        genreCmd.Transaction = transaction;
                        genreCmd.Parameters.Add(new SqliteParameter("bookId", bookId));
                        genreCmd.Parameters.Add(new SqliteParameter("genreId", genreId));
                        await genreCmd.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
                return Ok(new { Message = "کتاب پیش‌نویس با موفقیت ایجاد شد.", BookId = bookId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در هنگام ایجاد کتاب رخ داد.", Error = ex.Message });
            }
        }

        [HttpPut("book/{bookId}")]
        public async Task<IActionResult> UpdateBook(long bookId, [FromBody] BookRequest request)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { Message = "عنوان کتاب الزامی است." });
            }

            var checkSql = "SELECT COUNT(*) FROM BOOKS WHERE ID = :bookId AND AUTHOR_ID = :authorId";
            var ownsBook = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("bookId", bookId),
                new SqliteParameter("authorId", CurrentUserId)
            }));

            if (ownsBook == 0) return Forbid();

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var updateSql = @"
                    UPDATE BOOKS
                    SET TITLE = :title,
                        DESCRIPTION = :desc,
                        COVER_IMAGE = :cover,
                        STATUS = :status,
                        UPDATED_AT = CURRENT_TIMESTAMP
                    WHERE ID = :bookId";

                using var cmd = new SqliteCommand(updateSql, connection);
                cmd.Transaction = transaction;
                cmd.Parameters.Add(new SqliteParameter("title", request.Title));
                cmd.Parameters.Add(new SqliteParameter("desc", SqliteDbHelper.ToDbValue(request.Description)));
                cmd.Parameters.Add(new SqliteParameter("cover", SqliteDbHelper.ToDbValue(request.CoverImage)));
                cmd.Parameters.Add(new SqliteParameter("status", request.Status));
                cmd.Parameters.Add(new SqliteParameter("bookId", bookId));
                await cmd.ExecuteNonQueryAsync();

                using var deleteGenresCmd = new SqliteCommand("DELETE FROM BOOK_GENRES WHERE BOOK_ID = :bookId", connection);
                deleteGenresCmd.Transaction = transaction;
                deleteGenresCmd.Parameters.Add(new SqliteParameter("bookId", bookId));
                await deleteGenresCmd.ExecuteNonQueryAsync();

                if (request.GenreIds != null && request.GenreIds.Count > 0)
                {
                    foreach (var genreId in request.GenreIds)
                    {
                        using var genreCmd = new SqliteCommand("INSERT INTO BOOK_GENRES (BOOK_ID, GENRE_ID) VALUES (:bookId, :genreId)", connection);
                        genreCmd.Transaction = transaction;
                        genreCmd.Parameters.Add(new SqliteParameter("bookId", bookId));
                        genreCmd.Parameters.Add(new SqliteParameter("genreId", genreId));
                        await genreCmd.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
                return Ok(new { Message = "کتاب با موفقیت بروزرسانی شد." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در هنگام بروزرسانی کتاب رخ داد.", Error = ex.Message });
            }
        }

        [HttpGet("book/{bookId}/chapters")]
        public async Task<IActionResult> GetBookChapters(long bookId)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            // Verify author owns book
            var checkSql = "SELECT COUNT(*) FROM BOOKS WHERE ID = :bookId AND AUTHOR_ID = :authorId";
            var ownsBook = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("bookId", bookId),
                new SqliteParameter("authorId", CurrentUserId)
            }));

            if (ownsBook == 0) return Forbid();

            var sql = @"
                SELECT ID, BOOK_ID, TITLE, CONTENT, SEQUENCE_NUMBER, PRICE, IS_FREE, STATUS, CREATED_AT 
                FROM CHAPTERS 
                WHERE BOOK_ID = :bookId 
                ORDER BY SEQUENCE_NUMBER ASC";

            var chapters = await _db.QueryAsync(sql, new[] { new SqliteParameter("bookId", bookId) }, reader => new ChapterResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                Title = reader["TITLE"].ToString()!,
                SequenceNumber = Convert.ToInt32(reader["SEQUENCE_NUMBER"]),
                Price = Convert.ToDecimal(reader["PRICE"]),
                IsFree = Convert.ToInt32(reader["IS_FREE"]) == 1,
                Status = reader["STATUS"].ToString()!,
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]),
                IsPurchased = true, // Author always has access to their own chapters
                Content = reader["CONTENT"] == DBNull.Value ? null : reader["CONTENT"].ToString()
            });

            return Ok(chapters);
        }

        [HttpPost("book/{bookId}/chapter")]
        public async Task<IActionResult> CreateChapter(long bookId, [FromBody] ChapterRequest request)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { Message = "عنوان و محتوای فصل الزامی است." });
            }

            // Verify author owns book
            var checkSql = "SELECT COUNT(*) FROM BOOKS WHERE ID = :bookId AND AUTHOR_ID = :authorId";
            var ownsBook = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("bookId", bookId),
                new SqliteParameter("authorId", CurrentUserId)
            }));

            if (ownsBook == 0) return Forbid();

            // Check sequence number duplicate
            var checkSeqSql = "SELECT COUNT(*) FROM CHAPTERS WHERE BOOK_ID = :bookId AND SEQUENCE_NUMBER = :seq";
            var seqExists = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSeqSql, new[]
            {
                new SqliteParameter("bookId", bookId),
                new SqliteParameter("seq", request.SequenceNumber)
            }));

            if (seqExists > 0)
            {
                return Conflict(new { Message = "فصلی با این شماره ترتیب قبلاً ایجاد شده است." });
            }

            var insertSql = @"
                INSERT INTO CHAPTERS (BOOK_ID, TITLE, CONTENT, SEQUENCE_NUMBER, PRICE, IS_FREE, STATUS) 
                VALUES (:bookId, :title, :content, :seq, :price, :isFree, :status)";

            var parameters = new[]
            {
                new SqliteParameter("bookId", bookId),
                new SqliteParameter("title", request.Title),
                new SqliteParameter("content", request.Content), // CLOB maps automatically
                new SqliteParameter("seq", request.SequenceNumber),
                new SqliteParameter("price", request.Price),
                new SqliteParameter("isFree", request.IsFree ? 1 : 0),
                new SqliteParameter("status", request.Status)
            };

            await _db.ExecuteNonQueryAsync(insertSql, parameters);
            return Ok(new { Message = "فصل با موفقیت ایجاد شد." });
        }

        [HttpPut("chapter/{chapterId}")]
        public async Task<IActionResult> UpdateChapter(long chapterId, [FromBody] ChapterRequest request)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            // Verify author owns book of this chapter
            var checkSql = @"
                SELECT COUNT(*) 
                FROM CHAPTERS c 
                JOIN BOOKS b ON c.BOOK_ID = b.ID 
                WHERE c.ID = :chapterId AND b.AUTHOR_ID = :authorId";

            var ownsChapter = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("chapterId", chapterId),
                new SqliteParameter("authorId", CurrentUserId)
            }));

            if (ownsChapter == 0) return Forbid();

            var updateSql = @"
                UPDATE CHAPTERS 
                SET TITLE = :title, CONTENT = :content, SEQUENCE_NUMBER = :seq, PRICE = :price, IS_FREE = :isFree, STATUS = :status, UPDATED_AT = CURRENT_TIMESTAMP 
                WHERE ID = :chapterId";

            var parameters = new[]
            {
                new SqliteParameter("title", request.Title),
                new SqliteParameter("content", request.Content),
                new SqliteParameter("seq", request.SequenceNumber),
                new SqliteParameter("price", request.Price),
                new SqliteParameter("isFree", request.IsFree ? 1 : 0),
                new SqliteParameter("status", request.Status),
                new SqliteParameter("chapterId", chapterId)
            };

            await _db.ExecuteNonQueryAsync(updateSql, parameters);
            return Ok(new { Message = "فصل با موفقیت بروزرسانی شد." });
        }

        [HttpDelete("chapter/{chapterId}")]
        public async Task<IActionResult> DeleteChapter(long chapterId)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            var checkSql = @"
                SELECT COUNT(*)
                FROM CHAPTERS c
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                WHERE c.ID = :chapterId AND b.AUTHOR_ID = :authorId";

            var ownsChapter = Convert.ToInt32(await _db.ExecuteScalarAsync(checkSql, new[]
            {
                new SqliteParameter("chapterId", chapterId),
                new SqliteParameter("authorId", CurrentUserId)
            }));

            if (ownsChapter == 0) return Forbid();

            await _db.ExecuteNonQueryAsync("DELETE FROM CHAPTERS WHERE ID = :chapterId", new[]
            {
                new SqliteParameter("chapterId", chapterId)
            });

            return Ok(new { Message = "فصل با موفقیت حذف شد." });
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetAuthorStats()
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            // Unified high-performance stats query
            var sql = @"
                SELECT 
                    (SELECT COUNT(*) FROM BOOKS WHERE AUTHOR_ID = :authorId) AS TOTAL_BOOKS,
                    (SELECT COUNT(*) FROM CHAPTERS c JOIN BOOKS b ON c.BOOK_ID = b.ID WHERE b.AUTHOR_ID = :authorId2) AS TOTAL_CHAPTERS,
                    COALESCE((SELECT SUM(p.PRICE_PAID) FROM PURCHASES p JOIN CHAPTERS c ON p.CHAPTER_ID = c.ID JOIN BOOKS b ON c.BOOK_ID = b.ID WHERE b.AUTHOR_ID = :authorId3), 0.0) AS TOTAL_REVENUE,
                    (SELECT COUNT(*) FROM PURCHASES p JOIN CHAPTERS c ON p.CHAPTER_ID = c.ID JOIN BOOKS b ON c.BOOK_ID = b.ID WHERE b.AUTHOR_ID = :authorId4) AS TOTAL_PURCHASES,
                    (SELECT COUNT(*) FROM LIKES l JOIN BOOKS b ON l.BOOK_ID = b.ID WHERE b.AUTHOR_ID = :authorId5) AS TOTAL_LIKES,
                    COALESCE((SELECT AVG(r.RATING) FROM REVIEWS r JOIN BOOKS b ON r.BOOK_ID = b.ID WHERE b.AUTHOR_ID = :authorId6), 0.0) AS AVG_RATING
                ";

            var parameters = new[]
            {
                new SqliteParameter("authorId", CurrentUserId),
                new SqliteParameter("authorId2", CurrentUserId),
                new SqliteParameter("authorId3", CurrentUserId),
                new SqliteParameter("authorId4", CurrentUserId),
                new SqliteParameter("authorId5", CurrentUserId),
                new SqliteParameter("authorId6", CurrentUserId)
            };

            var stats = await _db.QuerySingleOrDefaultAsync(sql, parameters, reader => new AuthorStatsResponse
            {
                TotalBooks = Convert.ToInt32(reader["TOTAL_BOOKS"]),
                TotalChapters = Convert.ToInt32(reader["TOTAL_CHAPTERS"]),
                TotalCoinsEarned = Convert.ToDecimal(reader["TOTAL_REVENUE"]),
                TotalPurchases = Convert.ToInt32(reader["TOTAL_PURCHASES"]),
                TotalLikes = Convert.ToInt32(reader["TOTAL_LIKES"]),
                AverageRating = Convert.ToDouble(reader["AVG_RATING"])
            });

            return Ok(stats);
        }

        [HttpPost("asset")]
        public async Task<IActionResult> CreateAsset([FromBody] WriterStudioAssetRequest request)
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            var sql = @"
                INSERT INTO WRITER_STUDIO_ASSETS (AUTHOR_ID, FILE_NAME, FILE_PATH, FILE_TYPE, FILE_SIZE) 
                VALUES (:authorId, :name, :path, :type, :size)";

            var parameters = new[]
            {
                new SqliteParameter("authorId", CurrentUserId),
                new SqliteParameter("name", request.FileName),
                new SqliteParameter("path", request.FilePath),
                new SqliteParameter("type", request.FileType),
                new SqliteParameter("size", request.FileSize)
            };

            await _db.ExecuteNonQueryAsync(sql, parameters);
            return Ok(new { Message = "فایل پیش‌نویس/منبع با موفقیت در استودیو ثبت شد." });
        }

        [HttpGet("assets")]
        public async Task<IActionResult> GetAssets()
        {
            var forbidResult = CheckAuthorRole();
            if (forbidResult != null) return forbidResult;

            var sql = "SELECT ID, AUTHOR_ID, FILE_NAME, FILE_PATH, FILE_TYPE, FILE_SIZE, CREATED_AT FROM WRITER_STUDIO_ASSETS WHERE AUTHOR_ID = :authorId ORDER BY CREATED_AT DESC";
            var assets = await _db.QueryAsync(sql, new[] { new SqliteParameter("authorId", CurrentUserId) }, reader => new WriterStudioAssetResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                AuthorId = Convert.ToInt64(reader["AUTHOR_ID"]),
                FileName = reader["FILE_NAME"].ToString()!,
                FilePath = reader["FILE_PATH"].ToString()!,
                FileType = reader["FILE_TYPE"].ToString()!,
                FileSize = Convert.ToInt64(reader["FILE_SIZE"]),
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
            });

            return Ok(assets);
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
            IsLiked = false
        };
    }
}
