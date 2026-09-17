class DzrQuestTriggerSettings
{
    ref array<ref TStringArray> Location;
    ref array<ref TStringArray> NPC;
    ref TStringArray EnableSchedule;
    ref TStringArray TimeAvailable;
    ref TStringArray WeekAvailable;
    ref TStringArray DateAvailable;
    ref ref array<ref TStringArray> LocationNPC;
    ref ref array<ref TStringArray> OrientationNPC;
    ref TStringArray RemoveNPCWhenUnavailable;
    
    string UseLoadingScreen;
    string ActionName;
    string Icon;
    string Image;
    string Layout;
    string Sound;
    string Text1;
    string Text2;
    string Text3;
    string Title;
    string QuestName;
    
    string Currency;
    
    ref array<ref TStringArray> TeleportTo;
    string TeleportToOrder;
    
    string DelayBeforeTeleport;
    string DelayAfterTeleport;
    ref TStringArray RequiredInventoryItems;
    ref TStringArray RemoveItems;
    ref TStringArray GiveItems;
    ref TStringArray RequiredQuests;
    ref TStringArray IncompatibleQuests;
    ref TStringArray ResetQuests;
    
    string ActivationsPerPlayer;
    string InstantActivation;
    string Autoclose;
    
    ref TStringArray CustomFunction;
    
    
    void DzrQuestTriggerSettings(TStringArray SettingsFileArray)
    {
        Location = new array<ref TStringArray>();
        NPC = new array<ref TStringArray>();
        EnableSchedule = new TStringArray();
        TimeAvailable = new TStringArray();
        WeekAvailable = new TStringArray();
        DateAvailable = new TStringArray();
        LocationNPC =  new array<ref TStringArray>();
        OrientationNPC =  new array<ref TStringArray>();
        RemoveNPCWhenUnavailable = new TStringArray();
        
        UseLoadingScreen = "1";
        ActionName = "INIT";
        Icon = "INIT";
        Image = "INIT";
        Layout = "INIT";
        Sound = "INIT";
        Text1 = "INIT";
        Text2 = "INIT";
        Text3 = "INIT";
        Title = "INIT";
        QuestName = "INIT";
        
        TeleportTo = new array<ref TStringArray>();
        TeleportToOrder = "INIT";
        
        DelayBeforeTeleport = "8";
        DelayAfterTeleport = "5";
        RequiredInventoryItems = new TStringArray();
        RemoveItems = new TStringArray();
        GiveItems = new TStringArray();
        RequiredQuests = new TStringArray();
        IncompatibleQuests = new TStringArray();
        ResetQuests = new TStringArray();
        
        ActivationsPerPlayer = "INIT";
        InstantActivation = "INIT";
        Autoclose = "0";
        
        CustomFunction = new TStringArray();
        
        
        
        foreach (string SettingsLine : SettingsFileArray)
        {
            TStringArray LineArrr = new TStringArray;
            SettingsLine.Split("=", LineArrr);
            
            //Print(LineArrr);
            
            string TheLine = LineArrr.Get(0);
            
            TheLine = TheLine.Trim();
            //TheLine.Replace(" ","");
            
            
            
            //Print(LineArrr.Get(1));
            // Print(LineArrr.Get(1).Trim());
            
            //check if LineArrr is not single settings
            TStringArray multiSetting = new TStringArray;
            TStringArray multiSettingArr = new TStringArray;
            LineArrr.Get(1).Split(",",multiSetting);
            bool isMulti = 0;
            if(multiSetting.Count() > 1)
            {
                // PrintD("Is multi");
                isMulti = 1;
                foreach(string aSetting : multiSetting)
                {
                    multiSettingArr.Insert(aSetting.Trim());
                    // PrintD(aSetting);
                }
            }
            else
            {
                // PrintD("Is NOT multi");
                multiSettingArr.Insert(LineArrr.Get(1).Trim());
                // PrintD(LineArrr.Get(1).Trim())
            }
            //if multiple, split
            //switch to insertall
            //Print(isMulti);
            
            switch(TheLine)
            {
                //Arrays
                
                case "Location":
                // PrintD("LOCATION mutliseting");
                // PrintD(multiSettingArr);
                Location.Insert(multiSettingArr);
                break;
                case "NPC":
                // PrintD("NPC mutliseting");
                // PrintD(multiSettingArr);
                NPC.Insert(multiSettingArr);
                break;
                case "EnableSchedule":
                //TODO: Can be simplified to not use isMulti
                if(isMulti) {EnableSchedule.InsertAll(multiSettingArr);} else { EnableSchedule.Insert(LineArrr.Get(1).Trim()); };
                break;
                case "TimeAvailable":
                if(isMulti) {TimeAvailable.InsertAll(multiSettingArr);} else { TimeAvailable.Insert(LineArrr.Get(1).Trim()); };
                break;
                case "WeekAvailable":
                if(isMulti) {WeekAvailable.InsertAll(multiSettingArr);} else { WeekAvailable.Insert(LineArrr.Get(1).Trim()); };
                break;
                case "DateAvailable":
                if(isMulti) {DateAvailable.InsertAll(multiSettingArr);} else { DateAvailable.Insert(LineArrr.Get(1).Trim()); };
                break;
                case "LocationNPC":
                LocationNPC.Insert(multiSettingArr);
                break;
                case "OrientationNPC":
                OrientationNPC.Insert(multiSettingArr);
                break;
                case "RemoveNPCWhenUnavailable":
                RemoveNPCWhenUnavailable.Insert(LineArrr.Get(1).Trim());
                break;
                case "TeleportTo":
                TeleportTo.Insert(multiSettingArr);
                break;
                case "RequiredInventoryItems":
                RequiredInventoryItems.Insert(LineArrr.Get(1).Trim());
                break;
                case "RemoveItems":
                RemoveItems.Insert(LineArrr.Get(1).Trim());
                break;
                case "GiveItems":
                GiveItems.Insert(LineArrr.Get(1).Trim());
                break;
                case "RequiredQuests":
                RequiredQuests.Insert(LineArrr.Get(1).Trim());
                break;
                case "IncompatibleQuests":
                IncompatibleQuests.Insert(LineArrr.Get(1).Trim());
                break;
                case "ResetQuests":
                ResetQuests.Insert(LineArrr.Get(1).Trim());
                break;
                
                // strings
                case "UseLoadingScreen":
                UseLoadingScreen = LineArrr.Get(1).Trim();
                break;
                case "ActionName":
                ActionName = LineArrr.Get(1).Trim();
                break;
                case "Title":
                Title = LineArrr.Get(1).Trim();
                break;
                case "QuestName":
                QuestName = LineArrr.Get(1).Trim();
                break;
                case "Text1":
                Text1 = LineArrr.Get(1).Trim();
                break;
                case "Text2":
                Text2 = LineArrr.Get(1).Trim();
                break;
                case "Text3":
                Text3 = LineArrr.Get(1).Trim();
                break;
                case "Sound":
                Sound = LineArrr.Get(1).Trim();
                break;
                case "Layout":
                Layout = LineArrr.Get(1).Trim();
                break;
                case "Image":
                Image = LineArrr.Get(1).Trim();
                break;
                case "Icon":
                Icon = LineArrr.Get(1).Trim();
                break;
                case "TeleportToOrder":
                TeleportToOrder = LineArrr.Get(1).Trim();
                break;
                case "DelayBeforeTeleport":
                DelayBeforeTeleport = LineArrr.Get(1).Trim();
                break;
                case "DelayAfterTeleport":
                DelayAfterTeleport = LineArrr.Get(1).Trim();
                break;
                case "ActivationsPerPlayer":
                ActivationsPerPlayer = LineArrr.Get(1).Trim();
                break;
                case "InstantActivation":
                InstantActivation = LineArrr.Get(1).Trim();
                break;
                case "Autoclose":
                Autoclose = LineArrr.Get(1).Trim();
                break;
                case "Currency":
                Currency = LineArrr.Get(1).Trim();
                break;
                
                
                
            }
        }
        int locationBlockIndex = 0;
        //TODO: Same index processing here
        // Print("|||||||||||||||PLAYERBASE|||||||||||||||");
        //Print(m_Settings);
        // Print(Location);
        // Print(Location[locationBlockIndex]);
        // Print(Location[locationBlockIndex][0]);
        // Print("=================PLAYERBASE===============");
        //Print(NPC);
    }
    
    
    
    
    static bool LoadSettingsFile(string m_QuestName)
    {
        string SettingsPath = "$profile:DZR\\Quests\\"+m_QuestName+"\\Settings.txt"; 
        if(FileExist(SettingsPath))
        {
            
            TStringArray SettingsFileArray = new TStringArray();
            
            
            DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+m_QuestName, "Settings.txt", SettingsFileArray);
            DzrQuestTriggerSettings m_Settings = new DzrQuestTriggerSettings(SettingsFileArray);
            string Response;
            // m_Settings = CompileSettings(SettingsFileArray, Response);
            // Print("-----------------------LoadSettingsFile ---------------------------- SETTINGS EXIST");
            // Print(m_Settings);
            // Print(m_Settings.ActionName);
            // Print("-----------------------LoadSettingsFile ---------------------------- SETTINGS EXIST");
            
            if(FileExist("$profile:DZR\\Quests\\SettingsCache\\"+m_QuestName+"_Settings.json"))
            {
                DeleteFile("$profile:DZR\\Quests\\SettingsCache\\"+m_QuestName+"_Settings.json");
            }
            
            JsonFileLoader<DzrQuestTriggerSettings>.JsonSaveFile("$profile:DZR\\Quests\\SettingsCache\\"+m_QuestName+"_Settings.json", m_Settings);
            if(FileExist("$profile:DZR\\Quests\\SettingsCache\\"+m_QuestName+"_Settings.json"))
            {
                return true;
            }
        }
        return false;
    }
    
    
    /*
        
        static DzrQuestTriggerSettings CompileSettings(TStringArray SettingsFileArray, out string Response)
        {
        // Print("---------CompileSettings-------------=============================================");
        DzrQuestTriggerSettings m_Settings = new DzrQuestTriggerSettings();
        //Print(SettingsFileArray);
        Response = "TOTAL: " + SettingsFileArray.Count().ToString();
        foreach (string SettingsLine : SettingsFileArray)
        {
        TStringArray LineArrr = new TStringArray;
        SettingsLine.Split("=", LineArrr);
        
        //Print(LineArrr);
        
        string TheLine = LineArrr.Get(0);
        
        TheLine = TheLine.Trim();
        //TheLine.Replace(" ","");
        
        // Print(TheLine);
        // Print(LineArrr.Get(1).Trim());
        Response = Response + ", " +TheLine + " : "+LineArrr.Get(1).Trim();
        switch(TheLine)
        {
        //Arrays
        
        case "Location":
        m_Settings.Location.Insert(LineArrr.Get(1).Trim());
        break;
        case "NPC":
        m_Settings.NPC.Insert(LineArrr.Get(1).Trim());
        break;
        case "TimeAvailable":
        m_Settings.TimeAvailable.Insert(LineArrr.Get(1).Trim());
        break;
        case "WeekAvailable":
        m_Settings.WeekAvailable.Insert(LineArrr.Get(1).Trim());
        break;
        case "DateAvailable":
        m_Settings.DateAvailable.Insert(LineArrr.Get(1).Trim());
        break;
        case "LocationNPC":
        m_Settings.LocationNPC.Insert(LineArrr.Get(1).Trim());
        break;
        case "OrientationNPC":
        m_Settings.OrientationNPC.Insert(LineArrr.Get(1).Trim());
        break;
        case "RemoveNPCWhenUnavailable":
        m_Settings.RemoveNPCWhenUnavailable.Insert(LineArrr.Get(1).Trim());
        break;
        case "TeleportTo":
        m_Settings.TeleportTo.Insert(LineArrr.Get(1).Trim());
        break;
        case "RequiredInventoryItems":
        m_Settings.RequiredInventoryItems.Insert(LineArrr.Get(1).Trim());
        break;
        case "RemoveItems":
        m_Settings.RemoveItems.Insert(LineArrr.Get(1).Trim());
        break;
        case "GiveItems":
        m_Settings.GiveItems.Insert(LineArrr.Get(1).Trim());
        break;
        case "RequiredQuests":
        m_Settings.RequiredQuests.Insert(LineArrr.Get(1).Trim());
        break;
        case "IncompatibleQuests":
        m_Settings.IncompatibleQuests.Insert(LineArrr.Get(1).Trim());
        break;
        
        // strings
        case "ActionName":
        m_Settings.ActionName = LineArrr.Get(1).Trim();
        break;
        case "Title":
        m_Settings.Title = LineArrr.Get(1).Trim();
        break;
        case "QuestName":
        m_Settings.QuestName = LineArrr.Get(1).Trim();
        break;
        case "Text1":
        m_Settings.Text1 = LineArrr.Get(1).Trim();
        break;
        case "Text2":
        m_Settings.Text2 = LineArrr.Get(1).Trim();
        break;
        case "Text3":
        m_Settings.Text3 = LineArrr.Get(1).Trim();
        break;
        case "Sound":
        m_Settings.Sound = LineArrr.Get(1).Trim();
        break;
        case "Layout":
        m_Settings.Layout = LineArrr.Get(1).Trim();
        break;
        case "Image":
        m_Settings.Image = LineArrr.Get(1).Trim();
        break;
        case "Icon":
        m_Settings.Icon = LineArrr.Get(1).Trim();
        break;
        case "TeleportToOrder":
        m_Settings.TeleportToOrder = LineArrr.Get(1).Trim();
        break;
        case "ActivationsPerPlayer":
        m_Settings.ActivationsPerPlayer = LineArrr.Get(1).Trim();
        break;
        case "InstantActivation":
        m_Settings.InstantActivation = LineArrr.Get(1).Trim();
        break;
        
        
        
        }
        
        }
        //Print(m_Settings.ActionName);
        //Print(m_Settings.Text1);
        // Print("---------CompileSettings-------------=============================================");
        
        return m_Settings;
        }
        
    */
    
    static bool DZR_PTL_GetFileLines(string folderPath, string fileName, out TStringArray fileContent)
    {
        string m_TxtFileName = fileName;
        FileHandle fhandle;
        fileContent = new TStringArray();
        
        if ( FileExist(folderPath +"\\"+ m_TxtFileName) )
        {
            fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
            string line_content;
            while ( FGets( fhandle,  line_content ) > 0 )
            {
                fileContent.Insert(line_content);
            }
            //////////Print("saved_access: "+saved_access);
            CloseFile(fhandle);				
            return true;
        }
        return false;
    }
    
    static bool DZR_PTL_GetFile(string folderPath, string fileName, out string fileContent, string defaults = "0", bool toDelete = false)
    {
        string m_TxtFileName = fileName;
        FileHandle fhandle;
        if ( FileExist(folderPath +"\\"+ m_TxtFileName) && !toDelete)
        {
            //file
            fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
            //string server_id;
            FGets( fhandle,  fileContent );
            CloseFile(fhandle);
            //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
            return true;
        }
        else 
        {
            
            TStringArray parts();
            string path = folderPath;
            path.Split("\\", parts);
            path = "";
            foreach (string part: parts)
            {
                path += part + "\\";
                if (part.IndexOf(":") == part.Length() - 1)
                continue;
                
                if(!toDelete)
                {
                    if (!FileExist(path) && !MakeDirectory(path))
                    {
                        //Print("Could not make dirs from path: " + path);
                        return false;
                    }
                }
                else
                {
                    DeleteFile(path);
                    //Print("Deleted folder: "+path);
                }
            }
            
            string pth = folderPath +"\\"+ m_TxtFileName;
            FileHandle file = OpenFile(pth, FileMode.WRITE);
            
            if (file != 0 && !toDelete)
            {
                FPrintln(file, defaults);
                CloseFile(file);
                //file
                fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
                //string server_id;
                FGets( file,  fileContent );
                
                //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
                CloseFile(file);
                return true;
            }
        }
        //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" ERROR!");
        return false;
    }
    
}

