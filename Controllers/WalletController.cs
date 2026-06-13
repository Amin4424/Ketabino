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
    public class WalletController : ControllerBase
    {
        private readonly SqliteDbHelper _db;
        private readonly DatabaseConnectionProvider _connectionProvider;

        public WalletController(SqliteDbHelper db, DatabaseConnectionProvider connectionProvider)
        {
            _db = db;
            _connectionProvider = connectionProvider;
        }

        [HttpGet]
        public async Task<IActionResult> GetBalance()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = "SELECT USER_ID, BALANCE, UPDATED_AT FROM WALLETS WHERE USER_ID = :userId";
            var wallet = await _db.QuerySingleOrDefaultAsync(sql, new[] { new SqliteParameter("userId", userId) }, reader => new WalletResponse
            {
                UserId = Convert.ToInt64(reader["USER_ID"]),
                Balance = Convert.ToDecimal(reader["BALANCE"]),
                UpdatedAt = SqliteDbHelper.GetUtcDateTime(reader["UPDATED_AT"])
            });

            if (wallet == null)
            {
                return NotFound(new { Message = "کیف پول یافت نشد." });
            }

            return Ok(wallet);
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions()
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            var sql = @"
                SELECT ID, USER_ID, AMOUNT, TYPE, DESCRIPTION, CREATED_AT 
                FROM TRANSACTIONS 
                WHERE USER_ID = :userId 
                ORDER BY CREATED_AT DESC";

            var transactions = await _db.QueryAsync(sql, new[] { new SqliteParameter("userId", userId) }, reader => new TransactionResponse
            {
                Id = Convert.ToInt64(reader["ID"]),
                UserId = Convert.ToInt64(reader["USER_ID"]),
                Amount = Convert.ToDecimal(reader["AMOUNT"]),
                Type = reader["TYPE"].ToString()!,
                Description = reader["DESCRIPTION"] == DBNull.Value ? null : reader["DESCRIPTION"].ToString(),
                CreatedAt = SqliteDbHelper.GetUtcDateTime(reader["CREATED_AT"])
            });

            return Ok(transactions);
        }

        [HttpGet("packages")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCoinPackages()
        {
            var sql = "SELECT ID, NAME, COINS, PRICE, IS_ACTIVE FROM COIN_PACKAGES WHERE IS_ACTIVE = 1 ORDER BY PRICE ASC";
            var packages = await _db.QueryAsync(sql, null, reader => new CoinPackageResponse
            {
                Id = Convert.ToInt32(reader["ID"]),
                Name = reader["NAME"].ToString()!,
                Coins = Convert.ToInt32(reader["COINS"]),
                Price = Convert.ToDecimal(reader["PRICE"]),
                IsActive = Convert.ToInt32(reader["IS_ACTIVE"]) == 1
            });

            return Ok(packages);
        }

        [HttpPost("buy-coins")]
        public async Task<IActionResult> BuyCoins([FromBody] BuyCoinsRequest request)
        {
            long userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

            using var connection = await _connectionProvider.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Fetch coin package details
                var getPackageSql = "SELECT ID, NAME, COINS, PRICE, IS_ACTIVE FROM COIN_PACKAGES WHERE ID = :id AND IS_ACTIVE = 1";
                using var packageCmd = new SqliteCommand(getPackageSql, connection);
                packageCmd.Transaction = transaction;
                packageCmd.Parameters.Add(new SqliteParameter("id", request.CoinPackageId));

                using var reader = await packageCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return NotFound(new { Message = "بسته سکه مورد نظر یافت نشد یا غیرفعال است." });
                }

                var coins = Convert.ToInt32(reader["COINS"]);
                var packageName = reader["NAME"].ToString()!;
                var price = Convert.ToDecimal(reader["PRICE"]);

                await reader.CloseAsync();

                // Increase Wallet Balance
                var updateWalletSql = "UPDATE WALLETS SET BALANCE = BALANCE + :coins, UPDATED_AT = CURRENT_TIMESTAMP WHERE USER_ID = :userId";
                using var walletCmd = new SqliteCommand(updateWalletSql, connection);
                walletCmd.Transaction = transaction;
                walletCmd.Parameters.Add(new SqliteParameter("coins", coins));
                walletCmd.Parameters.Add(new SqliteParameter("userId", userId));
                await walletCmd.ExecuteNonQueryAsync();

                // Log Transaction
                var insertTxSql = @"
                    INSERT INTO TRANSACTIONS (USER_ID, AMOUNT, TYPE, DESCRIPTION) 
                    VALUES (:userId, :amount, 'Deposit', :desc)";
                using var txCmd = new SqliteCommand(insertTxSql, connection);
                txCmd.Transaction = transaction;
                txCmd.Parameters.Add(new SqliteParameter("userId", userId));
                txCmd.Parameters.Add(new SqliteParameter("amount", (decimal)coins));
                txCmd.Parameters.Add(new SqliteParameter("desc", $"خرید بسته سکه «{packageName}» به مبلغ {price:N0} ریال"));
                await txCmd.ExecuteNonQueryAsync();

                transaction.Commit();

                await NotificationHelper.SendAsync(_db, userId,
                    "🪙 خرید موفق",
                    $"خرید «{packageName}» با موفقیت انجام شد. {coins:N0} سکه به کیف پول شما اضافه گردید.");

                return Ok(new { Message = "خرید سکه با موفقیت انجام شد و کیف پول شارژ گردید.", NewCoinsAdded = coins });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "خطایی در شارژ کیف پول رخ داد.", Error = ex.Message });
            }
        }
    }
}
