// Simple test for Quest Marker functionality
// Run with: dotnet run -- --test-quest-markers

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models.MapTracker;
using TarkovHelper.Services;
using TarkovHelper.Services.MapTracker;

namespace TarkovHelper.Tests;

public static class QuestMarkerTest
{
    public static async Task<bool> RunAllTests()
    {
        Console.WriteLine("=== Quest Marker Test Suite ===\n");

        var allPassed = true;

        allPassed &= await TestQuestDataLoading();
        allPassed &= TestQuestObjectiveFiltering();
        allPassed &= TestMarkerCounting();
        allPassed &= TestQuestTypeColors();

        Console.WriteLine("\n=== Test Results ===");
        Console.WriteLine(allPassed ? "✓ All tests PASSED" : "✗ Some tests FAILED");

        return allPassed;
    }

    /// <summary>
    /// TC1: Quest data loading test
    /// </summary>
    private static async Task<bool> TestQuestDataLoading()
    {
        Console.WriteLine("TC1: Quest Data Loading");
        Console.WriteLine("-----------------------");

        try
        {
            var service = QuestObjectiveService.Instance;

            Console.WriteLine("  Loading quest objectives from API/cache...");
            await service.EnsureLoadedAsync(msg => Console.WriteLine($"    {msg}"));

            if (!service.IsLoaded)
            {
                Console.WriteLine("  ✗ FAILED: Service not loaded");
                return false;
            }

            var count = service.AllObjectives.Count;
            Console.WriteLine($"  Total objectives loaded: {count}");

            if (count == 0)
            {
                Console.WriteLine("  ✗ FAILED: No objectives loaded");
                return false;
            }

            // Check data structure
            var sample = service.AllObjectives.FirstOrDefault();
            if (sample != null)
            {
                Console.WriteLine($"  Sample objective:");
                Console.WriteLine($"    Task: {sample.TaskName}");
                Console.WriteLine($"    Type: {sample.Type}");
                Console.WriteLine($"    Description: {sample.Description?.Substring(0, Math.Min(50, sample.Description?.Length ?? 0))}...");
                Console.WriteLine($"    Locations: {sample.Locations.Count}");
            }

            Console.WriteLine("  ✓ PASSED\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// TC2: Quest objective filtering by map
    /// </summary>
    private static bool TestQuestObjectiveFiltering()
    {
        Console.WriteLine("TC2: Quest Objective Filtering by Map");
        Console.WriteLine("--------------------------------------");

        try
        {
            var service = QuestObjectiveService.Instance;
            var mapNames = service.GetAllMapNames();

            Console.WriteLine($"  Available maps: {string.Join(", ", mapNames.Take(5))}...");

            // Test filtering for common maps
            var testMaps = new[] { "customs", "woods", "shoreline", "interchange", "reserve" };

            foreach (var map in testMaps)
            {
                if (!mapNames.Contains(map)) continue;

                // Get all objectives for map (without progress filtering)
                var objectives = service.AllObjectives
                    .Where(o => o.Locations.Any(l =>
                        l.MapNormalizedName?.ToLowerInvariant() == map ||
                        l.MapName?.ToLowerInvariant() == map))
                    .ToList();

                Console.WriteLine($"  {map}: {objectives.Count} objectives");
            }

            Console.WriteLine("  ✓ PASSED\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// TC3: Marker counting
    /// </summary>
    private static bool TestMarkerCounting()
    {
        Console.WriteLine("TC3: Marker Counting");
        Console.WriteLine("--------------------");

        try
        {
            var service = QuestObjectiveService.Instance;

            // Count by type
            var typeGroups = service.AllObjectives
                .GroupBy(o => o.Type?.ToLowerInvariant() ?? "unknown")
                .OrderByDescending(g => g.Count())
                .ToList();

            Console.WriteLine("  Objectives by type:");
            foreach (var group in typeGroups.Take(10))
            {
                Console.WriteLine($"    {group.Key}: {group.Count()}");
            }

            // Count total locations
            var totalLocations = service.AllObjectives.Sum(o => o.Locations.Count);
            Console.WriteLine($"  Total location points: {totalLocations}");

            // Count multi-point objectives
            var multiPoint = service.AllObjectives.Count(o => o.Locations.Count > 1);
            Console.WriteLine($"  Multi-point objectives: {multiPoint}");

            Console.WriteLine("  ✓ PASSED\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// TC4: Quest type colors
    /// </summary>
    private static bool TestQuestTypeColors()
    {
        Console.WriteLine("TC4: Quest Type Colors");
        Console.WriteLine("----------------------");

        try
        {
            var types = new[] { "visit", "mark", "plantitem", "extract", "finditem", "kill", "unknown" };

            Console.WriteLine("  Type -> Color mapping:");
            foreach (var type in types)
            {
                var colorName = type switch
                {
                    "visit" => "Blue (#2196F3)",
                    "mark" => "Green (#4CAF50)",
                    "plantitem" => "Orange (#FF9800)",
                    "extract" => "Red (#F44336)",
                    "finditem" => "Yellow (#FFEB3B)",
                    "kill" => "Purple (#9C27B0)",
                    _ => "Gold (#FFC107)"
                };
                Console.WriteLine($"    {type} -> {colorName}");
            }

            Console.WriteLine("  ✓ PASSED\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            return false;
        }
    }
}
