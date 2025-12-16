// dotnet script로 실행: dotnet script CheckDb.csx
#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"bin\Debug\net8.0-windows\tarkov_data.db";
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

// QuestRequiredItems 전체 확인
Console.WriteLine("\n=== All QuestRequiredItems (first 20) ===");
using (var cmd = new SqliteCommand("SELECT r.QuestId, r.ItemId, r.ItemName, r.Count, r.RequirementType, q.Name as QuestName FROM QuestRequiredItems r LEFT JOIN Quests q ON r.QuestId = q.Id LIMIT 20", connection))
using (var reader = cmd.ExecuteReader())
{
    while (reader.Read())
    {
        Console.WriteLine($"Quest: {(reader.IsDBNull(5) ? "NULL" : reader.GetString(5))}");
        Console.WriteLine($"  ItemId: {(reader.IsDBNull(1) ? "NULL" : reader.GetString(1))}");
        Console.WriteLine($"  ItemName: '{(reader.IsDBNull(2) ? "NULL" : reader.GetString(2))}'");
        Console.WriteLine($"  Count: {reader.GetInt32(3)}, Type: {reader.GetString(4)}");
        Console.WriteLine();
    }
}