class DzrQuestTriggerSettingsTest
{
    string ActionName;
    
    static DzrQuestTriggerSettingsTest CompileSettings(TStringArray SettingsFileArray, out string Response)
    {
        // Print("---------CompileSettings-------------=============================================");
        DzrQuestTriggerSettingsTest m_Settings = new DzrQuestTriggerSettingsTest();
        //Print(SettingsFileArray);
        Response = "TOTAL: " + SettingsFileArray.Count().ToString();
        foreach (string SettingsLine : SettingsFileArray)
        {
            TStringArray LineArrr = new TStringArray;
            SettingsLine.Split("=", LineArrr);
            
            //Print(LineArrr);
            
            string TheLine = LineArrr.Get(0);
            
            TheLine = TheLine.Trim();
            //TheLine.Replace(" ","");
            
            // Print(TheLine);
            // Print(LineArrr.Get(1).Trim());
            Response = Response + ", " +TheLine + " : "+LineArrr.Get(1).Trim();
            switch(TheLine)
            {
                
                case "ActionName":
                m_Settings.ActionName = LineArrr.Get(1).Trim();
                break;
                
            }
            
        }
        //Print(m_Settings.ActionName);
        //Print(m_Settings.Text1);
        // Print("---------CompileSettings-------------=============================================");
        
        return m_Settings;
    }
    
}

