using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace MC_SVLoadout
{
    internal class SVUtil
    {
        // SV constants
        internal enum LangTextSection { basicandui, weapons, equipment, items, equipmenteffects, dynamicinterface, messagestexts, factions, skills, weaponcomponents }
        internal enum GlobalItemType { none, weapon, equipment, genericitem, ship }
        internal enum AIBehaviourRole { dps, healer, miner }
        internal enum GameMode { standard, arena, casualstreamer }
        internal static Dictionary<int, string> rarityStrings = new Dictionary<int, string>
        {
            { 0, "Poor" },
            { 1, "Common" },
            { 2, "Uncommon" },
            { 3, "Rare" },
            { 4, "Epic" },
            { 5, "Legendary" }
        };

        // Utility constants
        private const string backupFolder = "Backups/";
        private const string saveTempFilename = "Temp.dat";

        // Utility variables
        internal static bool opRunning = false;
        internal static string lastBackupPath = "None.  No files have been modifed.";

        internal static string[] GetSavesList()
        {            
            string[] saveFiles = new string[1];

            if (opRunning)
                return null;

            opRunning = true;

            try
            {
                saveFiles = Directory.GetFiles(Application.dataPath + GameData.saveFolderName, "SaveGameData_??.dat");
                if (saveFiles.Length > 0)
                {
                    for (int i = 0; i < saveFiles.Length; i++)
                        saveFiles[i] = Path.GetFileName(saveFiles[i]);
                }
                else
                {
                    saveFiles[0] = "No saves found";
                }
            }
            catch
            {
                saveFiles[0] = "Error loading saves list";
            }

            opRunning = false;
            return saveFiles;
        }

        internal static GameDataInfo LoadGame(string saveFilePath)
        {
            if (opRunning)
                return null;

            opRunning = true;
            
            if (!File.Exists(saveFilePath))
                throw new Exception("Save file does not exist.");

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            FileStream fileStream = File.Open(saveFilePath, FileMode.Open);
            GameDataInfo gameInfo = (GameDataInfo)binaryFormatter.Deserialize(fileStream);
            fileStream.Close();

            if (gameInfo == null)
                throw new Exception("Failed to load save file data.");

            opRunning = false;
            return gameInfo;
        }

        internal static void SaveGame(GameDataInfo gameInfo, string saveFilePath, string modSaveFolder)
        {
            if (opRunning)
                return;

            opRunning = true;

            lastBackupPath = "None.  No files have been modifed.";
            if (!File.Exists(saveFilePath))
                throw new Exception("Save file path does not exist.");

            lastBackupPath = CreateBackup(saveFilePath, modSaveFolder);

            string tempPath = Path.GetDirectoryName(saveFilePath) + modSaveFolder + saveTempFilename;

            if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            FileStream fileStream = null;
            fileStream = File.Create(tempPath);
            binaryFormatter.Serialize(fileStream, gameInfo);
            fileStream.Close();

            File.Copy(tempPath, saveFilePath, true);
            File.Delete(tempPath);

            opRunning = false;
        }

        private static string CreateBackup(string saveFilePath, string modSaveFolder)
        {
            string backupPath = Path.GetDirectoryName(saveFilePath) + modSaveFolder + backupFolder + Path.GetFileNameWithoutExtension(saveFilePath) + "_" + DateTime.Now.ToString("yyyy-MM-dd--HH-mm") + ".dat";
            if (File.Exists(backupPath))
                throw new Exception();

            if (!Directory.Exists(Path.GetDirectoryName(backupPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));

            File.Copy(saveFilePath, backupPath, false);

            return backupPath;
        }

        internal static GameDataInfo RemoveObjectsFromGameData(GameDataInfo gameDataInfo, List<SVUtil.RemoveReplaceEntry> items)
        {
            SVUtil.opRunning = true;

            gameDataInfo = SVUtil.RemoveReplaceFromShips(true, gameDataInfo, items);
            gameDataInfo = SVUtil.RemoveReplaceFromTowedObjects(true, gameDataInfo, items);
            gameDataInfo = SVUtil.RemoveReplaceFromMarkets(true, gameDataInfo, items);
            gameDataInfo = SVUtil.RemoveReplaceFromSectors(true, gameDataInfo, items);

            SVUtil.opRunning = false;
            return gameDataInfo;
        }

        internal static GameDataInfo ReplaceObjectsInGameData(GameDataInfo gameDataInfo, List<SVUtil.RemoveReplaceEntry> items)
        {
            SVUtil.opRunning = true;

            gameDataInfo = SVUtil.RemoveReplaceFromShips(false, gameDataInfo, items);
            gameDataInfo = SVUtil.RemoveReplaceFromTowedObjects(false, gameDataInfo, items);
            gameDataInfo = SVUtil.RemoveReplaceFromMarkets(false, gameDataInfo, items);
            gameDataInfo = SVUtil.RemoveReplaceFromSectors(false, gameDataInfo, items);

            SVUtil.opRunning = false;
            return gameDataInfo;
        }

        private static GameDataInfo RemoveReplaceFromShips(bool isRemoveOp, GameDataInfo gameInfo, List<RemoveReplaceEntry> items)
        {
            // Space ship data - player            
            if (gameInfo.spaceShipData != null)
                gameInfo.spaceShipData = RemoveReplaceFromShip(isRemoveOp, gameInfo.spaceShipData, items);

            // Stored ships
            if (gameInfo.shipLoadouts != null && gameInfo.shipLoadouts.Count > 0)
                for (int i = 0; i < gameInfo.shipLoadouts.Count; i++)
                    if (gameInfo.shipLoadouts[i].data != null)
                        gameInfo.shipLoadouts[i].data = RemoveReplaceFromShip(isRemoveOp, gameInfo.shipLoadouts[i].data, items);

            // Crew ships
            if (gameInfo.crew != null && gameInfo.crew.Count > 0)
                for (int i = 0; i < gameInfo.crew.Count; i++)
                    if (gameInfo.crew[i].aiChar != null && gameInfo.crew[i].aiChar.shipData != null)
                        gameInfo.crew[i].aiChar.shipData = RemoveReplaceFromShip(isRemoveOp, gameInfo.crew[i].aiChar.shipData, items);

            // Mercenaries
            if (gameInfo.character != null && gameInfo.character.mercenaries != null && gameInfo.character.mercenaries.Count > 0)
                for (int i = 0; i < gameInfo.character.mercenaries.Count; i++)
                    if (gameInfo.character.mercenaries[i].shipData != null)
                        gameInfo.character.mercenaries[i].shipData = RemoveReplaceFromShip(isRemoveOp, gameInfo.character.mercenaries[i].shipData, items);

            return gameInfo;
        }

        private static SpaceShipData RemoveReplaceFromShip(bool isRemoveOp, SpaceShipData ssData, List<RemoveReplaceEntry> items)
        {
            List<RemoveReplaceEntry> equipmentsOnly = RemoveReplaceEntry.GetSubetOfType(items, GlobalItemType.equipment);

            if (ssData.cargo != null && ssData.cargo.Count > 0)
                ssData.cargo = RemoveReplaceFromShipCargo(isRemoveOp, ssData.cargo, items);
            if (equipmentsOnly.Count > 0 && ssData.equipments != null && ssData.equipments.Count > 0)
                ssData.equipments = SVEquipmentUtil.RemoveReplaceFromShipEquipment(isRemoveOp, ssData.equipments, equipmentsOnly);

            return ssData;
        }

        private static List<CargoItem> RemoveReplaceFromShipCargo(bool isRemoveOp, List<CargoItem> cargo, List<RemoveReplaceEntry> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (isRemoveOp)
                    cargo.RemoveAll(ci => ci.itemType == (int)items[i].type && ci.itemID == items[i].targetID);
                else
                    cargo.ForEach(ci => { if (ci.itemType == (int)items[i].type && ci.itemID == items[i].targetID) ci.itemID = items[i].newID; });
            }

            return cargo;
        }

        private static GameDataInfo RemoveReplaceFromTowedObjects(bool isRemoveOp, GameDataInfo gameInfo, List<RemoveReplaceEntry> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (gameInfo.towedObjects != null && gameInfo.towedObjects.Count > 0)
                    if (isRemoveOp)
                        gameInfo.towedObjects.RemoveAll(to => to.driftingObject.itemType == (int)items[i].type && to.driftingObject.itemID == items[i].targetID);
                    else
                        gameInfo.towedObjects.ForEach(to => { if (to.driftingObject.itemType == (int)items[i].type && to.driftingObject.itemID == items[i].targetID) to.driftingObject.itemID = items[i].newID; });
            }
            return gameInfo;
        }

        private static GameDataInfo RemoveReplaceFromMarkets(bool isRemoveOp, GameDataInfo gameInfo, List<RemoveReplaceEntry> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                // Stations list
                foreach (Station s in gameInfo.stationList)
                    if (s.market != null && s.market.Count > 0)
                        if (isRemoveOp)
                            s.market.RemoveAll(mi => mi.itemType == (int)items[i].type && mi.itemID == items[i].targetID);
                        else
                            s.market.ForEach(mi => { if (mi.itemType == (int)items[i].type && mi.itemID == items[i].targetID) mi.itemID = items[i].newID; });

                // Arena market
                if (gameInfo.arenaData != null &&
                    gameInfo.arenaData.currMarket != null &&
                    gameInfo.arenaData.currMarket.Count > 0)
                    if (isRemoveOp)
                        gameInfo.arenaData.currMarket.RemoveAll(mi => mi.itemType == (int)items[i].type && mi.itemID == items[i].targetID);
                    else
                        gameInfo.arenaData.currMarket.ForEach(mi => { if (mi.itemType == (int)items[i].type && mi.itemID == items[i].targetID) mi.itemID = items[i].newID; });
            }

            return gameInfo;
        }

        private static GameDataInfo RemoveReplaceFromSectors(bool isRemoveOp, GameDataInfo gameInfo, List<RemoveReplaceEntry> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (gameInfo.sectors != null && gameInfo.sectors.Count > 0)
                    foreach (TSector sec in gameInfo.sectors)
                        if (sec.driftingObjects != null && sec.driftingObjects.Count > 0)
                            if (isRemoveOp)
                                sec.driftingObjects.RemoveAll(dro => dro.itemType == (int)items[i].type && dro.itemID == items[i].targetID);
                            else
                                sec.driftingObjects.ForEach(dro => { if (dro.itemType == (int)items[i].type && dro.itemID == items[i].targetID) dro.itemID = items[i].newID; });

                if (gameInfo.lastSector != null)
                    if (isRemoveOp)
                        gameInfo.lastSector.driftingObjects.RemoveAll(dro => dro.itemType == (int)items[i].type && dro.itemID == items[i].targetID);
                    else
                        gameInfo.lastSector.driftingObjects.ForEach(dro => { if (dro.itemType == (int)items[i].type && dro.itemID == items[i].targetID) dro.itemID = items[i].newID; });
            }

            return gameInfo;
        }

        internal class RemoveReplaceEntry
        {
            internal int targetID;
            internal int newID;
            internal GlobalItemType type;

            internal static List<RemoveReplaceEntry> GetSubetOfType(List<RemoveReplaceEntry> items, GlobalItemType type)
            {
                List<RemoveReplaceEntry> subset = new List<RemoveReplaceEntry>();

                if (items == null && items.Count > 0)
                    return subset;

                foreach (RemoveReplaceEntry item in items)
                    if (item.type == type)
                        subset.Add(item);

                return subset;
            }
        }
    }

    internal class SVEquipmentUtil
    {
        internal static int GetNextID(List<Equipment> equipments)
        {
            int id = -1;
            if (equipments == null || equipments.Count == 0)
                return id;

            foreach (Equipment equipment in equipments)
                if (equipment.id > id)
                    id = equipment.id;
            id++;

            return id;
        }

        internal static int AddToEffectsTextSection(string effectText)
        {
            LanguageTextStruct[] lang = AccessTools.StaticFieldRefAccess<Lang, LanguageTextStruct[]>("section");
            lang[(int)SVUtil.LangTextSection.equipmenteffects].text.Add(effectText);
            return lang[(int)SVUtil.LangTextSection.equipmenteffects].text.Count - 1;
        }

        internal static GameDataInfo AddToRandomStations(GameDataInfo gameDataInfo, Equipment equipment)
        {
            System.Random rand = new System.Random();
            
            foreach (Station s in gameDataInfo.stationList)
            {
                if (s.level >= equipment.itemLevel &&
                    rand.Next(1, 100) <= equipment.sellChance &&
                    s.market != null && s.market.Count > 0)
                {
                    MarketItem mi = new MarketItem((int)SVUtil.GlobalItemType.equipment, equipment.id, (int)ItemRarity.Common_1, rand.Next(1, 5), null);
                    s.market.Add(mi);
                }
            }

            return gameDataInfo;
        }

        internal static GameDataInfo RemoveEquipment(GameDataInfo gameDataInfo, int equipmentID)
        {
            return SVUtil.RemoveObjectsFromGameData(gameDataInfo, new List<SVUtil.RemoveReplaceEntry> { 
                new SVUtil.RemoveReplaceEntry { 
                    targetID = equipmentID, 
                    newID = 0, 
                    type = SVUtil.GlobalItemType.equipment 
                } 
            });
        }

        internal static GameDataInfo ReplaceEquipment(GameDataInfo gameDataInfo, int targetID, int newID)
        {
            return SVUtil.ReplaceObjectsInGameData(gameDataInfo, new List<SVUtil.RemoveReplaceEntry> { 
                new SVUtil.RemoveReplaceEntry { 
                    targetID = targetID, 
                    newID = newID, 
                    type = SVUtil.GlobalItemType.equipment 
                } 
            });
        }

        internal static List<InstalledEquipment> RemoveReplaceFromShipEquipment(bool isRemoveOp, List<InstalledEquipment> equipments, List<SVUtil.RemoveReplaceEntry> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (isRemoveOp)
                    equipments.RemoveAll(ie => ie.equipmentID == items[i].targetID);
                else
                    equipments.ForEach(ie => { if (ie.equipmentID == items[i].targetID) ie.equipmentID = items[i].newID; });
            }

            return equipments;
        }
    }
    internal class SVItemUtil
    {
        private static List<Item> items = AccessTools.StaticFieldRefAccess<ItemDB, List<Item>>("items");

        internal static int GetNextID(List<Item> items)
        {
            int id = -1;
            if (items == null || items.Count == 0)
                return id;

            foreach (Item item in items)
                if (item.id > id)
                    id = item.id;
            id++;

            return id;
        }

        internal static void ReplaceInDB(int targetItemID, Item newItem)
        {
            if (items != null && items.Count > 0)
                foreach (Item item in items)
                    if (item.id == targetItemID)
                    {
                        items[targetItemID] = newItem;
                        break;
                    }
        }
    }
}
