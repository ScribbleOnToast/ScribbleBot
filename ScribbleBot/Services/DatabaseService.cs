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

            -- Codebase Structural Map (AST Parsing)
            CREATE TABLE IF NOT EXISTS code_symbols (
                id TEXT PRIMARY KEY,
                project_name TEXT NOT NULL,
                file_path TEXT NOT NULL,
                symbol_type TEXT NOT NULL,
                symbol_name TEXT NOT NULL,
                signature TEXT,
                start_line INTEGER,
                end_line INTEGER,
                content TEXT
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS code_symbols_fts USING fts5(
                project_name,
                symbol_name,
                signature,
                content,
                content=code_symbols,
                content_rowid=rowid
            );

            CREATE TRIGGER IF NOT EXISTS code_symbols_ai AFTER INSERT ON code_symbols BEGIN
                INSERT INTO code_symbols_fts(rowid, symbol_name, signature, content) 
                VALUES (new.rowid, new.symbol_name, new.signature, new.content);
            END;

            -- Code Review Findings & Recommendations
            CREATE TABLE IF NOT EXISTS review_items (
                id TEXT PRIMARY KEY,
                project_name TEXT NOT NULL,
                file_path TEXT NOT NULL,
                target_symbol TEXT,
                category TEXT NOT NULL,
                severity TEXT NOT NULL,
                issue_description TEXT NOT NULL,
                suggested_fix TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Pending',
                created_at TEXT NOT NULL
            );
        ";
        command.ExecuteNonQuery();
    }

    #region Thread & Message Operations
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
    #endregion

    #region Code Symbol Structural Map Operations
    public async Task SaveCodeSymbolsAsync(IEnumerable<CodeSymbolModel> symbols, string projectName)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO code_symbols (id, project_name, file_path, symbol_type, symbol_name, signature, start_line, end_line, content)
            VALUES ($id, $projectName, $filePath, $symbolType, $symbolName, $signature, $startLine, $endLine, $content)
            ON CONFLICT(id) DO UPDATE SET
                project_name = excluded.project_name,
                file_path = excluded.file_path,
                symbol_type = excluded.symbol_type,
                symbol_name = excluded.symbol_name,
                signature = excluded.signature,
                start_line = excluded.start_line,
                end_line = excluded.end_line,
                content = excluded.content;
        ";

        var pId = command.Parameters.Add("$id", SqliteType.Text);
        var pProject = command.Parameters.Add("$projectName", SqliteType.Text);
        var pPath = command.Parameters.Add("$filePath", SqliteType.Text);
        var pType = command.Parameters.Add("$symbolType", SqliteType.Text);
        var pName = command.Parameters.Add("$symbolName", SqliteType.Text);
        var pSig = command.Parameters.Add("$signature", SqliteType.Text);
        var pStart = command.Parameters.Add("$startLine", SqliteType.Integer);
        var pEnd = command.Parameters.Add("$endLine", SqliteType.Integer);
        var pContent = command.Parameters.Add("$content", SqliteType.Text);

        foreach (var symbol in symbols)
        {
            pId.Value = symbol.Id;
            pProject.Value = projectName;
            pPath.Value = symbol.FilePath;
            pType.Value = symbol.SymbolType;
            pName.Value = symbol.SymbolName;
            pSig.Value = symbol.Signature ?? (object)DBNull.Value;
            pStart.Value = symbol.StartLine;
            pEnd.Value = symbol.EndLine;
            pContent.Value = symbol.Content ?? (object)DBNull.Value;

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<List<CodeSymbolModel>> SearchSymbolsFtsAsync(string queryTerm, string? projectName = null)
    {
        var results = new List<CodeSymbolModel>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        if (!string.IsNullOrEmpty(projectName))
        {
            command.CommandText = @"
            SELECT cs.id, cs.project_name, cs.file_path, cs.symbol_type, cs.symbol_name, cs.signature, cs.start_line, cs.end_line, cs.content
            FROM code_symbols_fts fts
            JOIN code_symbols cs ON fts.rowid = cs.rowid
            WHERE code_symbols_fts MATCH $query AND cs.project_name = $projectName
            LIMIT 20;";
            command.Parameters.AddWithValue("$projectName", projectName);
        }
        else
        {
            command.CommandText = @"
            SELECT cs.id, cs.project_name, cs.file_path, cs.symbol_type, cs.symbol_name, cs.signature, cs.start_line, cs.end_line, cs.content
            FROM code_symbols_fts fts
            JOIN code_symbols cs ON fts.rowid = cs.rowid
            WHERE code_symbols_fts MATCH $query
            LIMIT 20;";
        }
        command.Parameters.AddWithValue("$query", queryTerm);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new CodeSymbolModel
            {
                Id = reader.GetString(0),
                FilePath = reader.GetString(1),
                SymbolType = reader.GetString(2),
                SymbolName = reader.GetString(3),
                Signature = reader.IsDBNull(4) ? null : reader.GetString(4),
                StartLine = reader.GetInt32(5),
                EndLine = reader.GetInt32(6),
                Content = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return results;
    }
    #endregion

    #region Review Items Operations
    public async Task SaveReviewItemsAsync(IEnumerable<ReviewItemModel> items, string projectName)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO review_items (id, project_name, file_path, target_symbol, category, severity, issue_description, suggested_fix, status, created_at)
            VALUES ($id, $projectName, $filePath, $targetSymbol, $category, $severity, $issueDescription, $suggestedFix, $status, $createdAt)
            ON CONFLICT(id) DO UPDATE SET
                status = excluded.status;
        ";

        var pId = command.Parameters.Add("$id", SqliteType.Text);
        var pProject = command.Parameters.Add("$projectName", SqliteType.Text);
        var pPath = command.Parameters.Add("$filePath", SqliteType.Text);
        var pSym = command.Parameters.Add("$targetSymbol", SqliteType.Text);
        var pCat = command.Parameters.Add("$category", SqliteType.Text);
        var pSev = command.Parameters.Add("$severity", SqliteType.Text);
        var pDesc = command.Parameters.Add("$issueDescription", SqliteType.Text);
        var pFix = command.Parameters.Add("$suggestedFix", SqliteType.Text);
        var pStat = command.Parameters.Add("$status", SqliteType.Text);
        var pCreated = command.Parameters.Add("$createdAt", SqliteType.Text);

        foreach (var item in items)
        {
            pId.Value = item.Id;
            pProject.Value = projectName;
            pPath.Value = item.FilePath;
            pSym.Value = item.TargetSymbol ?? (object)DBNull.Value;
            pCat.Value = item.Category;
            pSev.Value = item.Severity;
            pDesc.Value = item.IssueDescription;
            pFix.Value = item.SuggestedFix;
            pStat.Value = item.Status;
            pCreated.Value = item.CreatedAt.ToString("o");

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<List<ReviewItemModel>> GetPendingReviewItemsAsync()
    {
        var items = new List<ReviewItemModel>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, file_path, target_symbol, category, severity, issue_description, suggested_fix, status, created_at FROM review_items WHERE status = 'Pending' ORDER BY created_at DESC;";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new ReviewItemModel
            {
                Id = reader.GetString(0),
                FilePath = reader.GetString(1),
                TargetSymbol = reader.IsDBNull(2) ? null : reader.GetString(2),
                Category = reader.GetString(3),
                Severity = reader.GetString(4),
                IssueDescription = reader.GetString(5),
                SuggestedFix = reader.GetString(6),
                Status = reader.GetString(7),
                CreatedAt = DateTime.Parse(reader.GetString(8))
            });
        }

        return items;
    }

    public async Task ClearProjectSymbolsAsync(string projectName)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM code_symbols WHERE project_name = $projectName;";
        command.Parameters.AddWithValue("$projectName", projectName);

        await command.ExecuteNonQueryAsync();
    }
    #endregion
}