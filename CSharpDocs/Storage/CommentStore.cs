using Microsoft.Data.Sqlite;

namespace CSharpDocs.Storage;

/// <summary>Комментарий к элементу документации.</summary>
public sealed class Comment
{
    public long Id { get; init; }
    public string Target { get; init; } = "";
    public string TargetTitle { get; init; } = "";
    public string TargetUrl { get; init; } = "";
    public string Author { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

/// <summary>Хранилище комментариев (SQLite).</summary>
public sealed class CommentStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public CommentStore(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        Migrate();
    }

    private void Migrate()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS comments (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                target       TEXT NOT NULL,
                target_title TEXT NOT NULL DEFAULT '',
                target_url   TEXT NOT NULL DEFAULT '',
                author       TEXT NOT NULL,
                body         TEXT NOT NULL,
                created_at   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_comments_target ON comments(target);
            """);
    }

    public void Add(Comment comment)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO comments(target, target_title, target_url, author, body, created_at)
            VALUES ($t, $tt, $tu, $a, $b, $c)
            """;
        cmd.Parameters.AddWithValue("$t", comment.Target);
        cmd.Parameters.AddWithValue("$tt", comment.TargetTitle);
        cmd.Parameters.AddWithValue("$tu", comment.TargetUrl);
        cmd.Parameters.AddWithValue("$a", comment.Author.Length == 0 ? "Аноним" : comment.Author);
        cmd.Parameters.AddWithValue("$b", comment.Body);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<Comment> ForTarget(string target) =>
        Query("SELECT id,target,target_title,target_url,author,body,created_at FROM comments WHERE target=$t ORDER BY created_at DESC, id DESC",
            ("$t", target));

    public List<Comment> All() =>
        Query("SELECT id,target,target_title,target_url,author,body,created_at FROM comments ORDER BY created_at DESC, id DESC");

    /// <summary>Число комментариев по каждой цели (target → count).</summary>
    public Dictionary<string, int> Counts()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT target, COUNT(*) FROM comments GROUP BY target";

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    private List<Comment> Query(string sql, params (string Name, object Value)[] args)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);

        var result = new List<Comment>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Comment
            {
                Id = reader.GetInt64(0),
                Target = reader.GetString(1),
                TargetTitle = reader.GetString(2),
                TargetUrl = reader.GetString(3),
                Author = reader.GetString(4),
                Body = reader.GetString(5),
                CreatedAt = DateTime.TryParse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow,
            });
        }
        return result;
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
