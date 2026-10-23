using System.Text;
using WolvenKit.Core.Compression;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.IO;
using WolvenKit.RED4.Types;

namespace SaveRoundTripHarness;

/// <summary>
/// Headless parse → serialize → re-parse check for Cyberpunk 2077 sav.dat files.
/// Usage: SaveRoundTripHarness &lt;path-to-sav.dat&gt; [--write &lt;out.dat&gt;]
/// Exit codes: 0 = OK, 1 = usage/IO error, 2 = parse/serialize/validation failure.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CompressionSettings.Get().UseOodle = false;

        if (args.Length < 1 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Usage: SaveRoundTripHarness <path-to-sav.dat> [--write <out.dat>]");
            Console.WriteLine();
            Console.WriteLine("Parses a Cyberpunk 2077 save, serializes it, re-parses the bytes,");
            Console.WriteLine("and compares node / inventory / fact / stats layout counts.");
            Console.WriteLine("Refuses to write output if validation fails.");
            Console.WriteLine();
            Console.WriteLine("For Update 2.5 testing, supply a save folder's sav.dat from game version 2.5");
            Console.WriteLine("(metadata.9.json gameDefinition / playthrough should reflect patch 2.5).");
            return args.Length < 1 ? 1 : 0;
        }

        var inputPath = args[0];
        string? writePath = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--write" && i + 1 < args.Length)
            {
                writePath = args[++i];
            }
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"File not found: {inputPath}");
            return 1;
        }

        Console.WriteLine($"Loading: {inputPath}");

        CyberpunkSaveFile save;
        try
        {
            using var fs = File.OpenRead(inputPath);
            using var reader = new CyberpunkSaveReader(fs);
            var status = reader.ReadFile(out var parsed);
            if (status != EFileReadErrorCodes.NoError || parsed is null)
            {
                if (status == EFileReadErrorCodes.UnsupportedVersion)
                {
                    fs.Position = 0;
                    reader.ReadFileInfo(out var info);
                    Console.Error.WriteLine($"Parse failed: unsupported game version {info?.GameVersion}");
                }
                else
                {
                    Console.Error.WriteLine($"Parse failed: {status}");
                }

                return 2;
            }

            save = parsed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Parse threw: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }

        var before = Describe(save);
        PrintDiagnostics("Parsed", before);

        byte[] bytes;
        try
        {
            using var ms = new MemoryStream();
            using (var writer = new CyberpunkSaveWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.WriteFile(save, compress: true);
            }

            bytes = ms.ToArray();
            Console.WriteLine($"Serialized {bytes.Length} bytes");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Serialize failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }

        CyberpunkSaveFile reparsed;
        try
        {
            using var ms = new MemoryStream(bytes);
            using var reader = new CyberpunkSaveReader(ms);
            var status = reader.ReadFile(out var parsed);
            if (status != EFileReadErrorCodes.NoError || parsed is null)
            {
                Console.Error.WriteLine($"Re-parse failed: {status}");
                return 2;
            }

            reparsed = parsed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Re-parse threw: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }

        var after = Describe(reparsed);
        PrintDiagnostics("Re-parsed", after);

        var mismatch = Compare(before, after);
        if (mismatch is not null)
        {
            Console.Error.WriteLine($"VALIDATION FAILED: {mismatch}");
            Console.Error.WriteLine("Refusing to write output.");
            return 2;
        }

        Console.WriteLine("Round-trip OK.");

        if (writePath is not null)
        {
            File.WriteAllBytes(writePath, bytes);
            Console.WriteLine($"Wrote: {writePath}");
        }

        return 0;
    }

    private sealed record Diag(
        uint GameVersion,
        int NodeCount,
        string NodeNames,
        int InventoryItems,
        int Facts,
        int StatsKeys,
        int StatsValues,
        int StatsOptimized);

    private static Diag Describe(CyberpunkSaveFile save)
    {
        var names = string.Join("|", save.Nodes.Select(n => n.Name));
        var items = 0;
        var facts = 0;
        var keys = 0;
        var values = 0;
        var optimized = 0;

        try
        {
            if (save.Nodes.FirstOrDefault(n => n.Name == "inventory")?.Value is Inventory inv)
            {
                items = inv.SubInventories.Sum(s => s.Items?.Count ?? 0);
            }
        }
        catch { /* diagnostic only */ }

        try
        {
            var quest = save.Nodes.FirstOrDefault(n => n.Name == "questSystem");
            var factsNode = quest?.Children?.FirstOrDefault(n => n.Name == "FactsDB");
            if (factsNode?.Value is FactsDB db)
            {
                facts = db.FactsTables?.Sum(t => t.FactEntries?.Count ?? 0) ?? 0;
            }
        }
        catch { /* diagnostic only */ }

        try
        {
            if (save.Nodes.FirstOrDefault(n => n.Name == "StatsSystem")?.Value is Package pkg
                && pkg.Content is RedPackage red
                && red.RootChunk is gameStatsStateMapStructure map)
            {
                keys = map.Keys?.Count ?? 0;
                values = map.Values?.Count ?? 0;
                optimized = map.ValuesOptimized?.Count ?? 0;
            }
        }
        catch { /* diagnostic only */ }

        return new Diag(save.FileHeader.GameVersion, save.Nodes.Count, names, items, facts, keys, values, optimized);
    }

    private static void PrintDiagnostics(string label, Diag d)
    {
        Console.WriteLine($"{label}:");
        Console.WriteLine($"  GameVersion={d.GameVersion} (enum Current=2500 for 2.5)");
        Console.WriteLine($"  Nodes={d.NodeCount}");
        Console.WriteLine($"  InventoryItems={d.InventoryItems}");
        Console.WriteLine($"  Facts={d.Facts}");
        Console.WriteLine($"  Stats Keys/Values/ValuesOptimized={d.StatsKeys}/{d.StatsValues}/{d.StatsOptimized}");
        if (d.StatsOptimized > 0)
        {
            Console.WriteLine("  NOTE: Save uses ValuesOptimized stats layout (2.4+/2.5). Editor materializes entries on edit.");
        }
    }

    private static string? Compare(Diag a, Diag b)
    {
        if (a.GameVersion != b.GameVersion) return $"GameVersion {a.GameVersion} -> {b.GameVersion}";
        if (a.NodeCount != b.NodeCount) return $"NodeCount {a.NodeCount} -> {b.NodeCount}";
        if (a.NodeNames != b.NodeNames) return "node name list mismatch";
        if (a.InventoryItems != b.InventoryItems) return $"InventoryItems {a.InventoryItems} -> {b.InventoryItems}";
        if (a.Facts != b.Facts) return $"Facts {a.Facts} -> {b.Facts}";
        if (a.StatsKeys != b.StatsKeys || a.StatsValues != b.StatsValues || a.StatsOptimized != b.StatsOptimized)
        {
            return $"Stats {a.StatsKeys}/{a.StatsValues}/{a.StatsOptimized} -> {b.StatsKeys}/{b.StatsValues}/{b.StatsOptimized}";
        }

        return null;
    }
}
