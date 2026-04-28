using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

public interface ITeamTaskStore
{
    int Insert(TeamTask t);
    bool Update(TeamTask t);
    bool Delete(int id);
    TeamTask? Get(int id);
    List<TeamTask> ListAll(int? assignedToUserId = null, int limit = 2000);
    /// <summary>Active tasks for assignee where today is within start/end (caller passes today as yyyy-MM-dd).</summary>
    List<TeamTask> ListActiveForAssignee(int userId, string todayYyyyMmDd);
    bool HasCompletion(int teamTaskId, string periodKey);
    bool InsertCompletion(int teamTaskId, string periodKey);
    bool DeleteCompletion(int teamTaskId, string periodKey);
    void DeleteCompletionsForTask(int teamTaskId);
    string? GetNote(int teamTaskId, int userId, string periodKey);
    bool HasNote(int teamTaskId, int userId, string periodKey);
    /// <summary>Empty or whitespace note deletes the row.</summary>
    bool SaveNote(int teamTaskId, int userId, string periodKey, string? noteText);
}

public class TeamTaskStore : ITeamTaskStore
{
    private readonly ILogger _log = LoggingService.ForContext<TeamTaskStore>();
    private readonly string _connectionString;

    private T ExecuteDbOperation<T>(Func<T> operation, string operationName, T defaultValue = default!)
    {
        try
        {
            return operation();
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error in {Operation}: {Message}", operationName, ex.Message);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error in {Operation}: {Message}", operationName, ex.Message);
            return defaultValue;
        }
    }

    public TeamTaskStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public int Insert(TeamTask t)
    {
        return ExecuteDbOperation(() =>
        {
            var now = DateTime.UtcNow.ToString("O");
            t.Uuid = string.IsNullOrEmpty(t.Uuid) ? Guid.NewGuid().ToString() : t.Uuid;
            t.CreatedAt = now;
            t.UpdatedAt = now;

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO team_tasks (uuid, title, description, assigned_to_user_id, assigned_to_username,
                    assigned_by_user_id, assigned_by_username, recurrence, start_date, end_date, is_active, created_at, updated_at)
                VALUES (@uuid, @title, @desc, @to_id, @to_name, @by_id, @by_name, @rec, @start, @end, @active, @created, @updated)";
            cmd.Parameters.AddWithValue("@uuid", t.Uuid);
            cmd.Parameters.AddWithValue("@title", t.Title);
            cmd.Parameters.AddWithValue("@desc", t.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@to_id", t.AssignedToUserId);
            cmd.Parameters.AddWithValue("@to_name", t.AssignedToUsername);
            cmd.Parameters.AddWithValue("@by_id", t.AssignedByUserId);
            cmd.Parameters.AddWithValue("@by_name", t.AssignedByUsername);
            cmd.Parameters.AddWithValue("@rec", t.Recurrence);
            cmd.Parameters.AddWithValue("@start", t.StartDate);
            cmd.Parameters.AddWithValue("@end", t.EndDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@active", t.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@created", t.CreatedAt);
            cmd.Parameters.AddWithValue("@updated", t.UpdatedAt);
            cmd.ExecuteNonQuery();

            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            t.Id = Convert.ToInt32(idCmd.ExecuteScalar());
            return t.Id;
        }, nameof(Insert), 0);
    }

    public bool Update(TeamTask t)
    {
        return ExecuteDbOperation(() =>
        {
            t.UpdatedAt = DateTime.UtcNow.ToString("O");
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE team_tasks SET title=@title, description=@desc, assigned_to_user_id=@to_id, assigned_to_username=@to_name,
                    recurrence=@rec, start_date=@start, end_date=@end, is_active=@active, updated_at=@updated
                WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", t.Id);
            cmd.Parameters.AddWithValue("@title", t.Title);
            cmd.Parameters.AddWithValue("@desc", t.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@to_id", t.AssignedToUserId);
            cmd.Parameters.AddWithValue("@to_name", t.AssignedToUsername);
            cmd.Parameters.AddWithValue("@rec", t.Recurrence);
            cmd.Parameters.AddWithValue("@start", t.StartDate);
            cmd.Parameters.AddWithValue("@end", t.EndDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@active", t.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@updated", t.UpdatedAt);
            return cmd.ExecuteNonQuery() > 0;
        }, nameof(Update), false);
    }

    public bool Delete(int id)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using (var delC = conn.CreateCommand())
            {
                delC.CommandText = "DELETE FROM team_task_completions WHERE team_task_id = @id";
                delC.Parameters.AddWithValue("@id", id);
                delC.ExecuteNonQuery();
            }
            using (var delN = conn.CreateCommand())
            {
                delN.CommandText = "DELETE FROM team_task_notes WHERE team_task_id = @id";
                delN.Parameters.AddWithValue("@id", id);
                delN.ExecuteNonQuery();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM team_tasks WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }, nameof(Delete), false);
    }

    public TeamTask? Get(int id)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM team_tasks WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadTask(r) : null;
        }, nameof(Get), null);
    }

    public List<TeamTask> ListAll(int? assignedToUserId = null, int limit = 2000)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            var sql = "SELECT * FROM team_tasks WHERE 1=1";
            if (assignedToUserId.HasValue)
            {
                sql += " AND assigned_to_user_id = @uid";
                cmd.Parameters.AddWithValue("@uid", assignedToUserId.Value);
            }
            sql += " ORDER BY updated_at DESC LIMIT @lim";
            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.CommandText = sql;
            var list = new List<TeamTask>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadTask(r));
            return list;
        }, nameof(ListAll), new List<TeamTask>());
    }

    public List<TeamTask> ListActiveForAssignee(int userId, string todayYyyyMmDd)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM team_tasks
                WHERE assigned_to_user_id = @uid AND is_active = 1
                  AND start_date <= @today
                  AND (end_date IS NULL OR end_date >= @today)
                ORDER BY title";
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@today", todayYyyyMmDd);
            var list = new List<TeamTask>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadTask(r));
            return list;
        }, nameof(ListActiveForAssignee), new List<TeamTask>());
    }

    public bool HasCompletion(int teamTaskId, string periodKey)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM team_task_completions WHERE team_task_id = @tid AND period_key = @pk LIMIT 1";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.Parameters.AddWithValue("@pk", periodKey);
            return cmd.ExecuteScalar() != null;
        }, nameof(HasCompletion), false);
    }

    public bool InsertCompletion(int teamTaskId, string periodKey)
    {
        return ExecuteDbOperation(() =>
        {
            var now = DateTime.UtcNow.ToString("O");
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO team_task_completions (team_task_id, period_key, completed_at) VALUES (@tid, @pk, @at)";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.Parameters.AddWithValue("@pk", periodKey);
            cmd.Parameters.AddWithValue("@at", now);
            return cmd.ExecuteNonQuery() > 0;
        }, nameof(InsertCompletion), false);
    }

    public bool DeleteCompletion(int teamTaskId, string periodKey)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM team_task_completions WHERE team_task_id = @tid AND period_key = @pk";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.Parameters.AddWithValue("@pk", periodKey);
            return cmd.ExecuteNonQuery() > 0;
        }, nameof(DeleteCompletion), false);
    }

    public void DeleteCompletionsForTask(int teamTaskId)
    {
        ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM team_task_completions WHERE team_task_id = @tid";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.ExecuteNonQuery();
            return true;
        }, nameof(DeleteCompletionsForTask), false);
    }

    public string? GetNote(int teamTaskId, int userId, string periodKey)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT note_text FROM team_task_notes WHERE team_task_id = @tid AND user_id = @uid AND period_key = @pk";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@pk", periodKey);
            var o = cmd.ExecuteScalar();
            return o == null || o is DBNull ? null : Convert.ToString(o);
        }, nameof(GetNote), null);
    }

    public bool HasNote(int teamTaskId, int userId, string periodKey)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM team_task_notes WHERE team_task_id = @tid AND user_id = @uid AND period_key = @pk LIMIT 1";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@pk", periodKey);
            return cmd.ExecuteScalar() != null;
        }, nameof(HasNote), false);
    }

    public bool SaveNote(int teamTaskId, int userId, string periodKey, string? noteText)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            if (string.IsNullOrWhiteSpace(noteText))
            {
                using var del = conn.CreateCommand();
                del.CommandText =
                    "DELETE FROM team_task_notes WHERE team_task_id = @tid AND user_id = @uid AND period_key = @pk";
                del.Parameters.AddWithValue("@tid", teamTaskId);
                del.Parameters.AddWithValue("@uid", userId);
                del.Parameters.AddWithValue("@pk", periodKey);
                del.ExecuteNonQuery();
                return true;
            }

            var trimmed = noteText.Trim();
            if (trimmed.Length > 8000)
                trimmed = trimmed[..8000];
            var now = DateTime.UtcNow.ToString("O");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO team_task_notes (team_task_id, user_id, period_key, note_text, updated_at)
                VALUES (@tid, @uid, @pk, @txt, @ua)
                ON CONFLICT(team_task_id, user_id, period_key) DO UPDATE SET
                    note_text = excluded.note_text,
                    updated_at = excluded.updated_at";
            cmd.Parameters.AddWithValue("@tid", teamTaskId);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@pk", periodKey);
            cmd.Parameters.AddWithValue("@txt", trimmed);
            cmd.Parameters.AddWithValue("@ua", now);
            cmd.ExecuteNonQuery();
            return true;
        }, nameof(SaveNote), false);
    }

    private static TeamTask ReadTask(OracleDataReader r)
    {
        return new TeamTask
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            Title = r.GetString(r.GetOrdinal("title")),
            Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            AssignedToUserId = r.GetInt32(r.GetOrdinal("assigned_to_user_id")),
            AssignedToUsername = r.GetString(r.GetOrdinal("assigned_to_username")),
            AssignedByUserId = r.GetInt32(r.GetOrdinal("assigned_by_user_id")),
            AssignedByUsername = r.GetString(r.GetOrdinal("assigned_by_username")),
            Recurrence = r.GetString(r.GetOrdinal("recurrence")),
            StartDate = r.GetString(r.GetOrdinal("start_date")),
            EndDate = r.IsDBNull(r.GetOrdinal("end_date")) ? null : r.GetString(r.GetOrdinal("end_date")),
            IsActive = r.GetInt32(r.GetOrdinal("is_active")) != 0,
            CreatedAt = r.GetString(r.GetOrdinal("created_at")),
            UpdatedAt = r.GetString(r.GetOrdinal("updated_at"))
        };
    }
}