modded class PlayerBase
{
    
    ref map<string, DzrQuestTrigger> m_DzrQuestObjects = new map<string, DzrQuestTrigger>;
    //ref map<int, ref map<bool, string> > m_DzrQuests;
    int m_CurrentPlayerDzrQuest = -1;
    string m_CurrentPlayerQuestName;
    string m_CurrentLayout;
    ref map<int, ref map<int, string>> m_DzrQuests = new map<int, ref map<int, string>>();
    
    ref map<int, ref array<string> > m_DzrQuestsOptions = new map<int, ref array<string> >();
    bool m_Option1 = false;
    bool m_Option2 = false;
    bool m_Option3 = false;
    bool m_Option4 = false;
    bool m_Option5 = false;
    bool m_Option6 = false;
    
    bool m_isDZRNPC = false;
    bool m_isDZRVisible = true;
    bool m_isDzrQuestAdmin = false;
    
    protected int m_SelectedOption = 0;
    
    string m_Option1_text = "0";
    string m_Option2_text = "0";
    string m_Option3_text = "0";
    string m_Option4_text = "0";
    string m_Option5_text = "0";
    string m_Option6_text = "0";
    
    //FIXING SYNC
    ref TStringArray m_QuestNames;
    ref TIntArray m_DzrQuestPermissions;
    ref TStringArray m_DzrQuestOptions1;
    ref TStringArray m_DzrQuestOptions2;
    ref TStringArray m_DzrQuestOptions3;
    ref TStringArray m_DzrQuestOptions4;
    ref TStringArray m_DzrQuestOptions5;
    ref TStringArray m_DzrQuestOptions6;
    int CurrentDzrQuestID = -1;
    bool m_ShowOptions = false;
    //FIXING SYNC
    
    bool DebugMode;
    string DisableCustomLayouts;
    
    bool InQuest = false;
    
    //DzrQuestTriggerSettings m_Settings = new DzrQuestTriggerSettings();
    ref TStringArray SettingsFileArrayRPC = new TStringArray();
    ref array<ref DzrQuestTriggerSettings> QuestsSettingsArray = new array<ref DzrQuestTriggerSettings>;
    ref array<DzrQuestTriggerSettingsTest> QuestsSettingsArrayTest = new array<DzrQuestTriggerSettingsTest>;
    //ref map<int, ref DzrQuestTriggerSettings> QuestsSettingsArray = new map<int, ref DzrQuestTriggerSettings>;
    ref TStringArray FullSettingsLinesArray = new TStringArray();
    override void Init()
    {	
        
        super.Init();
        
        GetRPCManager().AddRPC( "dzr_dquests", "ReceiveSettingsLineRPC", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "CompileGlobalArryaRPC", this, SingleplayerExecutionType.Both );
        
        
        
        //FIXING NEW
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateQuestNames", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateDzrQuestPermissions", this, SingleplayerExecutionType.Both );
        
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateOptions1", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateOptions2", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateOptions3", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateOptions4", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateOptions5", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "UpdateOptions6", this, SingleplayerExecutionType.Both );
        
        GetRPCManager().AddRPC( "dzr_dquests", "ResetOptions", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "ResetDzrQuestPermissions", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "ResetQuestNames", this, SingleplayerExecutionType.Both );
        //FIXING NEW
        
        
        GetRPCManager().AddRPC( "dzr_dquests", "Save1DzrQuestFromServer", this, SingleplayerExecutionType.Both );
        
        GetRPCManager().AddRPC( "dzr_dquests", "ResetDzrQuestVars", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "SetSelectedOption", this, SingleplayerExecutionType.Both );
        
        GetRPCManager().AddRPC( "dzr_dquests", "DzrQuestsDebug", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "DzrQuestData", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "PlayMusic1", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "SetDzrQuestAdmin", this, SingleplayerExecutionType.Both );
        
        // GetRPCManager().AddRPC( "dzr_dquests", "DZR_SetvInvisibleRPC", this, SingleplayerExecutionType.Both );
        
        //m_DzrQuests = new map<int, ref map<bool, string> >;
        
        //RegisterNetSyncVariableInt("m_CurrentPlayerQuestName");
        RegisterNetSyncVariableInt("m_CurrentPlayerDzrQuest");
        RegisterNetSyncVariableBool("m_Option1");
        RegisterNetSyncVariableBool("m_Option2");
        RegisterNetSyncVariableBool("m_Option3");
        //RegisterNetSyncVariableInt("m_SelectedOption");
        RegisterNetSyncVariableInt("m_isDZRNPC");
        RegisterNetSyncVariableInt("m_isDZRVisible");
        
        m_QuestNames = new TStringArray;
        m_DzrQuestPermissions = new TIntArray;
        m_DzrQuestOptions1 = new TStringArray;
        m_DzrQuestOptions2 = new TStringArray;
        m_DzrQuestOptions3 = new TStringArray;
        m_DzrQuestOptions4 = new TStringArray;
        m_DzrQuestOptions5 = new TStringArray;
        m_DzrQuestOptions6 = new TStringArray;
        
        string FileContents;
        DZR_PTL_GetFile("$profile:DZR\\Quests","Debug.txt", FileContents, "0");
        
        
        
        
        DebugMode = FileContents.ToInt();
        
        
        DZR_PTL_GetFile("$profile:DZR\\Quests","DisableCustomLayouts.txt", DisableCustomLayouts, "0");
        PrintD("_____________________________");
        PrintD(DisableCustomLayouts);
        /* 
            bool is_visible = true;
            
            int FLAGS_INVIS = EntityFlags.VISIBLE|EntityFlags.SOLID|EntityFlags.ACTIVE;
            //this.m_isInvisible = is_visible;
            if (is_visible)
            {
            this.ClearFlags(FLAGS_INVIS, true);
            }
            else
            {
            this.SetFlags(FLAGS_INVIS, true);
            // dBodySetInteractionLayer(survivor, PhxInteractionLayers.CHARACTER);
            }
            
            this.SetInvisible(is_visible);
            this.Update();
            this.SetSynchDirty();
            
        */
    }
    
    
	void DZR_SetHeadInvisible( bool invisible )
	{
		private Head_Default m_PlayerHead_DZR;
		if ( !m_PlayerHead_DZR )
		{
			int slot_id = InventorySlots.GetSlotIdFromString( "Head" );
			m_PlayerHead_DZR = Head_Default.Cast( GetInventory().FindPlaceholderForSlot( slot_id ) );
        }
		
		m_PlayerHead_DZR.SetInvisible( invisible );
        
        /*
            SetAttachmentInvisible( "Head", invisible );
            SetAttachmentInvisible( "Headgear", invisible );
            SetAttachmentInvisible( "Mask", invisible );
            SetAttachmentInvisible( "Eyewear", invisible );
        */
		//m_JM_IsHeadInvisible = invisible;
    }
    
    void DZR_SetvInvisibleRPC(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Print("DZR_SetvInvisibleRPC");
        Param2<EntityAI, bool> params;
        if (!ctx.Read(params))
        {
            Print("Empty RPC");
            return;
        }
        
        
        EntityAI theai = params.param1;
        Print("RPC theai");
        Print(theai);
        
        //Print(theOType);
        //GetGame().Chat(theOType, "colorFriendly" );
        /*
            if (!GetGame().IsClient())
            {
            GetGame().Chat("IS NOT CLIENT", "colorFriendly" );
            return;
            };
        */
        
        bool is_visible =  params.param2;
        if(theai.IsFlagSet(EntityFlags.VISIBLE))
        {
            GetGame().Chat("EntityFlags.VISIBLE = true", "colorFriendly" );
            Print("EntityFlags.VISIBLE = true");
            is_visible = true;
            theai.ClearFlags(EntityFlags.VISIBLE, true);
        }
        else
        {
            GetGame().Chat("EntityFlags.VISIBLE = false", "colorFriendly" );
            Print("EntityFlags.VISIBLE = false");
            theai.SetFlags(EntityFlags.VISIBLE, true);
        };
        
        GetGame().Chat(theai.GetType() + ": visible = "+is_visible, "colorFriendly" );
        Print(theai.GetType() + ": visible = "+is_visible);
        theai.Update();
        theai.SetSynchDirty();
        
        theai.SetInvisible(is_visible);
        theai.OnInvisibleSet(is_visible);
        theai.SetInvisibleRecursive(is_visible,theai);
        
        theai.Update();
        theai.SetSynchDirty();
        
    }
    
    
    bool HasItems(ref map<string, int> itemList, out ref map<string, int> missingItems)
    {
        // no items
        //bool hasItems = false;
        // every line
        //int reqnumber;
        
        Print("-----------HasItems---------------");
        Print("itemList.GetKey(0): "+itemList.GetKey(0));
        Print(itemList);
        int i = 0;
        foreach (int reqnumber : itemList) 
        {
            string className = itemList.GetKey(i);
            Print("Checking: "+className);
            // get the reqnumber from map
            //reqnumber = itemList.Get(className);
            int itemcount = CountItemsInPlayer(className);
            
            // if reqnumber > 0
            if(reqnumber > 0)
            {
                // count quantities
                if(itemcount >= reqnumber)
                {
                    Print("-----------HasItems true---------------");
                    return true;
                }
                missingItems.Set(className, itemcount)
                // quantity is >= reqnumber
                // if not enough - insert "ItemName" + quantity to returned array
                
            }
            // reqnumber = 0, itemcount >= reqnumber
            // if all items present and enough - return "1"
            Print(i);
            Print(className);
            Print(reqnumber);
            Print(itemcount);
            i++;
        }
        Print("-----------HasItems false---------------");
        return false;
    }
    
    bool RemoveItems2 (ref map<string, int> itemList)
    {   
        ref map<string, int> missingItems = new map<string, int>;
        int result = 0;
        Print("-----------RemoveItems2---------------");
        Print("itemList.GetKey(0): "+itemList.GetKey(0));
        if(HasItems(itemList, missingItems))
        {
            int i = 0;
            foreach(int theQuantinty : itemList)
            {
                string removalClassname = itemList.GetKey(i);
                CountItemsInPlayer(removalClassname, "remove", theQuantinty);
                i++;
            }
        }
        else
        {
            Print("-----------false---------------");
            return false;
        }
        Print("-----------true---------------");
        return true;
    }
    
    int CountItemsInPlayer(string className, string command = "check", int removalQuantity = 1)
    {
        Print("-----------CountItemsInPlayer "+className+" ------- "+command+": "+removalQuantity+" --------");
        Print(command);
        PlayerBase m_Player = this;
        
		GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        
		ref array<EntityAI> items = new array<EntityAI>;
        
		inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        int alreadyRemovedQuantity = 0;
        
        int itemq = 0;
        ItemBase removedObject;
		for(int i = 0; i < items.Count(); i++)
		{
			EntityAI item = items.Get(i);
			if(item && item.IsKindOf(className))
			{
                Print(item.GetQuantityMax());
                if( item.GetQuantityMax() <= 0)
                {
                    Print("item.GetQuantityMax() <= 0");
                    itemq++;
                    if(command == "remove" && alreadyRemovedQuantity < removalQuantity)
                    {
                        alreadyRemovedQuantity = alreadyRemovedQuantity + IterateUntilAllRemoved(items, className, removalQuantity,"single");
                    }
                    
                }
                else
                {
                    
                    Print("item.GetQuantityMax() >= 1");
                    itemq = itemq + item.GetQuantity();
                    if(command == "remove" && alreadyRemovedQuantity < removalQuantity)
                    {
                        
                        
                        //removedObject = ItemBase.Cast(item);
                        alreadyRemovedQuantity = alreadyRemovedQuantity + IterateUntilAllRemoved(items, className, removalQuantity);
                        //alreadyRemovedQuantity++;
                        
                    }
                };
            };
        };
        Print("PLAYER HAS TOTAL: "+itemq+" of "+className);
        Print("-----------CountItemsInPlayer END---------------");
		return itemq;
        
    }
    
    int IterateUntilAllRemoved(ref array<EntityAI> items, string className, int requiredQuantity, string mode = "quantity" )
    {
        
        Print("-----------IterateUntilAllRemoved---------------");
        int alreadyRemoved = 0;
        Print(className);
        // Print(requiredQuantity);
        // Print(alreadyRemoved);
        
        for(int i = 0; alreadyRemoved < requiredQuantity; i++)
        {
            ItemBase item = items.Get(i);;
            if(item && item.IsKindOf(className))
			{
                if(mode == "quantity")
                {
                    if(requiredQuantity > item.GetQuantity())
                    {
                        alreadyRemoved =  alreadyRemoved + item.GetQuantity();
                        removeObjectFromPlayer(item);
                        Print("Removed only "+alreadyRemoved+". Removing further.");
                    }
                    else
                    {
                        
                        
                        alreadyRemoved =  alreadyRemoved + requiredQuantity;
                        item.AddQuantity(-requiredQuantity);
                        
                        Print("Removed "+requiredQuantity+". Player has "+item.GetQuantity()+" items left");                    
                    };
                    
                    Print("requiredQuantity: "+requiredQuantity);
                    Print("alreadyRemoved: "+alreadyRemoved);
                }
                else
                {
                    alreadyRemoved =  alreadyRemoved + 1;
                    Print("Removed "+alreadyRemoved+" of "+requiredQuantity);
                    removeObjectFromPlayer(item);
                };
                
            };
        };
        
        if(requiredQuantity < alreadyRemoved)
        {
            Print("Not enough items");
            Print(className);
            Print(requiredQuantity);
            Print(alreadyRemoved);
            
        }
        return alreadyRemoved;
        Print("--------------------------");
        
    };
    
    void removeObjectFromPlayer(ItemBase item)
    {
        
        GetGame().ObjectDelete(item);
        ItemBase item_in_hands = ItemBase.Cast( this.GetHumanInventory().GetEntityInHands() );
        
        if ( item_in_hands == item )
        {
            this.LocalDestroyEntityInHands();
        }
    }
    
    void CompileGlobalArryaRPC()
    {
        
        ref TStringArray TempSettings;
        // JsonFileLoader<TStringArray>.JsonSaveFile("$profile:DZR\\Quests\\Global_Settings.json", FullSettingsLinesArray);
        
        // QuestsSettingsArray = new array<DzrQuestTriggerSettings>;
        
        // DzrQuestTriggerSettings m_Settings1 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings2 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings3 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings4 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings5 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings6 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings7 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings8 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings9 = new DzrQuestTriggerSettings();
        // DzrQuestTriggerSettings m_Settings10 = new DzrQuestTriggerSettings();
        
        // m_Settings1.ActionName = "1";
        // m_Settings2.ActionName = "2";
        // m_Settings3.ActionName = "3";
        // m_Settings4.ActionName = "4";
        // m_Settings5.ActionName = "5";
        // m_Settings6.ActionName = "6";
        // m_Settings7.ActionName = "7";
        // m_Settings8.ActionName = "8";
        // m_Settings9.ActionName = "9";
        // m_Settings10.ActionName = "10";
        
        TempSettings = new TStringArray();
        Print("CompileGlobalArryaRPC() 633");
        bool LoopGo = true;
        int mapIndex = 0;
        for(int i = 0; LoopGo; i++)
        {
            //Print("CompileGlobalArryaRPC() 637");
            if(!FullSettingsLinesArray[i])
            {
                LoopGo = false;
            };
            //Print("CompileGlobalArryaRPC() 642");
            string OneLine = FullSettingsLinesArray[i];
            Print(OneLine);
            if(OneLine != "--COMPILE")
            {
                TempSettings.Insert(OneLine);
                //Print("TempSettings");
                //Print(TempSettings);
                // Print("CompileGlobalArryaRPC() 650");
            }
            else
            {
                
                // Print("CompileGlobalArryaRPC() 655");
                string Response;
                DzrQuestTriggerSettings m_Settings1 = new DzrQuestTriggerSettings(TempSettings);
                //m_Settings1.ActionName = "HUY";
                //Print(m_Settings1.ActionName);
                //Print("TempSettings");
                //Print(TempSettings);
                //Print("m_Settings1 : RESPONSE: "+ Response);
                //Print(m_Settings1);
                int index = QuestsSettingsArray.Insert(m_Settings1);
                Print(index);
                Print(m_Settings1);
                //QuestsSettingsArray[index].CompileSettings(TempSettings, Response);
                //Print(QuestsSettingsArray.Get(index).ActionName);
                //QuestsSettingsArray.Set(index, m_Settings1);
                Print(QuestsSettingsArray);
                m_Settings1 = NULL;
                TempSettings.Clear();
                //mapIndex++;
            }
            
        }
        Print("CompileGlobalArryaRPC() 676");
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        // QuestsSettingsArray.Insert(m_Settings1);
        // Print(QuestsSettingsArray);
        
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
        Print(QuestsSettingsArray);
        Print(QuestsSettingsArray.Count());
        Print(QuestsSettingsArray[4].ActionName);
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
    }
    
    
    void CompileGlobalArryaRPC_ORIG()
    {
        
        ref TStringArray TempSettings;
        //JsonFileLoader<TStringArray>.JsonSaveFile("$profile:DZR\\Quests\\Global_Settings.json", FullSettingsLinesArray);
        ref DzrQuestTriggerSettingsTest m_Settings1;
        // QuestsSettingsArray = new array<DzrQuestTriggerSettings>;
        
        //m_Settings1 = new DzrQuestTriggerSettings();
        TempSettings = new TStringArray();
        //m_Settings = DzrQuestTriggerSettings.CompileSettings(OneArray);
        foreach (string OneLine: FullSettingsLinesArray)
        {
            if(OneLine != "--COMPILE")
            {
                TempSettings.Insert(OneLine);
                //Print("TempSettings");
                //Print(TempSettings);
            }
            else
            {
                
                
                string Response;
                m_Settings1 = DzrQuestTriggerSettingsTest.CompileSettings(TempSettings, Response);
                //m_Settings1.ActionName = "HUY";
                //Print(m_Settings1.ActionName);
                //Print("TempSettings");
                //Print(TempSettings);
                //Print("m_Settings1 : RESPONSE: "+ Response);
                //Print(m_Settings1);
                int index = QuestsSettingsArrayTest.Insert(m_Settings1);
                Print(index);
                //QuestsSettingsArray[index].CompileSettings(TempSettings, Response);
                //Print(QuestsSettingsArray.Get(index).ActionName);
                //QuestsSettingsArray.Set(index, m_Settings1);
                Print(QuestsSettingsArrayTest);
                
                TempSettings.Clear();
                
            }
        }
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
        //Print(QuestsSettingsArray[0]);
        Print(QuestsSettingsArrayTest[4].ActionName);
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
    }
    
    /*
        void CompileGlobalArryaRPC2()
        {
        ref DzrQuestTriggerSettings m_Settings1;
        ref TStringArray TempSettings;
        JsonFileLoader<TStringArray>.JsonSaveFile("$profile:DZR\\Quests\\Global_Settings.json", FullSettingsLinesArray);
        
        ref array <ref TStringArray> QuestsSettingsArray = new array <ref TStringArray>;
        
        m_Settings1 = new DzrQuestTriggerSettings();
        TempSettings = new TStringArray();
        //m_Settings = DzrQuestTriggerSettings.CompileSettings(OneArray);
        foreach (string OneLine: FullSettingsLinesArray)
        {
        if(OneLine != "--COMPILE")
        {
        TempSettings.Insert(OneLine);
        //Print("TempSettings");
        //Print(TempSettings);
        }
        else
        {
        m_Settings1 = new DzrQuestTriggerSettings();
        string Response;
        //m_Settings1 = DzrQuestTriggerSettings.CompileSettings(TempSettings, Response);
        //Print("TempSettings");
        //Print(TempSettings);
        //Print("m_Settings1 : RESPONSE: "+ Response);
        //Print(m_Settings1);
        int index = QuestsSettingsArray.Insert(TempSettings);
        Print(index);
        Print(QuestsSettingsArray.Get(index));
        Print(QuestsSettingsArray);
        
        TempSettings = new TStringArray();
        }
        }
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
        //Print(QuestsSettingsArray[0]);
        //Print(QuestsSettingsArray[0].ActionName);
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
        }
    */
    void ReceiveSettingsLineRPC(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        {
            GetGame().Chat("Empty RPC", "colorImportant");
            return;
        }
        FullSettingsLinesArray.Insert(params.param1);
        Print("LN RCV: "+params.param1);
        
        /*
            if(params.param1 == "--COMPILE")
            {
        */  
        
        //JsonFileLoader<TStringArray>.JsonSaveFile("$profile:DZR\\Quests\\"+SettingsFileArrayRPC[0]+"_Settings.json", SettingsFileArrayRPC);
        /*
            ref DzrQuestTriggerSettings m_Settings = new DzrQuestTriggerSettings();
            m_Settings = DzrQuestTriggerSettings.CompileSettings(SettingsFileArrayRPC);
            // Print("_______________________________________");
            // Print("________________m_Settings_______________________");
            // Print(m_Settings);
            // Print(m_Settings.ActionName);
            // Print("_______________________________________");
            
            Print("__________________QuestsSettingsArray AFTER COMPIEL_____________________");
            if(m_Settings != NULL)
            {
            QuestsSettingsArray.Insert(m_Settings);
            Print("m_Settings NULL!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            }
            
            Print(QuestsSettingsArray);
            Print(QuestsSettingsArray[0]);
            Print(QuestsSettingsArray[0].Title);
            Print("SETTINGS COMPILED");
            Print("__________________m_Settings_____________________");
            
        */
        //SettingsFileArrayRPC = new TStringArray();
        
        /*
            }
            else
            {
            if(!SettingsFileArrayRPC)
            {
            SettingsFileArrayRPC = new TStringArray();
            SettingsFileArrayRPC.Insert(params.param1);
            }
            else
            {
            SettingsFileArrayRPC.Insert(params.param1);
            }
            }
        */
    }
    
    bool m_DZRQ_isInsivible = false;
    override bool CanBeTargetedByAI( EntityAI ai )
    {
        if (!super.CanBeTargetedByAI( ai ))
        return false;
        
        if (m_DZRQ_isInsivible)
        return false;
        
        return true;
    }
    
    void SetDZRQVisibility(bool val)
    {
        m_DZRQ_isInsivible = val;
    }
    
    
    
    void PrintD(string text)
    {
        if(DebugMode)
        {
            Print("[DZR_QUESTS Client PLAYERBASE Debug] :::: "+ text);
        }
    }
    
    bool DZR_PTL_GetFileLines(string folderPath, string fileName, out TStringArray fileContent)
    {
        string m_TxtFileName = fileName;
        FileHandle fhandle;
        fileContent = new TStringArray();
        
        if ( FileExist(folderPath +"\\"+ m_TxtFileName) )
        {
            fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
            string line_content;
            while ( FGets( fhandle,  line_content ) > 0 )
            {
                fileContent.Insert(line_content);
            }
            //////////Print("saved_access: "+saved_access);
            CloseFile(fhandle);				
            return true;
        }
        return false;
    }
    
    bool DZR_PTL_GetFile(string folderPath, string fileName, out string fileContent, string defaults = "0", bool toDelete = false)
    {
        string m_TxtFileName = fileName;
        FileHandle fhandle;
        if ( FileExist(folderPath +"\\"+ m_TxtFileName) && !toDelete)
        {
            //file
            fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
            //string server_id;
            FGets( fhandle,  fileContent );
            CloseFile(fhandle);
            //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
            return true;
        }
        else 
        {
            
            TStringArray parts();
            string path = folderPath;
            path.Split("\\", parts);
            path = "";
            foreach (string part: parts)
            {
                path += part + "\\";
                if (part.IndexOf(":") == part.Length() - 1)
                continue;
                
                if(!toDelete)
                {
                    if (!FileExist(path) && !MakeDirectory(path))
                    {
                        //Print("Could not make dirs from path: " + path);
                        return false;
                    }
                }
                else
                {
                    DeleteFile(path);
                    //Print("Deleted folder: "+path);
                }
            }
            
            string pth = folderPath +"\\"+ m_TxtFileName;
            FileHandle file = OpenFile(pth, FileMode.WRITE);
            
            if (file != 0 && !toDelete)
            {
                FPrintln(file, defaults);
                CloseFile(file);
                //file
                fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
                //string server_id;
                FGets( file,  fileContent );
                
                //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
                CloseFile(file);
                return true;
            }
        }
        //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" ERROR!");
        return false;
    }
    
    //FIXING NEW
    void SetDzrQuestAdmin(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<bool> params;
        if (!ctx.Read(params))
        {
            GetGame().Chat("Empty RPC", "colorImportant");
            return;
        }
        
        m_isDzrQuestAdmin = params.param1;
        
    }
    
    bool IsDzrQuestAdmin()
    {
        return m_isDzrQuestAdmin;
    }
    
    void DzrQuestData(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<int> params;
        if (!ctx.Read(params))
        {
            GetGame().Chat("Empty RPC", "colorImportant");
            return;
        }
        // return;
        //m_QuestNames = new TStringArray;
        //Param1<int> nullData;
        if(params.param1 == -1)
        {
            int pi = 0;
            foreach (string aDzrQuest : m_QuestNames)
            {        
                GetGame().Chat(aDzrQuest+": "+m_DzrQuestPermissions.Get(pi).ToString(), "colorFriendly");
                
                pi++;
            }
        }
        else
        {
            string theName  = m_QuestNames.Get(params.param1);
            string thePerm  = m_DzrQuestPermissions.Get(params.param1).ToString();
        }
        
        //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestVars", nullData, true, identity); 
        GetGame().Chat(theName+": "+thePerm, "colorFriendly");
        //GetGame().Chat(thePerm, "colorFriendly");
        
    }
    
    void SetCurrentLayout(string path)
    {
        // GetGame().Chat("SetCurrentLayout: "+path, "colorFriendly");  
        // GetGame().Chat(this.ToString(), "colorFriendly");  
        Print(path);
        Print(this);
        //if(DisableCustomLayouts == "0")
        //{
        m_CurrentLayout = path;
        //}
    }
    
    string GetCurrentLayout()
    {
        return m_CurrentLayout;
    }
    
    void PlayMusic1(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        /*
            Param2<string, string> params;
            if (!ctx.Read(params))
            return;
            //Print("===================== RPC EXPLODE ON CLIENT ==================");
            if ( GetGame().IsClient() || !GetGame().IsMultiplayer() )
            {
            Print("SOUNDING!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!___________________PlayMusic1");
            PlayMusic();
            
            }
        */
    }
    
    //FIXING NEW
    void DzrQuestsDebug(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        
        int pi = 0;
        foreach (string aDzrQuest : m_QuestNames)
        {        
            ////Print("_______"+pi+"_________");
            ////Print(aDzrQuest);
            ////Print(m_DzrQuestPermissions.Get(pi));
            ////Print(m_DzrQuestOptions1.Get(pi));
            ////Print(m_DzrQuestOptions2.Get(pi));
            ////Print(m_DzrQuestOptions3.Get(pi));
            ////Print(m_DzrQuestOptions4.Get(pi));
            ////Print(m_DzrQuestOptions5.Get(pi));
            ////Print(m_DzrQuestOptions6.Get(pi));
            ////Print("_____________________");
            pi++;
        }
    }
    void UpdateQuestNames(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        //m_QuestNames = new TStringArray;
        int index = m_QuestNames.Insert(params.param1);
        ////Print("███ m_QuestNames: " + m_QuestNames.Get(index));
        // GetGame().Chat("PlayerBase Receiving new name: "+params.param1, "colorFriendly");
    }
    
    void ControlsOff(bool val)
    {
        if(val)
        {
            GetGame().GetMission().PlayerControlDisable(INPUT_EXCLUDE_ALL);
            GetGame().GetMission().PlayerControlEnable(false);
            //GetGame().GetUIManager().ShowUICursor( true );
            
            //GetRPCManager().SendRPC( "dzr_dquests", "PlayMusic1", NULL, true);
        }
        else
        {
            GetGame().GetMission().PlayerControlEnable(true);
        }
    }
    
    void UpdateDzrQuestPermissions(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<int> params;
        if (!ctx.Read(params))
        return;
        //m_DzrQuestPermissions = new TStringArray;
        //Print("||||||||||||||||================================ UpdateDzrQuestPermissions");
        //Print(m_DzrQuestPermissions);
        int index = m_DzrQuestPermissions.Insert(params.param1);
        //Print("███ UpdateDzrQuestPermissions: " +  params.param1);
        //Print("███ m_DzrQuestPermissions: " + m_DzrQuestPermissions.Get(index));
        //Print("||||||||||||||||==========================================================");
        
    }
    
    void ResetQuestNames(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<int> params;
        Param1<int> nullData;
        
        m_QuestNames = new TStringArray;
        
        GetRPCManager().SendRPC( "dzr_dquests", "PlayerRedquestsNames", nullData, true, this.GetIdentity());
        GetGame().Chat("PlayerBase Names Reset. Redquesting new.", "colorImportant");
    }
    
    void ResetDzrQuestPermissions(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<int> params;
        Param1<int> nullData;
        
        m_DzrQuestPermissions = new TIntArray;
        //m_DzrQuestPermissions.Insert(params.param1);
        //////Print("███ ResetDzrQuestPermissions: " +  params.param1);
        ////Print("██ █ ResetDzrQuestPermissions" );
        ////Print(m_DzrQuestPermissions);
        GetRPCManager().SendRPC( "dzr_dquests", "PlayerRedquestsPermissions", nullData, true, this.GetIdentity());
    }
    
    void ResetOptions(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        
        
        m_SelectedOption = 0;
        
        m_DzrQuestOptions1 = new TStringArray;
        m_DzrQuestOptions2 = new TStringArray;
        m_DzrQuestOptions3 = new TStringArray;
        m_DzrQuestOptions4 = new TStringArray;
        m_DzrQuestOptions5 = new TStringArray;
        m_DzrQuestOptions6 = new TStringArray;
        
        Param1<int> nullData;
        ////Print("██ █ ResetDzrQuestPermissions" );
        GetRPCManager().SendRPC( "dzr_dquests", "PlayerRedquestsOptions", nullData, true, this.GetIdentity());
        
    }
    
    void UpdateOptions1(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        // m_DzrQuestOptions1 = new TStringArray;
        
        m_DzrQuestOptions1.Insert(params.param1);
        ////Print("███ UpdateOptions1: "  + params.param1);
    }
    
    void UpdateOptions2(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        // m_DzrQuestOptions2 = new TStringArray;
        m_DzrQuestOptions2.Insert(params.param1);
        ////Print("███ UpdateOptions2: " +  params.param1);
    }
    
    void UpdateOptions3(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        // m_DzrQuestOptions3 = new TStringArray;
        m_DzrQuestOptions3.Insert(params.param1);
        ////Print("███ UpdateOptions3: " +  params.param1);
    }
    
    void UpdateOptions4(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        // m_DzrQuestOptions4 = new TStringArray;
        m_DzrQuestOptions4.Insert(params.param1);
        ////Print("███ UpdateOptions4: " +  params.param1);
    }
    
    void UpdateOptions5(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        // m_DzrQuestOptions5 = new TStringArray;
        m_DzrQuestOptions5.Insert(params.param1);
        ////Print("███ UpdateOptions5: " +  params.param1);
    }
    
    void UpdateOptions6(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<string> params;
        if (!ctx.Read(params))
        return;
        //m_DzrQuestOptions6 = new TStringArray;
        m_DzrQuestOptions6.Insert(params.param1);
        ////Print("███ UpdateOptions6: " +  params.param1);
    }
    
    //FIXING NEW
    
    
    override void OnStoreSave(ParamsWriteContext ctx) {
        // auto writeData = new Param1<ref array<string>>(arrayGuests);
        
        super.OnStoreSave(ctx);
        //ctx.Write(m_CurrentPlayerQuestName);
        ctx.Write(m_CurrentPlayerDzrQuest);
        ctx.Write(m_Option1);
        ctx.Write(m_Option2);
        ctx.Write(m_Option3);
        //ctx.Write(m_SelectedOption);
        ctx.Write(m_isDZRNPC);
        ctx.Write(m_isDZRVisible);
    }
    
    void SetSelectedOption(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        Param1<int> params;
        if (!ctx.Read(params))
        return;
        
        m_SelectedOption = params.param1;
        
        GetGame().Chat("PlayerBase m_SelectedOption: "+m_SelectedOption.ToString(), "colorFriendly");
        
        //NotificationSystem.SendNotificationToPlayerExtended(this, 2, "m_SelectedOption!", m_SelectedOption.ToString(), "set:dayz_gui image:open");
        
        // //Print("███RPC████ ----- ██ SetSelectedOption RPC ███████████████████████████: ");
        ////Print(m_SelectedOption);
        this.SetSynchDirty();
        
    }
    
    void SetSelectedOption2(int value)
    {
        m_SelectedOption = value;
        ////Print("███████ ----- ██ SetSelectedOption2 ███████████████████████████: ");
        ////Print(m_SelectedOption);
        this.SetSynchDirty();
    }
    int GetSelectedOption()
    {
        this.SetSynchDirty();
        ////Print("███████ ----- ██ GetSelectedOption ███████████████████████████: ");
        ////Print(m_SelectedOption);
        return m_SelectedOption;
    }
    
    void SetShowOptions(bool val)
    {
        m_ShowOptions = val;
    }
    
    bool ShowOptions()
    {
        return m_ShowOptions;
    }
    
    bool HasOption1()
    {
        return m_Option1;
    }
    
    bool HasOption2()
    {
        return m_Option2;
    }
    
    bool HasOption3()
    {
        return m_Option3;
    }
    
    bool HasOption4()
    {
        return m_Option3;
    }
    
    bool HasOption5()
    {
        return m_Option5;
    }
    
    bool HasOption6()
    {
        return m_Option6;
    }
    
    void SetOption1(bool value)
    {
        m_Option1 = value;
    }
    
    void SetOption2(bool value)
    {
        m_Option2 = value;
    }
    
    void SetOption3(bool value)
    {
        m_Option3 = value;
    }
    
    void SetOption4(bool value)
    {
        m_Option4 = value;
    }
    
    void SetOption5(bool value)
    {
        m_Option5 = value;
    }
    
    void SetOption6(bool value)
    {
        m_Option6 = value;
    }
    
    
    string Option1()
    {
        return m_Option1_text;
    }
    string Option2()
    {
        return m_Option2_text;
    }
    string Option3()
    {
        return m_Option3_text;
    }
    string Option4()
    {
        return m_Option4_text;
    }
    string Option5()
    {
        return m_Option5_text;
    }
    string Option6()
    {
        return m_Option6_text;
    }
    
    
    //FIXING NEW
    
    
    bool GetQuestInstant(int index)
    {
        ////Print("███ >>>>|||||||||| ███ GetQuestNames ███.Get(index)========= INDEX: "+ index+" > " + m_QuestNames.Get(index));
        int pi = 0;
        foreach (string aDzrQuest : m_QuestNames)
        {        
            //PrintD(">>>>||||||||||_______"+pi+"_________");
            //PrintD(aDzrQuest);
            //PrintD(m_QuestNames.Get(pi));
            //PrintD("___________________███__________________");
            pi++;
        }
        /*
            if(QuestsSettingsArray)
            {
            Print(QuestsSettingsArray);
            Print(QuestsSettingsArray[index]);
            //Print(QuestsSettingsArray[index]);
            return QuestsSettingsArray[index].ActionName;
            }
        */
        if(QuestsSettingsArray[index].InstantActivation == "1")
        {
            return true;
        }
        return false;
    }
    
    string GetQuestNames(int index)
    {
        ////Print("███ >>>>|||||||||| ███ GetQuestNames ███.Get(index)========= INDEX: "+ index+" > " + m_QuestNames.Get(index));
        int pi = 0;
        foreach (string aDzrQuest : m_QuestNames)
        {        
            //PrintD(">>>>||||||||||_______"+pi+"_________");
            //PrintD(aDzrQuest);
            //PrintD(m_QuestNames.Get(pi));
            //PrintD("___________________███__________________");
            pi++;
        }
        /*
            if(QuestsSettingsArray)
            {
            Print(QuestsSettingsArray);
            Print(QuestsSettingsArray[index]);
            //Print(QuestsSettingsArray[index]);
            return QuestsSettingsArray[index].ActionName;
            }
        */
        if(QuestsSettingsArray[index].ActionName)
        {
            return QuestsSettingsArray[index].ActionName;
        }
        return "#dayz_game_loading";
    }
    
    
    int GetCurrentDzrQuestID()
    {
        return CurrentDzrQuestID;
    }
    
    
    void SetCurrentDzrQuestID(int index)
    {
        CurrentDzrQuestID = index;
    }
    
    int GetDzrQuestPermissions(int index)
    {
        ////Print(">>>>|||||||||| m_DzrQuestPermissions.Get(index)========= INDEX: "+ index+" > " + m_DzrQuestPermissions.Get(index));
        int pi = 0;
        foreach (string aDzrQuest : m_QuestNames)
        {        
            ////Print(">>>>||||||||||_______"+pi+"_________");
            ////Print(aDzrQuest);
            ////Print(m_DzrQuestPermissions.Get(pi));
            ////Print("_____________________________________");
            pi++;
        }
        return m_DzrQuestPermissions.Get(index);
    }
    
    string GetOptions1(int index)
    {
        //////Print("============================================================================GetOptions1=========" + GetOptions1(index))
        return m_DzrQuestOptions1.Get(index);
    }
    
    string GetOptions2(int index)
    {
        return m_DzrQuestOptions2.Get(index);
    }
    
    string GetOptions3(int index)
    {
        return m_DzrQuestOptions3.Get(index);
    }
    
    string GetOptions4(int index)
    {
        return m_DzrQuestOptions4.Get(index);
    }
    
    string GetOptions5(int index)
    {
        return m_DzrQuestOptions5.Get(index);
    }
    
    string GetOptions6(int index)
    {
        return m_DzrQuestOptions6.Get(index);
    }
    
    
    //FIXING NEW
    
    string OptionText(int OptionID)
    {
        ////////Print("███ OptionText ███████████████████████████: ");
        ////////Print(m_Option1_text);
        ////////Print(m_Option2_text);
        ////////Print(m_Option3_text);
        if(OptionID == 1)
        {
            return m_Option1_text;
        }
        if(OptionID == 2)
        {
            return m_Option2_text;
        }
        if(OptionID == 3)
        {
            return m_Option3_text;
        }
        return "ERROR";
    }
    
    override bool OnStoreLoad(ParamsReadContext ctx, int version) {
        Param1<ref array<string>> readData;
        
        if (!super.OnStoreLoad(ctx, version)) {
            return false;
        }
        
        if (!ctx.Read(m_CurrentPlayerDzrQuest)) {
            return false;
        }
        if (!ctx.Read(m_Option1)) {
            return false;
        }
        if (!ctx.Read(m_Option2)) {
            return false;
        }
        if (!ctx.Read(m_Option3)) {
            return false;
        }
        
        if (!ctx.Read(m_isDZRNPC)) {
            return false;
        }
        
        if (!ctx.Read(m_isDZRVisible)) {
            return false;
        }
        
        return true;
    }
    
    override void SetActions()
    {
        super.SetActions();
        AddAction(ActionActivateDzrQuest);
        
        AddAction(ActionOption1);
        AddAction(ActionOption2);
        AddAction(ActionOption3);
        AddAction(ActionOption4);
        AddAction(ActionOption5);
        AddAction(ActionOption6);
        // AddAction(ActionSecondaryDzrQuestActivation);
    }
    
    
    
    void Save1DzrQuestFromServer(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        return;
        Param6<int, int, string, string, string, string> params;
        if (!ctx.Read(params))
        return;
        //NEW FIX
        m_QuestNames.Insert(params.param3);
        m_DzrQuestPermissions.Insert(params.param2);
        m_DzrQuestOptions1.Insert(params.param4);
        m_DzrQuestOptions2.Insert(params.param5);
        m_DzrQuestOptions3.Insert(params.param6);
        //NEW FIX
        
        if(GetGame().IsClient())
        {
            PrintD("Save1DzrQuestFromServer ID + NPC+++++++++++++++");
            
            
            int hour;
            int minute;
            int second;
            
            GetHourMinuteSecond(hour, minute, second);
            
            
            
            ////Print("____________"+minute+":"+second+"________________" );
            ////Print("███ Got RPC: ");
            ////Print("███ NAME: "+params.param3);
            ////Print("███ ALLOWED: "+params.param2);
            ////Print("███ ID: "+params.param1);
            //////////Print(params.param2);
            //////////Print( m_DzrQuestObjects.Insert(params.param1, params.param2) );
            //map< bool, string> mData = new map< bool, string>(params.param2, params.param3);
            
            
            ref array<string> theOptions = new array<string>;
            theOptions.Insert(params.param4);
            theOptions.Insert(params.param5);
            theOptions.Insert(params.param6);
            
            //////Print(theOptions);
            
            if( m_DzrQuestsOptions.Get(params.param1) )
            {
                m_DzrQuestsOptions.Set(params.param1, theOptions);
            }
            else
            {
                m_DzrQuestsOptions.Insert(params.param1, theOptions );
            }
            
            
            
            
            ////////Print("██    ███        ██CURRENT m_DzrQuestOptions███████             █");
            ////////Print(m_DzrQuestsOptions);
            ////////Print(theOptions);
            ////////Print(theOptions.Get(0));
            ////////Print(theOptions.Get(1));
            ////////Print(theOptions.Get(2));
            
            map<int, string> mData = new map<int, string>();
            mData.Insert(params.param2, params.param3);
            
            if( m_DzrQuests.Get(params.param1) )
            {
                
                ////Print("__________SETTING EXISTING____________");
                ////Print("__________ EXISTING "+m_DzrQuests.Get(params.param1).GetKey(0)+"____________");
                
                ////Print("__________ NEW "+mData.GetKey(0)+"____________");
                
                m_DzrQuests.Set(params.param1, mData);
                
                ////Print("__________ UPDATED "+m_DzrQuests.Get(params.param1).GetKey(0)+"____________");
            }
            else
            {
                ////Print("__________INSERTING NEW____________");
                //////Print("__________ NEW "+mData.GetKey(0)+"____________");
                //m_DzrQuests.Set(params.param1, mData);
                //////Print("__________ UPDATED "+m_DzrQuests.Get(params.param1).GetKey(0)+"____________");
                
                m_DzrQuests.Insert(params.param1, mData );
                
                ////Print("__________ UPDATED "+m_DzrQuests.Get(params.param1).GetKey(0)+"____________");
                ////Print("__________ Allowed "+this.DzrQuestAllowed(params.param1)+"____________");
                
                //////////Print("███████CURRENT PORTALS████████");
                //////////Print(m_DzrQuestObjects);
                ////////Print("███████CURRENT m_DzrQuests████████");
                ////////Print( "m_DzrQuests.Count())" );
                ////////Print( m_DzrQuests.Count() );
                ////////Print( "█m_DzrQuests.Get("+params.param1+")█" );
                ////////Print( m_DzrQuests.Get(params.param1) );
                ////////Print( "█m_DzrQuests.GetKey("+params.param1+")█" );
                ////////Print( m_DzrQuests.GetKey(params.param1) );
                ////////Print( "█m_DzrQuests.Get("+params.param1+").GetKey( 0 )█" );
                ////////Print( m_DzrQuests.Get(params.param1).GetKey( 0 ) );
                ////////Print( "█m_DzrQuests.Get("+params.param1+").Get( m_DzrQuests.Get("+params.param1+").GetKey( 0 ) )█" );
                ////////Print( m_DzrQuests.Get(params.param1).Get( m_DzrQuests.Get(params.param1).GetKey( 0 ) ) );
                ////////Print( "_____________________________" );
                //////////Print("█GET█: "+params.param1);
                //////////Print(m_DzrQuestObjects.Get(params.param1));
            }
            
            ////Print("_________________________");
            
        }
        //NotificationSystem.SendNotificationToPlayerExtended(this, 2, "RPC!", "Got portal "+params.param1, "set:dayz_gui image:open");
        
        this.SetSynchDirty();
    }
    
    void SetCurrentDzrQuest(int value)
    {
        m_CurrentPlayerDzrQuest = value;
        ////////Print( "█ █ █ █ SetCurrentDzrQuest : "+ m_CurrentPlayerDzrQuest );
    }
    
    int GetCurrentDzrQuest()
    {
        return m_CurrentPlayerDzrQuest;
    }
    
    bool DzrQuestAllowed(int DzrQuestID)
    {
        if(m_DzrQuests)
        {
            ////////Print( "█ █ █ █ DzrQuestAllowed m_DzrQuests.Count()" );
            ////////Print( m_DzrQuests.Count() );
            ////Print("████████ START █ ------------ DzrQuestAllowed m_DzrQuests.Get(DzrQuestID).GetKey( "+DzrQuestID+" ).GetKey(0 ) █");
            ////Print(m_DzrQuests);
            ////Print(m_DzrQuests.Get(DzrQuestID));
            ////Print(m_DzrQuests.Get(DzrQuestID).GetKey( 0 ));
            ////Print(m_DzrQuests.Get(DzrQuestID).Get( 0 ));
            ////Print("████████ END █ ------------ DzrQuestAllowed m_DzrQuests.Get(DzrQuestID).GetKey( "+DzrQuestID+" ).GetKey(0 ) █");
            
            if(m_DzrQuests.Get(DzrQuestID))
            {
                if( m_DzrQuests.Get(DzrQuestID).GetKey(0) == 1)
                {
                    ////////Print("█ █ █ █  DzrQuestAllowed m_DzrQuests.Get(DzrQuestID).GetKey( "+DzrQuestID+" ).GetKey(0 ) == "+m_DzrQuests.Get(DzrQuestID).GetKey(0 ));
                    
                    return true;
                }
            }
        }
        ////////Print( "DzrQuestAllowed != 1" );
        return false;
    }
    
    void ActivateDzrQuest(int DzrQuestID)
    {
        InQuest=true;
        PrintD( "ActivateDzrQuest 924" );
        string QuestName = QuestsSettingsArray[DzrQuestID].QuestName;
        ref Param4<PlayerBase, int, int, string> m_Data = new Param4<PlayerBase, int, int, string>(this, DzrQuestID, m_SelectedOption, QuestName);
        GetRPCManager().SendRPC( "dzr_dquests", "ActivateDzrQuestForPlayer", m_Data, true, this.GetIdentity());
        m_SelectedOption = 0;
        PrintD( "ActivateDzrQuest 929" );
        
    }
    
    void SetInQuest(bool val)
    {
        InQuest = val;
    }
    
    bool IsInQuest()
    {
        return InQuest;
    }
    
    string CurrentDzrQuest()
    {
        ////////Print("███    ██                     ██ m_CurrentPlayerQuestName ████   ███ █");
        ////////Print(m_CurrentPlayerQuestName);
        return m_CurrentPlayerQuestName;
    }
    
    string QuestName(int DzrQuestID)
    {
        m_Option1_text = m_DzrQuestsOptions.Get(DzrQuestID).Get( 0 );
        m_Option2_text = m_DzrQuestsOptions.Get(DzrQuestID).Get( 1 );
        m_Option3_text = m_DzrQuestsOptions.Get(DzrQuestID).Get( 2 );
        m_CurrentPlayerQuestName = m_DzrQuests.Get(DzrQuestID).Get( m_DzrQuests.Get(DzrQuestID).GetKey( 0 ) );
        
    
    return m_CurrentPlayerQuestName;
    }
    
    string DzrQuestOption(int DzrQuestID, int OptionID)
    {
        //OptionID = OptionID;
        
        ////////Print("███    ████DzrQuestOption ████   ████");
        ////////Print(m_DzrQuestsOptions.Get(DzrQuestID).Get( OptionID ));
        
        return m_DzrQuestsOptions.Get(DzrQuestID).Get( OptionID );
    }
    
    bool PlayerNearDzrQuest(vector portal_coords)
    {
        
    }
    
    ref map<int, ref map<int, string>> GetDzrQuests()
    {
        return m_DzrQuests;
        ////////Print("███████PlayerBase GET PORTALS████████");
        //////////Print(m_DzrQuestObjects);
    }
    
    
}

