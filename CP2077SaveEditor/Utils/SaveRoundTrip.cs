using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.IO;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Utils
{
    /// <summary>
    /// Parse / serialize helpers with a mandatory re-parse check so we never write a silently corrupted save.
    /// </summary>
    public static class SaveRoundTrip
    {
        public sealed class Result
        {
            public bool Success { get; init; }
            public string Message { get; init; }
            public CyberpunkSaveFile Save { get; init; }
            public byte[] SerializedBytes { get; init; }
            public SaveDiagnostics Diagnostics { get; init; }
        }

        public sealed class SaveDiagnostics
        {
            public uint GameVersion { get; init; }
            public uint SaveVersion { get; init; }
            public int NodeCount { get; init; }
            public IReadOnlyList<string> NodeNames { get; init; }
            public int InventoryItemCount { get; init; }
            public int FactCount { get; init; }
            public int StatsKeys { get; init; }
            public int StatsValues { get; init; }
            public int StatsValuesOptimized { get; init; }
            public bool HasAppearanceNode { get; init; }
            public bool HasQuestSystem { get; init; }
            public bool HasPersistencySystem { get; init; }
            public bool UsesOptimizedStats => StatsValuesOptimized > 0;
        }

        public static Result Load(Stream stream)
        {
            using var reader = new CyberpunkSaveReader(stream, Encoding.UTF8, leaveOpen: true);
            var status = reader.ReadFile(out var save);
            return status switch
            {
                EFileReadErrorCodes.NoError when save != null => new Result
                {
                    Success = true,
                    Message = "OK",
                    Save = save,
                    Diagnostics = BuildDiagnostics(save)
                },
                EFileReadErrorCodes.NoCSav => new Result
                {
                    Success = false,
                    Message = "Failed to parse save file: File contains invalid data"
                },
                EFileReadErrorCodes.UnsupportedVersion => UnsupportedVersionMessage(stream, reader),
                _ => new Result { Success = false, Message = $"Failed to parse save file: {status}" }
            };
        }

        public static Result LoadFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Load(fs);
        }

        /// <summary>
        /// Serialize <paramref name="save"/> and re-parse the bytes. On failure, SerializedBytes is null.
        /// </summary>
        public static Result SerializeAndValidate(CyberpunkSaveFile save)
        {
            byte[] bytes;
            try
            {
                using var ms = new MemoryStream();
                using (var writer = new CyberpunkSaveWriter(ms, Encoding.UTF8, leaveOpen: true))
                {
                    writer.WriteFile(save, compress: true);
                }

                bytes = ms.ToArray();
            }
            catch (Exception ex)
            {
                return new Result
                {
                    Success = false,
                    Message = "Serialize failed: " + ex.Message
                };
            }

            try
            {
                using var reparseStream = new MemoryStream(bytes);
                var reparsed = Load(reparseStream);
                if (!reparsed.Success)
                {
                    return new Result
                    {
                        Success = false,
                        Message = "Re-parse after serialize failed: " + reparsed.Message
                    };
                }

                var originalDiag = BuildDiagnostics(save);
                var roundTripDiag = reparsed.Diagnostics;
                var mismatch = CompareDiagnostics(originalDiag, roundTripDiag);
                if (mismatch != null)
                {
                    return new Result
                    {
                        Success = false,
                        Message = "Round-trip validation failed: " + mismatch,
                        Diagnostics = roundTripDiag
                    };
                }

                return new Result
                {
                    Success = true,
                    Message = "OK",
                    Save = reparsed.Save,
                    SerializedBytes = bytes,
                    Diagnostics = roundTripDiag
                };
            }
            catch (Exception ex)
            {
                return new Result
                {
                    Success = false,
                    Message = "Re-parse after serialize threw: " + ex.Message
                };
            }
        }

        public static SaveDiagnostics BuildDiagnostics(CyberpunkSaveFile save)
        {
            var nodeNames = save.Nodes.Select(n => n.Name).ToList();
            var inventoryItems = 0;
            var factCount = 0;
            var statsKeys = 0;
            var statsValues = 0;
            var statsOptimized = 0;

            try
            {
                var invNode = save.Nodes.FirstOrDefault(x => x.Name == "inventory");
                if (invNode?.Value is Inventory inventory)
                {
                    inventoryItems = inventory.SubInventories.Sum(s => s.Items?.Count ?? 0);
                }
            }
            catch
            {
                // ignore diagnostic failures
            }

            try
            {
                var quest = save.Nodes.FirstOrDefault(x => x.Name == "questSystem");
                var facts = quest?.Children?.FirstOrDefault(x => x.Name == "FactsDB");
                if (facts?.Value is FactsDB factsDb)
                {
                    factCount = factsDb.FactsTables?.Sum(t => t.FactEntries?.Count ?? 0) ?? 0;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                var statsNode = save.Nodes.FirstOrDefault(x => x.Name == "StatsSystem");
                if (statsNode?.Value is Package package && package.Content is RedPackage redPackage
                    && redPackage.RootChunk is gameStatsStateMapStructure map)
                {
                    statsKeys = map.Keys?.Count ?? 0;
                    statsValues = map.Values?.Count ?? 0;
                    statsOptimized = map.ValuesOptimized?.Count ?? 0;
                }
            }
            catch
            {
                // ignore
            }

            return new SaveDiagnostics
            {
                GameVersion = save.FileHeader.GameVersion,
                SaveVersion = save.FileHeader.SaveVersion,
                NodeCount = save.Nodes.Count,
                NodeNames = nodeNames,
                InventoryItemCount = inventoryItems,
                FactCount = factCount,
                StatsKeys = statsKeys,
                StatsValues = statsValues,
                StatsValuesOptimized = statsOptimized,
                HasAppearanceNode = nodeNames.Contains("CharacetrCustomization_Appearances"),
                HasQuestSystem = nodeNames.Contains("questSystem"),
                HasPersistencySystem = nodeNames.Contains("PersistencySystem") || nodeNames.Contains("PSData")
                    || nodeNames.Any(n => n.Contains("Persistency", StringComparison.OrdinalIgnoreCase))
            };
        }

        private static string CompareDiagnostics(SaveDiagnostics a, SaveDiagnostics b)
        {
            if (a.NodeCount != b.NodeCount)
            {
                return $"node count {a.NodeCount} -> {b.NodeCount}";
            }

            if (!a.NodeNames.SequenceEqual(b.NodeNames))
            {
                return "node name list mismatch";
            }

            if (a.GameVersion != b.GameVersion)
            {
                return $"game version {a.GameVersion} -> {b.GameVersion}";
            }

            if (a.InventoryItemCount != b.InventoryItemCount)
            {
                return $"inventory item count {a.InventoryItemCount} -> {b.InventoryItemCount}";
            }

            if (a.FactCount != b.FactCount)
            {
                return $"fact count {a.FactCount} -> {b.FactCount}";
            }

            if (a.StatsKeys != b.StatsKeys || a.StatsValues != b.StatsValues || a.StatsValuesOptimized != b.StatsValuesOptimized)
            {
                return $"stats layout keys/values/optimized {a.StatsKeys}/{a.StatsValues}/{a.StatsValuesOptimized} -> {b.StatsKeys}/{b.StatsValues}/{b.StatsValuesOptimized}";
            }

            return null;
        }

        private static Result UnsupportedVersionMessage(Stream stream, CyberpunkSaveReader reader)
        {
            try
            {
                stream.Position = 0;
                reader.ReadFileInfo(out var info);
                return new Result
                {
                    Success = false,
                    Message = $"Failed to parse save file: Game version {info?.GameVersion} is not supported!"
                };
            }
            catch
            {
                return new Result
                {
                    Success = false,
                    Message = "Failed to parse save file: unsupported game version"
                };
            }
        }
    }
}
