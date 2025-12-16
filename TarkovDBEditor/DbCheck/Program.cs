using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "bin", "Debug", "net8.0-windows", "tarkov_data.db");
dbPath = Path.GetFullPath(dbPath);

Console.WriteLine($"DB Path: {dbPath}");
Console.WriteLine($"Exists: {File.Exists(dbPath)}");
Console.WriteLine();

if (!File.Exists(dbPath))
{
    Console.WriteLine("DB file not found!");
    return;
}

var connectionString = $"Data Source={dbPath}";
using var connection = new SqliteConnection(connectionString);
connection.Open();

// Samples 퀘스트 찾기
Console.WriteLine("=== Quests with 'Samples' ===");
using (var cmd = new SqliteCommand("SELECT Id, Name, NameEN FROM Quests WHERE Name LIKE '%Samples%' OR NameEN LIKE '%Samples%'", connection))
using (var reader = cmd.ExecuteReader())
{
    while (reader.Read())
    {
        Console.WriteLine($"Id: {reader.GetString(0)}");
        Console.WriteLine($"Name: {reader.GetString(1)}");
        Console.WriteLine($"NameEN: {(reader.IsDBNull(2) ? "NULL" : reader.GetString(2))}");
        Console.WriteLine();
    }
}

// QuestRequiredItems 테이블 스키마 확인
Console.WriteLine("=== QuestRequiredItems Schema ===");
using (var cmd = new SqliteCommand("PRAGMA table_info(QuestRequiredItems)", connection))
using (var reader = cmd.ExecuteReader())
{
    while (reader.Read())
    {
        Console.WriteLine($"Column: {reader["name"]} ({reader["type"]})");
    }
}
Console.WriteLine();

// Samples 퀘스트의 QuestRequiredItems
Console.WriteLine("=== QuestRequiredItems for Samples ===");
using (var cmd = new SqliteCommand(@"
    SELECT r.Id, r.ItemId, r.ItemName, r.Count, r.RequirementType, r.RequiresFIR, q.Name as QuestName
    FROM QuestRequiredItems r
    JOIN Quests q ON r.QuestId = q.Id
    WHERE q.Name LIKE '%Samples%' OR q.NameEN LIKE '%Samples%'", connection))
using (var reader = cmd.ExecuteReader())
{
    int count = 0;
    while (reader.Read())
    {
        count++;
        var itemId = reader.IsDBNull(1) ? "NULL" : reader.GetString(1);
        var itemName = reader.IsDBNull(2) ? "NULL" : reader.GetString(2);
        var cnt = reader.GetInt32(3);
        var reqType = reader.GetString(4);
        var fir = reader.GetInt64(5) != 0;
        Console.WriteLine($"{count}. ItemId: {itemId}");
        Console.WriteLine($"   ItemName: '{itemName}'");
        Console.WriteLine($"   Count: {cnt}, Type: {reqType}, FIR: {fir}");
        Console.WriteLine();
    }
    Console.WriteLine($"Total: {count} items");
}

// QuestRequiredItems 전체 개수
Console.WriteLine("\n=== Total QuestRequiredItems ===");
using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM QuestRequiredItems", connection))
{
    var total = cmd.ExecuteScalar();
    Console.WriteLine($"Total rows: {total}");
}