modded class SurvivorBase {
    
    void SurvivorBase()
    {
        GetRPCManager().AddRPC( "dzr_dquests", "DZR_SetNPCVisibility", this, SingleplayerExecutionType.Both );
        //m_isInvisible = true;
    }
    
    void DZR_SetNPCVisibility(bool visible)
    {
        Print("_______DZR_SetNPCVisibility_________");
        Print(visible);
        
        int b1, b2, b3, b4;
        EntityAI theObjEnt = this;
        theObjEnt.GetPersistentID( b1, b2, b3, b4);
        
        string PID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
        
        //string visible = params.param1;
        
        //Print("_______CheckNPCCleanup 4_________");
        //Print(PID);
        
        EntityAI theai =  EntityAI.Cast(this);
        string path;
        if(visible)
        {
            path = "$profile:DZR\\Quests\\Cleanup\\Visible\\"+PID+".txt";
            DeleteFile("$profile:DZR\\Quests\\Cleanup\\Invisible\\"+PID+".txt");
        }
        else
        {
            path = "$profile:DZR\\Quests\\Cleanup\\Invisible\\"+PID+".txt";
            DeleteFile("$profile:DZR\\Quests\\Cleanup\\Visible\\"+PID+".txt");
        };
        
        FileHandle file = OpenFile(path, FileMode.WRITE);
        FPrintln(file, visible.ToString());
        CloseFile(file);
        
        if(FileExist(path))
        {
            
            Param2<EntityAI, bool> paramvis = new Param2<EntityAI, bool>(theai, visible);
            GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_CLIENT", paramvis, true );
            //GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_SERVER", paramvis, true );
            //DeleteFile(path);
            
        };
        
        //Print("________________");
        
    };
}
