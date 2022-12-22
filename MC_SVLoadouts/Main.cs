using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.UI.Button;
using static UnityEngine.UI.Toggle;

namespace MC_SVLoadout
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        private enum ConfirmAction { overwrite, delete, savenobp }

        // BepInEx
        public const string pluginGuid = "mc.starvalor.loadouts";
        public const string pluginName = "SV Loadouts";
        public const string pluginVersion = "2.0.0";

        // Mod
        private const int hangerPanelCode = 3;
        private const int newSelectedIndex = -1;
        private const int noneSelectedIndex = -2;
        private const string modSaveFolder = "/MCSVSaveData/";  // /SaveData/ sub folder
        private const string modSaveFilePrefix = "Loadouts_"; // modSaveFlePrefixNN.dat
        private static PersistentData data;
        private static ShipInfo shipInfo;
        private static int selectedIndex;
        private static AccessTools.FieldRef<ShipInfo, int> shipInfoGearModeRef = AccessTools.FieldRefAccess<ShipInfo, int>("gearMode");
        private static bool respectRarity = true;

        // UI
        private const string msgConfirmOverwrite = "Really overwrite existing loadout NAME?";
        private const string msgConfirmDelete = "Really delete loadout NAME?";
        private const string msgConfirmSaveNoBP = "Custom weapons in this loadout have no associated saved blueprint.\n Auto crafting will be disabled for this loadout.\n  Do you still wish to save?";
        private const float listItemSpacing = 20f;
        private static GameObject mainPanelAsset;
        private static GameObject confirmDialogAsset;
        private static GameObject inputDialogAsset;
        private static GameObject listItemAsset;
        private static GameObject craftingDialogAsset;

        private static GameObject btnDockUIManage;
        private static GameObject pnlMain;
        private static Transform scrlSavedLoadoutsList;
        private static Text txtSelectedLoadoutName;
        private static Transform scrlSelectedLoadoutContent;
        private static GameObject dlgConfirm;
        private static Text txtConfirmDlg;
        private static Button btnConfirmDlgYes;
        private static GameObject dlgInput;
        private static InputField txtFldLoadoutName;
        private static GameObject goCurrentHighlight;
        private static GameObject dlgCraftingList;
        private static Transform scrlCraftingItemList;

        // Debug
        internal static BepInEx.Logging.ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("SV Loadouts");

        public void Awake()
        {
            LoadAssets();
            Harmony.CreateAndPatchAll(typeof(Main));
        }

        internal void LoadAssets()
        {
            string pluginfolder = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);

            // Load assets
            string bundleName = "mc_svloadouts";
            AssetBundle assets = AssetBundle.LoadFromFile($"{pluginfolder}\\{bundleName}");
            GameObject pack = assets.LoadAsset<GameObject>("Assets/mc_loadouts.prefab");

            mainPanelAsset = pack.transform.Find("mc_saveloadoutMainPanel").gameObject;
            confirmDialogAsset = pack.transform.Find("mc_saveloadoutConfirmDlg").gameObject;
            inputDialogAsset = pack.transform.Find("mc_saveloadoutInputDlg").gameObject;
            listItemAsset = pack.transform.Find("mc_saveloadoutListItem").gameObject;
            craftingDialogAsset = pack.transform.Find("mc_saveloadoutCraftDlg").gameObject;
        }

        [HarmonyPatch(typeof(DockingUI), nameof(DockingUI.OpenPanel))]
        [HarmonyPostfix]
        private static void DocingUIOpenPanel_Post(DockingUI __instance, ShipInfo ___shipInfo, Inventory ___inventory, WeaponCrafting ___weaponCrafting, int code)
        {
            if (code == hangerPanelCode)
            {
                shipInfo = ___shipInfo;

                if (btnDockUIManage == null)
                    CreateUI(___shipInfo, ___inventory, ___weaponCrafting);

                btnDockUIManage.SetActive(true);
            }
        }

        [HarmonyPatch(typeof(DockingUI), nameof(DockingUI.CloseDockingStation))]
        [HarmonyPrefix]
        private static void DockingUICloseDockingStation_Pre()
        {
            if (btnDockUIManage != null)
                btnDockUIManage.SetActive(false);
            if (pnlMain != null)
                pnlMain.SetActive(false);
            if (dlgConfirm != null)
                dlgConfirm.SetActive(false);
            if (dlgInput != null)
                dlgInput.SetActive(false);
        }

        private static void CreateUI(ShipInfo shipInfo, Inventory inventory, WeaponCrafting weaponCrafting)
        {
            Transform itemMainPanel = ((GameObject)AccessTools.Field(typeof(ShipInfo), "shipDataScreen").GetValue(shipInfo)).transform;
            GameObject templateBtn = ((Transform)AccessTools.Field(typeof(ShipInfo), "equipGO").GetValue(shipInfo)).Find("BtnRemove").gameObject;
            GameObject btnRemoveAll = GameObject.Find("BtnRemoveAll");

            btnDockUIManage = Instantiate(templateBtn);
            btnDockUIManage.name = "BtnMngLoadouts";
            btnDockUIManage.SetActive(true);
            btnDockUIManage.GetComponentInChildren<Text>().text = "Manage Loadouts";
            btnDockUIManage.GetComponentInChildren<Text>().fontSize--;
            btnDockUIManage.SetActive(false);
            ButtonClickedEvent btnDockUIManageClickEvent = new Button.ButtonClickedEvent();
            btnDockUIManageClickEvent.AddListener(btnDockUIManage_Click);
            btnDockUIManage.GetComponentInChildren<Button>().onClick = btnDockUIManageClickEvent;
            btnDockUIManage.transform.SetParent(btnRemoveAll.transform.parent);
            btnDockUIManage.layer = btnRemoveAll.layer;
            btnDockUIManage.transform.localPosition = new Vector3(btnRemoveAll.transform.localPosition.x,
                btnRemoveAll.transform.localPosition.y - (btnRemoveAll.GetComponent<RectTransform>().rect.height * 1.5f),
                btnRemoveAll.transform.localPosition.z); ;
            btnDockUIManage.transform.localScale = btnRemoveAll.transform.localScale;

            pnlMain = GameObject.Instantiate(mainPanelAsset);
            pnlMain.transform.SetParent(itemMainPanel.parent.parent, false);
            pnlMain.layer = itemMainPanel.gameObject.layer;
            pnlMain.SetActive(false);

            scrlSavedLoadoutsList = pnlMain.transform.GetChild(0).GetChild(1).GetChild(1).GetChild(0).GetChild(0);
            txtSelectedLoadoutName = pnlMain.transform.GetChild(0).GetChild(2).GetChild(1).GetComponent<Text>();
            scrlSelectedLoadoutContent = pnlMain.transform.GetChild(0).GetChild(2).GetChild(2).GetChild(0).GetChild(0);

            ButtonClickedEvent btnDeleteClickedEvent = new ButtonClickedEvent();
            btnDeleteClickedEvent.AddListener(btnDelete_Click);
            pnlMain.transform.GetChild(0).GetChild(3).GetComponent<Button>().onClick = btnDeleteClickedEvent;

            ButtonClickedEvent btnLoadClickedEvent = new ButtonClickedEvent();
            btnLoadClickedEvent.AddListener(btnLoad_Click);
            pnlMain.transform.GetChild(0).GetChild(4).GetComponent<Button>().onClick = btnLoadClickedEvent;

            Toggle tglRespectRarity = pnlMain.transform.GetChild(0).GetChild(5).GetComponent<Toggle>();
            ToggleEvent tglRespectRarityEvent = new ToggleEvent();
            tglRespectRarityEvent.AddListener(new UnityAction<bool>(tglRespectRarity_ValChange));
            tglRespectRarity.onValueChanged = tglRespectRarityEvent;
            tglRespectRarity.isOn = respectRarity;

            ButtonClickedEvent btnSaveClickedEvent = new ButtonClickedEvent();
            btnSaveClickedEvent.AddListener(btnSave_Click);
            pnlMain.transform.GetChild(0).GetChild(6).GetComponent<Button>().onClick = btnSaveClickedEvent;

            ButtonClickedEvent btnCancelClickedEvent = new ButtonClickedEvent();
            btnCancelClickedEvent.AddListener(btnCancel_Click);
            pnlMain.transform.GetChild(0).GetChild(7).GetComponent<Button>().onClick = btnCancelClickedEvent;

            dlgConfirm = GameObject.Instantiate(confirmDialogAsset);
            dlgConfirm.transform.SetParent(pnlMain.transform, false);
            dlgConfirm.layer = pnlMain.layer;
            dlgConfirm.SetActive(false);

            txtConfirmDlg = dlgConfirm.transform.GetChild(0).GetChild(0).GetComponent<Text>();
            btnConfirmDlgYes = dlgConfirm.transform.GetChild(0).GetChild(2).GetComponent<Button>();

            ButtonClickedEvent btnConfirmCancelClickedEvent = new ButtonClickedEvent();
            btnConfirmCancelClickedEvent.AddListener(btnConfirmDlgCancel_Click);
            dlgConfirm.transform.GetChild(0).GetChild(1).GetComponent<Button>().onClick = btnConfirmCancelClickedEvent;

            dlgInput = GameObject.Instantiate(inputDialogAsset);
            dlgInput.transform.SetParent(pnlMain.transform, false);
            dlgInput.layer = pnlMain.layer;
            dlgInput.SetActive(false);

            txtFldLoadoutName = dlgInput.transform.GetChild(0).GetChild(3).GetComponent<InputField>();

            ButtonClickedEvent btnInputCancelClickedEvent = new ButtonClickedEvent();
            btnInputCancelClickedEvent.AddListener(btnInputDlgCancel_Click);
            dlgInput.transform.GetChild(0).GetChild(1).GetComponent<Button>().onClick = btnInputCancelClickedEvent;

            ButtonClickedEvent btnInputConfirmClickedEvent = new ButtonClickedEvent();
            btnInputConfirmClickedEvent.AddListener(btnInputDlgConfirm_Click);
            dlgInput.transform.GetChild(0).GetChild(2).GetComponent<Button>().onClick = btnInputConfirmClickedEvent;

            dlgCraftingList = GameObject.Instantiate(craftingDialogAsset);
            dlgCraftingList.transform.SetParent(pnlMain.transform, false);
            dlgCraftingList.layer = pnlMain.layer;
            dlgCraftingList.SetActive(false);

            scrlCraftingItemList = dlgCraftingList.transform.GetChild(0).GetChild(1).GetChild(0).GetChild(0);
        }

        private static void DestroyAllChildren(Transform transform)
        {
            for (int i = 0; i < transform.childCount; i++)
                Destroy(transform.GetChild(i).gameObject);
        }

        private static void btnDockUIManage_Click()
        {
            if (data == null)
                LoadData("");

            RefreshSavedLoadoutList();
            RefreshSelectedLoadoutContent();            

            pnlMain.SetActive(true);
        }

        private static void ListItem_Click(PointerEventData pointerEventData)
        {
            GameObject listItem = pointerEventData.pointerCurrentRaycast.gameObject.transform.parent.gameObject;
            
            if (goCurrentHighlight != null)
                goCurrentHighlight.SetActive(false);

            selectedIndex = listItem.GetComponent<ListItemData>().index;
            goCurrentHighlight = listItem.transform.Find("mc_saveloadoutHighlight").gameObject;
            goCurrentHighlight.SetActive(true);
            RefreshSelectedLoadoutContent();
        }

        private static void tglRespectRarity_ValChange(bool val)
        {
            respectRarity = val;
        }

        private static void RefreshSavedLoadoutList()
        {
            DestroyAllChildren(scrlSavedLoadoutsList);
            
            selectedIndex = -2;

            if (data.loadouts.Count < 0)
                return;

            scrlSavedLoadoutsList.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, listItemSpacing * (Main.data.loadouts.Count + 1));

            // "New..." item
            GameObject newListItem = GameObject.Instantiate(listItemAsset);
            newListItem.transform.SetParent(scrlSavedLoadoutsList, false);
            newListItem.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = "New...";
            newListItem.layer = scrlSavedLoadoutsList.gameObject.layer;
            ListItemData newListItemData = newListItem.AddComponent<ListItemData>();
            newListItemData.index = newSelectedIndex;
            EventTrigger.Entry newItemTrig = new EventTrigger.Entry();
            newItemTrig.eventID = EventTriggerType.PointerDown;
            newItemTrig.callback.AddListener((data) => { ListItem_Click((PointerEventData)data); });
            newListItem.GetComponent<EventTrigger>().triggers.Add(newItemTrig);

            for (int i = 0; i < data.loadouts.Count; i++)
            {
                GameObject li = GameObject.Instantiate(listItemAsset);
                li.transform.SetParent(scrlSavedLoadoutsList, false);
                li.transform.localPosition = new Vector3(
                    li.transform.localPosition.x,
                    li.transform.localPosition.y - (listItemSpacing * (i + 1)),
                    li.transform.localPosition.z);
                li.layer = scrlSavedLoadoutsList.gameObject.layer;

                li.transform.Find("mc_saveloadoutItemText").GetComponent<Text>().text = data.loadouts[i].name;

                ListItemData listItemData = li.AddComponent<ListItemData>();
                listItemData.index = i;

                EventTrigger.Entry listItemTrig = new EventTrigger.Entry();
                listItemTrig.eventID = EventTriggerType.PointerDown;
                listItemTrig.callback.AddListener((data) => { ListItem_Click((PointerEventData)data); });
                li.GetComponent<EventTrigger>().triggers.Add(listItemTrig);
            }
        }

        private static void RefreshSelectedLoadoutContent()
        {
            DestroyAllChildren(scrlSelectedLoadoutContent);

            if (selectedIndex < 0)
            {
                txtSelectedLoadoutName.text = "";
                return;
            }

            PersistentData.Loadout lo = data.loadouts[selectedIndex];

            txtSelectedLoadoutName.text = lo.name;

            scrlSelectedLoadoutContent.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, listItemSpacing * (lo.weapons.Length + lo.equipments.Length + 2));

            GameObject weaponsHeading = GameObject.Instantiate(listItemAsset);
            weaponsHeading.transform.SetParent(scrlSelectedLoadoutContent, false);
            weaponsHeading.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = "Weapons:";
            weaponsHeading.layer = scrlSelectedLoadoutContent.gameObject.layer;

            for(int i = 1; i < lo.weapons.Length + 1; i++)
            {
                int modI = i - 1;
                GameObject weaponLi = GameObject.Instantiate(listItemAsset);
                weaponLi.transform.SetParent(scrlSelectedLoadoutContent, false);
                weaponLi.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = GameData.data.weaponList[lo.weapons[modI].weaponIndex].GetNameModified(lo.weapons[modI].rarity, 0);
                weaponLi.transform.localPosition = new Vector3(
                        weaponLi.transform.localPosition.x,
                        weaponLi.transform.localPosition.y - (listItemSpacing * i),
                        weaponLi.transform.localPosition.z);
                weaponLi.layer = scrlSelectedLoadoutContent.gameObject.layer;
            }

            GameObject spacer = GameObject.Instantiate(listItemAsset);
            spacer.transform.SetParent(scrlSelectedLoadoutContent, false);
            spacer.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = "_____________________________";
            spacer.transform.localPosition = new Vector3(
                    spacer.transform.localPosition.x,
                    spacer.transform.localPosition.y - (listItemSpacing * (lo.weapons.Length + 1)),
                    spacer.transform.localPosition.z);
            spacer.layer = scrlSelectedLoadoutContent.gameObject.layer;
            GameObject equipmentsHeading = GameObject.Instantiate(listItemAsset);
            equipmentsHeading.transform.SetParent(scrlSelectedLoadoutContent, false);
            equipmentsHeading.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = "Equipment:";
            equipmentsHeading.transform.localPosition = new Vector3(
                    equipmentsHeading.transform.localPosition.x,
                    equipmentsHeading.transform.localPosition.y - (listItemSpacing * (lo.weapons.Length + 2)),
                    equipmentsHeading.transform.localPosition.z);
            equipmentsHeading.layer = scrlSelectedLoadoutContent.gameObject.layer;

            for (int i = lo.weapons.Length + 3; i < lo.weapons.Length + 2 + lo.equipments.Length; i++)
            {
                int modI = i - (lo.weapons.Length + 3);

                GameObject equipLi = GameObject.Instantiate(listItemAsset);
                equipLi.transform.SetParent(scrlSelectedLoadoutContent, false);
                equipLi.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = "(" + lo.equipments[modI].qnt + ") " + ItemDB.GetRarityColor(lo.equipments[modI].rarity) + EquipmentDB.GetEquipment(lo.equipments[modI].equipmentID).equipName + "</color>";
                equipLi.transform.localPosition = new Vector3(
                        equipLi.transform.localPosition.x,
                        equipLi.transform.localPosition.y - (listItemSpacing * i),
                        equipLi.transform.localPosition.z);
                equipLi.layer = scrlSelectedLoadoutContent.gameObject.layer;
            }
        }

        private static void btnDelete_Click()
        {
            if (selectedIndex < 0)
                InfoPanelControl.inst.ShowWarning("No loadout selected.", 1, false);
            else
                ShowConfirmDialog(ConfirmAction.delete);
        }

        private static void btnLoad_Click()
        {
            if (selectedIndex < 0)
                InfoPanelControl.inst.ShowWarning("No loadout selected.", 1, false);
            else
                LoadLoadout();
        }

        private static void btnSave_Click()
        {
            if (selectedIndex == noneSelectedIndex)
                InfoPanelControl.inst.ShowWarning("Select \"New\" or an existing loadout to overwrite.", 1, false);
            else
            {
                if (selectedIndex == newSelectedIndex)
                    ShowInputDialog();
                else
                    ShowConfirmDialog(ConfirmAction.overwrite);
            }
        }

        private static void btnCancel_Click()
        {
            pnlMain.SetActive(false);
        }

        private static void ShowInputDialog()
        {
            dlgInput.SetActive(true);
            txtFldLoadoutName.Select();
        }

        private static void btnInputDlgCancel_Click()
        {
            dlgInput.SetActive(false);
        }

        private static void btnInputDlgConfirm_Click()
        {
            if (txtFldLoadoutName.text.IsNullOrWhiteSpace())
            {
                InfoPanelControl.inst.ShowWarning("Enter a loadout name.", 1, false);
            }
            else
            {
                dlgInput.SetActive(false);

                if (HasCraftedWeaponWithNoBP(AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip").shipData.weapons))
                {
                    ShowConfirmDialog(ConfirmAction.savenobp);
                }
                else
                {
                    SaveLoadout(txtFldLoadoutName.text);
                }
            }
        }

        private static void ShowConfirmDialog(ConfirmAction action)
        {
            string message = "";
            ButtonClickedEvent confirmClickedEvent = new ButtonClickedEvent();

            switch(action)
            {
                case ConfirmAction.delete:
                    message = msgConfirmDelete.Replace("NAME", data.loadouts[selectedIndex].name);
                    confirmClickedEvent.AddListener(btnConfirmDlgConfirm_Delete_Click);
                    break;
                case ConfirmAction.overwrite:
                    message = msgConfirmOverwrite.Replace("NAME", data.loadouts[selectedIndex].name);
                    confirmClickedEvent.AddListener(btnConfirmDlgConfirm_Save_Click);
                    break;
                case ConfirmAction.savenobp:
                    message = msgConfirmSaveNoBP;
                    confirmClickedEvent.AddListener(btnConfirmDlgConfirm_SaveNoBP_Click);
                    break;
            }

            txtConfirmDlg.text = message;
            btnConfirmDlgYes.onClick = confirmClickedEvent;

            dlgConfirm.SetActive(true);
        }

        private static void btnConfirmDlgCancel_Click()
        {
            dlgConfirm.SetActive(false);
        }

        private static void btnConfirmDlgConfirm_Save_Click()
        {
            dlgConfirm.SetActive(false);
            if (HasCraftedWeaponWithNoBP(AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip").shipData.weapons))
                ShowConfirmDialog(ConfirmAction.savenobp);
            else
                SaveLoadout(data.loadouts[selectedIndex].name);
        }

        private static void btnConfirmDlgConfirm_SaveNoBP_Click()
        {
            dlgConfirm.SetActive(false);
            if (selectedIndex == newSelectedIndex)
                SaveLoadout(txtFldLoadoutName.text);
            else
                SaveLoadout(data.loadouts[selectedIndex].name);
        }

        private static void btnConfirmDlgConfirm_Delete_Click()
        {
            data.loadouts.RemoveAt(selectedIndex);
            dlgConfirm.SetActive(false);
            RefreshSavedLoadoutList();
            RefreshSelectedLoadoutContent();
        }

        private static void btnCraftingDlgConfirm_Click(CraftingList craftingList, int curStation)
        {
            CargoSystem cs = GameObject.FindGameObjectWithTag("Player").GetComponent<CargoSystem>();

            // Pay credits
            if(craftingList.GetCost() > 0)
                cs.PayCreditCost(craftingList.GetCost());

            // Pay materials
            Dictionary<int, int> materials = craftingList.GetMaterials();
            foreach (int material in materials.Keys)
                cs.ConsumeItem((int)SVUtil.GlobalItemType.genericitem, material, materials[material], curStation);
           
            // Make items            
            foreach(MC_SVManageBP.PersistentData.Blueprint bp in craftingList.customWeaponBPs.Keys)
            {
                TWeapon template = GameData.data.weaponList[bp.weaponIDs[0]];
                
                for (int i = 0; i < craftingList.customWeaponBPs[bp]; i++)
                {
                    TWeapon weapon = new TWeapon()
                    {
                        name = template.name,
                        index = GameData.data.weaponList.Count,
                        type = template.type,
                        compType = template.compType,
                        damageType = template.damageType,
                        aoe = template.aoe,
                        damage = template.damage,
                        critChance = template.critChance,
                        armorPen = template.armorPen,
                        massKiller = template.massKiller,
                        rateOfFire = template.rateOfFire,
                        chargeTime = template.chargeTime,
                        chargedFireTime = template.chargedFireTime,
                        chargedFireCooldown = template.chargedFireCooldown,
                        fluxDamageMod = template.fluxDamageMod,
                        burst = template.burst,
                        speed = template.speed,
                        boosterSpeedMod = template.boosterSpeedMod,
                        range = template.range,
                        boosterRangeMod = template.boosterRangeMod,
                        turnSpeed = template.turnSpeed,
                        space = template.space,
                        energyCostMod = template.energyCostMod,
                        heatGenMod = template.heatGenMod,
                        canHitProjectiles = template.canHitProjectiles,
                        piercing = template.piercing,
                        tradable = template.tradable,
                        timedFuse = template.timedFuse,
                        explodeOnMaxRange = template.explodeOnMaxRange,
                        longRange = template.longRange,
                        dropLevel = template.dropLevel,
                        techLevel = template.techLevel,
                        size = template.size,
                        spriteName = template.spriteName,
                        projectileName = template.projectileName,
                        audioName = template.audioName,
                        beamName = template.beamName,
                        shortCooldown = template.shortCooldown,
                        repReq = template.repReq,
                        ammo = template.ammo,
                        materials = template.materials,
                        description = template.description,
                        craftingMaterials = template.craftingMaterials
                    };
                    GameData.data.weaponList.Add(weapon);
                    bp.weaponIDs.Add(weapon.index);
                    cs.StoreItem((int)SVUtil.GlobalItemType.weapon, weapon.index, (int)ItemRarity.Common_1, 1, 0f, -1, -1, -1);

                    // Replace a missing weapon with the new one
                    foreach(EquipedWeapon loWeapon in data.loadouts[selectedIndex].weapons)
                    {
                        if(craftingList.missingCustomWeaponIndexes.Contains(loWeapon.weaponIndex) &&
                            bp.weaponIDs.Contains(loWeapon.weaponIndex))
                        {
                            craftingList.missingCustomWeaponIndexes.Remove(loWeapon.weaponIndex);
                            loWeapon.weaponIndex = weapon.index;
                        }
                    }
                }
            }

            foreach(CraftingList.BaseBP bp in craftingList.otherBPs.Keys)
                cs.StoreItem(bp.blueprint.itemType, bp.blueprint.itemID, bp.level, craftingList.otherBPs[bp], 0f, -1, -1, -1);

            Inventory inventory = (Inventory)AccessTools.Field(typeof(ShipInfo), "inventory").GetValue(shipInfo);
            inventory.LoadItems();

            // And finally equip it
            DoEquip(data.loadouts[selectedIndex],
                AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip").shipData,
                inventory);

            dlgCraftingList.SetActive(false);
            pnlMain.SetActive(false);
        }

        private static void btnCraftingDlgCancel_Click(PersistentData.Loadout currentLoadout)
        {            
            DoEquip(currentLoadout,
                AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip").shipData,
                (Inventory)AccessTools.Field(typeof(ShipInfo), "inventory").GetValue(shipInfo));

            dlgCraftingList.SetActive(false);
            pnlMain.SetActive(false);
        }

        private static bool HasCraftedWeaponWithNoBP(List<EquipedWeapon> loadoutWeapons)
        {
            List<int> customWeaponIDs = new List<int>();
            foreach (MC_SVManageBP.PersistentData.Blueprint bp in MC_SVManageBP.Main.data.blueprints)
                customWeaponIDs.AddRange(bp.weaponIDs);

            foreach(EquipedWeapon weapon in loadoutWeapons)
            {
                TWeapon rawWeapon = GameData.data.weaponList[weapon.weaponIndex];
                if (rawWeapon.isCrafted && !customWeaponIDs.Contains(rawWeapon.index))
                    return true;
            }

            return false;
        }

        private static Blueprint GetEquipmentBP(InstalledEquipment equip)
        {
            foreach (Blueprint bp in PChar.Char.blueprints)
            {
                if (bp.itemID == equip.equipmentID && bp.itemType == (int)SVUtil.GlobalItemType.equipment &&
                    (!respectRarity || (respectRarity && ((bp.hasMultiLevel && (bp.level + 1) >= equip.rarity) || !bp.hasMultiLevel))))
                    return bp;
            }

            return null;
        }

        private static Blueprint GetWeaponBP(EquipedWeapon weapon)
        {
            foreach (Blueprint bp in PChar.Char.blueprints)
            {
                if (bp.itemID == weapon.weaponIndex && bp.itemType == (int)SVUtil.GlobalItemType.weapon &&
                    (!respectRarity || (respectRarity && ((bp.hasMultiLevel && (bp.level + 1) >= weapon.rarity) || !bp.hasMultiLevel))))
                    return bp;
            }

            return null;
        }

        private static MC_SVManageBP.PersistentData.Blueprint GetCustomWeaponBP(int weaponIndex)
        {
            foreach (MC_SVManageBP.PersistentData.Blueprint bp in MC_SVManageBP.Main.data.blueprints)
            {
                if (bp.weaponIDs.Contains(weaponIndex))
                    return bp;
            }

            return null;
        }

        private static void SaveLoadout(string name)
        {
            SpaceShipData shipData = AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip").shipData;
            
            PersistentData.Loadout loadout = new PersistentData.Loadout(name, shipData.weapons, shipData.equipments, shipData.shipModelID);

            if (selectedIndex == newSelectedIndex)
                data.loadouts.Add(loadout);
            else
                data.loadouts[selectedIndex] = loadout;
            RefreshSavedLoadoutList();
            RefreshSelectedLoadoutContent();
        }

        private static void LoadLoadout()
        {
            SpaceShipData shipData = AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip").shipData;
            Inventory inventory = (Inventory)AccessTools.Field(typeof(ShipInfo), "inventory").GetValue(shipInfo);
            PersistentData.Loadout currentLoadout = new PersistentData.Loadout(null, shipData.weapons, shipData.equipments, shipData.shipModelID);
            PersistentData.Loadout loadout = data.loadouts[selectedIndex];
            if (loadout == null)
            {
                InfoPanelControl.inst.ShowWarning("Failed to load loadout.", 1, false);
                return;
            }

            UnEquip(shipData);

            CraftingList missing = CheckCargo(loadout, shipData, inventory);
            if (missing.missingBPs.Count > 0)
            {
                InfoPanelControl.inst.ShowWarning("Missing items and associated blueprints.  Missing blueprints listed in side info.", 1, false);
                foreach (string item in missing.missingBPs)
                    SideInfo.AddMsg(item + ", ");
                DoEquip(currentLoadout, shipData, inventory);
                pnlMain.SetActive(false);
            }
            else
            {
                if (missing.otherBPs.Count > 0 || missing.customWeaponBPs.Count > 0)
                {
                    ShowCraftPopup(missing, currentLoadout, inventory.currStation.id);
                }
                else
                {
                    DoEquip(loadout, shipData, inventory);
                    pnlMain.SetActive(false);
                }
            }
        }

        private static void ShowCraftPopup(CraftingList craftingList, PersistentData.Loadout currentLoadout, int curStation)
        {
            Dictionary<int, int> materials = craftingList.GetMaterials();
            float cost = craftingList.GetCost();            
            CargoSystem cs = GameObject.FindGameObjectWithTag("Player").GetComponent<CargoSystem>();
            List<int> missingMaterialIDs = CargoHasMaterials(materials, cs, curStation);

            // Show or hide craft button
            Transform confirmButton = dlgCraftingList.transform.GetChild(0).GetChild(3);
            if (cs.credits >= cost &&
                missingMaterialIDs.Count == 0)
            {
                UnityAction confirmButtonAction = null;
                confirmButtonAction += () => btnCraftingDlgConfirm_Click(craftingList, curStation);
                ButtonClickedEvent confirmBtnClickedEvent = new ButtonClickedEvent();
                confirmBtnClickedEvent.AddListener(confirmButtonAction);
                confirmButton.GetComponent<Button>().onClick = confirmBtnClickedEvent;
                confirmButton.gameObject.SetActive(true);                
            }
            else
            {
                confirmButton.gameObject.SetActive(false);
            }

            // Cancel button event
            Transform cancelButton = dlgCraftingList.transform.GetChild(0).GetChild(2);
            UnityAction cancelButtonAction = null;
            cancelButtonAction += () => btnCraftingDlgCancel_Click(currentLoadout);
            ButtonClickedEvent cancelButtonClickedEvent = new ButtonClickedEvent();
            cancelButtonClickedEvent.AddListener(cancelButtonAction);
            cancelButton.GetComponent<Button>().onClick = cancelButtonClickedEvent;

            // Clear list
            DestroyAllChildren(scrlCraftingItemList);

            // Generate list
            int cnt = 0;
            if (cost > 0)
            {
                GameObject credits = GameObject.Instantiate(listItemAsset);
                credits.transform.SetParent(scrlCraftingItemList, false);
                credits.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = "(" + cost + ") Credits";
                credits.layer = scrlCraftingItemList.gameObject.layer;
                cnt++;
            }

            scrlCraftingItemList.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, listItemSpacing * (materials.Count + cnt));

            foreach (KeyValuePair<int,int> kvp in materials)
            {                
                GameObject materialLi = GameObject.Instantiate(listItemAsset);
                materialLi.transform.SetParent(scrlCraftingItemList, false);
                string color = ColorSys.white;
                if (missingMaterialIDs.Contains(kvp.Key))
                    color = ColorSys.infoNeg;
                materialLi.transform.Find("mc_saveloadoutItemText").gameObject.GetComponent<Text>().text = color + "(" + kvp.Value + ") " + ItemDB.GetItem(kvp.Key).itemName + "</color>";
                materialLi.transform.localPosition = new Vector3(
                        materialLi.transform.localPosition.x,
                        materialLi.transform.localPosition.y - (listItemSpacing * cnt),
                        materialLi.transform.localPosition.z);
                materialLi.layer = scrlCraftingItemList.gameObject.layer;
                cnt++;
            }

            dlgCraftingList.SetActive(true);
        }

        private static List<int> CargoHasMaterials(Dictionary<int,int> materials, CargoSystem cs, int curStation)
        {
            List<int> missing = new List<int>();

            foreach(KeyValuePair<int,int> kvp in materials)
                if (cs.CheckCargoItemQuantity((int)SVUtil.GlobalItemType.genericitem, kvp.Key, curStation, true) < kvp.Value)
                    missing.Add(kvp.Key);

            return missing;
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
              
        private static CraftingList CheckCargo(PersistentData.Loadout loadout, SpaceShipData shipData, Inventory inventory)
        {
            CraftingList missing = new CraftingList();

            Dictionary<int, int> cargoIndexes = new Dictionary<int, int>();
            if (loadout.weapons.Length > 0)
            {
                foreach (EquipedWeapon weapon in loadout.weapons)
                {
                    int[] cargoEntry = TryGetCargoItemIndex(cargoIndexes, inventory,
                        (int)SVUtil.GlobalItemType.weapon,
                        weapon.weaponIndex,
                        weapon.rarity);
                    if (cargoEntry != null)
                    {
                        if (!cargoIndexes.ContainsKey(cargoEntry[0]))
                            cargoIndexes.Add(cargoEntry[0], cargoEntry[1] - 1);
                        else
                            cargoIndexes[cargoEntry[0]]--;
                    }
                    else
                    {
                        if (GameData.data.weaponList[weapon.weaponIndex].isCrafted)
                        {
                            MC_SVManageBP.PersistentData.Blueprint bp = GetCustomWeaponBP(weapon.weaponIndex);
                            if (bp != null)
                            {
                                missing.missingCustomWeaponIndexes.Add(weapon.weaponIndex);
                                if (missing.customWeaponBPs.Count > 0 && missing.customWeaponBPs.ContainsKey(bp))
                                    missing.customWeaponBPs[bp]++;
                                else
                                    missing.customWeaponBPs.Add(bp, 1);
                            }
                            else
                            {
                                string str = GameData.data.weaponList[weapon.weaponIndex].name;
                                if (!missing.missingBPs.Contains(str))
                                    missing.missingBPs.Add(str);
                            }
                        }
                        else
                        {
                            int level = 1;
                            if (respectRarity)
                                level = weapon.rarity - 1;
                            Blueprint weaponBP = GetWeaponBP(weapon);
                            if (weaponBP != null)
                            {
                                CraftingList.BaseBP bp = new CraftingList.BaseBP(weaponBP, level);
                                if (missing.otherBPs.Count > 0 && missing.otherBPs.ContainsKey(bp))
                                    missing.otherBPs[bp]++;
                                else
                                    missing.otherBPs.Add(bp, 1);
                            }
                            else
                            {
                                string str = ItemDB.GetRarityColor(level + 1) + GameData.data.weaponList[weapon.weaponIndex].name + "</color>";
                                if (!missing.missingBPs.Contains(str))
                                    missing.missingBPs.Add(str);
                            }
                        }
                    }
                }
            }

            if (loadout.equipments.Length > 0)
            {
                foreach (InstalledEquipment equipment in loadout.equipments)
                {
                    for (int i = 0; i < equipment.qnt; i++)
                    {
                        int[] cargoEntry = TryGetCargoItemIndex(cargoIndexes, inventory,
                            (int)SVUtil.GlobalItemType.equipment,
                            equipment.equipmentID,
                            equipment.rarity);
                        if (cargoEntry != null)
                        {
                            if (!cargoIndexes.ContainsKey(cargoEntry[0]))
                                cargoIndexes.Add(cargoEntry[0], cargoEntry[1] - 1);
                            else
                                cargoIndexes[cargoEntry[0]]--;
                        }
                        else
                        {
                            int level = 1;
                            if (respectRarity)
                                level = equipment.rarity - 1;
                            Blueprint equipmentBP = GetEquipmentBP(equipment);
                            if (equipmentBP != null)
                            {
                                CraftingList.BaseBP bp = new CraftingList.BaseBP(equipmentBP, level);
                                if (missing.otherBPs.Count > 0 && missing.otherBPs.ContainsKey(bp))
                                    missing.otherBPs[bp]++;
                                else
                                    missing.otherBPs.Add(bp, 1);
                            }
                            else
                            {
                                string str = ItemDB.GetRarityColor(level + 1) + EquipmentDB.GetEquipment(equipment.equipmentID).equipName + "</color>";
                                if (!missing.missingBPs.Contains(str))
                                    missing.missingBPs.Add(str);
                            }
                        }
                    }
                }
            }

            return missing;
        }

        private static int[] TryGetCargoItemIndex(Dictionary<int, int> currentIndexes, Inventory inventory, int itemType, int itemID, int rarity)
        {
            Transform itemPanel = (Transform)AccessTools.Field(typeof(Inventory), "itemPanel").GetValue(inventory);
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);

            for (int i = 0; i < itemPanel.childCount; i++)
            {
                InventorySlot invSlot = itemPanel.GetChild(i).GetComponent<InventorySlot>();
                if (invSlot.itemIndex >= 0 && invSlot.itemIndex < cs.cargo.Count)
                {
                    CargoItem cargoItem = cs.cargo[invSlot.itemIndex];
                    if (cargoItem.itemType == itemType && cargoItem.itemID == itemID && 
                        ((respectRarity && cargoItem.rarity >= rarity) || !respectRarity))
                    {
                        if ((!currentIndexes.TryGetValue(invSlot.itemIndex, out int quantity)) || quantity > 0)
                            return new int[] { invSlot.itemIndex, cs.cargo[invSlot.itemIndex].qnt };
                    }
                }
            }
            return null;
        }

        private static bool TrySelectCargoItem(Inventory inventory, int itemType, int itemID, int rarity)
        {
            Transform itemPanel = (Transform)AccessTools.Field(typeof(Inventory), "itemPanel").GetValue(inventory);
            CargoSystem cs = GameObject.FindGameObjectWithTag("Player").GetComponent<CargoSystem>();
            //(CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);
            for (int i = 0; i < itemPanel.childCount; i++)
            {
                InventorySlot invSlot = itemPanel.GetChild(i).GetComponent<InventorySlot>();
                if (invSlot.itemIndex >= 0 && invSlot.itemIndex < cs.cargo.Count)
                {
                    CargoItem cargoItem = cs.cargo[invSlot.itemIndex];
                    if (cargoItem.itemType == itemType && cargoItem.itemID == itemID &&
                        ((respectRarity && cargoItem.rarity >= rarity) || !respectRarity))
                    {
                        invSlot.SlotClick();
                        return true;
                    }
                }
            }
            return false;
        }

        private static void DoEquip(PersistentData.Loadout loadout, SpaceShipData shipData, Inventory inventory)
        {
            float oldVol = SoundSys.SFXvolume;
            SoundSys.SetSFXVolume(0);

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

            SoundSys.SetSFXVolume(oldVol);

            if (failed)
                InfoPanelControl.inst.ShowWarning("Failed to load loadout " + loadout.name, 1, false);
            else if (!loadout.name.IsNullOrWhiteSpace())
            {
                InfoPanelControl.inst.ShowWarning("Equipped loadout " + loadout.name, 2, false);
                SoundSys.PlaySound(11, true);
            }
        }
        
        [HarmonyPatch(typeof(GameData), nameof(GameData.SaveGame))]
        [HarmonyPrefix]
        private static void GameDataSaveGame_Pre()
        {
            SaveGame();
        }

        private static void SaveGame()
        {
            if (data == null || data.loadouts.Count == 0)
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

        private class ListItemData : MonoBehaviour
        {
            internal int index;
        }
    }

    internal class CraftingList
    {
        internal Dictionary<MC_SVManageBP.PersistentData.Blueprint, int> customWeaponBPs;
        internal List<int> missingCustomWeaponIndexes;
        internal Dictionary<BaseBP, int> otherBPs;
        internal List<string> missingBPs;
        internal Dictionary<int, int> materials;

        internal CraftingList()
        {
            customWeaponBPs = new Dictionary<MC_SVManageBP.PersistentData.Blueprint, int>();
            missingCustomWeaponIndexes = new List<int>();
            otherBPs = new Dictionary<BaseBP, int>();
            missingBPs = new List<string>();
            materials = null;
        }

        internal float GetCost()
        {
            float cost = 0;

            foreach (MC_SVManageBP.PersistentData.Blueprint bp in customWeaponBPs.Keys)
            {
                float costMod = 0;
                foreach(SelectedItems item in bp.modifiers)
                {
                    WeaponModifier mod = Crafting.GetWeaponModifier(item.id);
                    costMod += mod.valueCost * item.qnt;
                }
                cost += GameData.data.weaponList[bp.weaponIDs[0]].price(1) * costMod + 100f;
            }

            return cost;
        }

        internal Dictionary<int, int> GetMaterials()
        {
            if (materials != null)
                return materials;

            materials = new Dictionary<int, int>();

            foreach (MC_SVManageBP.PersistentData.Blueprint bp in customWeaponBPs.Keys)
            {
                foreach (CraftMaterial mat in GameData.data.weaponList[bp.weaponIDs[0]].materials)
                {
                    if (materials.ContainsKey(mat.itemID))
                        materials[mat.itemID] += mat.quantity * customWeaponBPs[bp];
                    else
                        materials.Add(mat.itemID, mat.quantity * customWeaponBPs[bp]);
                }
            }

            foreach (BaseBP bp in otherBPs.Keys)
            {
                List<CraftMaterial> cm = new List<CraftMaterial>();
                if (bp.blueprint.itemType == (int)SVUtil.GlobalItemType.equipment)
                {
                    SpaceShip ss = AccessTools.StaticFieldRefAccess<SpaceShip>(typeof(PChar), "playerSpaceShip");
                    GenericCargoItem genI = new GenericCargoItem(bp.blueprint.itemType, bp.blueprint.itemID, bp.level + 1, null, ss.shipData.GetShipModelData(), ss, null);
                    cm = genI.GetCraftingMaterials(bp.level);                    
                }
                else if (bp.blueprint.itemType == (int)SVUtil.GlobalItemType.weapon)
                {
                    cm = GameData.data.weaponList[bp.blueprint.itemID].materials;
                }

                foreach (CraftMaterial mat in cm)
                {
                    if (materials.ContainsKey(mat.itemID))
                        materials[mat.itemID] += mat.quantity * otherBPs[bp];
                    else
                        materials.Add(mat.itemID, mat.quantity * otherBPs[bp]);
                }
            }

            return materials;
        }

        internal class BaseBP
        {
            internal Blueprint blueprint;
            internal int level;

            internal BaseBP(Blueprint bp, int level)
            {
                this.blueprint = bp;
                this.level = level;
            }
        }
    }

    [Serializable]
    internal class PersistentData
    {
        internal readonly List<Loadout> loadouts;

        internal PersistentData()
        {
            loadouts = new List<Loadout>();
        }

        [Serializable]
        internal class Loadout
        {
            internal string name = "";
            internal EquipedWeapon[] weapons;
            internal InstalledEquipment[] equipments;
            internal int shipModelID;

            internal Loadout(string name, List<EquipedWeapon> weapons, List<InstalledEquipment> equipments, int shipModel)
            {
                this.name = name;
                this.weapons = new EquipedWeapon[weapons.Count];
                weapons.ForEach(weapon => { this.weapons[weapons.IndexOf(weapon)] = new EquipedWeapon() { buttonCode = weapon.buttonCode, delayTime = weapon.delayTime, key = weapon.key, rarity = weapon.rarity, slotIndex = weapon.slotIndex, weaponIndex = weapon.weaponIndex }; });
                this.equipments = new InstalledEquipment[equipments.Count];
                equipments.ForEach(equipment => { this.equipments[equipments.IndexOf(equipment)] = new InstalledEquipment() { buttonCode = equipment.buttonCode, equipmentID = equipment.equipmentID, qnt = equipment.qnt, rarity = equipment.rarity }; });
                this.shipModelID = shipModel;
            }
        }
    }
}


