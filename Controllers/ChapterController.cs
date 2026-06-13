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
    [ApiController]
    [Route("api/[controller]")]
    public class ChapterController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public ChapterController(SqliteDbHelper db, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _connectionProvider = connectionProvider;
        }

        [HttpGet("book/{bookId}")]
        public async Task<IActionResult> GetBookChapters(long bookId)
        {
            // Resolve current user ID if logged in (for checking purchase status)
            long userId = 0;
            if (User.Identity?.IsAuthenticated == true)
            {
                long.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out userId);
            }

            var sql = @"
                SELECT c.ID, c.BOOK_ID, c.TITLE, c.SEQUENCE_NUMBER, c.PRICE, c.IS_FREE, c.STATUS, c.CREATED_AT,
                       CASE WHEN c.IS_FREE = 1 THEN 1
                            WHEN b.AUTHOR_ID = :userId THEN 1
                            WHEN EXISTS (SELECT 1 FROM PURCHASES p WHERE p.USER_ID = :userId2 AND p.CHAPTER_ID = c.ID) THEN 1
                            WHEN EXISTS (SELECT 1 FROM USER_SUBSCRIPTIONS us WHERE us.USER_ID = :userId3 AND us.STATUS = 'Active' AND us.END_DATE > CURRENT_TIMESTAMP) THEN 1
                            ELSE 0 END AS IS_PURCHASED
                FROM CHAPTERS c
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                WHERE c.BOOK_ID = :bookId AND (c.STATUS = 'Published' OR b.AUTHOR_ID = :userId4)
                ORDER BY c.SEQUENCE_NUMBER ASC";

            var parameters = new[]
            {
                new SqliteParameter("userId", userId),
                new SqliteParameter("userId2", userId),
                new SqliteParameter("userId3", userId),
                new SqliteParameter("userId4", userId),
                new SqliteParameter("bookId", bookId)
            };

            var chapters = await _db.QueryAsync(sql, parameters, reader => new ChapterResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                BookId = Convert.ToInt64(reader["BOOK_ID"]),
                Title = reader["TITLE"].ToString()!,
                SequenceNumber = Convert.ToInt32(reader["SEQUENCE_NUMBER"]),
                Price = Convert.ToDecimal(reader["PRICE"]),
                IsFree = Convert.ToInt32(reader["IS_FREE"]) == 1,
                Status = reader["STATUS"].ToString()!,
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]),
                IsPurchased = Convert.ToInt32(reader["IS_PURCHASED"]) == 1,
                Content = null // Do not return content in list
            });

            return Ok(chapters);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetChapter(long id)
        {
            // Verify chapter and book detail
            var sql = @"
                SELECT c.ID, c.BOOK_ID, c.TITLE, c.CONTENT, c.SEQUENCE_NUMBER, c.PRICE, c.IS_FREE, c.STATUS, c.CREATED_AT, b.AUTHOR_ID, b.TITLE AS BOOK_TITLE
                FROM CHAPTERS c
                JOIN BOOKS b ON c.BOOK_ID = b.ID
                WHERE c.ID = :id";

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.Add(new SqliteParameter("id", id));

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return NotFound(new { Message = "فصل مورد نظر یافت نشد." });
            }

            var authorId = Convert.ToInt64(reader["AUTHOR_ID"]);
            var isFree = Convert.ToInt32(reader["IS_FREE"]) == 1;
            var status = reader["STATUS"].ToString()!;
            var title = reader["TITLE"].ToString()!;
            var bookId = Convert.ToInt64(reader["BOOK_ID"]);
            var seq = Convert.ToInt32(reader["SEQUENCE_NUMBER"]);
            var price = Convert.ToDecimal(reader["PRICE"]);
            var created = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"]);
            
            // Read CLOB content in Oracle
            var contentOracle = reader["CONTENT"];
            var contentStr = contentOracle == DBNull.Value ? string.Empty : contentOracle.ToString();

            if (status != "Published")
            {
                // Verify if current user is the author
                if (User.Identity?.IsAuthenticated != true) return Forbid();
                long.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var curUser);
                if (curUser != authorId)
                {
                    return StatusCode(403, new { Message = "این فصل هنوز منتشر نشده است." });
                }
            }

            var response = new ChapterResponse
            {
                Id = id,
                BookId = bookId,
                Title = title,
                SequenceNumber = seq,
                Price = price,
                IsFree = isFree,
                Status = status,
                CreatedAt = created
            };

            // Check if user has access to content
            bool hasAccess = false;
            if (isFree)
            {
                hasAccess = true;
            }
            else
            {
                if (User.Identity?.IsAuthenticated == true)
                {
                    long.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var currentUserId);
                    if (currentUserId == authorId)
                    {
                        hasAccess = true;
                    }
                    else
                    {
                        // Check if purchased
                        var checkPurchaseSql = "SELECT COUNT(*) FROM PURCHASES WHERE USER_ID = :userId AND CHAPTER_ID = :chapterId";
                        using var pCmd = new SqliteCommand(checkPurchaseSql, connection);
                        pCmd.Parameters.Add(new SqliteParameter("userId", currentUserId));
                        pCmd.Parameters.Add(new SqliteParameter("chapterId", id));
                        var purchased = Convert.ToInt32(await pCmd.ExecuteScalarAsync());

                        if (purchased > 0)
                        {
                            hasAccess = true;
                        }
                        else
                        {
                            // Check active subscription
                            var checkSubSql = "SELECT COUNT(*) FROM USER_SUBSCRIPTIONS WHERE USER_ID = :userId AND STATUS = 'Active' AND END_DATE > CURRENT_TIMESTAMP";
                            using var sCmd = new SqliteCommand(checkSubSql, connection);
                            sCmd.Parameters.Add(new SqliteParameter("userId", currentUserId));
                            var subActive = Convert.ToInt32(await sCmd.ExecuteScalarAsync());

                            if (subActive > 0)
                            {
                                hasAccess = true;
                            }
                        }
                    }
                }
            }

            if (hasAccess)
            {
                response.IsPurchased = true;
                response.Content = contentStr;
                return Ok(response);
            }
            else
            {
                response.IsPurchased = false;
                response.Content = null;
                return StatusCode(402, new
                {
                    Message = "برای مطالعه این فصل ابتدا باید آن را خریداری کنید یا اشتراک فعال تهیه فرمایید.",
                    ChapterDetails = response
                });
            }
        }

        [Authorize]
        [HttpPost("{id}/purchase")]
        public async Task<IActionResult> PurchaseChapter(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1. Fetch Chapter and Author info
                var getChapterSql = @"
                    SELECT c.ID, c.PRICE, c.IS_FREE, c.TITLE, b.AUTHOR_ID, b.TITLE AS BOOK_TITLE
                    FROM CHAPTERS c
                    JOIN BOOKS b ON c.BOOK_ID = b.ID
                    WHERE c.ID = :id";
                
                using var chapterCmd = new SqliteCommand(getChapterSql, connection);
                chapterCmd.Transaction = transaction;
                chapterCmd.Parameters.Add(new SqliteParameter("id", id));

                using var reader = await chapterCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return NotFound(new { Message = "فصل مورد نظر یافت نشد." });
                }

                var price = Convert.ToDecimal(reader["PRICE"]);
                var isFree = Convert.ToInt32(reader["IS_FREE"]) == 1;
                var chapterTitle = reader["TITLE"].ToString()!;
                var bookTitle = reader["BOOK_TITLE"].ToString()!;
                var authorId = Convert.ToInt64(reader["AUTHOR_ID"]);

                await reader.CloseAsync();

                if (isFree || price <= 0)
                {
                    return BadRequest(new { Message = "این فصل رایگان است و نیازی به خرید ندارد." });
                }

                if (userId == authorId)
                {
                    return BadRequest(new { Message = "شما نویسنده این کتاب هستید و نیازی به خرید ندارید." });
                }

                // 2. Check if already purchased
                var checkPurchaseSql = "SELECT COUNT(*) FROM PURCHASES WHERE USER_ID = :userId AND CHAPTER_ID = :chapterId";
                using var checkCmd = new SqliteCommand(checkPurchaseSql, connection);
                checkCmd.Transaction = transaction;
                checkCmd.Parameters.Add(new SqliteParameter("userId", userId));
                checkCmd.Parameters.Add(new SqliteParameter("chapterId", id));
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                if (exists > 0)
                {
                    return BadRequest(new { Message = "شما قبلاً این فصل را خریداری کرده‌اید." });
                }

                // 3. Check reader's Wallet Balance
                var getWalletSql = "SELECT BALANCE FROM WALLETS WHERE USER_ID = :userId"; // Lock row
                using var walletCmd = new SqliteCommand(getWalletSql, connection);
                walletCmd.Transaction = transaction;
                walletCmd.Parameters.Add(new SqliteParameter("userId", userId));
                
                var balanceObj = await walletCmd.ExecuteScalarAsync();
                if (balanceObj == null)
                {
                    return NotFound(new { Message = "کیف پول کاربر یافت نشد." });
                }
                var balance = Convert.ToDecimal(balanceObj);

                if (balance < price)
                {
                    return BadRequest(new { Message = "موجودی کیف پول شما کافی نیست. لطفا سکه تهیه کنید." });
                }

                // 4. Deduct balance from Reader's Wallet
                var deductWalletSql = "UPDATE WALLETS SET BALANCE = BALANCE - :price, UPDATED_AT = CURRENT_TIMESTAMP WHERE USER_ID = :userId";
                using var deductCmd = new SqliteCommand(deductWalletSql, connection);
                deductCmd.Transaction = transaction;
                deductCmd.Parameters.Add(new SqliteParameter("price", price));
                deductCmd.Parameters.Add(new SqliteParameter("userId", userId));
                await deductCmd.ExecuteNonQueryAsync();

                // 5. Add Transaction Log for Reader
                var insertTxReaderSql = @"
                    INSERT INTO TRANSACTIONS (USER_ID, AMOUNT, TYPE, DESCRIPTION) 
                    VALUES (:userId, :amount, 'Purchase', :desc)";
                using var txReaderCmd = new SqliteCommand(insertTxReaderSql, connection);
                txReaderCmd.Transaction = transaction;
                txReaderCmd.Parameters.Add(new SqliteParameter("userId", userId));
                txReaderCmd.Parameters.Add(new SqliteParameter("amount", -price));
                txReaderCmd.Parameters.Add(new SqliteParameter("desc", $"خرید فصل «{chapterTitle}» از کتاب «{bookTitle}»"));
                await txReaderCmd.ExecuteNonQueryAsync();

                // 6. Record Purchase
                var insertPurchaseSql = @"
                    INSERT INTO PURCHASES (USER_ID, CHAPTER_ID, PRICE_PAID) 
                    VALUES (:userId, :chapterId, :price)";
                using var purchaseCmd = new SqliteCommand(insertPurchaseSql, connection);
                purchaseCmd.Transaction = transaction;
                purchaseCmd.Parameters.Add(new SqliteParameter("userId", userId));
                purchaseCmd.Parameters.Add(new SqliteParameter("chapterId", id));
                purchaseCmd.Parameters.Add(new SqliteParameter("price", price));
                await purchaseCmd.ExecuteNonQueryAsync();

                // 7. Add Revenue to Author's Wallet
                var addAuthorWalletSql = "UPDATE WALLETS SET BALANCE = BALANCE + :price, UPDATED_AT = CURRENT_TIMESTAMP WHERE USER_ID = :authorId";
                using var addAuthorCmd = new SqliteCommand(addAuthorWalletSql, connection);
                addAuthorCmd.Transaction = transaction;
                addAuthorCmd.Parameters.Add(new SqliteParameter("price", price));
                addAuthorCmd.Parameters.Add(new SqliteParameter("authorId", authorId));
                await addAuthorCmd.ExecuteNonQueryAsync();

                // 8. Add Transaction Log for Author
                var insertTxAuthorSql = @"
                    INSERT INTO TRANSACTIONS (USER_ID, AMOUNT, TYPE, DESCRIPTION) 
                    VALUES (:authorId, :amount, 'Revenue', :desc)";
                using var txAuthorCmd = new SqliteCommand(insertTxAuthorSql, connection);
                txAuthorCmd.Transaction = transaction;
                txAuthorCmd.Parameters.Add(new SqliteParameter("authorId", authorId));
                txAuthorCmd.Parameters.Add(new SqliteParameter("amount", price));
                txAuthorCmd.Parameters.Add(new SqliteParameter("desc", $"فروش فصل «{chapterTitle}» از کتاب «{bookTitle}»"));
                await txAuthorCmd.ExecuteNonQueryAsync();

                transaction.Commit();

                // Notify buyer
                await NotificationHelper.SendAsync(_db, userId,
                    "📖 خرید فصل موفق",
                    $"فصل «{chapterTitle}» از کتاب «{bookTitle}» با موفقیت خریداری شد و اکنون در دسترس شماست.");

                // Notify author about sale
                await NotificationHelper.SendAsync(_db, authorId,
                    "💰 فروش جدید",
                    $"فصل «{chapterTitle}» از کتاب «{bookTitle}» توسط یک خواننده خریداری شد. {price:N0} سکه به کیف پول شما اضافه گردید.");

                return Ok(new { Message = "خرید فصل با موفقیت انجام شد." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در هنگام خرید فصل رخ داد.", Error = ex.Message });
            }
        }
    }
}
