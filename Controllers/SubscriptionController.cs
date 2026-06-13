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
    public class SubscriptionController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public SubscriptionController(SqliteDbHelper db, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _connectionProvider = connectionProvider;
        }

        [HttpGet("plans")]
        public async Task<IActionResult> GetPlans()
        {
            var sql = "SELECT ID, NAME, PRICE, DURATION_DAYS, DESCRIPTION, IS_ACTIVE FROM SUBSCRIPTION_PLANS WHERE IS_ACTIVE = 1 ORDER BY PRICE ASC";
            var plans = await _db.QueryAsync(sql, null, reader => new SubscriptionPlanResponse
            {
                Id = Convert.ToInt32(reader["ID"]),
                Name = reader["NAME"].ToString()!,
                Price = Convert.ToDecimal(reader["PRICE"]),
                DurationDays = Convert.ToInt32(reader["DURATION_DAYS"]),
                Description = reader["DESCRIPTION"] == DBNull.Value ? null : reader["DESCRIPTION"].ToString(),
                IsActive = Convert.ToInt32(reader["IS_ACTIVE"]) == 1
            });

            return Ok(plans);
        }

        [Authorize]
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveSubscription()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT us.ID, us.USER_ID, us.PLAN_ID, sp.NAME AS PLAN_NAME, us.START_DATE, us.END_DATE, us.STATUS 
                FROM USER_SUBSCRIPTIONS us
                JOIN SUBSCRIPTION_PLANS sp ON us.PLAN_ID = sp.ID
                WHERE us.USER_ID = :userId AND us.STATUS = 'Active' AND us.END_DATE > CURRENT_TIMESTAMP";

            var subscription = await _db.QuerySingleOrDefaultAsync(sql, new[] { new SqliteParameter("userId", userId) }, reader => new UserSubscriptionResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                PlanId = Convert.ToInt32(reader["PLAN_ID"]),
                PlanName = reader["PLAN_NAME"].ToString()!,
                StartDate = SqliteDbHelper.GetUtcDateTime(reader["START_DATE"]),
                EndDate = SqliteDbHelper.GetUtcDateTime(reader["END_DATE"]),
                Status = reader["STATUS"].ToString()!
            });

            if (subscription == null)
            {
                return Ok(new { Message = "اشتراک فعالی یافت نشد." });
            }

            return Ok(subscription);
        }

        [Authorize]
        [HttpPost("purchase")]
        public async Task<IActionResult> PurchaseSubscription([FromBody] PurchaseSubscriptionRequest request)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1. Fetch Subscription Plan Details
                var getPlanSql = "SELECT ID, NAME, PRICE, DURATION_DAYS, IS_ACTIVE FROM SUBSCRIPTION_PLANS WHERE ID = :planId AND IS_ACTIVE = 1";
                using var planCmd = new SqliteCommand(getPlanSql, connection);
                planCmd.Transaction = transaction;
                planCmd.Parameters.Add(new SqliteParameter("planId", request.PlanId));

                using var reader = await planCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return NotFound(new { Message = "طرح اشتراک مورد نظر یافت نشد یا غیرفعال است." });
                }

                var price = Convert.ToDecimal(reader["PRICE"]);
                var planName = reader["NAME"].ToString()!;
                var duration = Convert.ToInt32(reader["DURATION_DAYS"]);

                await reader.CloseAsync();

                // 2. Check if user already has an active subscription
                var checkActiveSql = "SELECT COUNT(*) FROM USER_SUBSCRIPTIONS WHERE USER_ID = :userId AND STATUS = 'Active' AND END_DATE > CURRENT_TIMESTAMP";
                using var activeCmd = new SqliteCommand(checkActiveSql, connection);
                activeCmd.Transaction = transaction;
                activeCmd.Parameters.Add(new SqliteParameter("userId", userId));
                var activeCount = Convert.ToInt32(await activeCmd.ExecuteScalarAsync());

                if (activeCount > 0)
                {
                    return BadRequest(new { Message = "شما در حال حاضر یک اشتراک فعال دارید و نمی‌توانید اشتراک جدیدی همزمان خریداری کنید." });
                }

                // 3. Check Wallet Balance
                var getWalletSql = "SELECT BALANCE FROM WALLETS WHERE USER_ID = :userId";
                using var walletCmd = new SqliteCommand(getWalletSql, connection);
                walletCmd.Transaction = transaction;
                walletCmd.Parameters.Add(new SqliteParameter("userId", userId));
                var balance = Convert.ToDecimal(await walletCmd.ExecuteScalarAsync());

                if (balance < price)
                {
                    return BadRequest(new { Message = "موجودی کیف پول شما کافی نیست. لطفا سکه تهیه کنید." });
                }

                // 4. Deduct balance from Wallet
                var deductWalletSql = "UPDATE WALLETS SET BALANCE = BALANCE - :price, UPDATED_AT = CURRENT_TIMESTAMP WHERE USER_ID = :userId";
                using var deductCmd = new SqliteCommand(deductWalletSql, connection);
                deductCmd.Transaction = transaction;
                deductCmd.Parameters.Add(new SqliteParameter("price", price));
                deductCmd.Parameters.Add(new SqliteParameter("userId", userId));
                await deductCmd.ExecuteNonQueryAsync();

                // 5. Add Transaction Log
                var insertTxSql = @"
                    INSERT INTO TRANSACTIONS (USER_ID, AMOUNT, TYPE, DESCRIPTION) 
                    VALUES (:userId, :amount, 'Purchase', :desc)";
                using var txCmd = new SqliteCommand(insertTxSql, connection);
                txCmd.Transaction = transaction;
                txCmd.Parameters.Add(new SqliteParameter("userId", userId));
                txCmd.Parameters.Add(new SqliteParameter("amount", -price));
                txCmd.Parameters.Add(new SqliteParameter("desc", $"خرید طرح «{planName}»"));
                await txCmd.ExecuteNonQueryAsync();

                // 6. Create User Subscription record
                var endDate = DateTime.UtcNow.AddDays(duration);
                var insertSubSql = @"
                    INSERT INTO USER_SUBSCRIPTIONS (USER_ID, PLAN_ID, START_DATE, END_DATE, STATUS) 
                    VALUES (:userId, :planId, CURRENT_TIMESTAMP, :endDate, 'Active')";
                using var subCmd = new SqliteCommand(insertSubSql, connection);
                subCmd.Transaction = transaction;
                subCmd.Parameters.Add(new SqliteParameter("userId", userId));
                subCmd.Parameters.Add(new SqliteParameter("planId", request.PlanId));
                subCmd.Parameters.Add(new SqliteParameter("endDate", endDate));
                await subCmd.ExecuteNonQueryAsync();

                transaction.Commit();

                await NotificationHelper.SendAsync(_db, userId,
                    "⭐ اشتراک فعال شد!",
                    $"اشتراک «{planName}» با موفقیت خریداری شد. دسترسی شما تا {endDate:yyyy/MM/dd} فعال است.");

                return Ok(new { Message = "اشتراک با موفقیت خریداری شد.", ActiveUntil = endDate });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در هنگام خرید اشتراک رخ داد.", Error = ex.Message });
            }
        }
    }
}
