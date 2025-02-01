using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using TaskScheduler.Models;

namespace TaskScheduler.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<List<TaskInfo>> GetActiveTasksAsync()
    {
        var tasks = new List<TaskInfo>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = "SELECT TaskId, TaskName, TriggerType, StartTime, Priority, Status FROM Tasks WHERE Status = 'ACTIVE'";

        using var cmd = new SqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tasks.Add(new TaskInfo
            {
                TaskId = reader["TaskId"].ToString(),
                TaskName = reader["TaskName"].ToString(),
                TriggerType = reader["TriggerType"].ToString(),
                StartTime = reader["StartTime"] as DateTime?,
                Priority = Convert.ToInt32(reader["Priority"]),
                Status = reader["Status"].ToString()
            });
        }

        return tasks;
    }
}
