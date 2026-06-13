using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Ketabino.Database;

namespace Ketabino.Services
{
    /// <summary>
    /// Central helper for inserting notification records silently (never throws).
    /// Call from any controller after a successful action.
    /// </summary>
    public static class NotificationHelper
    {
        public static async Task SendAsync(SqliteDbHelper db, long userId, string title, string message)
        {
            try
            {
                var sql = @"
                    INSERT INTO NOTIFICATIONS (USER_ID, TITLE, MESSAGE, IS_READ) 
                    VALUES (:userId, :title, :msg, 0)";

                await db.ExecuteNonQueryAsync(sql, new[]
                {
                    new SqliteParameter("userId", userId),
                    new SqliteParameter("title", title),
                    new SqliteParameter("msg", message)
                });
            }
            catch { /* silent — notification failure must never break the main action */ }
        }
    }
}
