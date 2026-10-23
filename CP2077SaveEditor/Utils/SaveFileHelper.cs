using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CP2077SaveEditor.Utils;
using WolvenKit.RED4.Save;
using static WolvenKit.RED4.Types.Enums;
using WolvenKit.RED4.Types;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Save.Classes;

namespace CP2077SaveEditor
{
    public class SaveFileHelper
    {
        public AppearanceHelper2 Appearance { get; }
        public CyberpunkSaveFile SaveFile { get; set; }
        public Dictionary<uint, string> KnownFacts;

        public byte[] Metadata { get; set; }
        public byte[] ImageData { get; set; }

        public SaveFileHelper() : base()
        {
            Appearance = new AppearanceHelper2(this);
        }

        public gameuiCharacterCustomizationPresetWrapper GetAppearanceContainer()
        {
            return (gameuiCharacterCustomizationPresetWrapper)SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == "CharacetrCustomization_Appearances")].Value;
        }

        public void SetAppearanceContainer(gameuiCharacterCustomizationPresetWrapper preset)
        {
            SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == "CharacetrCustomization_Appearances")].Value = preset;
        }

        public Inventory GetInventoriesContainer()
        {
            return (Inventory)SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == "inventory")].Value;
        }

        public NodeEntry GetFactsContainer()
        {
            var questSystem = SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == "questSystem")]; return questSystem.Children[questSystem.Children.FindIndex(x => x.Name == "FactsDB")];
        }

        public gameStatsStateMapStructure GetStatsContainer()
        {
            return (gameStatsStateMapStructure)((RedPackage)((Package)SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == "StatsSystem")].Value).Content).RootChunk;
        }

        public RedPackage GetScriptableContainer()
        {
            return (RedPackage)((Package)SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == "ScriptableSystemsContainer")].Value).Content;
        }

        public T GetScriptableSystem<T>() where T : gameScriptableSystem
        {
            return (T)GetScriptableContainer().Chunks.FirstOrDefault(x => x is T);
        }

        public PlayerDevelopmentData GetPlayerDevelopmentData()
        {
            var devSystem = (PlayerDevelopmentSystem)this.GetScriptableContainer().Chunks.FirstOrDefault(x => x is PlayerDevelopmentSystem);
            return devSystem.PlayerData.FirstOrDefault(x => x.Chunk.OwnerID.Hash == 1).Chunk;
        }

        public PersistencySystem2 GetPSDataContainer()
        {
            return GetSystem<PersistencySystem2>();
        }

        public RedPackage GetGodModeSystem()
        {
            return (RedPackage)((Package)SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == Constants.NodeNames.GOD_MODE_SYSTEM)].Value).Content;
        }

        public T GetSystem<T>()
        {
            return (T)SaveFile.Nodes.FirstOrDefault(x => x.Value.GetType() == typeof(T))?.Value;
        }

        public Dictionary<gameItemID, string> GetEquippedItems()
        {
            var playerEquipAreas = GetEquipAreas();
            var equippedItems = new Dictionary<gameItemID, string>();

            var weaponSlotNum = 1;
            foreach (gameSEquipArea area in playerEquipAreas)
            {
                var areaName = area.AreaType.ToString();
                if (area.EquipSlots != null)
                {
                    foreach (gameSEquipSlot slot in area.EquipSlots)
                    {
                        if (area.AreaType == gamedataEquipmentArea.Weapon)
                        {
                            areaName = "Weapon " + weaponSlotNum.ToString();
                            weaponSlotNum++;
                        }
                        if (slot.ItemID != null)
                        {
                            equippedItems.Add(slot.ItemID, areaName);
                        }
                    }
                }
            }
            return equippedItems;
        }

        public CArray<gameSEquipArea> GetEquipAreas()
        {
            var equipSystem = (EquipmentSystem)this.GetScriptableContainer().Chunks.FirstOrDefault(x => x is EquipmentSystem);
            return equipSystem.OwnerData.FirstOrDefault(x => x.Chunk.OwnerID.Hash == 1).Chunk.Equipment.EquipAreas;
        }

        public List<gameSEquipSlot> GetEquipSlotsFromID(gameItemID id)
        {
            var slots = new List<gameSEquipSlot>();
            foreach (gameSEquipArea area in this.GetEquipAreas())
            {
                if (area.EquipSlots != null)
                {
                    foreach (gameSEquipSlot slot in area.EquipSlots)
                    {
                        if (slot.ItemID != null && slot.ItemID.Id == id.Id)
                        {
                            slots.Add(slot);
                        }
                    }
                }
            }
            return slots;
        }

        public gameSavedStatsData GetStatsFromEntityId(ulong hash)
        {
            return GetStatsFromStatsObjectId(new gameStatsObjectID { IdType = gameStatIDType.EntityID, EntityHash = hash });
        }

        public gameSavedStatsData GetStatsFromItemId(gameItemID itemId)
        {
            return GetStatsFromStatsObjectId(new gameStatsObjectID { IdType = gameStatIDType.ItemID, EntityHash = InventoryHelper.GetItemIdHash(itemId) });
        }

        public gameSavedStatsData GetStatsFromStatsObjectId(gameStatsObjectID id)
        {
            var map = GetStatsContainer();
            var index = map.Keys.IndexOf(id);

            if (index >= 0 && index < map.Values.Count)
            {
                var legacy = map.Values[index];
                if (HasUsableLegacyStats(legacy))
                {
                    return legacy;
                }
            }

            // Update 2.4 / 2.5 may store stats in ValuesOptimized instead of Values.
            if (map.ValuesOptimized is { Count: > 0 })
            {
                for (var i = 0; i < map.ValuesOptimized.Count; i++)
                {
                    var optimized = map.ValuesOptimized[i];
                    if (optimized.EntityHash == id.EntityHash && optimized.IdType.Equals(id.IdType))
                    {
                        return MaterializeOptimizedStats(map, id, optimized, index, i);
                    }
                }
            }

            if (index >= 0 && index < map.Values.Count)
            {
                return map.Values[index];
            }

            return null;
        }

        private static bool HasUsableLegacyStats(gameSavedStatsData data)
        {
            if (data == null)
            {
                return false;
            }

            if (data.ModifiersBuffer?.Data != null)
            {
                return true;
            }

            if (data.ForcedModifiersBuffer?.Data != null)
            {
                return true;
            }

            if (data.StatModifiers is { Count: > 0 })
            {
                return true;
            }

            if (data.Seed != 0 || (ulong)data.RecordID != 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Expand a ValuesOptimized entry into the legacy Keys/Values layout the editor mutates,
        /// then remove the optimized entry so we do not keep two copies of the same object.
        /// </summary>
        private static gameSavedStatsData MaterializeOptimizedStats(
            gameStatsStateMapStructure map,
            gameStatsObjectID id,
            gameSavedStatsDataOptimized optimized,
            int keyIndex,
            int optimizedIndex)
        {
            var data = new gameSavedStatsData
            {
                Seed = optimized.Seed,
                StatModifiers = optimized.StatModifiers ?? new CArray<CHandle<gameStatModifierData_Deprecated>>(),
                InactiveStats = new CArray<CEnum<gamedataStatType>>()
            };

            if (map.SharedRecordId != null && optimized.RecordIDIndex < map.SharedRecordId.Count)
            {
                data.RecordID = map.SharedRecordId[optimized.RecordIDIndex];
            }

            if (map.SharedInactiveStats != null && optimized.InactiveStatsIndices != null)
            {
                foreach (var inactiveIndex in optimized.InactiveStatsIndices)
                {
                    if (inactiveIndex < map.SharedInactiveStats.Count)
                    {
                        data.InactiveStats.Add(map.SharedInactiveStats[inactiveIndex]);
                    }
                }
            }

            // Prefer StatModifiers handles. SharedDataBuffer slices (BufferSliceIndices) are a
            // denser 2.5 packing; without a verified sample we only expand what we can safely map.
            var modifiers = new ModifiersBuffer();
            if (data.StatModifiers != null)
            {
                foreach (var handle in data.StatModifiers)
                {
                    if (handle?.Chunk is gameStatModifierData_Deprecated modifier)
                    {
                        modifiers.Entries.Add(modifier);
                    }
                }
            }

            data.ModifiersBuffer = new DataBuffer { Data = modifiers };

            if (keyIndex < 0)
            {
                map.Keys.Add(id);
                map.Values.Add(data);
            }
            else
            {
                while (map.Values.Count <= keyIndex)
                {
                    map.Values.Add(new gameSavedStatsData());
                }

                map.Values[keyIndex] = data;
            }

            map.ValuesOptimized.RemoveAt(optimizedIndex);
            return data;
        }

        public void SetConstantStat(gamedataStatType stat, float value, gameSavedStatsData statsData, gameStatModifierType mod = gameStatModifierType.Additive)
        {
            var foundStat = false;
            foreach (gameStatModifierData_Deprecated modifier in ((ModifiersBuffer)statsData.ModifiersBuffer.Data!).Entries)
            {
                if (modifier is gameConstantStatModifierData_Deprecated constantModifier)
                {
                    if (constantModifier.StatType == stat && constantModifier.ModifierType == mod)
                    {
                        constantModifier.Value = value;
                        foundStat = true;
                    }
                }
            }

            if (!foundStat)
            {
                var newModifierData = new gameConstantStatModifierData_Deprecated
                {
                    ModifierType = mod,
                    StatType = stat,
                    Value = value
                };

                this.AddStat(typeof(gameConstantStatModifierData_Deprecated), statsData, newModifierData);
            }
        }

        public gameStatModifierData_Deprecated AddStat(Type statType, gameSavedStatsData statsData, gameStatModifierData_Deprecated modifierData = null)
        {
            if (modifierData == null)
            {
                modifierData = (gameStatModifierData_Deprecated)System.Activator.CreateInstance(statType);

                if (modifierData is gameCurveStatModifierData_Deprecated curveStat)
                {
                    curveStat.ColumnName = "<null>";
                    curveStat.CurveName = "<null>";
                }
            }

            statsData.ModifiersBuffer.Data ??= new ModifiersBuffer();

            ((ModifiersBuffer)statsData.ModifiersBuffer.Data).Entries.Add(modifierData);

            return modifierData;
        }

        public uint CreateUniqueSeed(gameStatsStateMapStructure statsMap = null)
        {
            statsMap ??= GetStatsContainer();

            var rand = new Random();

            do
            {
                var seed = (uint)(rand.Next(1 << 30)) << 2 | (uint)(rand.Next(1 << 2));
                var usedInValues = statsMap.Values != null && statsMap.Values.Any(x => x.Seed == seed);
                var usedInOptimized = statsMap.ValuesOptimized != null && statsMap.ValuesOptimized.Any(x => x.Seed == seed);
                if (!usedInValues && !usedInOptimized)
                {
                    return seed;
                }
            } while (true);
        }

        public gameSavedStatsData CreateStatDataFromEntityId(ulong id)
        {
            var statsMap = GetStatsContainer();

            statsMap.Keys.Add(new gameStatsObjectID
            {
                IdType = gameStatIDType.EntityID,
                EntityHash = 1
            });

            var inactiveStats = new CArray<CEnum<gamedataStatType>>
            {
                gamedataStatType.Level,
                gamedataStatType.QuickHackUpload
            };

            var modifierBuffer = new ModifiersBuffer();
            modifierBuffer.Entries.Add(new gameConstantStatModifierData_Deprecated
            {
                StatType = gamedataStatType.Humanity,
                ModifierType = gameStatModifierType.Additive,
                Value = 2
            });

            var statsData = new gameSavedStatsData
            {
                RecordID = "Character.Player_Puppet_Base",
                Seed = CreateUniqueSeed(statsMap)
            };

            statsMap.Values.Add(statsData);

            return statsData;
        }

        public gameSavedStatsData CreateStatData(gameItemID gameItemId)
        {
            var statsMap = GetStatsContainer();

            if (gameItemId.RngSeed == 0)
            {
                gameItemId.RngSeed = CreateUniqueSeed(statsMap);
            }

            if (gameItemId.UniqueCounter == 0)
            {
                gameItemId.RngSeed = GetNextUniqueCounter();
            }

            statsMap.Keys.Add(new gameStatsObjectID
            {
                IdType = gameStatIDType.ItemID,
                EntityHash = InventoryHelper.GetItemIdHash(gameItemId)
            });

            var statsData = new gameSavedStatsData
            {
                RecordID = gameItemId.Id,
                Seed = gameItemId.RngSeed,
                StatModifiers = new(),
                ModifiersBuffer = new DataBuffer
                {
                    Data = new ModifiersBuffer()
                }
            };

            if (ResourceHelper.ItemClasses.TryGetValue(gameItemId.Id, out var itemRecord))
            {
                if (itemRecord.Type == "Weapon" || itemRecord.Type == "WeaponItem")
                {
                    statsData.InactiveStats.Add(Enums.gamedataStatType.IsItemPlus);

                    ((ModifiersBuffer)statsData.ModifiersBuffer.Data).Entries.Add(new gameConstantStatModifierData_Deprecated
                    {
                        StatType = gamedataStatType.IsItemPlus,
                        ModifierType = gameStatModifierType.Additive,
                        Value = itemRecord.IsItemPlus
                    });
                }

                if (itemRecord.Type == "Clothing")
                {
                    ((ModifiersBuffer)statsData.ModifiersBuffer.Data).Entries.Add(new gameConstantStatModifierData_Deprecated
                    {
                        StatType = gamedataStatType.LootLevel,
                        ModifierType = gameStatModifierType.Additive,
                        Value = 1
                    });
                }
            }

            statsMap.Values.Add(statsData);

            return statsData;
        }

        public List<FactsTable.FactEntry> GetKnownFacts()
        {
            if (KnownFacts == null)
            {
                KnownFacts = JsonSerializer.Deserialize<Dictionary<uint, string>>(CP2077SaveEditor.Properties.Resources.Facts);
            }

            var factsList = new List<FactsTable.FactEntry>();
            foreach (NodeEntry tableEntry in this.GetFactsContainer().Children)
            {
                var tableData = (FactsTable)tableEntry.Value;
                foreach (FactsTable.FactEntry fact in tableData.FactEntries)
                {
                    var hash = (uint)fact.FactName;

                    if (KnownFacts.ContainsKey(hash))
                    {
                        factsList.Add(fact);
                    }
                }
            }
            return (factsList);
        }

        public bool SetFactByName(string factName, uint factValue)
        {
            var facts = GetKnownFacts();

            if (KnownFacts.ContainsValue(factName) && facts.Any(x => KnownFacts[x.FactName] == factName))
            {
                facts.First(x => KnownFacts[x.FactName] == factName).Value = factValue;
                return true;
            }
            else
            {
                return AddFactByName(factName, factValue);
            }
        }

        public bool AddFactByName(string factName, uint factValue)
        {
            if (!KnownFacts.Values.Contains(factName))
            {
                return false;
            }

            var factHash = KnownFacts.FirstOrDefault(x => x.Value == factName).Key;
            AddFactByHash(factHash, factValue);
            return true;
        }

        public void AddFactByHash(uint factHash, uint factValue)
        {
            var newFact = new FactsTable.FactEntry();
            newFact.FactName = factHash;
            newFact.Value = factValue;

            ((FactsDB)GetFactsContainer().Value).FactsTables[0].FactEntries.Add(newFact);
        }

        public SubInventory GetInventory(ulong id)
        {
            return this.GetInventoriesContainer().SubInventories.FirstOrDefault(x => x.InventoryId == id);
        }

        public ushort GetNextUniqueCounter()
        {
            var uniqueItemCounterNode = (UniqueItemCounter)SaveFile.Nodes[SaveFile.Nodes.FindIndex(x => x.Name == Constants.NodeNames.UNIQUE_ITEM_COUNTER)].Value;

            return ++uniqueItemCounterNode.Count;
        }
    }

}
