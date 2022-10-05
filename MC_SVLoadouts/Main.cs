using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MC_SVLoadout
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        // BepInEx
        public const string pluginGuid = "mc.starvalor.loadouts";
        public const string pluginName = "SV Loadouts";
        public const string pluginVersion = "0.0.3";

        // Mod
        private const int hangerPanelCode = 3;        
        private const string modSaveFolder = "/MCSVSaveData/";  // /SaveData/ sub folder
        private const string modSaveFilePrefix = "Loadouts_"; // modSaveFilePrefixNN.dat
        private static PersistentData data;
        private static GameObject btnDockUILoad;
        private static GameObject btnDockUISave;
        private static GameObject pnlSaveLoadout;
        private static GameObject pnlConfirmReplace;
        private static GameObject pnlLoadLoadout;
        private static GameObject txtSaveLoadoutName;
        private static Transform scrlpnlLoadoutList;
        private static GameObject scrlpnlListItemTemplate;
        private static ShipInfo shipInfo;
        private static string saveLoadoutName;
        private static string loadLoadoutName;
        private static bool loadRequest = false;
        private static AccessTools.FieldRef<ShipInfo, int> shipInfoGearModeRef = AccessTools.FieldRefAccess<ShipInfo, int>("gearMode");

        // Debug
        internal static BepInEx.Logging.ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("SV Loadouts");

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));
        }

        public void Update()
        {
            if(loadRequest)
                DockingUI_LoadLoadoutBtnAction();
        }

        [HarmonyPatch(typeof(DockingUI), nameof(DockingUI.OpenPanel))]
        [HarmonyPostfix]
        private static void DocingUIOpenPanel_Post(ShipInfo ___shipInfo, Inventory ___inventory, WeaponCrafting ___weaponCrafting, int code)
        {
            if (code == hangerPanelCode)
            {
                shipInfo = ___shipInfo;

                if (btnDockUILoad == null || btnDockUISave == null || pnlConfirmReplace == null ||
                        pnlSaveLoadout == null)
                    CreateUI(___shipInfo, ___inventory, ___weaponCrafting);

                btnDockUILoad.SetActive(true);
                btnDockUISave.SetActive(true);
            }
        }

        [HarmonyPatch(typeof(DockingUI), nameof(DockingUI.CloseDockingStation))]
        [HarmonyPrefix]
        private static void DockingUICloseDockingStation_Pre()
        {
            if (btnDockUILoad != null)
                btnDockUILoad.SetActive(false);
            if (btnDockUISave != null)
                btnDockUISave.SetActive(false);
            if (pnlSaveLoadout != null)
                pnlSaveLoadout.SetActive(false);
            if (pnlConfirmReplace != null)
                pnlConfirmReplace.SetActive(false);
            if (pnlLoadLoadout != null)
                pnlLoadLoadout.SetActive(false);
        }

        private static void CreateUI(ShipInfo shipInfo, Inventory inventory, WeaponCrafting weaponCrafting)
        {   
            Button.ButtonClickedEvent btnClickEvent;

            // Docing UI buttons
            GameObject templateBtn = ((Transform)AccessTools.Field(typeof(ShipInfo), "equipGO").GetValue(shipInfo)).Find("BtnRemove").gameObject;
            GameObject templateScroll = ((GameObject)AccessTools.Field(typeof(Inventory), "invGO").GetValue(inventory)).transform.Find("ScrollView").gameObject;            
            GameObject refBtn = GameObject.Find("BtnRemoveAll");            
            btnDockUILoad = Instantiate(templateBtn);
            btnDockUILoad.name = "BtnLoadLoadout";
            btnDockUILoad.SetActive(true);
            btnDockUILoad.GetComponentInChildren<Text>().text = "Load Loadout";
            btnDockUILoad.SetActive(false);
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(DockingUI_LoadLoadoutBtnAction));
            btnDockUILoad.GetComponentInChildren<Button>().onClick = btnClickEvent;
            btnDockUILoad.transform.SetParent(refBtn.transform.parent);
            btnDockUILoad.layer = refBtn.layer;            
            btnDockUILoad.transform.position = new Vector3(refBtn.transform.position.x,
                refBtn.transform.position.y - (refBtn.GetComponent<RectTransform>().rect.height * 1.5f),
                refBtn.transform.position.z); ;
            btnDockUILoad.transform.localScale = refBtn.transform.localScale;

            btnDockUISave = Instantiate(templateBtn);
            btnDockUISave.name = "BtnSaveLoadout";
            btnDockUISave.SetActive(true);
            btnDockUISave.GetComponentInChildren<Text>().text = "Save Loadout";
            btnDockUISave.SetActive(false);
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(DockingUI_SaveLoadoutBtnAction));
            btnDockUISave.GetComponentInChildren<Button>().onClick = btnClickEvent;
            btnDockUISave.transform.SetParent(refBtn.transform.parent);
            btnDockUISave.layer = refBtn.layer;
            btnDockUISave.transform.position = new Vector3(refBtn.transform.position.x,
                refBtn.transform.position.y - ((refBtn.GetComponent<RectTransform>().rect.height * 1.5f) * 2),
                refBtn.transform.position.z);
            btnDockUISave.transform.localScale = refBtn.transform.localScale;

            // Panels
            GameObject templatePanel = (GameObject)AccessTools.Field(typeof(Inventory), "confirmPanel").GetValue(inventory);
            
            // Save loadout panel
            pnlSaveLoadout = Instantiate(templatePanel);
            pnlSaveLoadout.transform.SetParent(templatePanel.transform.parent);
            pnlSaveLoadout.transform.position = templatePanel.transform.position;
            pnlSaveLoadout.layer = templatePanel.layer;
            Transform pnlSaveMainText = pnlSaveLoadout.transform.Find("MainText");
            pnlSaveLoadout.SetActive(true);
            pnlSaveMainText.GetComponent<Text>().text = "Save Loadout";
            pnlSaveLoadout.SetActive(false);
            txtSaveLoadoutName = Instantiate(((GameObject)AccessTools.Field(typeof(WeaponCrafting), "MainPanel").GetValue(weaponCrafting)).transform.Find("Result").Find("EdtWeaponName").gameObject);
            txtSaveLoadoutName.name = "txtLoadoutName";
            txtSaveLoadoutName.transform.SetParent(pnlSaveLoadout.transform);
            txtSaveLoadoutName.transform.position = new Vector3(pnlSaveMainText.position.x,
                pnlSaveMainText.position.y - (txtSaveLoadoutName.GetComponent<RectTransform>().rect.height * 1.5f),
                pnlSaveMainText.position.z);
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(SavePanel_Cancel));
            pnlSaveLoadout.transform.Find("BtnCancel").GetComponentInChildren<Button>().onClick = btnClickEvent;
            Transform saveButton = pnlSaveLoadout.transform.Find("BtnYes");            
            saveButton.GetComponentInChildren<Text>().text = "Save";            
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(SavePanel_Save));
            saveButton.GetComponentInChildren<Button>().onClick = btnClickEvent;

            // Confirm replace loadout panel
            pnlConfirmReplace = Instantiate(templatePanel);
            pnlConfirmReplace.transform.SetParent(templatePanel.transform.parent);
            pnlConfirmReplace.transform.position = templatePanel.transform.position;
            pnlConfirmReplace.layer = templatePanel.layer;
            pnlConfirmReplace.SetActive(true);
            pnlConfirmReplace.GetComponentInChildren<Text>().text = "Replace existing loadout?";
            pnlConfirmReplace.SetActive(false);
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(ReplacePanel_Cancel));
            pnlConfirmReplace.transform.Find("BtnCancel").GetComponentInChildren<Button>().onClick = btnClickEvent;
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(ReplacePanel_Yes));
            pnlConfirmReplace.transform.Find("BtnYes").GetComponentInChildren<Button>().onClick = btnClickEvent;

            // Load loadout panel
            pnlLoadLoadout = Instantiate(templatePanel);
            pnlLoadLoadout.transform.SetParent(templatePanel.transform.parent);
            pnlLoadLoadout.transform.position = templatePanel.transform.position;            
            pnlLoadLoadout.transform.Find("BG").localScale = new Vector3(1, 2, 1);            
            pnlLoadLoadout.layer = templatePanel.layer;
            Text mainText = pnlLoadLoadout.GetComponentInChildren<Text>();
            mainText.transform.position = new Vector3(mainText.transform.position.x,
                pnlLoadLoadout.transform.position.y + (pnlLoadLoadout.GetComponent<RectTransform>().rect.height / 1.5f),
                mainText.transform.position.z);
            pnlLoadLoadout.SetActive(true);
            mainText.text = "Load Loadout";
            pnlLoadLoadout.SetActive(false);
            GameObject scrlLoadoutList = Instantiate(templateScroll);
            scrlLoadoutList.name = "scrlLoadoutsList";
            scrlLoadoutList.transform.SetParent(pnlLoadLoadout.transform);        
            scrlLoadoutList.transform.position = new Vector3(pnlLoadLoadout.transform.position.x - (pnlLoadLoadout.GetComponent<RectTransform>().rect.width * 0.8f),
                pnlLoadLoadout.transform.position.y + (pnlLoadLoadout.GetComponent<RectTransform>().rect.height / 2.25f),
                pnlLoadLoadout.transform.position.z);
            scrlLoadoutList.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, pnlLoadLoadout.GetComponent<RectTransform>().rect.height * 0.75f);
            scrlpnlLoadoutList = scrlLoadoutList.transform.Find("Panel");
            (scrlpnlListItemTemplate = Instantiate(scrlpnlLoadoutList.GetChild(0).gameObject)).transform.SetParent(scrlLoadoutList.transform, false);
            scrlpnlListItemTemplate.GetComponentInChildren<Button>().interactable = true;
            scrlpnlListItemTemplate.GetComponentInChildren<Text>().fontSize = 16;
            scrlpnlListItemTemplate.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;            
            Destroy(scrlpnlListItemTemplate.GetComponentInChildren<InventorySlot>());
            DestroyAllChildren(scrlpnlLoadoutList.transform);
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(LoadPanel_Cancel));
            pnlLoadLoadout.transform.Find("BtnCancel").GetComponentInChildren<Button>().onClick = btnClickEvent;
            Transform loadButton = pnlLoadLoadout.transform.Find("BtnYes");
            loadButton.GetComponentInChildren<Text>().text = "Load";
            btnClickEvent = new Button.ButtonClickedEvent();
            btnClickEvent.AddListener(new UnityAction(LoadPanel_Load));
            loadButton.GetComponentInChildren<Button>().onClick = btnClickEvent;
        }

        private static void DestroyAllChildren(Transform transform)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(false);
                Destroy(transform.GetChild(i).gameObject);
            }
        }

        private static void DockingUI_LoadLoadoutBtnAction()
        {
            loadRequest = true;
            DestroyAllChildren(scrlpnlLoadoutList.transform);

            if (scrlpnlLoadoutList.childCount == 0)
            {
                loadRequest = false;
                string[] loadouts = data.GetLoadoutNamesList();
                for (int i = 0; i < loadouts.Length; i++)
                {
                    GameObject item = Instantiate(scrlpnlListItemTemplate);
                    item.transform.SetParent(scrlpnlLoadoutList, false);
                    item.GetComponentInChildren<Text>().text = loadouts[i];
                    Button.ButtonClickedEvent btnClickEvent = new Button.ButtonClickedEvent();
                    UnityAction ua = null;
                    ua += () => LoadoutList_ItemClick(item);
                    btnClickEvent.AddListener(ua);
                    item.GetComponentInChildren<Button>().onClick = btnClickEvent;
                }

                pnlLoadLoadout.SetActive(true);
            }
        }

        private static void LoadoutList_ItemClick(GameObject listItem)
        {
            for (int i = 0; i < scrlpnlLoadoutList.childCount; i++)
                listItem.transform.GetChild(0).GetChild(0).gameObject.SetActive(false);
            
            listItem.transform.GetChild(0).GetChild(0).gameObject.SetActive(true);
            string name = listItem.GetComponentInChildren<Text>().text;
            loadLoadoutName = name;
        }

        private static void DockingUI_SaveLoadoutBtnAction()
        {
            txtSaveLoadoutName.GetComponent<InputField>().text = "";
            pnlSaveLoadout.SetActive(true);
        }

        private static void SavePanel_Cancel()
        {
            if (pnlSaveLoadout != null)
                pnlSaveLoadout.SetActive(false);
        }

        private static void SavePanel_Save()
        {
            saveLoadoutName = txtSaveLoadoutName.GetComponent<InputField>().text;
            if (!saveLoadoutName.IsNullOrWhiteSpace())
            {
                if (pnlSaveLoadout != null)
                    pnlSaveLoadout.SetActive(false);

                if (data.GetLoadout(saveLoadoutName) == null)
                    SaveLoadout(false);
                else
                    pnlConfirmReplace.SetActive(true);
            }
            else
            {
                InfoPanelControl.inst.ShowWarning("Invalid loadout name.", 1, false);
            }
        }

        private static void ReplacePanel_Cancel()
        {
            if (pnlConfirmReplace != null)
                pnlConfirmReplace.SetActive(false);
        }

        private static void ReplacePanel_Yes()
        {
            if (pnlConfirmReplace != null)
                pnlConfirmReplace.SetActive(false);

            SaveLoadout(true);
        }

        private static void LoadPanel_Load()
        {
            LoadLoadout(loadLoadoutName);
            if (pnlLoadLoadout != null)
                pnlLoadLoadout.SetActive(false);
        }

        private static void LoadPanel_Cancel()
        {
            if (pnlLoadLoadout != null)
                pnlLoadLoadout.SetActive(false);
        }

        private static void SaveLoadout(bool replace)
        {
            SpaceShipData shipData = GetShipData();
            
            PersistentData.Loadout loadout = new PersistentData.Loadout(shipData.weapons, shipData.equipments, shipData.shipModelID);

            if (replace)
                data.ReplaceLoadout(saveLoadoutName, loadout);
            else
                data.AddLoadout(saveLoadoutName, loadout);
        }

        private static void LoadLoadout(string name)
        {
            SpaceShipData shipData = GetShipData();
            Inventory inventory = (Inventory)AccessTools.Field(typeof(ShipInfo), "inventory").GetValue(shipInfo);
            PersistentData.Loadout currentLoadout = new PersistentData.Loadout(shipData.weapons, shipData.equipments, shipData.shipModelID);
            PersistentData.Loadout loadout = data.GetLoadout(name);
            if (loadout == null)
            {
                InfoPanelControl.inst.ShowWarning("Failed to load loadout: " + name, 1, false);
                return;
            }

            UnEquip(shipData);

            Dictionary<string, int> missing = CheckCargo(loadout, shipData, inventory);
            if (missing.Count > 0)
            {
                InfoPanelControl.inst.ShowWarning("Missing items.  Full list in side info.", 1, false);
                foreach (KeyValuePair<string, int> kvp in missing)
                    SideInfo.AddMsg("(" + kvp.Value + ") " + kvp.Key + ", ");
                DoEquip("", currentLoadout, shipData, inventory);                
            }
            else
            {
                DoEquip(name, loadout, shipData, inventory);
            }
        }

        private static void UnEquip(SpaceShipData shipData)
        {
            if (shipData.weapons.Count > 0)
            {
                int storedGM = (int)AccessTools.Field(typeof(ShipInfo), "gearMode").GetValue(shipInfo);
                shipInfoGearModeRef(shipInfo) = 0;
                shipInfo.RemoveAllItems();
                shipInfoGearModeRef(shipInfo) = storedGM;
            }

            if (shipData.equipments.Count > 0)
            {
                int storedGM = (int)AccessTools.Field(typeof(ShipInfo), "gearMode").GetValue(shipInfo);
                shipInfoGearModeRef(shipInfo) = 1;
                shipInfo.RemoveAllItems();
                shipInfoGearModeRef(shipInfo) = storedGM;
            }
        }

        private static Dictionary<string, int> CheckCargo(PersistentData.Loadout loadout, SpaceShipData shipData, Inventory inventory)
        {
            Dictionary<string, int> missing = new Dictionary<string, int>();
            Dictionary<int, int> cargoIndicies = new Dictionary<int, int>();
            if (loadout.weapons.Length > 0)
            {
                foreach (EquipedWeapon weapon in loadout.weapons)
                {
                    int[] cargoEntry = TryGetCargoItemIndex(inventory, 
                        (int)SVUtil.GlobalItemType.weapon,
                        weapon.weaponIndex,
                        weapon.rarity);
                    if (cargoEntry != null &&
                            (!cargoIndicies.TryGetValue(cargoEntry[0], out int quantity) ||
                            quantity > 0))
                    {
                        if (!cargoIndicies.ContainsKey(cargoEntry[0]))
                            cargoIndicies.Add(cargoEntry[0], cargoEntry[1] - 1);
                        else
                            cargoIndicies[cargoEntry[0]] = quantity--;
                    }
                    else
                    {
                        string itemstr = ItemDB.GetRarityColor(weapon.rarity) + GameData.data.weaponList[weapon.weaponIndex].name + "</color>";
                        if (missing.Count > 0 && missing.ContainsKey(itemstr))
                            missing[itemstr]++;
                        else
                            missing.Add(itemstr, 1);
                    }
                }
            }

            if (loadout.equipments.Length > 0)
            {                
                foreach (InstalledEquipment equipment in loadout.equipments)
                {                    
                    for (int i = 0; i < equipment.qnt; i++)
                    {
                        int[] cargoEntry = TryGetCargoItemIndex(inventory, 
                            (int)SVUtil.GlobalItemType.equipment,
                            equipment.equipmentID,
                            equipment.rarity);
                        if (cargoEntry != null &&
                            (!cargoIndicies.TryGetValue(cargoEntry[0], out int quantity) ||
                            quantity > 0))
                        {
                            if (!cargoIndicies.ContainsKey(cargoEntry[0]))
                                cargoIndicies.Add(cargoEntry[0], cargoEntry[1] - 1);
                            else
                                cargoIndicies[cargoEntry[0]] = quantity--;
                        }
                        else
                        {
                            string itemstr = ItemDB.GetRarityColor(equipment.rarity) + EquipmentDB.GetEquipment(equipment.equipmentID).equipName + "</color>";
                            if (missing.Count > 0 && missing.ContainsKey(itemstr))
                                missing[itemstr]++;
                            else
                                missing.Add(itemstr, 1);
                        }
                    }
                }
            }
            return missing;
        }

        private static int[] TryGetCargoItemIndex(Inventory inventory, int itemType, int itemID, int rarity)
        {
            Transform itemPanel = (Transform)AccessTools.Field(typeof(Inventory), "itemPanel").GetValue(inventory);
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);

            for (int i = 0; i < itemPanel.childCount; i++)
            {
                InventorySlot invSlot = itemPanel.GetChild(i).GetComponent<InventorySlot>();
                if (invSlot.itemIndex >= 0 && invSlot.itemIndex < cs.cargo.Count)
                {
                    CargoItem cargoItem = cs.cargo[invSlot.itemIndex];
                    if (cargoItem.itemType == itemType && cargoItem.itemID == itemID && cargoItem.rarity == rarity)
                    {
                        return new int[] { invSlot.itemIndex, cs.cargo[invSlot.itemIndex].qnt };
                    }
                }
            }
            return null;
        }

        private static bool TrySelectCargoItem(Inventory inventory, int itemType, int itemID, int rarity)
        {
            Transform itemPanel = (Transform)AccessTools.Field(typeof(Inventory), "itemPanel").GetValue(inventory);
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);

            for (int i = 0; i < itemPanel.childCount; i++)
            {
                InventorySlot invSlot = itemPanel.GetChild(i).GetComponent<InventorySlot>();
                if (invSlot.itemIndex >= 0 && invSlot.itemIndex < cs.cargo.Count)
                {
                    CargoItem cargoItem = cs.cargo[invSlot.itemIndex];
                    if (cargoItem.itemType == itemType && cargoItem.itemID == itemID && cargoItem.rarity == rarity)
                    {
                        invSlot.SlotClick();
                        return true;
                    }
                }
            }
            return false;
        }

        private static void DoEquip(string name, PersistentData.Loadout loadout, SpaceShipData shipData, Inventory inventory)
        {
            bool failed = false;

            if (loadout.weapons.Length > 0)
            {
                foreach (EquipedWeapon weapon in loadout.weapons)
                {
                    if (TrySelectCargoItem(inventory, (int)SVUtil.GlobalItemType.weapon, weapon.weaponIndex, weapon.rarity))
                    {
                        inventory.EquipItem();
                        shipData.weapons[shipData.weapons.Count - 1].buttonCode = weapon.buttonCode;
                        shipData.weapons[shipData.weapons.Count - 1].delayTime = weapon.delayTime;
                        shipData.weapons[shipData.weapons.Count - 1].key = weapon.key;
                        if(shipData.shipModelID == loadout.shipModelID)
                            shipData.weapons[shipData.weapons.Count - 1].slotIndex = weapon.slotIndex;
                    }
                    else
                    {
                        failed = true;
                        break;
                    }
                }
            }

            if (!failed && loadout.equipments.Length > 0)
            {
                foreach (InstalledEquipment equipment in loadout.equipments)
                {
                    for (int i = 0; i < equipment.qnt; i++)
                    {
                        if (TrySelectCargoItem(inventory, (int)SVUtil.GlobalItemType.equipment, equipment.equipmentID, equipment.rarity))
                        {
                            inventory.EquipItem();
                            shipData.equipments[shipData.equipments.Count - 1].buttonCode = equipment.buttonCode;
                        }
                        else
                        {
                            failed = true;
                            break;
                        }
                    }
                }
            }

            if (failed)
                InfoPanelControl.inst.ShowWarning("Failed to load loadout " + name, 1, false);
            else if (!name.IsNullOrWhiteSpace())
                InfoPanelControl.inst.ShowWarning("Equipped loadout " + name, 2, false);
        }
        
        private static SpaceShipData GetShipData()
        {
            SpaceShipData shipData = ((SpaceShip)AccessTools.Field(typeof(ShipInfo), "ss").GetValue(shipInfo)).shipData;
            if (shipInfo.editingFleetShip != null)
                shipData = (SpaceShipData)AccessTools.Field(typeof(ShipInfo), "tempSpaceShipData").GetValue(shipInfo);

            return shipData;
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.SaveGame))]
        [HarmonyPrefix]
        private static void GameDataSaveGame_Pre()
        {
            SaveGame();
        }

        private static void SaveGame()
        {
            if (data == null || data.GetLoadoutNamesList().Length == 0)
                return;

            string tempPath = Application.dataPath + GameData.saveFolderName + modSaveFolder + "LOTemp.dat";

            if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            FileStream fileStream = File.Create(tempPath);
            binaryFormatter.Serialize(fileStream, data);
            fileStream.Close();

            File.Copy(tempPath, Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat", true);
            File.Delete(tempPath);
        }

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.LoadGame))]
        [HarmonyPostfix]
        private static void MenuControlLoadGame_Post()
        {
            LoadData(GameData.gameFileIndex.ToString("00"));
        }

        private static void LoadData(string saveIndex)
        {
            string modData = Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + saveIndex + ".dat";
            try
            {
                if (!saveIndex.IsNullOrWhiteSpace() && File.Exists(modData))
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    FileStream fileStream = File.Open(modData, FileMode.Open);
                    PersistentData loadData = (PersistentData)binaryFormatter.Deserialize(fileStream);
                    fileStream.Close();

                    if (loadData == null)
                        data = new PersistentData();
                    else
                        data = loadData;
                }
                else
                    data = new PersistentData();
            }
            catch
            {
                SideInfo.AddMsg("<color=red>Loadouts mod load failed.</color>");
            }
        }
    }

    [Serializable]
    internal class PersistentData
    {
        private readonly List<string> names;
        private readonly List<Loadout> loadouts;

        internal PersistentData()
        {
            names = new List<string>();
            loadouts = new List<Loadout>();
        }

        internal string[] GetLoadoutNamesList()
        {
            string[] loadoutList = new string[names.Count];
            names.CopyTo(loadoutList);
            return loadoutList;
        }

        internal Loadout GetLoadout(string name)
        {
            int index = names.IndexOf(name);
            if (index == -1 || index >= loadouts.Count)
                return null;

            return loadouts[index];
        }

        internal bool AddLoadout(string name, Loadout loadout)
        {
            if (names.Contains(name))
                return false;

            names.Add(name);
            loadouts.Add(loadout);
            return true;
        }

        internal bool RemoveLoadout(string name)
        {
            int index = names.IndexOf(name);
            if (index == -1 || index > loadouts.Count - 1)
                return false;

            names.RemoveAt(index);
            loadouts.RemoveAt(index);
            return true;
        }
        
        internal bool ReplaceLoadout(string name, Loadout loadout)
        {
            int index = names.IndexOf(name);
            if (index == -1 || index > loadouts.Count - 1)
                return false;

            loadouts[index] = loadout;
            return true;
        }

        [Serializable]
        internal class Loadout
        {
            internal EquipedWeapon[] weapons;
            internal InstalledEquipment[] equipments;
            internal int shipModelID;

            internal Loadout(List<EquipedWeapon> weapons, List<InstalledEquipment> equipments, int shipModel)
            {
                this.weapons = new EquipedWeapon[weapons.Count];
                weapons.ForEach(weapon => { this.weapons[weapons.IndexOf(weapon)] = new EquipedWeapon() { buttonCode = weapon.buttonCode, delayTime = weapon.delayTime, key = weapon.key, rarity = weapon.rarity, slotIndex = weapon.slotIndex, weaponIndex = weapon.weaponIndex }; });
                this.equipments = new InstalledEquipment[equipments.Count];
                equipments.ForEach(equipment => { this.equipments[equipments.IndexOf(equipment)] = new InstalledEquipment() { buttonCode = equipment.buttonCode, equipmentID = equipment.equipmentID, qnt = equipment.qnt, rarity = equipment.rarity }; });
                this.shipModelID = shipModel;
            }
        }
    }
}


