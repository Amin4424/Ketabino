using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Ketabino.Database;

namespace Ketabino.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChallengeController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public ChallengeController(SqliteDbHelper db, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _connectionProvider = connectionProvider;
        }

        [HttpGet]
        public async Task<IActionResult> GetChallenges()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT c.ID, c.TITLE, c.DESCRIPTION, c.TARGET_TYPE, c.TARGET_COUNT, c.COIN_REWARD, c.END_DATE,
                       IFNULL(uc.CURRENT_PROGRESS, 0) AS CURRENT_PROGRESS,
                       IFNULL(uc.IS_COMPLETED, 0) AS IS_COMPLETED,
                       uc.CLAIMED_AT,
                       CASE WHEN uc.USER_ID IS NOT NULL THEN 1 ELSE 0 END AS JOINED
                FROM CHALLENGES c
                LEFT JOIN USER_CHALLENGES uc ON c.ID = uc.CHALLENGE_ID AND uc.USER_ID = :userId
                WHERE c.IS_ACTIVE = 1
                ORDER BY c.END_DATE ASC";

            var challenges = await _db.QueryAsync(sql, new[] { new SqliteParameter("userId", userId) }, reader => new
            {
                Id = Convert.ToInt64(reader["ID"]),
                Title = reader["TITLE"].ToString()!,
                Description = reader["DESCRIPTION"].ToString()!,
                TargetType = reader["TARGET_TYPE"].ToString()!,
                TargetCount = Convert.ToInt32(reader["TARGET_COUNT"]),
                CoinReward = Convert.ToInt32(reader["COIN_REWARD"]),
                EndDate = SqliteDbHelper.GetUtcDateTime(reader["END_DATE"]),
                CurrentProgress = Convert.ToInt32(reader["CURRENT_PROGRESS"]),
                IsCompleted = Convert.ToInt32(reader["IS_COMPLETED"]) == 1,
                ClaimedAt = reader["CLAIMED_AT"] == DBNull.Value ? null : (DateTime?)SqliteDbHelper.GetUtcDateTime(reader["CLAIMED_AT"]),
                Joined = Convert.ToInt32(reader["JOINED"]) == 1
            });

            return Ok(challenges);
        }


        [HttpPost("{id}/join")]
        public async Task<IActionResult> JoinChallenge(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if challenge exists
            var checkChallengeSql = "SELECT COUNT(*) FROM CHALLENGES WHERE ID = :id AND IS_ACTIVE = 1";
            var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(checkChallengeSql, new[] { new SqliteParameter("id", id) }));
            if (exists == 0) return NotFound(new { Message = "چالش مورد نظر یافت نشد." });

            // Check if already joined
            var checkJoinedSql = "SELECT COUNT(*) FROM USER_CHALLENGES WHERE USER_ID = :userId AND CHALLENGE_ID = :challengeId";
            var joined = Convert.ToInt32(await _db.ExecuteScalarAsync(checkJoinedSql, new[] { 
                new SqliteParameter("userId", userId),
                new SqliteParameter("challengeId", id)
            }));

            if (joined > 0) return BadRequest(new { Message = "شما قبلاً در این چالش شرکت کرده‌اید." });

            // Join challenge
            var insertSql = "INSERT INTO USER_CHALLENGES (USER_ID, CHALLENGE_ID, CURRENT_PROGRESS, IS_COMPLETED) VALUES (:userId, :challengeId, 0, 0)";
            await _db.ExecuteNonQueryAsync(insertSql, new[] {
                new SqliteParameter("userId", userId),
                new SqliteParameter("challengeId", id)
            });

            return Ok(new { Message = "شما با موفقیت به این چالش پیوستید!" });
        }

        [HttpPost("{id}/progress")]
        public async Task<IActionResult> UpdateProgress(long id, [FromBody] int amount)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if joined
            var checkSql = "SELECT CURRENT_PROGRESS, IS_COMPLETED FROM USER_CHALLENGES WHERE USER_ID = :userId AND CHALLENGE_ID = :challengeId";
            var state = await _db.QuerySingleOrDefaultAsync(checkSql, new[] {
                new SqliteParameter("userId", userId),
                new SqliteParameter("challengeId", id)
            }, reader => new {
                CurrentProgress = Convert.ToInt32(reader["CURRENT_PROGRESS"]),
                IsCompleted = Convert.ToInt32(reader["IS_COMPLETED"]) == 1
            });

            if (state == null) return BadRequest(new { Message = "ابتدا باید به این چالش بپیوندید." });
            if (state.IsCompleted) return BadRequest(new { Message = "این چالش قبلاً به پایان رسیده است." });

            // Get target count
            var getTargetSql = "SELECT TARGET_COUNT FROM CHALLENGES WHERE ID = :id";
            var targetCount = Convert.ToInt32(await _db.ExecuteScalarAsync(getTargetSql, new[] { new SqliteParameter("id", id) }));

            int newProgress = state.CurrentProgress + amount;
            int isCompleted = newProgress >= targetCount ? 1 : 0;
            if (newProgress > targetCount) newProgress = targetCount;

            var updateSql = "UPDATE USER_CHALLENGES SET CURRENT_PROGRESS = :progress, IS_COMPLETED = :isCompleted WHERE USER_ID = :userId AND CHALLENGE_ID = :challengeId";
            await _db.ExecuteNonQueryAsync(updateSql, new[] {
                new SqliteParameter("progress", newProgress),
                new SqliteParameter("isCompleted", isCompleted),
                new SqliteParameter("userId", userId),
                new SqliteParameter("challengeId", id)
            });

            // If just completed, send a notification
            if (isCompleted == 1 && state.CurrentProgress < targetCount)
            {
                var notifSql = @"
                    INSERT INTO NOTIFICATIONS (USER_ID, TITLE, MESSAGE, IS_READ) 
                    VALUES (:userId, :title, :msg, 0)";
                await _db.ExecuteNonQueryAsync(notifSql, new[]
                {
                    new SqliteParameter("userId", userId),
                    new SqliteParameter("title", "🏆 چالش تکمیل شد!"),
                    new SqliteParameter("msg", $"تبریک! چالش را با موفقیت تکمیل کردید. برای دریافت جایزه به صفحه چالش‌ها بروید.")
                });
            }

            return Ok(new { Message = "پیشرفت چالش بروزرسانی شد.", Progress = newProgress, IsCompleted = isCompleted == 1 });
        }

        [HttpPost("{id}/done")]
        public async Task<IActionResult> MarkAsDone(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Check if joined
            var checkSql = "SELECT CURRENT_PROGRESS, IS_COMPLETED, TARGET_COUNT FROM USER_CHALLENGES uc JOIN CHALLENGES c ON uc.CHALLENGE_ID = c.ID WHERE uc.USER_ID = :userId AND uc.CHALLENGE_ID = :challengeId AND c.IS_ACTIVE = 1";
            var state = await _db.QuerySingleOrDefaultAsync(checkSql, new[] {
                new SqliteParameter("userId", userId),
                new SqliteParameter("challengeId", id)
            }, reader => new {
                CurrentProgress = Convert.ToInt32(reader["CURRENT_PROGRESS"]),
                IsCompleted = Convert.ToInt32(reader["IS_COMPLETED"]) == 1,
                TargetCount = Convert.ToInt32(reader["TARGET_COUNT"])
            });

            if (state == null) return BadRequest(new { Message = "ابتدا باید به این چالش بپیوندید." });
            if (state.IsCompleted) return BadRequest(new { Message = "این چالش قبلاً تکمیل شده است." });

            // Get challenge title and reward
            var getSql = "SELECT TITLE, COIN_REWARD FROM CHALLENGES WHERE ID = :id";
            var challenge = await _db.QuerySingleOrDefaultAsync(getSql, new[] { new SqliteParameter("id", id) }, reader => new
            {
                Title = reader["TITLE"].ToString()!,
                CoinReward = Convert.ToInt32(reader["COIN_REWARD"])
            });

            // Mark as completed
            var updateSql = "UPDATE USER_CHALLENGES SET CURRENT_PROGRESS = :progress, IS_COMPLETED = 1 WHERE USER_ID = :userId AND CHALLENGE_ID = :challengeId";
            await _db.ExecuteNonQueryAsync(updateSql, new[] {
                new SqliteParameter("progress", state.TargetCount),
                new SqliteParameter("userId", userId),
                new SqliteParameter("challengeId", id)
            });

            // Send notification about completing the challenge
            if (challenge != null)
            {
                var notifSql = @"
                    INSERT INTO NOTIFICATIONS (USER_ID, TITLE, MESSAGE, IS_READ) 
                    VALUES (:userId, :title, :msg, 0)";
                await _db.ExecuteNonQueryAsync(notifSql, new[]
                {
                    new SqliteParameter("userId", userId),
                    new SqliteParameter("title", "🏆 چالش تکمیل شد!"),
                    new SqliteParameter("msg", $"تبریک! چالش «{challenge.Title}» را با موفقیت تکمیل کردید. برای دریافت {challenge.CoinReward} سکه جایزه، روی «دریافت جایزه» کلیک کنید.")
                });
            }

            return Ok(new { Message = "چالش با موفقیت انجام شد! جایزه آماده دریافت است." });
        }

        [HttpPost("{id}/claim")]
        public async Task<IActionResult> ClaimReward(long id)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Get user challenge state
                var getChallengeStateSql = @"
                    SELECT uc.IS_COMPLETED, uc.CLAIMED_AT, c.COIN_REWARD, c.TITLE 
                    FROM USER_CHALLENGES uc
                    JOIN CHALLENGES c ON uc.CHALLENGE_ID = c.ID
                    WHERE uc.USER_ID = :userId AND uc.CHALLENGE_ID = :challengeId";

                using var stateCmd = new SqliteCommand(getChallengeStateSql, connection);
                stateCmd.Transaction = transaction;
                stateCmd.Parameters.Add(new SqliteParameter("userId", userId));
                stateCmd.Parameters.Add(new SqliteParameter("challengeId", id));

                using var reader = await stateCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return BadRequest(new { Message = "چالش مورد نظر یافت نشد یا شما هنوز عضو آن نشده‌اید." });
                }

                var isCompleted = Convert.ToInt32(reader["IS_COMPLETED"]) == 1;
                var claimedAt = reader["CLAIMED_AT"];
                var rewardCoins = Convert.ToInt32(reader["COIN_REWARD"]);
                var title = reader["TITLE"].ToString()!;

                await reader.CloseAsync();

                if (!isCompleted) return BadRequest(new { Message = "چالش هنوز تکمیل نشده است." });
                if (claimedAt != DBNull.Value) return BadRequest(new { Message = "شما قبلاً جایزه این چالش را دریافت کرده‌اید." });

                // Mark challenge as claimed
                var claimSql = "UPDATE USER_CHALLENGES SET CLAIMED_AT = CURRENT_TIMESTAMP WHERE USER_ID = :userId AND CHALLENGE_ID = :challengeId";
                using var claimCmd = new SqliteCommand(claimSql, connection);
                claimCmd.Transaction = transaction;
                claimCmd.Parameters.Add(new SqliteParameter("userId", userId));
                claimCmd.Parameters.Add(new SqliteParameter("challengeId", id));
                await claimCmd.ExecuteNonQueryAsync();

                // Increase Wallet Balance
                var walletSql = "UPDATE WALLETS SET BALANCE = BALANCE + :coins, UPDATED_AT = CURRENT_TIMESTAMP WHERE USER_ID = :userId";
                using var walletCmd = new SqliteCommand(walletSql, connection);
                walletCmd.Transaction = transaction;
                walletCmd.Parameters.Add(new SqliteParameter("coins", rewardCoins));
                walletCmd.Parameters.Add(new SqliteParameter("userId", userId));
                await walletCmd.ExecuteNonQueryAsync();

                // Log Transaction
                var txSql = "INSERT INTO TRANSACTIONS (USER_ID, AMOUNT, TYPE, DESCRIPTION) VALUES (:userId, :amount, 'Deposit', :desc)";
                using var txCmd = new SqliteCommand(txSql, connection);
                txCmd.Transaction = transaction;
                txCmd.Parameters.Add(new SqliteParameter("userId", userId));
                txCmd.Parameters.Add(new SqliteParameter("amount", (decimal)rewardCoins));
                txCmd.Parameters.Add(new SqliteParameter("desc", $"پاداش تکمیل چالش «{title}»"));
                await txCmd.ExecuteNonQueryAsync();

                transaction.Commit();
                return Ok(new { Message = "جایزه با موفقیت دریافت شد و سکه‌ها به کیف پول شما اضافه شدند!", Reward = rewardCoins });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در دریافت جایزه رخ داد.", Error = ex.Message });
            }
        }
    }
}
