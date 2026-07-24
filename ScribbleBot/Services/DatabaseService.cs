using Microsoft.Data.Sqlite;
using ScribbleBot.Models;
using System.IO;

namespace ScribbleBot.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScribbleBot"
        );
        Directory.CreateDirectory(appDataFolder);

        var dbPath = Path.Combine(appDataFolder, "scribble.db");
        _connectionString = $"Data Source={dbPath};";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS threads (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                last_updated_at TEXT NOT NULL,
                system_summary TEXT
            );

            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                FOREIGN KEY(thread_id) REFERENCES threads(id) ON DELETE CASCADE
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                content, 
                content=messages, 
                content_rowid=id
            );

            CREATE TRIGGER IF NOT EXISTS messages_ai AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(rowid, content) VALUES (new.id, new.content);
            END;

            CREATE TRIGGER IF NOT EXISTS messages_ad AFTER DELETE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, content) VALUES('delete', old.id, old.content);
            END;
        ";
        command.ExecuteNonQuery();
    }

    public async Task<List<ChatThreadModel>> GetAllThreadsAsync()
    {
        var threads = new List<ChatThreadModel>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, created_at, last_updated_at, system_summary FROM threads ORDER BY last_updated_at DESC;";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            threads.Add(new ChatThreadModel
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                LastUpdatedAt = DateTime.Parse(reader.GetString(3)),
                SystemSummary = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
            });
        }

        return threads;
    }

    public async Task SaveThreadAsync(ChatThreadModel thread)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO threads (id, title, created_at, last_updated_at, system_summary)
            VALUES ($id, $title, $created_at, $last_updated_at, $system_summary)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                last_updated_at = excluded.last_updated_at,
                system_summary = excluded.system_summary;
        ";
        command.Parameters.AddWithValue("$id", thread.Id);
        command.Parameters.AddWithValue("$title", thread.Title);
        command.Parameters.AddWithValue("$created_at", thread.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("$last_updated_at", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$system_summary", thread.SystemSummary ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task AddMessageAsync(string threadId, ChatMessageModel message)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO messages (thread_id, role, content, timestamp)
            VALUES ($threadId, $role, $content, $timestamp);

            UPDATE threads SET last_updated_at = $timestamp WHERE id = $threadId;
        ";
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$role", message.Role);
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$timestamp", message.Timestamp.ToString("o"));

        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<List<ChatMessageModel>> GetMessagesForThreadAsync(string threadId)
    {
        var messages = new List<ChatMessageModel>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT role, content, timestamp FROM messages WHERE thread_id = $threadId ORDER BY id ASC;";
        command.Parameters.AddWithValue("$threadId", threadId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new ChatMessageModel
            {
                Role = reader.GetString(0),
                Content = reader.GetString(1),
                Timestamp = DateTime.Parse(reader.GetString(2))
            });
        }

        return messages;
    }

    public async Task DeleteThreadAsync(string threadId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM threads WHERE id = $id;";
        command.Parameters.AddWithValue("$id", threadId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateThreadSummaryAsync(string threadId, string summary)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
        UPDATE threads 
        SET system_summary = $summary, last_updated_at = $updatedAt 
        WHERE id = $id;";

        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("$id", threadId);

        await command.ExecuteNonQueryAsync();
    }
}