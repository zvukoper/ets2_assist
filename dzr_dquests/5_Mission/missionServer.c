modded class MissionServer
{
	ref array<DzrQuestTrigger> m_DzrQuests;
	ref map<string, Object> m_DzrQuestObjects;
	int m_MsgDelay = 8;
    bool DebugMode = 0;
    bool DisableProgress = 0;
    bool DisableBlocking = 0;
    static ref array<EntityAI>AllMissionCars = new array<EntityAI>;
    static ref array<EntityAI>InvisibleObjects = new array<EntityAI>;
    static ref array<EntityAI>VisibleObjects = new array<EntityAI>;
	
    ref TStringArray SettingsLines = new TStringArray();
    ref array<ref TStringArray> AllSettingsArray = new array<ref TStringArray>();
    
    ref array<ref DzrQuestTriggerSettings> QuestsSettingsArray = new array<ref DzrQuestTriggerSettings>;
    ref TStringArray FullSettingsLinesArray = new TStringArray();
    ref map<string, int> QuestIDs = new map<string,int>;
    ref map<int, string> QuestPIDs = new map<int,string>;
    
    ref QSchedule QScheduleC;
    
	void MissionServer()
	{
		Print("[dzr_dquests] ::: Starting Serverside");
        
		
		GetRPCManager().AddRPC("dzr_dquests", "SendDzrQuestAttributesToClient", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "SendPlayerAllowed", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "ActivateDzrQuestForPlayer", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "SendDzrQuestsToPlayers", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "PlayerRedquestsDzrQuests", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "PlayerRedquestsPermissions", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "PlayerRedquestsNames", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "PlayerRedquestsOptions", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "CheckNPCCleanup", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "NewObjectPosition_RPC", this, SingleplayerExecutionType.Server);
		GetRPCManager().AddRPC("dzr_dquests", "NewObjectVisibility_RPC", this, SingleplayerExecutionType.Both);
        
        GetRPCManager().AddRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_SERVER", this, SingleplayerExecutionType.Both );
		GetRPCManager().AddRPC("dzr_dquests", "PlayerSimpleTelepor_RPC", this, SingleplayerExecutionType.Server);
		
        string FileContents;
        if(!FileExist("$profile:DZR\\Quests\\QuestAdmins.txt"))
        {
            DZR_PTL_GetFile("$profile:DZR\\Quests","QuestAdmins.txt", FileContents, "Delete this file and join server to make yourself admin.\r\nУдалите этот файл и подключитесь к серверу, чтобы стать портальным админом.");
            
        }	
        
        DZR_PTL_GetFile("$profile:DZR\\Quests","Debug.txt", FileContents, "0");
        DebugMode = FileContents.ToInt();
        Print("___________________________________________________________________________________________________________________");
        Print(FileContents);
        Print(DebugMode);
        Print("___________________________________________________________________________________________________________________");
        DZR_PTL_GetFile("$profile:DZR\\Quests","DisableProgress.txt", FileContents, "0");
        DisableProgress = FileContents.ToInt();
        DZR_PTL_GetFile("$profile:DZR\\Quests","DisableBlocking.txt", FileContents, "0");
        DisableBlocking = FileContents.ToInt();
        
        DZR_PTL_GetFile("$profile:DZR\\Quests","DebugTeleport.txt", FileContents, "1");
        
        m_DzrQuestObjects = new map<string, Object>;
        dzrCheckFolders();
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(RecreateDzrQuests, 18000, false);
        StartSchedule();
        
        //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(, 22000, false);
		//RecreateDzrQuests();
        
        
        
    };
    
    /*
        void countCurrency(int amount)
        {
        int[] notes = new int[]{ 2000, 500, 200, 100, 50, 20, 10, 5, 1 };
        int[] noteCounter = new int[9];
        
        // count notes using Greedy approach
        for (int i = 0; i < 9; i++) {
        if (amount >= notes[i]) {
        noteCounter[i] = amount / notes[i];
        amount = amount % notes[i];
        }
        }
        
        // Print notes
        Console.WriteLine("Currency Count ->");
        for (int i = 0; i < 9; i++) {
        if (noteCounter[i] != 0) {
        Console.WriteLine(notes[i] + " : "
        + noteCounter[i]);
        }
        }
        }
    */
    
    int ClosestCurrency(int expectedChange, ref array<int> playerMoney)
    {
        
        playerMoney.Invert();
        bool selected = false;
        Print("------- Finding a closest note for "+expectedChange);
        int curNote = playerMoney[playerMoney.Count() - 1];
        int curNoteMax = playerMoney[playerMoney.Count() - 1];
        int curNoteMin = expectedChange;
        Print("------- Setting default curNoteMax: "+curNoteMax);
        Print("------- Setting default curNoteMin: "+curNoteMin);
        
        
        //finding min
        foreach( int aNote: playerMoney)
        {
            if(aNote / expectedChange >= 1 && !selected)
            {
                Print("Checking Note: "+aNote);
                Print("SELECTED Note: "+aNote+". Stop.");
                curNote = aNote;
                selected = true;
            }            
        }
        
        
        Print("Selected the closest note to "+expectedChange+": "+curNote)
        return curNote;
    }
    
    array<int> getChange(int amount, ref array<int> currency, ref array<int> currentChange)
    {
        //Print(currentChange);
        if(amount == 0)
        {
            return currentChange;
        }
        
        int count = 0;
        int change = 0;
        
        Print("---------------");
        //Print(currency);
        
        foreach(int curVal : currency)
        {
            count = amount / curVal;
            
            
            if(count <= 0)
            {
                //Print(curVal.ToString() + " not suitable for change.");
                
                //return currentChange;
            }
            else
            {
                
                if(amount == curVal)
                {
                    Print("---Note "+curVal+" matches "+amount+" value 1 to 1. Giving that note.");
                    
                    currentChange.Insert(curVal);
                    change = amount - count*curVal;
                    //Print(change);
                    if(change > 0)
                    {
                        return getChange(change, currency, currentChange);
                    }
                    return currentChange;
                }
                
                //convert many minor bills into bigger single
                if(count % 10 == 0 )
                {
                    Print("---Count reached 10 for "+amount+" and "+curVal+" so convert to a bigger note");
                    change = amount - count*curVal;
                    //Print(change);
                    if(change > 0)
                    {
                        return getChange(change, currency, currentChange);
                    }
                    return currentChange;
                }
                else
                {
                    //convert many minor bills into bigger single
                    
                    Print("--- Found change of "+curVal+" x"+count+" in "+amount);
                    
                    for(int i = 0; i < count; i++)
                    {
                        currentChange.Insert(curVal);
                    };
                    
                    //Print(currentChange);
                    
                    change = amount - count*curVal;
                    //Print(change);
                    if(change > 0)
                    {
                        return getChange(change, currency, currentChange);
                    }
                    return currentChange;
                }
            }
        }
        return currentChange;
        Print("---------------");
    }
    
    array<int> amountToCurrecny(int amount, ref array<int> currency, bool first = false) {
        // Check if the amount is 0
        
        /*
            double TotalAmount;
            int DollarsConversion;
            int Change;     
            int Dollars;     
            int Hundreds = 0;
            int Fifties = 0;
            int Twenties = 0;
            int Tens = 0;
            int Fives = 0;
            int Ones = 0;
            int CentsConversion;    
            int Cents;
            int Quarters = 0;
            int Dimes = 0;
            int Nickels = 0;
            int Pennies = 0;
            
            cout << "Please enter the amount to convert: $";
            cin >> TotalAmount;
            
            //Dollars Calculation.
            //DollarsConversion = TotalAmount * 100;
            exchangeCurrency = amount / currencyValue;
            Change = DollarsConversion % 10000;
            Fifties = Change / 5000;
            Change %= 5000;
            Twenties = Change / 2000;
            Change %= 2000;
            Tens = Change / 1000;
            Change %= 1000;
            Fives = Change / 500;
            Change %= 500;
            Ones = Change / 100;
            Change %= 100;
            
            //Resets the Stack to calculate for the cents.
            Dollars = TotalAmount;   
            CentsConversion = TotalAmount * 1000;   
            Dollars *= 1000;    
            Cents = CentsConversion - Dollars;
            
            //Cents Calculation.
            Quarters = Cents / 250;
            Change = Cents % 250;
            Dimes = Change / 100;
            Change %= 100;
            Nickels = Change / 50;
            Change %= 50;
            Pennies = Change / 10;
            Change %= 10;
            
            cout << "\nNumber of Hundred Dollar Bills: " << Hundreds << endl;
            cout << "Number of Fifty Dollar Bills: " << Fifties << endl;
            cout << "Number of Twenty Dollar Bills: " << Twenties << endl;
            cout << "Number of Ten Dollar Bills: " << Tens << endl;
            cout << "Number of Five Dollar Bills: " << Fives << endl;
            cout << "Number of One Dollar Bills: " << Ones << endl;
            
            cout << "\nNumber of Quarters: " << Quarters << endl;
            cout << "Number of Dimes: " << Dimes << endl;
            cout << "Number of Nickels: " << Nickels << endl;
            cout << "Number of Pennies: " << Pennies << endl;
            
            return 0;
        */
        if(first)
        {
            currency.Invert();
        }
        Print("------------- Trying to exchange " + amount + " using: ");
        Print(currency);
        if (amount == 0) 
        {
            // If true, return an empty array since no coins are needed
            array<int> empty_arr = new array<int>();
            return empty_arr;
        }
        else 
        {
            // Check if the amount is greater than or equal to the first coin in the coins array
            
            
            if (amount >= currency[0]) 
            {
                Print("amount >= currency[0] ===" + amount +" : "+currency[0]);
                // Calculate the amount left after using the first coin
                int  left = amount - currency[0];
                Print(left);
                // Concatenate the first coin with the result of recursively calling amountToCurrecny with the remaining amount and coins
                
                
                array<int> other_change = amountToCurrecny(left, currency);
                Print("Inserting other change");
                Print(other_change);
                
                currency.InsertAll(other_change);
                
                
                Print(currency);
                return currency;    
                
            } 
            else 
            {
                //Print( amount + " < " +currency[0] );
                // If the amount is less than the first coin, remove the first coin from the array and recursively call amountToCurrecny
                currency.RemoveOrdered(0);
                //currency.shift();
                Print(currency);
                return amountToCurrecny(amount, currency);
            }
            
            Print("------------- FINISHING");
        }
        Print("------------- FINISHED");
    }
    
    void DZR_SetvInvisibleRPC_SERVER(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Print("DZR_SetvInvisibleRPC_SERVER");
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
        
        if(theai.IsInherited(CarScript))
        {
            Print("RPC theai CarScript is_visible = "+is_visible);
            CarScript.Cast(theai).DZR_SetNPCVisibility(is_visible);
        };
        
        if(theai.IsInherited(SurvivorBase))
        {
            Print("RPC theai PlayerBase");
            SurvivorBase.Cast(theai).DZR_SetNPCVisibility(is_visible);
            PlayerBase.Cast(theai).m_isDZRVisible = is_visible;
        };
        
        
        
        
    }
    
    void DeleteQuestNPC(int questID, vector pos)
    {
        // remove related NPC
        m_DzrQuests[questID].SetDisabledBySchedule(1);
        ClearNPCAtPos(pos);
        //QuestsSettingsArray[questID].ActionName;
        // set action disabled
        
    };
    
    void SpawnQuest(int questID)
    {
        // create NPCs
        m_DzrQuests[questID].SetDisabledBySchedule(0);
        // string settingsPath = "$profile:DZR\\Quests\\"+m_DzrQuests[questID].m_QuestName;
        // CreateNPCFromSettingsFile(settingsPath, m_DzrQuests[questID].m_QuestName + "_NPC.txt", pos, ori);
        // set action enabled
    };
    
    ref map<string, int> ConvertSetArrToMap(ref array<string> itemsLines)
    {
        Print("---------------------------ConvertSetArrToMap--------------------------------");
        ref map<string, int> itemList = new  map<string, int>;
        foreach ( string aline : itemsLines)
        {
            
            
            TStringArray ParamsLine = new TStringArray;
            TStringArray ParamsArray = new TStringArray;
            TStringArray OptionsArray = new TStringArray;
            
            if(aline.Contains(":")) //HAS PARAMS TO CHECK
            {
                Print("HAS :");
                aline.Split(":",ParamsLine); //ITEM:PARAMS
                
                string theItem = ParamsLine.Get(0);
                string theItemOptions = ParamsLine.Get(1);
                itemList.Insert(theItem, theItemOptions.ToInt());
                Print(theItemOptions);
            }
            else
            {
                Print("Single param");
                
                itemList.Insert(aline, 1);
                Print(aline);
            };
            
            
            
            
            
        };
        Print("__________");
        Print(itemList);
        Print("-----------------------------------------------------------");
        return itemList;
    };
    
    
    void CompileGlobalArryaRPC()
    {
        
        ref TStringArray TempSettings;
        
        
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
        Print(QuestsSettingsArray[0].TimeAvailable);
        Print(QuestsSettingsArray[0].Location);
        //Print(QuestsSettingsArray[4].TimeAvailable);
        PrintD("||||||||||||||||-------------------------------------------------------------------------------CompileGlobalArryaRPC");
        RegisterScheduleEvents2();
        
        
    };
    
    void RegisterScheduleEvents2()
    {
        
        
        Print("||||||||||||||||---DATA TEST QuestsSettingsArray--");
        
        int loc_index;
        int quest_index = 0;
        foreach(ref DzrQuestTriggerSettings Quest : QuestsSettingsArray)
        {  
            loc_index = 0;
            foreach(TStringArray location : Quest.Location)
            {
                Print("---------- "+loc_index+" : "+location[0]+"-------------");
                Print(Quest.ActionName);
                
                
                Print(Quest.TimeAvailable[loc_index]);
                Print(location);
                
                Print(Quest.NPC[loc_index]);
                Print(Quest.LocationNPC[loc_index]);
                Print(Quest.OrientationNPC[loc_index]);
                Print("---------quest_index------");
                Print(quest_index);
                Print(QuestPIDs[quest_index]);
                Print("---------rest------");
                Print(Quest.EnableSchedule[loc_index]);
                Print(Quest.TimeAvailable[loc_index]);
                Print(Quest.WeekAvailable[loc_index]);
                Print(Quest.DateAvailable[loc_index]);
                Print(Quest.LocationNPC[loc_index]);
                Print(Quest.OrientationNPC[loc_index]);
                Print(Quest.RemoveNPCWhenUnavailable[loc_index]);
                Print(Quest.QuestName);
                
                QScheduleC.RegisterEvent(location[0], Quest.NPC[loc_index], Quest.EnableSchedule[loc_index], Quest.TimeAvailable[loc_index], Quest.WeekAvailable[loc_index] , Quest.DateAvailable[loc_index] , Quest.LocationNPC[loc_index], Quest.OrientationNPC[loc_index], Quest.RemoveNPCWhenUnavailable[loc_index] , Quest.QuestName, QuestPIDs[quest_index] );
                Print("-----------------------");
                loc_index++;
            };
            quest_index++;
        };
        
        Print("||||||||||||||||---DATA TEST QuestsSettingsArray END--");
    };
    
    
    void PrintD(string text)
    {
        if(DebugMode == 1)
        {
            Print("[DZR_QUESTS Server Debug] :::: "+ text);
        }
    }
    
    void dzrCheckFolders()
    {
        string path;
        
        // ROOTS
        path = "$profile:DZR";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\Quests";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\Quests\\Cleanup";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\Quests\\Cleanup\\Visible";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\Quests\\Cleanup\\Invisible";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\Quests\\SettingsCache";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\players";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\players\\local_player_db";
        if(!FileExist(path)) MakeDirectory(path);
        // ROOTS
        
        
        
    }
    
    string GetPersistentID( EntityAI entity )
    {
        int b1, b2, b3, b4;
        entity.GetPersistentID( b1, b2, b3, b4);
        return b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
    }
    
    void DzrQuestsDebug()
    {
        
        //PlayerIdentity identity = sender;
        
        Param1<int> nullData;
        //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestVars", nullData, true, identity); 
        
        //foreach portal
        int pi = 0;
        foreach (DzrQuestTrigger aDzrQuest : m_DzrQuests)
        {        
            ////Print("_______"+pi+"_________");
            ////Print(aDzrQuest.m_QuestActionName);
            ////Print(aDzrQuest.m_QuestID);
            
            pi++;
        }
        
        
        
    }
    
    bool CheckAdmins(string steamid)
    {
        TStringArray AdminFileArray = new TStringArray;
        DZR_PTL_GetFileLines("$profile:DZR\\Quests", "QuestAdmins.txt", AdminFileArray);
        foreach (string AdminLine : AdminFileArray)
        {
            TStringArray LineArrr = new TStringArray;
            AdminLine.Split(" ", LineArrr);
            if(LineArrr.Get(0) == steamid && LineArrr.Get(2) == "1")
            {
                return true;
            }           
        }
        return false;
    }
    
    
    override void InvokeOnConnect(PlayerBase player, PlayerIdentity identity)
    {
        super.InvokeOnConnect(player, identity);
        player.m_isDZRNPC = false;
        SendDzrQuestsToOnePlayer(identity);
        
        player.m_isDzrQuestAdmin = CheckAdmins(identity.GetPlainId());
        
        /*
            for ( int i = 0; i < m_DzrQuestObjects.Count(); ++i )
            {
            string folder = m_DzrQuestObjects.GetKey( i );
            Object aDzrQuest = m_DzrQuestObjects.Get( folder );
            Send1DzrQuestToClients(aDzrQuest);
            }
        */
        //SendSettingsLineByLine(player, identity); 
        
        
        
        Param1<string> aLine;
        foreach (TStringArray SettingsArray : AllSettingsArray)
        {
            foreach (string SettingsLine : SettingsArray)
            {
                aLine = new Param1<string>(SettingsLine);
                GetRPCManager().SendRPC( "dzr_dquests", "ReceiveSettingsLineRPC", aLine, true, identity ); 
            }
            aLine = new Param1<string>("--COMPILE");
            GetRPCManager().SendRPC( "dzr_dquests", "ReceiveSettingsLineRPC", aLine, true, identity ); 
        }
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(GetRPCManager().SendRPC, 3000, false, "dzr_dquests", "CompileGlobalArryaRPC", aLine, true, identity);
        
        GetRPCManager().SendRPC( "dzr_dquests", "UpdateOnConnectVisibility_RPC", NULL, true); 
        
    }
    
    void PassSingleSettingsFileToClient(DzrQuestTriggerSettings theSettings, PlayerIdentity sender)
    {
        /*
            foreach (string SettingsLine : theSettings)
            {
            Param1<string> aLine = new Param1<string>(SettingsLine);
            GetRPCManager().SendRPC( "dzr_dquests", "ReceiveOneLineOfSettingsRPC", aLine, true, sender ); 
            }
            GetRPCManager().SendRPC( "dzr_dquests", "CompileSettingsRPC", aLine, true, sender ); 
        */
    }
    
    /*
        void SendSettingsLineByLine(PlayerIdentity identity, PlayerBase player)
        {
        GetRPCManager().SendRPC( "dzr_dquests", "RequestLoadingScreen", Fadedata, true );
        };
    */
    
    override void OnClientReadyEvent(PlayerIdentity identity, PlayerBase player)
    {
        super.OnClientReadyEvent(identity, player);
        
        
        
        // FIRST RUN
        string FileContents;
        string path = "$profile:DZR\\Quests\\QuestAdmins.txt";
        if(!FileExist(path)) 
        {
            DZR_PTL_GetFile("$profile:DZR\\Quests","QuestAdmins.txt", FileContents, identity.GetPlainId() + " " + identity.GetName() + " 1 ChangeThisPassword");
            NotificationSystem.SendNotificationToPlayerExtended(player, 20, "DZR Quests", "Мод установлен. Теперь вы портальный админ. Список админов в файле DZR\\Quests\\QuestAdmins.txt. 1 = активен, 0 = не активен.", "set:dayz_gui image:icon_favourite_on");
            NotificationSystem.SendNotificationToPlayerExtended(player, 20, "DZR Quests", "Добавьте ваш первый портал: напишите в чате (DzrQuestAdd TEST) без скобок. TEST — это название портала и папки в DZR\\Quests", "set:dayz_gui image:settings");
        }
        // FIRST RUN
    }	
    
    string GetDisplayName(string type)
    {
        string text;
        if(type == "LiquidType") {GetLiquidID(type, text); return text;};
        text = GetGame().ConfigGetTextOut("CfgVehicles " + type + " displayName");
        if(text == "") text = GetGame().ConfigGetTextOut("CfgWeapons " + type + " displayName");
        if(text == "") text = GetGame().ConfigGetTextOut("CfgAmmo " + type + " displayName");
        if(text == "") text = GetGame().ConfigGetTextOut("CfgNonAIVehicles " + type + " displayName");
        if(text == "") text = GetGame().ConfigGetTextOut("CfgMagazines " + type + " displayName");
        if(text == "") text = type;
        
        return text;
    }
    
    int GetLiquidID(string LiquidName, out string name = "")
    {
        int lid;
        //Print("_________________________________FUNCTION START GetLiquidID");
        //Print("Getting text for ID "+LiquidName);
        if( LiquidName == "LIQUID_BLOOD_0_P")      { name = "#inv_inspect_blood"; lid = 1;}
        if( LiquidName == "LIQUID_BLOOD_0_N")    {   name = "#inv_inspect_blood"; lid = 2;}
        if( LiquidName == "LIQUID_BLOOD_A_P")   {    name = "#inv_inspect_blood"; lid = 4;}
        if( LiquidName == "LIQUID_BLOOD_A_N")   {    name = "#inv_inspect_blood"; lid = 8;}
        if( LiquidName == "LIQUID_BLOOD_B_P")   {    name = "#inv_inspect_blood"; lid = 16;}
        if( LiquidName == "LIQUID_BLOOD_B_N")   {    name = "#inv_inspect_blood"; lid = 32;}
        if( LiquidName == "LIQUID_BLOOD_AB_P")   {   name = "#inv_inspect_blood"; lid = 64;}
        if( LiquidName == "LIQUID_BLOOD_AB_N")  {    name = "#inv_inspect_blood"; lid = 128;}
        if( LiquidName == "LIQUID_SALINE ")      {   name = "#inv_inspect_saline"; lid = 256;}
        
        if( LiquidName == "LIQUID_WATER")        {   name = "#inv_inspect_water"; lid = 512;}
        if( LiquidName == "LIQUID_RIVERWATER")   {   name = "#inv_inspect_river_water"; lid = 1024;}
        if( LiquidName == "LIQUID_VODKA")        {   name = "#inv_inspect_alcohol"; lid = 2048;}
        if( LiquidName == "LIQUID_BEER")         {   name = "#inv_inspect_beer"; lid = 4096;}
        if( LiquidName == "LIQUID_GASOLINE")     {   name = "#inv_inspect_gasoline"; lid = 8192;}
        if( LiquidName == "LIQUID_DIESEL")       {   name = "#inv_inspect_diesel"; lid = 16384;}
        if( LiquidName == "LIQUID_DISINFECTANT")  {  name = "#inv_inspect_saline"; lid = 32768;}
        if( LiquidName == "LIQUID_SOLUTION")     {   name = "#inv_inspect_solution"; lid = 65536;}
        
        //Print("_________________________________GetLiquidID returned name: "+name+" for "+lid);
        return lid;
    }
    
    float CountLiquidInInventory(string LiquidTypeString, PlayerBase m_Player, float required_qn,  out EntityAI r_container)
    {
        //Print("██████████ █CountLiquidInInventory█ ███████████████████");
        int LiquidType = GetLiquidID(LiquidTypeString);
        
        
        
        GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        array<EntityAI> items = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        int quantity = 0;
        int m_LT;
        
        PrintC(m_Player, "Found items: "+items.Count() );
        bool found = false;
        for(int i = 0; i < items.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(items.Get(i));
            if(item && !found)
            {
                //Print("inventory_item: "+item.GetDisplayName());
                PrintC(m_Player, item.GetDisplayName() );
                m_LT = ItemBase.Cast(item).GetLiquidType();
                
                //two waters fix
                if( (m_LT == 512 || m_LT == 1024) && (LiquidType == 512 || LiquidType == 1024))
                {
                    m_LT = LiquidType;
                }
                //two waters fix
                
                PrintC(m_Player, "Inventory Item liquid: "+m_LT);
                if(m_LT == LiquidType)
                {
                    quantity = item.GetQuantity();
                    if(quantity >= required_qn)
                    {
                        //Print("Found item and enough liquid");
                        //Print(item);
                        r_container = item;
                        //Print(r_container);
                        found = true;
                        //Print("_______________");
                    }
                }
            }
        }
        
        //Print("██████████ █ COUNT END █ █████████");
        //Print("██████████████████████████████████");
        return quantity;
    }
    
    bool CheckLiquid( PlayerBase m_Player, string aParam, out string error_text, out EntityAI r_container, out float r_quanity)
    {
        
        
        
        TStringArray OptionsArray = new TStringArray;
        aParam.Split("=",OptionsArray);
        
        string optionName = OptionsArray.Get(0);
        string optionVal = OptionsArray.Get(1);
        
        //Print("███████████CheckLiquid████████████████████");
        //Print(optionName);
        //Print(optionVal);
        r_quanity = optionVal.ToFloat();
        bool theResult = true;
        
        float actual_quantity = CountLiquidInInventory(optionName, m_Player, r_quanity, r_container);
        //Print(actual_quantity);
        if (actual_quantity < optionVal.ToFloat())
        {
            string lqname;
            
            GetLiquidID(optionName, lqname);
            
            //Print(r_container);
            //Print(lqname);
            theResult = false;
            error_text = lqname+" ("+actual_quantity+"\\"+optionVal+")";
            //Print(error_text);
        }
        
        //Print("████████████████--CheckLiquid END--███████████████");
        //Print("██████████████████████████████████████████████████");
        return theResult;
    }
    
    bool CheckItemParams(EntityAI oneItem, string theItemParam, out TStringArray errorText, out float quantity)
    {
        
        //errorText = new TStringArray;
        if(!oneItem) 
        {    
            ////Print("||||||||||||||||||||||||| NULL ITEM TO CHECK: "+oneItem);
            return false;
        }
        
        //Print("Checking params: "+theItemParam);
        
        float m_HP = oneItem.GetHealth();
        int m_LT = ItemBase.Cast(oneItem).GetLiquidType();
        float m_QN = oneItem.GetQuantity();;
        string dispName = oneItem.GetDisplayName();
        TStringArray ParamsArray = new TStringArray;
        TStringArray OptionsArray;
        quantity = 1;
        bool theResult = false;
        
        //errorText = "";
        
        if(theItemParam.Contains(";") )
        { 
            // MULTIPLE PARAMS?
            theItemParam.Split(";",ParamsArray); //PARAM;PARAMS;...
            
            //Print(dispName+":::: Option has multiple params: "+ParamsArray);
        }
        else
        {
            ParamsArray.Insert(theItemParam);
            //Print(dispName+":::: Option has no params");
            //Print(ParamsArray);
        }
        
        int pari = 0;
        bool QuantityToItem = false;
        foreach (string aParam : ParamsArray)
        {
            OptionsArray = new TStringArray;
            aParam.Split("=",OptionsArray);
            
            string optionName = OptionsArray.Get(0);
            string optionVal = OptionsArray.Get(1);
            
            //Print("███████████ CheckItemParams ████████████████████");
            //Print(optionName);
            //Print(optionVal);
            
            theResult = true;
            
            //if QN
            
            switch (optionName)
            {
                
                case "HP":
                
                
                float m_MaxHP = oneItem.GetMaxHealth();
                float percHP = m_HP / (m_MaxHP / 100);
                
                //Print("----------------------------Checking HP: "+m_HP+" m_MaxHP:"+m_MaxHP+" percHP: "+percHP+" optionVal: "+optionVal+" ("+dispName+")");
                if (percHP < optionVal.ToFloat())
                {
                    errorText.Insert("Необходимые предметы слишком повреждены ( "+Math.Round(percHP)+"% из "+optionVal.ToFloat()+"%): "+dispName);
                    //Print(errorText);
                    //return false;
                    theResult = false;
                }
                
                break;
                case "LT":
                
                //Print("----------------------------Checking LT "+m_LT+" ("+dispName+")");
                string lname;
                int required_liquid = GetLiquidID(optionVal, lname);
                
                if (m_LT !=  required_liquid)
                {
                    errorText.Insert("В ёмкостях нет необходимой жидкости: "+lname);
                    //Print(errorText);
                    theResult = false;
                    
                }
                QuantityToItem = true;
                
                
                break;
                //if QN
                case "QN":
                
                //Print("----------------------------Checking QN m_QN: "+m_QN+" optionVal: "+optionVal+" ("+dispName+")");
                if(m_QN <= optionVal.ToFloat())
                {
                    errorText.Insert("Не хватает количества: "+dispName+ " ("+m_QN +  " из "+optionVal+")");
                    //Print(errorText);
                    theResult = false;
                }
                else
                {
                    quantity = optionVal.ToFloat();
                }
                break;
                
            }
            
            
            
            pari++;
            //Print(QuantityToItem);
            //Print("██████████████████████████████████████████████");
        }
        if(QuantityToItem)
        {
            quantity = 1;
        }
        
        //Print(errorText);
        
        return theResult;
    }
    
    protected PlayerBase GetPlayerByIdentity(PlayerIdentity identity)
    {
        int highBits, lowBits;
        
        if (!GetGame().IsMultiplayer())
        return PlayerBase.Cast(GetGame().GetPlayer());
        
        if (identity == null)
        return null;
        
        GetGame().GetPlayerNetworkIDByIdentityID(identity.GetPlayerId(), lowBits, highBits);
        return PlayerBase.Cast(GetGame().GetObjectByNetworkId(lowBits, highBits));
    }
    
    void NewObjectVisibility_RPC(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        //if (!GetGame().IsClient())
        //return;
        
        Param2<EntityAI, bool> params;
        Print("_______NewObjectVISIBILITY_RPC__2_______");
        
        if (!ctx.Read(params))
        {
            //Print("_______CheckNPCCleanup NO PARAMS!_________");
            return;
        }
        
        
        
        Print(params.param1);
        Print(params.param2);
        
        params.param1.DisableSimulation(false);
        bool is_enabled = params.param2;
        int FLAGS_INVIS = EntityFlags.VISIBLE|EntityFlags.SOLID|EntityFlags.ACTIVE;
        
        SurvivorBase survivor;
        
        EntityAI objt = EntityAI.Cast(params.param1);
        
        HideMapObject(objt);
        
        Print("objt.IsFlagSet(EntityFlags.VISIBLE)");
        if (objt.IsFlagSet(EntityFlags.VISIBLE))
        {
            Print("VIS?");
            Print(objt.IsFlagSet(EntityFlags.VISIBLE));
            objt.ClearFlags(FLAGS_INVIS, true);
            objt.SetInvisible(true);
            objt.OnInvisibleSet(true);
            objt.SetInvisibleRecursive(true,objt);
        }
        else
        {
            Print("INVIS?");
            Print(objt.IsFlagSet(EntityFlags.VISIBLE));
            objt.SetFlags(FLAGS_INVIS, true);
            objt.SetInvisible(false);
            objt.OnInvisibleSet(false);
            objt.SetInvisibleRecursive(false,objt); 
        }
        
        
        objt.Update();
        
        objt.SetSynchDirty();
        
        if (Class.CastTo(survivor, target))
        {
            //survivor.m_isInvisible = is_enabled;
            if (is_enabled)
            {
                survivor.ClearFlags(FLAGS_INVIS, true);
                
                //Ty Wardog for this snippet :)
                //interactionLayer &= ~PhxInteractionLayers.CHARACTER;
                //dBodySetInteractionLayer(survivor, interactionLayer|PhxInteractionLayers.NOCOLLISION|PhxInteractionLayers.AI_NO_COLLISION|PhxInteractionLayers.RAGDOLL);
            }
            else
            {
                
                
                survivor.SetFlags(FLAGS_INVIS, true);
                dBodySetInteractionLayer(survivor, PhxInteractionLayers.CHARACTER);
            }
            
            if (!survivor.IsControlledPlayer())
            {
                //survivor.SetPosition("0 -6000 0");
                dBodySetInteractionLayer(survivor, PhxInteractionLayers.NOCOLLISION);
                survivor.Update();
                survivor.DisableSimulation(is_enabled);
                //survivor.DeleteOnClient();
            }
            survivor.SetInvisible(is_enabled);
        }
    }
    
    static Object HideMapObject(Object object, bool updatePathGraph = true)
    {
        // if (!IsMapObject(object)) return NULL; //Object is not a map object
        
        // if (IsMapObjectHidden(object)) return NULL; //Object already hidden
        
        //Remember the original position for path graph updates
        auto originalPosition = object.GetPosition();
        
        //Register object in it's current state
        auto link = new CF_ObjectManager_ObjectLink(object);
        link.flags = object.GetFlags();
        link.eventMask = object.GetEventMask();
        
        object.ClearFlags(link.flags, true);
        object.ClearEventMask(link.eventMask);
        
        //m_HiddenObjects.Set(object, link);
        //m_HiddenObjectsArray.Insert(link);
        
        object.Update();
        
        
        
        return object;
    }
    
    void NewObjectPosition_RPC(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Print("_______NewObjectPosition_RPC_________");
        Param3<EntityAI, vector, vector> params;
        if (!ctx.Read(params))
        {
            //Print("_______CheckNPCCleanup NO PARAMS!_________");
            return;
        }
        Print(params.param1);
        Print(params.param2);
        params.param1.SetPosition(params.param2);
        params.param1.SetOrientation(params.param3);
        string null_content;
        string arg1 = DzrQuestTrigger.Cast(params.param1).GetQuestName();
        if(FileExist("$profile:DZR\\Quests\\"+arg1+"\\QuestPosition.txt"))
        {
            DeleteFile("$profile:DZR\\Quests\\"+arg1+"\\QuestPosition.txt");
        }
        vector pos = params.param2;
        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"QuestPosition.txt", null_content, pos[0].ToString()+" "+pos[1].ToString()+ " "+pos[2].ToString());
        
    }
    
    void CheckNPCCleanup(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        //Print("_______CheckNPCCleanup START_________");
        Param1<string> params;
        if (!ctx.Read(params))
        {
            //Print("_______CheckNPCCleanup NO PARAMS!_________");
        }
        return;
        EntityAI theObject;
        int b1, b2, b3, b4;
        
        string PID = params.param1;
        foreach (EntityAI aMisCar:AllMissionCars)
        {
            //  EntityAI theObjEnt = EntityAI.Cast(obj);
            aMisCar.GetPersistentID( b1, b2, b3, b4);
            string ePID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
            if(PID == ePID)
            {
                theObject=aMisCar;
            }
        }
        //Print("_______CheckNPCCleanup_________");
        //Print(PID);
        //Print(ePID);
        string path = "$profile:DZR\\Quests\\Cleanup\\"+PID+".txt";
        if(FileExist(path))
        {
            if(theObject)
            {
                theObject.Delete();
            }
            DeleteFile(path);
        }
        //Print("________________");
        
    }
    
    void CheckNPCVisibility(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        //Print("_______CheckNPCCleanup START_________");
        Param1<string> params;
        if (!ctx.Read(params))
        {
            //Print("_______CheckNPCCleanup NO PARAMS!_________");
        }
        return;
        EntityAI theObject;
        int b1, b2, b3, b4;
        
        string PID = params.param1;
        foreach (EntityAI aMisCar:AllMissionCars)
        {
            //  EntityAI theObjEnt = EntityAI.Cast(obj);
            aMisCar.GetPersistentID( b1, b2, b3, b4);
            string ePID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
            if(PID == ePID)
            {
                theObject=aMisCar;
            }
        }
        //Print("_______CheckNPCCleanup_________");
        //Print(PID);
        //Print(ePID);
        EntityAI theai = theObject;
        //EntityAI theai =  EntityAI.Cast(this);
        string path = "$profile:DZR\\Quests\\Cleanup\\Visible\\"+PID+".txt";
        if(FileExist(path))
        {
            if(theObject)
            {
                Param2<EntityAI, bool> paramvis = new Param2<EntityAI, bool>(theai, true);
                GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_CLIENT", paramvis, true );
            }
            
        }
        path = "$profile:DZR\\Quests\\Cleanup\\Invisible\\"+PID+".txt";
        if(FileExist(path))
        {
            if(theObject)
            {
                Param2<EntityAI, bool> paramINvis = new Param2<EntityAI, bool>(theai, false);
                GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_CLIENT", paramINvis, true );
            }
            
        }
        //Print("________________");
        
    }
    
    
    void ActivateDzrQuestForPlayer(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        
        
        int fdelay = 10;
        PrintD("ActivateDzrQuestForPlayer 519");
        string FileContents;
        Param4<PlayerBase, int, int, string> params;
        if (!ctx.Read(params))
        return;
        
        int delayAfterTeleport = QuestsSettingsArray[params.param2].DelayAfterTeleport.ToInt();
        int delayBeforeTeleport = QuestsSettingsArray[params.param2].DelayBeforeTeleport.ToInt();
        int totalDelay = delayBeforeTeleport + delayAfterTeleport;
        string questType = "";
        string firstItem = QuestsSettingsArray[params.param2].RemoveItems[0];
        
        string Currency = QuestsSettingsArray[params.param2].Currency;
        string m_DzrQuestTitle = QuestsSettingsArray[params.param2].Title;
        
        //ref map<string, int> itemList = QuestsSettingsArray[params.param2].RemoveItems[0];
        PlayerBase player = GetPlayerByIdentity(sender);
        
        string path = "$profile:DZR\\Quests\\DebugTeleport.txt"; 
        DZR_PTL_GetFile("$profile:DZR\\Quests","DebugTeleport.txt", FileContents);
        
        array<string> errors = new array<string>();
        
        vector pos;
        //Param5<string , string , string, string, string> LoaderData;
        
        DzrQuestTrigger m_DzrQuestTrigger = m_DzrQuests.Get(params.param2);        
        string m_QuestName = m_DzrQuestTrigger.GetQuestName();
        
        Print("________________________________________________firstItem.ToInt() Currency: "+Currency);
        
        ref array<string> itemsToTake = new array<string>();
        ref map<string, int> itemsToTake2 = new map<string, int>();
        ref array<string> itemsToGive = new array<string>();
        ref map<string, int> itemsToGive2 = new map<string, int>();
        
        if(firstItem != "0" )
        {
            if(firstItem.ToInt() > 0)
            {
                //use currency
                
                //preapre currency map
                map<string, int> player_money = new map<string, int>();
                map<string, int> currency_map = new map<string, int>();
                map<int, string> icurrency_map = new map<int, string>();
                //preapre currency map
                
                
                
                Print("Using currency: "+Currency);
                if(Currency == "Trader")
                {
                    Print("Importing currency from \\Trader\\TraderConfig.txt");
                }
                else if(Currency == "DayZ-Expansion-Market")
                {
                    #ifdef EXPANSIONMODMARKET
                        Print("Importing currency from \\ExpansionMod\\Market\\Exchange.json");
                        ExpansionMarketCategory em_config = new ExpansionMarketCategory();
                        JsonFileLoader<ExpansionMarketCategory>.JsonLoadFile("$profile:ExpansionMod\\Market\\Exchange.json", em_config);
                        Print(em_config.m_Version);
                        //Print(em_config.Items);
                        for(int emc = 0; emc < em_config.Items.Count(); emc++)
                        {
                            currency_map.Insert( em_config.Items[emc].ClassName, em_config.Items[emc].MaxPriceThreshold );
                            icurrency_map.Insert( em_config.Items[emc].MaxPriceThreshold, em_config.Items[emc].ClassName );
                            Print( "---- Imported currency: "+em_config.Items[emc].ClassName+" = "+em_config.Items[emc].MaxPriceThreshold );
                            //em_config.Items[emc]
                        }
                        #else
                        Print("[ERROR] EXPANSIONMODMARKET is not loaded. Currency import failed. Using default.");
                        Currency == "DefaultCurrency.txt";
                    #endif
                }
                else if(Currency == "DefaultCurrency.txt")
                {
                    Print("Importing currency from \\DZR\Quests\\DefaultCurrency.txt");
                    
                    TStringArray CurrencyArray = new TStringArray();
                    DZR_PTL_GetFileLines("$profile:DZR\\Quests", "DefaultCurrency.txt", CurrencyArray);
                    CurrencyArray.Sort();
                    //<Currency> Money_Ruble5000 ,5000
                    foreach(string aCurrency : CurrencyArray)
                    {
                        if(aCurrency.Contains("<Currency>"))
                        {
                            aCurrency.Replace("<Currency>", "");
                            aCurrency.Replace("	", "");
                            
                            array<string> currencyValues = new array<string>();
                            aCurrency.Split(",", currencyValues);
                            
                            //Print(currencyValues);
                            
                            currency_map.Insert( currencyValues[0].Trim(), currencyValues[1].Trim().ToInt() );
                            icurrency_map.Insert( currencyValues[1].Trim().ToInt(), currencyValues[0].Trim() );
                            Print( "---- Imported currency: "+currencyValues[0].Trim()+" = "+currencyValues[1].Trim() );
                        }
                        //aFunc.Split(" ", aCurrency);
                        
                    }
                    
                }
                else
                {
                    Print("Using custom currency file DZR\\Quests\\"+m_QuestName+"\\"+Currency);
                };
                
                /*
                    array<string> icurrencyValuesSorted = icurrency_map.GetValueArray();
                    array<int> icurrencyKeysSorted = icurrency_map.GetKeyArray();
                    icurrencyKeysSorted.Sort();
                    icurrency_map.Clear();
                    
                    foreach(int aval : icurrencyKeysSorted)
                    {
                    icurrency_map.Insert(aval, currency_map.GetKey(aval));
                    }
                */
                
                
                
                
                ref array<EntityAI> allItems = new array<EntityAI>;
                foreach(string iaCur : icurrency_map)
                {
                    
                    Print("Checking player currency: "+iaCur);
                    ref array<EntityAI> tempItems = new array<EntityAI>;
                    bool ItemExists;
                    int curCount = CountItemsInInvetnory(iaCur, player, tempItems, ItemExists); 
                    Print(curCount);
                    if(curCount != 0)
                    {
                        foreach(EntityAI antItem : tempItems)
                        {
                            "Currency found: "+antItem.GetType();
                            allItems.Insert(antItem);
                        }
                    }
                    
                }
                Print(allItems);
                
                
                //get player money
                
                array<string> player_money_names = new array<string>();   
                array<int> player_money_values = new array<int>();    
                
                foreach( EntityAI anItemE : allItems)
                {
                    
                    ItemBase anItem = ItemBase.Cast(anItemE);
                    int iQuan = anItemE.GetQuantity();
                    player_money_names.Insert( anItem.GetType());
                    player_money_values.Insert( iQuan );
                    /*    
                        if( player_money.Get( anItem.GetType()) )
                        {
                        
                        player_money.Set( anItem.GetType(), player_money.Get( anItem.GetType() ) + iQuan) ;
                        }
                        else
                        {
                        
                        player_money.Set( anItem.GetType(), iQuan );
                        };
                    */  
                }
                
                int totalPlayerMoney = 0;
                /*
                    array<string> player_money_names = player_money.GetKeyArray();   
                    array<int> player_money_values = player_money.GetValueArray();   
                */
                array<int> player_note_vals = new array<int>();   
                array<int> player_note_vals_distinct = new array<int>();   
                int PMi = 0;
                foreach(auto noteQuantity : player_money_values)
                {
                    
                    Print(player_money_names[PMi] +" x"+noteQuantity);
                    Print("Adding to total player money: " + currency_map.Get(player_money_names[PMi]) * noteQuantity);
                    totalPlayerMoney = totalPlayerMoney + currency_map.Get(player_money_names[PMi]) * noteQuantity;
                    Print("TOTAL: "+totalPlayerMoney);
                    Print("......");
                    player_note_vals_distinct.Insert(currency_map.Get(player_money_names[PMi]));
                    for(int pvi=0; pvi < noteQuantity; pvi++)
                    {
                        player_note_vals.Insert(currency_map.Get(player_money_names[PMi]));
                    }
                    PMi++;
                }
                
                
                //get player money
                
                //calculate fee
                int fee = firstItem.ToInt();
                if(totalPlayerMoney < fee)
                {
                    Print("*****************");
                    Print("****NOT ENOUGH MONEY. "+fee+" required, you have "+totalPlayerMoney+".****");
                    Print("*****************");
                    errors.Insert("#dzrqst_no_money"+": "+totalPlayerMoney+" \ "+fee);
                }
                else
                {
                    Print("********TAKING FEE OF "+fee+"*********");
                    
                    
                    array<string> icurrencyValuesSorted = icurrency_map.GetValueArray();
                    array<int> icurrencyKeysSorted = icurrency_map.GetKeyArray();
                    icurrencyKeysSorted.Sort();
                    icurrencyKeysSorted.Invert();
                    
                    
                    player_note_vals_distinct.Sort();
                    player_note_vals_distinct.Invert();
                    
                    player_note_vals.Sort();
                    player_note_vals.Invert();
                    
                    
                    //array<int> change_currency = icurrencyKeysSorted;
                    //change_currency.Invert();
                    
                    Print("checking player cash");
                    //iterate over bigger notes
                    int rest = fee;
                    
                    int totalChange = 0;
                    
                    int taken_money = 0;
                    
                    array<int> nullarr = new array<int>();
                    
                    bool paid = false;
                    
                    Print(player_note_vals);
                    
                    
                    
                    for(int i=0; i < player_note_vals.Count(); i++ )
                    {
                        //Print(i);
                        if(!paid)
                        {
                            if(rest > 0)
                            {
                                
                                Print("AMOUNT LEFT TO PAY: "+rest);
                                
                                int noteValue = player_note_vals[i];
                                int expChange = noteValue - rest;
                                
                                
                                if(expChange > 0)
                                {
                                    Print("****EXPECTED CHANGE: "+expChange+".****");
                                    
                                    int selected_note = ClosestCurrency(rest, player_note_vals_distinct);
                                    
                                    Print("Taken "+icurrency_map.Get(selected_note)+ ". Exchanging.");
                                    
                                    itemsToTake.Insert(icurrency_map.Get(selected_note));
                                    
                                    //mapping
                                    if( itemsToTake2.Get(icurrency_map.Get(selected_note)) )
                                    {
                                        itemsToTake2.Set(icurrency_map.Get(selected_note), itemsToTake2.Get(icurrency_map.Get(selected_note)) +1) ;
                                    }
                                    else
                                    {
                                        itemsToTake2.Set(icurrency_map.Get(selected_note), 1);
                                    };
                                    //mapping
                                    
                                    array<int> change = getChange(selected_note - rest, icurrencyKeysSorted, nullarr);
                                    Print(change);
                                    foreach(int chAmount : change)
                                    {
                                        Print("Given change: "+icurrency_map.Get(chAmount));
                                        itemsToGive.Insert(icurrency_map.Get(chAmount));
                                        
                                        //mapping
                                        if( itemsToGive2.Get(icurrency_map.Get(chAmount)) )
                                        {
                                            itemsToGive2.Set(icurrency_map.Get(chAmount), itemsToGive2.Get(icurrency_map.Get(chAmount)) +1) ;
                                        }
                                        else
                                        {
                                            itemsToGive2.Set(icurrency_map.Get(chAmount), 1);
                                        };
                                        //mapping
                                        
                                        totalChange = totalChange + chAmount;
                                    }
                                    
                                    taken_money = taken_money + selected_note - totalChange;
                                    rest = rest - selected_note - totalChange ; 
                                }
                                else
                                {
                                    Print("****NO CHANGE NEEDED****");
                                    Print("Taken "+icurrency_map.Get(noteValue));
                                    itemsToTake.Insert(icurrency_map.Get(noteValue));
                                    
                                    //mapping
                                    if( itemsToTake2.Get(icurrency_map.Get(noteValue)) )
                                    {
                                        itemsToTake2.Set(icurrency_map.Get(noteValue), itemsToTake2.Get(icurrency_map.Get(noteValue)) +1) ;
                                    }
                                    else
                                    {
                                        itemsToTake2.Set(icurrency_map.Get(noteValue), 1);
                                    };
                                    //mapping
                                    
                                    Print(" ");
                                    taken_money = taken_money + noteValue;
                                    rest = rest - noteValue; 
                                }    
                                
                                
                            }
                            else
                            {
                                Print("ALL PAID. FINISHING.");
                                paid = true;
                            }
                        }
                    }
                    
                    //iterate over bigger notes
                    Print("____________");
                    Print("TOTAL CHANGE: "+totalChange);
                    Print("TOTAL PAID: "+taken_money);
                    int totalpm = totalPlayerMoney-taken_money;
                    Print("PLAYER MONEY BEFORE TRANSACTION: "+totalPlayerMoney);
                    Print("PLAYER MONEY NOW: "+totalpm);
                    Print("..........");
                    Print(itemsToTake2.Count());
                    Print(itemsToGive2.Count());
                    Print("*****************");
                }
                //calculate fee
            }
            else
            {
                //use items
                Print("Using items");
                ref map<string, int> removeItemMap = ConvertSetArrToMap(QuestsSettingsArray[params.param2].RemoveItems);
                Print("Original array");
                Print(QuestsSettingsArray[params.param2].RemoveItems);
                Print("Converted map");
                Print(removeItemMap.GetElement(0));
                
                
                
                if(QuestsSettingsArray[params.param2].RemoveItems[0] == "Robbery")
                {
                    
                    
                    RemoveItemsFromInvetnory2("Weapon_Base", 0, player);
                    RemoveItemsFromInvetnory2("Box_Base", 0, player);
                    RemoveItemsFromInvetnory2("Magazine_Base", 0, player);
                    RemoveItemsFromInvetnory2("Backpack_Base", 0, player);
                    player.SetHealth("", "Shock", 0);
                }
                else if(!player.RemoveItems2(removeItemMap))
                {
                    Print("---------------------------player.RemoveItems2 false--------------------------------");
                    Print("-----------------------------------------------------------");
                    return;
                }
            }
        }
        else
        {
            Print("No items required to be removed");
        }
        
        
        if(errors.Count() == 0)
        {
            Print("-----------------------------------------------------------");
            Print(QuestsSettingsArray[params.param2].ActionName);
            Print(QuestsSettingsArray[params.param2].RemoveItems);
            Print(QuestsSettingsArray[params.param2]);
            Print("-----------------------------------------------------------");
            string qText1 = QuestsSettingsArray[params.param2].Text1;// +" (" + questType + ")";
            string qText2 = QuestsSettingsArray[params.param2].Text2;
            string qText3 = QuestsSettingsArray[params.param2].Text3;
            string qSound = QuestsSettingsArray[params.param2].Sound;
            string qLayout = QuestsSettingsArray[params.param2].Layout;
            string qImage = QuestsSettingsArray[params.param2].Image;
            string qIcon = QuestsSettingsArray[params.param2].Icon;
            
            
            int locationBlockIndex = 0;
            //TODO: Process location index. Triggers must pass that index if they are multiple. It will select their target from TeleportTo array.
            //TODO: Randomize from TeleportTo array.
            
            pos = QuestsSettingsArray[params.param2].TeleportTo[locationBlockIndex][0].ToVector();
            
            Param8<string , string , string, string, string, int, string, string> LoaderData = new Param8<string , string , string, string, string, int, string, string>(qText1 ,"EMPTY" , qText3, qSound, qImage, totalDelay, qIcon, qLayout );
            // int locationBlockIndex = 0;
            //TODO: Process location index. Triggers must pass that index if they are multiple. It will select their target from TeleportTo array.
            //TODO: Randomize from TeleportTo array.
            
            
            //TODO: --------- Make notification folder. Send after closing loader.
            Param1<string > Fadedata = new Param1<string  >("NULL");
            
            GetRPCManager().SendRPC( "dzr_dquests", "RequestLoadingScreen", LoaderData, true, sender ); 
            
            //ref Param2<ref array<string>, ref array<string>> arData = new Param2<ref array<string>, ref array<string>>(itemsToGive, itemsToTake);
            PrintTime();
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(MakeTransaction, delayBeforeTeleport*1000, false, itemsToGive2, itemsToTake2, player, delayAfterTeleport*1000+10000, qIcon, m_DzrQuestTitle);
            
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(player.SetPosition, delayBeforeTeleport*1000, false, pos);
            //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(GiveCurrency, delayBeforeTeleport*1000, false, itemsToGive, player);
            //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(TakeCurrency, delayBeforeTeleport*1000, false, itemsToTake, player);
        }
        else
        {
            foreach(string  error : errors)
            {
                NotificationSystem.SendNotificationToPlayerExtended(player, 10, "DZR Quests", error); 
            }
        }
    }
    
    string PrintTime(string msg = "")
    {
        int hour;
		int minute;
		int second;
		int year;
		int month;
		int day;
		GetHourMinuteSecond(hour, minute, second);
		GetYearMonthDay(year, month, day);
		//string timestamp = year.ToString()+"."+month.ToString()+"."+day.ToString()+" "+hour.ToString()+":"+minute.ToString()+":"+second.ToString();
		string timestamp = hour.ToString()+":"+minute.ToString()+":"+second.ToString();
        Print("                          →" + timestamp);
        Print(msg);
        return timestamp;
    }
    
    void MakeTransaction(ref map<string, int> itemsToGive, ref map<string, int>itemsToTake, PlayerBase player, int delayAfterTeleport, string qIcon, string m_DzrQuestTitle)
    {
        Print(player);
        PrintTime();
        //delayAfterTeleport = delayAfterTeleport*2;
        Print(delayAfterTeleport);
        Print(qIcon);
        
        //get inventory
        GameInventory inv = PlayerBase.Cast(player).GetInventory();
        ref array<EntityAI> allItems = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.PREORDER, allItems);
        //get inventory
        /*
            ref array<int> itemsToTakeV = itemsToTake.GetValueArray();
            ref array<string> itemsToTakeK = itemsToTake.GetKeyArray();
            
            ref array<int> itemsToGiveV = itemsToGive.GetValueArray();
            ref array<string> itemsToGiveK = itemsToGive.GetKeyArray();
            
            Print(itemsToTakeV);
            Print(itemsToTakeK);
            Print(itemsToGiveV);
            Print(itemsToGiveK);
            {
        */
        
        
        
        Print("   .       TAKING             .");
        foreach(EntityAI aiitem : allItems)
        {
            ItemBase item = ItemBase.Cast(aiitem);
            if(item)
            {
                int val;
                string itemName = GetDisplayName(item.GetType());
                if( itemsToTake.Find(item.GetType(), val) )
                {
                    Print("Checking inventory item "+item.GetType());
                    
                    Print("(take) FOUND: "+item.GetType() + " x" +QuantityConversions.GetItemQuantity( item ));
                    Print("Max quantity: "+item.GetQuantityMax());
                    Print("Setting new quantity: "+val);
                    
                    int expectedQuantity = item.GetQuantity() - val;
                    
                    
                    int curQuant = item.GetQuantity();
                    if ( expectedQuantity < 0 )
                    {
                        Print("Remove item quantity "+item.GetQuantity()+" from array and delete item");
                        itemsToTake.Set( item.GetType(),  itemsToTake.Get(item.GetType()) - item.GetQuantity() );
                        if (item)
                        {
                            GetGame().ObjectDelete(item);
                        }
                        PrintTime();
                        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(QuestNotification, delayAfterTeleport, false, player, 5, m_DzrQuestTitle, "#dzrqst_item_removed"+ ": "+curQuant+" x "+itemName, qIcon);
                        
                        //NotificationSystem.SendNotificationToPlayerExtended(player, 5, "DZR Quests", "#dzrqst_item_removed"+ ": "+curQuant+" x "+item.GetType() );
                    }
                    else
                    {
                        item.SetQuantity(expectedQuantity);
                        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(QuestNotification, delayAfterTeleport, false, player, 5, m_DzrQuestTitle, "#dzrqst_item_removed"+ ": "+val+" x "+itemName, qIcon);
                        //NotificationSystem.SendNotificationToPlayerExtended(player, 5, "DZR Quests", "#dzrqst_item_removed"+ ": "+val+" x "+item.GetType());
                    };
                }
            } 
            else
            {
                Print("!!!!!!!!!!! NO ITEM ");
            }
            
        }
        
        Print("-------------------");
        
        Print("   .       GIVING             .");
        Print(itemsToGive.Count());
        
        if(itemsToGive.Count() > 0)
        {
            
            ref array<int> itemsToGiveV = itemsToGive.GetValueArray();
            ref array<string> itemsToGiveK = itemsToGive.GetKeyArray();
            
            // Print(itemsToGiveV);
            // Print(itemsToGiveK);
            
            for( int gi = 0; gi < itemsToGive.Count() ; gi++)
            {
                string givenItemType = itemsToGiveK[gi];
                val = itemsToGiveV[gi];
                
                Print(givenItemType);
                Print(val);
                item = ItemBase.Cast(GetGame().CreateObject(givenItemType, "0 1000 0"));
                Print(item);
                
                if(item)
                {
                    expectedQuantity = val;
                    
                    Print("Create new item "+item.GetType()+" and set quantity to "+expectedQuantity);
                    
                    ItemBase item_c;
                    string c_itemName = GetDisplayName(item_c.GetType());
                    if (player.GetInventory().CanAddEntityToInventory(item))
                    {
                        EntityAI entity = player.GetHumanInventory().CreateInInventory(item.GetType());
                        if(entity)
                        {
                            Class.CastTo(item_c, entity);
                            item_c.SetQuantity(expectedQuantity);
                            Print("New item "+item_c.GetType()+" with quantity "+item_c.GetQuantity()+ " added to inventory");
                            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(QuestNotification, delayAfterTeleport, false, player, 5, m_DzrQuestTitle, "#dzrqst_item_received"+ ": "+item_c.GetQuantity()+ " x "+c_itemName, qIcon);
                            //NotificationSystem.SendNotificationToPlayerExtended(player, 5, "DZR Quests", "#dzrqst_item_received"+ ": "+item_c.GetQuantity()+ " x "+item_c.GetType());
                        }
                        else
                        {
                            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(QuestNotification, delayAfterTeleport, false, player, 5, m_DzrQuestTitle, "ERROR: Could not receive item: "+item_c.GetQuantity()+ " x "+c_itemName, qIcon);
                            //NotificationSystem.SendNotificationToPlayerExtended(player, 5, "DZR Quests", "ERROR: Could not receive item: "+item_c.GetQuantity()+ " x "+item_c.GetType()
                        };
                    }
                    else
                    {
                        //TraderMessage.PlayerWhite("#tm_inventory_full" + "\n" + "#tm_your_currency_on_ground", this);
                        //GetGame().RPCSingleParam(this, TRPCs.RPC_SEND_MENU_BACK, new Param1<bool>(true), true, this.GetIdentity());
                        
                        entity = player.SpawnEntityOnGroundPos(item.GetType(), player.GetPosition());
                        if(entity)
                        {
                            Class.CastTo(item_c, entity);
                            item_c.SetQuantity(expectedQuantity);
                            Print("New item "+item_c.GetType()+" with quantity "+item_c.GetQuantity()+ " dropped on the ground");
                            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(QuestNotification, delayAfterTeleport, false, player, 5, m_DzrQuestTitle, "#dzrqst_no_space"+": "+item_c.GetQuantity()+ " x "+c_itemName, qIcon);
                            //NotificationSystem.SendNotificationToPlayerExtended(player, 5, "DZR Quests", "#dzrqst_no_space"+": "+item_c.GetQuantity()+ " x "+item_c.GetType());
                        }
                        else
                        {
                            NotificationSystem.SendNotificationToPlayerExtended(player, 5, "DZR Quests", "ERROR: No space in inventory. Could not drop item: "+item_c.GetQuantity()+ " x "+c_itemName, qIcon);
                        };
                    }
                    
                }
            }
        }
        Print("-------------------");
        
        //Print(itemsToGive);
    };
    
    void QuestNotification(PlayerBase player, int holdTime, string Title, string msg, string icon)
    {
        PrintTime(msg);
        NotificationSystem.SendNotificationToPlayerExtended(player, holdTime, Title, msg, icon);
    }
    
    void TakeCurrency(ref array<string> items, PlayerBase player)
    {
        foreach(string item : items)
        {
            Print("Taken "+item+" from player "+player.GetIdentity().GetName());
        }
    };
    
    void funcArray()
    {
        /*      
            foreach(string aFunc : FuncArray)
            {
            
            PrintD("█████████████████████████████████████ █████Preparing for : "+aFunc);
            
            if(aFunc.Contains("Teleport"))
            {
            TStringArray tp_aRemItemsArr = new TStringArray;
            aFunc.Split(" ", tp_aRemItemsArr);
            
            OptionTeleport = tp_aRemItemsArr.Get(1);
            }
            
            if(aFunc.Contains("NoLoadingScreen"))
            {
            NoLoading  = true;
            }
            
            if(aFunc.Contains("CheckItem"))
            {
            
            TStringArray c_aRemItemsArr = new TStringArray;
            aFunc.Split(" ", c_aRemItemsArr);
            ref array<EntityAI> c_allItems = new array<EntityAI>;
            
            int c_itemsNumber = CountItemsInInvetnory(c_aRemItemsArr.Get(1), m_Player, c_allItems, MetConditions);
            if(c_itemsNumber < 1)
            {
            AllItemsPresent = false;
            MetConditions = false;
            ErrorList.Insert("#dzrqst_item_missing"+": "+GetDisplayName( c_aRemItemsArr.Get(1) ));
            }
            
            
            
            
            }
            
            
            
            int remItemsCount = QuestsSettingsArray[params.param2].RemoveItems.Count();
            settingsRemoveItems = QuestsSettingsArray[params.param2].RemoveItems;
            if(remItemsCount > 0)
            {
            remCommands++;
            TStringArray aRemItemsArr = new TStringArray;
            aFunc.Split(" ", aRemItemsArr);
            TStringArray rItemsArray = QuestsSettingsArray[params.param2].RemoveItems;
            //////Print(rItemsArray);
            
            //rItemsArray.Remove(0); //REMOVE COMMAND
            //////Print(rItemsArray);
            string dispName;
            string RemoveItemname;
            
            string ItemLine=aRemItemsArr.Get(1);
            
            ////Print("Trying to remove: "+aFunc);
            int itemsCount;
            
            TStringArray ParamsLine = new TStringArray;
            TStringArray ParamsArray = new TStringArray;
            TStringArray OptionsArray = new TStringArray;
            
            float quantity = 1;
            //////Print(anItemR);
            EntityAI entItem;
            int itemsNumber;
            
            
            ref array<EntityAI> allItems;
            
            if(ItemLine.Contains(":")) //HAS PARAMS TO CHECK
            {
            
            ItemLine.Split(":",ParamsLine); //ITEM:PARAMS
            
            string theItem = ParamsLine.Get(0);
            string theItemOptions = ParamsLine.Get(1);
            bool ItemIsLiquid = false;
            
            ///LIQUID CHECK
            if(theItem == "LiquidType")
            {
            
            PrintC(m_Player, "theItem == LiquidType");
            //search all items for liquid type
            //if no param, get all
            //if param 
            //sumup quantity = requred quantity
            string error_text;
            EntityAI theContainer;
            float m_qnt;
            if(CheckLiquid(m_Player, theItemOptions, error_text, theContainer, m_qnt) )
            {
            PrintC(m_Player, "Checked liquid TRUE");
            
            RemoveList.Insert(theContainer, m_qnt);
            } 
            else
            {
            PrintC(m_Player, "Checked liquid FALSE Недостаточно жидкости: "+error_text);
            ErrorList.Insert("Недостаточно жидкости: "+error_text);
            }
            
            ItemIsLiquid = true;
            }
            ///LIQUID CHECK
            
            if(!ItemIsLiquid)
            {
            
            ////Print("Found params: "+theItem);
            //Print("Found Options: "+theItemOptions);
            
            
            
            dispName = GetDisplayName( theItem );
            RequiredItems.Insert(theItem);
            
            // find all items in inv
            itemsNumber = CountItemsInInvetnory(theItem, m_Player, allItems, MetConditions);
            
            ////Print("itemsNumber: "+itemsNumber);
            string errorText;
            
            bool itemSelected = false;
            //int quantity;
            int liquid;
            ref map<EntityAI, int> ItemToInsert = new map<EntityAI, int>;
            
            //ParseItemParams(oneItem, theItemOptions, errorText, quantity)
            
            if(itemsNumber >= 1)
            {
            TStringArray errorTexts = new TStringArray;
            foreach( EntityAI oneItem : allItems) 
            {
            // check every param
            
            if(oneItem)
            {
            if( oneItem.IsKindOf(theItem)  )
            {
            CheckItemParams(oneItem, theItemOptions, errorTexts, quantity);
            //Print("█ RETURNED ERROR TEXT█ FOR ");
            //Print(oneItem);
            //Print(errorTexts);
            //Print("██████████████████████");
            if(errorTexts.Count() != 0)
            {
            foreach( string anError : errorTexts)
            {
            //Print(anError);
            ErrorList.Insert(anError);
            }
            } 
            else
            {
            ItemToInsert.Insert(oneItem,quantity);
            TrueCommands++;
            //Print("376 TrueCommands++: " + TrueCommands);
            //Print(oneItem);
            RemoveList.Insert(oneItem,quantity);
            if(SelectedItems.Find(theItem) == -1)
            {
            SelectedItems.Insert(theItem);
            }
            }
            
            //ItemToInsert = NULL;
            //returns true + out paramError
            //itemSelected = true;
            //MetConditions++;
            }
            
            }
            }
            
            }
            else				
            {		
            if(theItem != "LiquidType")
            {
            MetConditions = false;
            TStringArray ParamsLines = new TStringArray;
            TStringArray OptionPArts = new TStringArray;
            theItemOptions.Split(";",ParamsLines);
            foreach( string AparamLine: ParamsLines)
            {
            
            AparamLine.Split("=",OptionPArts); 
            if(OptionPArts.Get(0) == "QN")
            {
            quantity = OptionPArts.Get(1).ToInt();
            }
            }
            ErrorList.Insert("#dzrqst_item_missing"+": "+dispName+" ("+quantity+")");
            }
            
            
            }
            } // is liquid
            }
            else
            {
            //NO PARAMS :
            
            ///LIQUID CHECK
            if(theItem == "LiquidType")
            {
            //search all items for liquid type
            //if no param, get all
            //if param 
            //sumup quantity = requred quantity
            
            
            ItemIsLiquid = true;
            
            
            }
            ///LIQUID CHECK
            
            if(!ItemIsLiquid)
            {
            
            dispName = GetDisplayName(ItemLine);
            RequiredItems.Insert(ItemLine);
            itemsNumber = CountItemsInInvetnory(ItemLine, m_Player, allItems, MetConditions);
            if( itemsNumber >= 1 )
            {
            TrueCommands++;
            //Print("438 TrueCommands++: " + TrueCommands);
            //Print(allItems.Get(0));
            RemoveList.Insert(allItems.Get(0), 0);
            if(SelectedItems.Find(ItemLine) == -1)
            {
            SelectedItems.Insert(ItemLine);
            }
            
            }
            else
            {
            
            MetConditions = false;
            
            ErrorList.Insert("#dzrqst_item_missing"+": "+dispName+" (1)");
            
            }
            }
            
            }
            }
            
            PrintD("ActivateDzrQuestForPlayer 831");
            
            //Print(RequiredItems);
            //Print(SelectedItems);
            //Print(ErrorList);
            
            if(remCommands < SelectedItems.Count() )
            {
            MetConditions = false;
            return;
            }
            
            
            }
            //Print(ErrorList);
        */
    }
    
    void ActivateDzrQuestForPlayer1(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        PrintD("ActivateDzrQuestForPlayer 519");
        
        Param4<PlayerBase, int, int, string> params;
        if (!ctx.Read(params))
        return;
        
        
        int delayAfterTeleport = QuestsSettingsArray[params.param2].DelayAfterTeleport.ToInt();
        int delayBeforeTeleport = QuestsSettingsArray[params.param2].DelayBeforeTeleport.ToInt();
        int totalDelay = delayBeforeTeleport + delayAfterTeleport;
        
        Print(delayAfterTeleport);
        Print(delayBeforeTeleport);
        Print(totalDelay);
        
        string FileContents;
        string null_content;
        
        string the_icon;
        
        //PlayerBase m_Player = params.param1;
        PlayerBase m_Player = GetPlayerByIdentity(sender);
        int DzrQuestID = params.param2;
        int locationBlockIndex = DzrQuestID;
        int OPtionID = params.param3;
        if(OPtionID == 51)
        {
            OPtionID = 0; 
        }
        
        DzrQuestTrigger m_DzrQuestTrigger = m_DzrQuests.Get(DzrQuestID);
        
        string PlainID = sender.GetPlainId();
        
        // PlayerIdentity identity = sender;
        
        string m_QuestName = m_DzrQuestTrigger.GetQuestName();
        // DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName, "Title.txt", FileContents);
        string m_DzrQuestTitle = QuestsSettingsArray[params.param2].QuestName;
        
        //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName, "QuestIcon.txt", FileContents);
        string m_QuestIcon = QuestsSettingsArray[params.param2].Icon;
        
        PrintD("ActivateDzrQuestForPlayer 552");
        
        vector DzrQuestDst;
        string portalFileTarget = m_DzrQuestTrigger.GetTarget();
        //////Print("_________");
        //////Print(portalFileTarget);
        //////Print(OPtionID);
        bool NoTeleport = false;
        
        //FCUNTIONS
        TStringArray FuncArray = new TStringArray();
        //DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+m_QuestName, "Option"+OPtionID+"Functions.txt", FuncArray);
        
        // PrintD(FuncArray);
        
        bool AllItemsPresent = true;
        bool NoLoading = false;
        
        bool MetConditions = true;
        int remCommands = 0;
        int TrueCommands = 0;
        ref map<EntityAI, float> RemoveList = new ref map<EntityAI, float> ;
        TStringArray SelectedItems = new TStringArray;
        TStringArray RequiredItems = new TStringArray;
        TStringArray ErrorList = new TStringArray;
        string OptionTeleport = "";
        PrintD("ActivateDzrQuestForPlayer 578");
        
        //removingItems 
        string qtTitle = QuestsSettingsArray[params.param2].Title;
        string qtIcon = QuestsSettingsArray[params.param2].Icon;
        m_DzrQuestTitle = qtTitle;
        m_QuestIcon = qtIcon;
        
        string firstItem = QuestsSettingsArray[params.param2].RemoveItems[0];
        Print("________________________________________________firstItem.ToInt()");
        Print(firstItem.ToInt());
        if(firstItem != "0" && firstItem.ToInt() > 0 )
        {
            ref array<string> settingsRemoveItems = QuestsSettingsArray[params.param2].RemoveItems;
            int n_remItemsCount = QuestsSettingsArray[params.param2].RemoveItems.Count();
            ref array<EntityAI> c_allItems2;
            if(n_remItemsCount > 0)
            {
                foreach(string oneItemName : settingsRemoveItems)
                {
                    //checkinventory
                    if(oneItemName != "0" && oneItemName != "" && oneItemName)
                    {
                        int c_itemsNumber2 = CountItemsInInvetnory(oneItemName, m_Player, c_allItems2, MetConditions);
                        if(c_itemsNumber2 < 1)
                        {
                            AllItemsPresent = false;
                            MetConditions = false;
                            ErrorList.Insert("#dzrqst_item_missing"+": "+GetDisplayName( oneItemName ));
                        }
                    }
                    //remove
                }
            }
            //removingItems 
            
            // TODO: Func aray was here
            
            if(ErrorList.Count() == 0)
            {
                foreach(string oneItemName2 : settingsRemoveItems)
                {
                    bool oneItemTaken = false;
                    foreach( EntityAI n_oneItem : c_allItems2) 
                    {
                        // check every param
                        if(n_oneItem)
                        {
                            if( n_oneItem.IsKindOf(oneItemName2)  )
                            {
                                if(!oneItemTaken)
                                {
                                    RemoveItemFromInvetnory(n_oneItem, m_Player,  1, qtTitle, qtIcon);
                                    oneItemTaken = true;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                MetConditions = false;
                foreach (string error : ErrorList)
                {
                    //Print(error);
                    the_icon = "set:dayz_gui image:gear";
                    if(m_QuestIcon != "0" || m_QuestIcon != "")
                    {
                        the_icon = m_QuestIcon;
                    }
                    NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, error, the_icon);
                }
            }
            
        }
        PrintD("ActivateDzrQuestForPlayer 875");
        foreach( string ritem: RequiredItems)
        {
            if(SelectedItems.Find(ritem) == -1)
            {
                MetConditions = false;
                string mitem = GetDisplayName(ritem);
                string Errors = "Отсутствуют предметы: "+mitem;
                //NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, "Отсутствуют предметы: "+mitem, "set:dayz_gui image:exit");
                //Print(Errors);
            }
        }
        
        if(!MetConditions) return;
        
        if( portalFileTarget != "0" && portalFileTarget != "")
        {
            //DzrQuestDst = m_DzrQuestTrigger.GetTarget().ToVector();
            DzrQuestDst = QuestsSettingsArray[params.param2].TeleportTo[locationBlockIndex][0].ToVector();
        }
        else
        {
            NoTeleport = true;
        }
        //////Print(DzrQuestDst);
        // TODO: Bring back activations per player
        /*
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName,"ActivationsPerPlayer.txt", FileContents);
            int m_ActLimit = FileContents.ToInt();
            
            if( FileExist( "$profile:DZR\\Quests\\"+m_QuestName+"\\Activations\\" + PlainID +".txt" ) && m_ActLimit != 0 && MetConditions)
            {
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName+"\\Activations", PlainID+".txt", FileContents);
            int m_PlayerActs = FileContents.ToInt();
            
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName,"ActivationLimitMessage.txt", FileContents);
            string m_ActLimitMessage = FileContents;
            
            if(m_PlayerActs >= m_ActLimit && m_ActLimit != -1)
            {
            NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_QuestName, m_ActLimitMessage, "set:dayz_gui image:open");
            return;
            }
            
            
            int AddedActivation = m_PlayerActs + 1;
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName+"\\Activations", PlainID+".txt", null_content, AddedActivation.ToString() );
            }
        */
        vector DzrQuestDstOri = Vector(0,0,0) + Vector(m_DzrQuestTrigger.GetTargetOrientation().ToInt() , 0, 0) ;
        string DzrQuestTitle = m_DzrQuestTrigger.GetTitle();
        string DzrQuestDesc1 = m_DzrQuestTrigger.GetDescription1();
        //string m_QuestName = m_DzrQuestTrigger.GetQuestName();
        string message = string.Format(DzrQuestDesc1);
        //////////Print(message + ": " +m_QuestName);
        //int OPtionID = m_Player.GetSelectedOption() + 1;
        
        
        PrintD("ActivateDzrQuestForPlayer 1147");
        
        
        DZR_PTL_GetFile("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated", m_QuestName+"_"+OPtionID+".txt", FileContents);
        
        if(OPtionID != 0 && OPtionID != 51)
        {
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+m_QuestName+"\\", "Option"+OPtionID+"Message.txt", FileContents);
            //DZR_PTL_GetFile("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated", m_QuestName+"_"+OPtionID+".txt", null_content, "0");
            
            if(FileContents != "-1" && FileContents != "0")
            {
                NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, FileContents, "set:dayz_gui image:icon_forums");
            }
        }
        
        PrintD("ActivateDzrQuestForPlayer 1163");
        
        //ResetQuests
        TStringArray FileContentsArray = new TStringArray();
        //DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+m_QuestName, "ResetQuests.txt", FileContentsArray);
        ref array<string> resetQuests = QuestsSettingsArray[params.param2].ResetQuests;
        foreach (string arDzrQuest : resetQuests)
        {
            
            //////Print(m_QuestName);
            //////Print(arDzrQuest);
            //////Print("Checking "+PlainID+"\\QuestsActivated\\"+arDzrQuest+".txt")
            if(FileExist( "$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+arDzrQuest+".txt" ))
            {
                //////Print(arDzrQuest+".txt PORTAL RESET!!!");
                DeleteFile("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+arDzrQuest+".txt");
            }
            else
            {
                //////Print(arDzrQuest+".txt NOTHING TO RESET. OK.");
            }
        }
        PrintD("ActivateDzrQuestForPlayer 1185");
        Print(MetConditions);
        if(MetConditions)
        {
            
            
            foreach(string aFunc2 : FuncArray)
            {
                if(aFunc2.Contains("ResetDzrQuest") && AllItemsPresent)
                {
                    TStringArray aDzrQuestResetArr = new TStringArray;
                    aFunc2.Split(" ", aDzrQuestResetArr);
                    
                    if(FileExist( "$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+aDzrQuestResetArr.Get(1)+".txt" ))
                    {
                        //////Print(arDzrQuest+".txt PORTAL RESET!!!");
                        DeleteFile("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+aDzrQuestResetArr.Get(1)+".txt");
                    }
                    
                    //Print("ResetDzrQuest 000000000000000000000000000000000000000000 TRIED DELETING "+aDzrQuestResetArr.Get(1));
                }
                
                
                
                
                if(aFunc2.Contains("GiveItems") && AllItemsPresent)
                {
                    TStringArray aGiveItemsArr = new TStringArray;
                    aFunc2.Split(" ", aGiveItemsArr);
                    TStringArray ItemsArray = aGiveItemsArr;
                    ItemsArray.Remove(0);
                    foreach(string anItem : ItemsArray) 
                    {
                        //////Print("Giving Item: "+anItem);
                        EntityAI theItemC = m_Player.GetInventory().CreateInInventory(anItem);
                        the_icon = "set:dayz_gui image:gear";
                        if(m_QuestIcon != "0" || m_QuestIcon != "")
                        {
                            the_icon = m_QuestIcon;
                        }
                        NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, "Получен предмет: "+theItemC.ConfigGetString("displayName"), the_icon );
                    }
                    //Print("GIVEITEMS  0000000000000000000000000000000000000000000000 TRIED GIVING "+ItemsArray.Get(1));
                }
                
                if(aFunc2.Contains("GiveItem") && AllItemsPresent)
                {
                    TStringArray i1_aGiveItemsArr = new TStringArray;
                    aFunc2.Split(" ", i1_aGiveItemsArr);
                    //TStringArray i1_ItemsArray = i1_aGiveItemsArr;
                    //i1_ItemsArray.Remove(0);;
                    string i1_theItem = i1_aGiveItemsArr.Get(1);
                    //////Print("Giving Item: "+anItem);
                    EntityAI i1_theItemC = m_Player.GetInventory().CreateInInventory(i1_theItem);
                    NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, "Получен предмет: "+i1_theItemC.ConfigGetString("displayName"), "set:dayz_gui image:gear");
                    
                    //Print("GiveItem  0000000000000000000000000000000000000000000000 TRIED GIVING "+i1_theItem);
                    
                }
            }
        }
        
        //ResetQuests
        
        //SendDzrQuestsToPlayers();
        Param1<int> nullData;
        
        PrintD("ActivateDzrQuestForPlayer 1252");
        Print(QuestsSettingsArray[params.param2].UseLoadingScreen);
        if(QuestsSettingsArray[params.param2].UseLoadingScreen)
        {
            
            
            // CUT OLD CODE
            
            string Autoclose = QuestsSettingsArray[params.param2].Autoclose;
            
            //debug
            ////Print(fdelay);
            ////Print(ftext1);
            ////Print(ftext2);
            ////Print(ftext3);
            //debug
            
            if(Autoclose == "1")
            {
                
                GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(ClosePlayerBlackScreen, delayBeforeTeleport*1000+3000+delayAfterTeleport*1000, false, m_Player.GetIdentity());
                ////Print("Autoclose CallLater(ClosePlayerBlackScreen");
            }
            
            vector pos = QuestsSettingsArray[params.param2].TeleportTo[0][0].ToVector();
            if(!NoLoading)
            {
                
                // Param1<string  > LayoutData = new Param1<string  >(CustomLayout);
                // GetRPCManager().SendRPC( "dzr_dquests", "SetPlayerActiveLayout", LayoutData, true, m_Player.GetIdentity() ); 
                
                // m_Player.SetCurrentLayout(CustomLayout);
                // m_Player.m_CurrentLayout = CustomLayout;
                
                // Print(m_Player.GetCurrentLayout());
                // Print(m_Player.m_CurrentLayout);
                // m_Player.SetSynchDirty();
                //Autoclose
                
                Param8<string , string , string, string, string, int, string, string> LoaderData = new Param8<string , string , string, string, string, int, string, string>(QuestsSettingsArray[params.param2].Text1 ,"EMPTY" , QuestsSettingsArray[params.param2].Text3, QuestsSettingsArray[params.param2].Sound, QuestsSettingsArray[params.param2].Image, totalDelay, QuestsSettingsArray[params.param2].Icon, QuestsSettingsArray[params.param2].Layout );
                // int locationBlockIndex = 0;
                //TODO: Process location index. Triggers must pass that index if they are multiple. It will select their target from TeleportTo array.
                //TODO: Randomize from TeleportTo array.
                
                
                
                Param1<string > Fadedata = new Param1<string  >("NULL");
                
                GetRPCManager().SendRPC( "dzr_dquests", "RequestLoadingScreen", LoaderData, true, sender ); 
                
                GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_Player.SetPosition, delayBeforeTeleport*1000, false, pos);
                
            }
        }
        
        pos = QuestsSettingsArray[params.param2].TeleportTo[0][0].ToVector();
        //int delayBeforeTeleport = QuestsSettingsArray[params.param2].DelayBeforeTeleport.ToInt();
        
        if(!NoTeleport)
        {
            //PlayerTP(m_Player,DzrQuestDst);
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(PlayerTP, delayBeforeTeleport*1000, false,m_Player, pos);
            //m_Player.SetOrientation(DzrQuestDstOri);
        }
        //m_Player.SetSelectedOption2(0);
        
        if(OptionTeleport != "")
        {
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+OptionTeleport,"QuestPosition.txt", FileContents);
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(PlayerTP, delayBeforeTeleport*1000, false,m_Player, pos);
        }
        
        GetRPCManager().SendRPC( "dzr_dquests", "ResetOptions", nullData, true, sender); 
        GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", nullData, true, sender); 
        
    }
    
    void PlayerTP(PlayerBase player, vector coords )
    {
        player.SetPosition(coords);
    }
    
    void ClosePlayerBlackScreen(PlayerIdentity m_Player)
    {
        Param1<int> nullData;
        GetRPCManager().SendRPC( "dzr_dquests", "RequestLoadingScreenClose", nullData, true, m_Player ); 
    }
    
    int CountItemsInInvetnory(string classname, PlayerBase m_Player, out ref array<EntityAI> allItems, out bool ItemExists)
    {
        ////Print("Counting items: "+classname);
        int counter = 0;
        allItems = new array<EntityAI>;
        GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        ref array<EntityAI> items = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        ItemExists = false;
        //////Print(items);
        for(int i = 0; i < items.Count(); i++)
        {
            EntityAI item = items.Get(i);
            ////Print(item);
            if(item && item.IsKindOf(classname))
            {
                allItems.Insert(item);
                ItemExists = true;
                ////Print("Item found!");
                counter++;
            }
            //item.SetHealthMax("","");
            ////////Print("Fixing item: "+item);
            
        }
        
        
        
        if(counter < 1) 
        {
            //Print("========== NO ITEM =========");
            ItemExists = false;
            
        }
        ////Print(allItems);
        return counter;
    }
    
    void RemoveItemsFromInvetnory(string classname, int quantity, PlayerBase m_Player)
    {
        
        GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        array<EntityAI> items = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        for(int i = 0; i < items.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(items.Get(i));
            if(item && !item.IsPlayer() && item.IsKindOf(classname))
            {
                GetGame().ObjectDelete(item);
            }
        }
        
        ItemBase item_in_hands = ItemBase.Cast( m_Player.GetHumanInventory().GetEntityInHands() );
        
        if ( item_in_hands && item_in_hands.IsKindOf(classname))
        {
            m_Player.LocalDestroyEntityInHands();
        }
        
    }
    void RemoveItemsFromInvetnory2(string classname, int quantity, PlayerBase m_Player)
    {
        GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        array<EntityAI> items = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        for(int i = 0; i < items.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(items.Get(i));
            Print("---------------- is "+item.GetType()+" deriving from "+classname);
            if( item && !item.IsPlayer() && item.IsInherited(classname.ToType()) )
            {
                GetGame().ObjectDelete(item);
            }
        }
        
        ItemBase item_in_hands = ItemBase.Cast( m_Player.GetHumanInventory().GetEntityInHands() );
        
        if ( item_in_hands && item_in_hands.IsInherited(classname.ToType())  )
        {
            Print("---------------- is "+item_in_hands.GetType()+" deriving from "+classname);
            m_Player.LocalDestroyEntityInHands();
        }
        
    }
    
    void RemoveItemFromInvetnoryByClassname(string classname, PlayerBase m_Player)
    {
        
        GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        array<EntityAI> items = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        for(int i = 0; i < items.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(items.Get(i));
            if(item && !item.IsPlayer() && item.IsKindOf(classname))
            {
                GetGame().ObjectDelete(item);
                return;
            }
        }
        
        ItemBase item_in_hands = ItemBase.Cast( m_Player.GetHumanInventory().GetEntityInHands() );
        
        if ( item_in_hands && item_in_hands.IsKindOf(classname))
        {
            m_Player.LocalDestroyEntityInHands();
            return;
        }
        
    }
    
    void RemoveItemFromInvetnory(EntityAI recItem, PlayerBase m_Player, int quantity = 0, string m_DzrQuestTitle = "DZR Quests & Portals", string m_QuestIcon = "0")
    {
        
        GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        array<EntityAI> items = new array<EntityAI>;
        inv.EnumerateInventory(InventoryTraversalType.INORDER, items);
        ItemBase redquestedItem = ItemBase.Cast(recItem);
        //Print(";;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; REMOVING ---------------------- ");
        //Print(recItem);
        //Print(quantity);
        ////Print(items.Count());
        if(recItem == NULL)
        {
            ////Print("No item ");
            return;
        }
        for(int i = 0; i < items.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(items.Get(i));
            if(item && !item.IsPlayer() && item == redquestedItem)
            {
                if(quantity > 1)
                {
                    //Print("Removing: " + quantity);
                    item.AddQuantity(-quantity);
                    NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, redquestedItem.GetDisplayName()+": Расход "+quantity+" ед." , "set:dayz_gui image:gear");
                }
                else
                {
                    //Print("ObjectDelete(item): ");
                    //Print(item);
                    GetGame().ObjectDelete(item);
                    string the_icon = "set:dayz_gui image:gear";
                    if(m_QuestIcon != "0" || m_QuestIcon != "")
                    {
                        the_icon = m_QuestIcon;
                    }
                    NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, "#dzrqst_you_gave_item"+": "+redquestedItem.GetDisplayName() , the_icon);
                }
            }
        }
        
        ItemBase item_in_hands = ItemBase.Cast( m_Player.GetHumanInventory().GetEntityInHands() );
        
        if ( item_in_hands && item_in_hands == redquestedItem && quantity <= 1)
        {
            
            {
                m_Player.LocalDestroyEntityInHands();
                the_icon = "set:dayz_gui image:gear";
                if(m_QuestIcon != "0" || m_QuestIcon != "")
                {
                    the_icon = m_QuestIcon;
                }
                NotificationSystem.SendNotificationToPlayerExtended(m_Player, 5, m_DzrQuestTitle, "#dzrqst_you_gave_item"+": "+redquestedItem.GetName()+" ("+quantity+" шт.)" , the_icon);
            }
        }
        
    }
    
    
    
    
    void SendNamesToOnePlayer(PlayerIdentity identity)
    {
        Param1<int> nullData;
        
        int pi = 0;
        foreach (DzrQuestTrigger aDzrQuest : m_DzrQuests)
        {        
            
            if(aDzrQuest)
            {
                if(aDzrQuest.m_QuestName)
                {
                    int m_Allowed = isPlayerAllowed(identity.GetPlainId(), aDzrQuest.m_QuestName);
                    
                    ////Print("____SendNamesToOnePlayer___"+pi+"_________");
                    ////Print(aDzrQuest.m_QuestActionName);
                    ////Print(m_Allowed);
                    string FileContents;
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+aDzrQuest.m_QuestName,"ActionName.txt", FileContents);
                    //theDzrQuestTrigger.SetDzrQuestActionName(FileContents);
                    ref Param1<string> m_DataString = new Param1<string>(FileContents);
                    //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", m_DataInt, true, identity); 
                    GetRPCManager().SendRPC( "dzr_dquests", "UpdateQuestNames", m_DataString, true, identity); 
                    
                    
                    aDzrQuest.SetSynchDirty();
                    identity.GetPlayer().SetSynchDirty();
                    ////Print("_____________");
                }
                else
                {
                    Print("aDzrQuest.m_QuestName is NULL!!! Strange!!!");
                }
            }
            pi++;
        }
    }
    
    void SendPermissionsToOnePlayer(PlayerIdentity identity)
    {
        Param1<int> nullData;
        
        int pi = 0;
        //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", m_DataInt, true, identity); 
        foreach (DzrQuestTrigger aDzrQuest : m_DzrQuests)
        {        
            int m_Allowed = isPlayerAllowed(identity.GetPlainId(), aDzrQuest.m_QuestName);
            
            ////Print("____SendPermissionsToOnePlayer___"+pi+"_________");
            ////Print(aDzrQuest.m_QuestActionName);
            ////Print(m_Allowed);
            
            ref Param1<int> m_DataInt = new Param1<int>(m_Allowed);
            //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", m_DataInt, true, identity); 
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateDzrQuestPermissions", m_DataInt, true, identity); 
            
            
            aDzrQuest.SetSynchDirty();
            identity.GetPlayer().SetSynchDirty();
            ////Print("_____________");
            pi++;
        }
        
        
        
    }
    
    void SendOptionsToOnePlayer(PlayerIdentity identity)
    {
        if(!identity)
        {
            return;
        }
        //PlayerIdentity identity = sender;
        
        Param1<int> nullData;
        //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestVars", nullData, true, identity); 
        
        //foreach portal
        int pi = 0;
        foreach (DzrQuestTrigger aDzrQuest : m_DzrQuests)
        {        
            
            string FileContents;
            string DzrQuestFolder = aDzrQuest.m_QuestName;
            
            string PlainID = identity.GetPlainId();
            /*
                DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1.txt", FileContents);
                if(FileExist("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+ DzrQuestFolder+"_1.txt")){FileContents = "0";};
                Param1<string>m_DataStr = new Param1<string>(FileContents);
                GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions1", m_DataStr, true, identity); 
                
                DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2.txt", FileContents);
                if(FileExist("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+ DzrQuestFolder+"_2.txt")){FileContents = "0";};
                m_DataStr = new Param1<string>(FileContents);
                GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions2", m_DataStr, true, identity); 
                
                DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3.txt", FileContents);
                if(FileExist("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+ DzrQuestFolder+"_3.txt")){FileContents = "0";};
                m_DataStr = new Param1<string>(FileContents);
                GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions3", m_DataStr, true, identity); 
                
                DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4.txt", FileContents);
                if(FileExist("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+ DzrQuestFolder+"_4.txt")){FileContents = "0";};
                m_DataStr = new Param1<string>(FileContents);
                GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions4", m_DataStr, true, identity); 
                
                DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5.txt", FileContents);
                if(FileExist("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+ DzrQuestFolder+"_5.txt")){FileContents = "0";};
                m_DataStr = new Param1<string>(FileContents);
                GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions5", m_DataStr, true, identity); 
                
                DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6.txt", FileContents);
                if(FileExist("$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+ DzrQuestFolder+"_6.txt")){FileContents = "0";};
                m_DataStr = new Param1<string>(FileContents);
                GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions6", m_DataStr, true, identity);  
            */
            
            //FIXING NEW
            
            
            aDzrQuest.SetSynchDirty();
            identity.GetPlayer().SetSynchDirty();
            ////Print("_____________");
            pi++;
        }
        
        
        
    }
    
    void SendDzrQuestsToOnePlayer(PlayerIdentity identity)
    {
        
        
        SendNamesToOnePlayer(identity);
        SendPermissionsToOnePlayer(identity);
        SendOptionsToOnePlayer(identity);
        /*
            //PlayerIdentity identity = sender;
            
            Param1<int> nullData;
            //GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestVars", nullData, true, identity); 
            
            //foreach portal
            int pi = 0;
            foreach (DzrQuestTrigger aDzrQuest : m_DzrQuests)
            {        
            
            
            
            
            int m_Allowed = isPlayerAllowed(identity.GetPlainId(), aDzrQuest.m_QuestName);
            
            ////Print("_______"+pi+"_________");
            ////Print(aDzrQuest.m_QuestActionName);
            ////Print(m_Allowed);
            /*
            ref Param6<int, int, string, string, string, string> m_Data = new Param6<int, int, string, string, string, string>(pi, m_Allowed, aDzrQuest.m_QuestActionName, aDzrQuest.m_Option1, aDzrQuest.m_Option2, aDzrQuest.m_Option3);
            GetRPCManager().SendRPC( "dzr_dquests", "Save1DzrQuestFromServer", m_Data, true, identity); 
        */
        //FIXING NEW
        
        // GetRPCManager().SendRPC( "dzr_dquests", "ResetOptions", nullData, true, identity); 
        // GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", nullData, true, identity); 
        // GetRPCManager().SendRPC( "dzr_dquests", "ResetQuestNames", nullData, true, identity); 
        
        //ref Param1<string> m_DataStr = new Param1<string>(aDzrQuest.m_QuestActionName);
        //GetRPCManager().SendRPC( "dzr_dquests", "UpdateQuestNames", m_DataStr, true, identity); 
        
        
        /*
            
            ref Param1<int> m_DataInt = new Param1<int>(m_Allowed);
            GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", m_DataInt, true, identity); 
            //GetRPCManager().SendRPC( "dzr_dquests", "UpdateDzrQuestPermissions", m_DataInt, true, identity); 
            
            m_DataStr = new Param1<string>(aDzrQuest.m_Option1);
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions1", m_DataStr, true, identity);  
            
            m_DataStr = new Param1<string>(aDzrQuest.m_Option2);
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions2", m_DataStr, true, identity);  
            
            m_DataStr = new Param1<string>(aDzrQuest.m_Option3);
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions3", m_DataStr, true, identity);  
            
            m_DataStr = new Param1<string>(aDzrQuest.m_Option4);
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions4", m_DataStr, true, identity);  
            
            m_DataStr = new Param1<string>(aDzrQuest.m_Option5);
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions5", m_DataStr, true, identity);  
            
            m_DataStr = new Param1<string>(aDzrQuest.m_Option6);
            GetRPCManager().SendRPC( "dzr_dquests", "UpdateOptions6", m_DataStr, true, identity); 
            
            //FIXING NEW
            
            
            aDzrQuest.SetSynchDirty();
            identity.GetPlayer().SetSynchDirty();
            ////Print("_____________");
            pi++;
            }
            
        */
        
    }
    
    void PlayerRedquestsDzrQuests(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<int> params;
        
        
        SendDzrQuestsToOnePlayer(sender);
    }
    
    void PlayerRedquestsOptions(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        ////Print("______PlayerRedquests Options <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<_______");
        SendOptionsToOnePlayer(sender);
    }
    
    void PlayerRedquestsPermissions(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        ////Print("______PlayerRedquests Permissions <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<_______");
        SendPermissionsToOnePlayer(sender);
    }
    
    void PlayerRedquestsNames(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        ////Print("______PlayerRedquests Names <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<_______");
        SendNamesToOnePlayer(sender);
    }
    
    
    
    void SendDzrQuestsToPlayers()
    {        
        array<Man> players = new array<Man>;
        GetGame().GetPlayers(players);
        Param1<int> nullData;
        ref Param1<string> m_DataRes = new Param1<string>("");
        //GetRPCManager().SendRPC( "dzr_dquests", "ResetQuests", m_DataRes, true);
        GetRPCManager().SendRPC( "dzr_dquests", "ResetOptions", nullData, true); 
        GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", nullData, true); 
        GetRPCManager().SendRPC( "dzr_dquests", "ResetQuestNames", nullData, true); 
        
        /*
            for (int i = 0; i < players.Count(); ++i)
            {
            PlayerIdentity identity = PlayerBase.Cast(players.Get(i)).GetIdentity();
            PlayerBase player = PlayerBase.Cast(players.Get(i));
            Param1<int> nullData;
            GetRPCManager().SendRPC( "dzr_dquests", "ResetOptions", nullData, true, identity); 
            GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", nullData, true, identity); 
            GetRPCManager().SendRPC( "dzr_dquests", "ResetQuestNames", nullData, true, identity); 
            
            }
        */
        
    }
    
    bool isPlayerAllowed(string PlainID, string QuestName)
    {
        bool UserAllowed = true;
        Object theDzrQuestObject;
        string FileContents;
        m_DzrQuestObjects.Find(QuestName, theDzrQuestObject);
        if(theDzrQuestObject)
        {
            DzrQuestTrigger theDzrQuestTrigger = DzrQuestTrigger.Cast(theDzrQuestObject);
            if( (theDzrQuestTrigger.GetisPrivate() == "1") && !FileExist( "$profile:DZR\\Quests\\"+QuestName+"\\AllowedPlayers\\" + PlainID +".txt" ) )
            {
                UserAllowed = false;	
                //////Print(PlainID +" not allowed for PRIVATE portal "+QuestName);
            }
            
            //REQUIRED PORTALS
            TStringArray FileContentsArray = new TStringArray();
            DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+QuestName, "RequiredQuests.txt", FileContentsArray);
            
            foreach (string aDzrQuest : FileContentsArray)
            {
                
                // MULTIPLE CONDITIONS
                TStringArray r_MConditions = new TStringArray;
                aDzrQuest.Split(" ", r_MConditions);
                
                int r_ActualConditionsNum = 0;
                int r_ConditionsNum = r_MConditions.Count();
                //Print(r_MConditions);
                if(r_ConditionsNum > 1)
                {
                    //Print(QuestName+" =================== MULTIPLE CONDITIONS RequiredQuests ===================");
                    foreach (string r_aCondition : r_MConditions)
                    {
                        //Print(r_aCondition);
                        if(FileExist( "$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+r_aCondition+".txt" ))
                        {
                            r_ActualConditionsNum++;
                            //Print(r_aCondition);
                        }
                        
                    }
                    if(r_ActualConditionsNum < r_ConditionsNum) 
                    {
                        //Print(r_ActualConditionsNum.ToString()+":"+r_ConditionsNum.ToString());
                        UserAllowed = false;
                        //Print(UserAllowed);
                    }
                    //Print(QuestName+" ===========================================================");
                    
                }
                else
                {
                    
                    // MULTIPLE CONDITIONS
                    
                    if(!FileExist( "$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+aDzrQuest+".txt" ))
                    {
                        //////Print(aDzrQuest+".txt MISSING");
                        UserAllowed = false;
                    }
                    else
                    {
                        //////Print(aDzrQuest+".txt FOUND !!!");
                    }
                }
            }
            //REQUIRED PORTALS
            
            //RestrictedQuests
            // TStringArray FileContentsArray = new TStringArray();
            DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+QuestName, "RestrictedQuests.txt", FileContentsArray);
            
            foreach (string aiDzrQuest : FileContentsArray)
            {
                // MULTIPLE CONDITIONS
                TStringArray MConditions = new TStringArray;
                aiDzrQuest.Split(" ", MConditions);
                
                int ActualConditionsNum = 0;
                int ConditionsNum = MConditions.Count();
                //Print(MConditions);
                if(ConditionsNum > 1)
                {
                    //Print(QuestName+" =================== MULTIPLE CONDITIONS RestrictedQuests ===================");
                    foreach (string aCondition : MConditions)
                    {
                        //Print(ConditionsNum);
                        if(FileExist( "$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+aCondition+".txt" ))
                        {
                            ActualConditionsNum++;
                            //Print(aCondition);
                        }
                        //Print(ActualConditionsNum);
                    }
                    if(ActualConditionsNum >= ConditionsNum) 
                    {
                        UserAllowed = false;
                    }
                    //Print(UserAllowed);
                    //Print(QuestName+" ============================================================================");
                }
                else
                {
                    // MULTIPLE CONDITIONS
                    
                    //////Print(QuestName);
                    //////Print(aiDzrQuest);
                    //////Print("Checking "+PlainID+"\\QuestsActivated\\"+aiDzrQuest+".txt")
                    if(FileExist( "$profile:DZR\\players\\local_player_db\\"+PlainID+"\\QuestsActivated\\"+aiDzrQuest+".txt" ))
                    {
                        //////Print(aiDzrQuest+".txt INCOMPATIBLE PORTAL FOUND!!!");
                        UserAllowed = false;
                    }
                    else
                    {
                        //////Print(aiDzrQuest+".txt NO INCOMPATIBLES. OK.");
                    }
                }
            }
            //RestrictedQuests
            
            
        }
        //Print(UserAllowed);
        //Print(QuestName+" ==========________________________________________FINAL UserAllowed");
        return UserAllowed;
    }
    
    
    
    void SendPlayerAllowed(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param2<string, DzrQuestTrigger> params;
        if (!ctx.Read(params))
        return;
        
        ref Param1<bool> m_Data = new Param1<bool>(isPlayerAllowed(params.param1, params.param2.GetQuestName()));
        GetRPCManager().SendRPC( "dzr_dquests", "SetUserAllowed", m_Data, true, sender);
    }
    
    void SendDzrQuestAttributesToClient(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<DzrQuestTrigger> params;
        if (!ctx.Read(params))
        return;
        
        string ActionName = params.param1.GetDzrQuestActionName();
        bool PlayerAllowed = isPlayerAllowed( sender.GetPlainId(), params.param1.GetQuestName());
        ref Param2<string, bool> m_Data = new Param2<string, bool>(ActionName, PlayerAllowed);
        //GetRPCManager().SendRPC( "dzr_dquests", "SetDzrQuestAttributesOnClient", m_Data, true, sender);
        //////////Print("SendDzrQuestAttributesToClient ActionName:"+ActionName+" PlayerAllowed:"+PlayerAllowed);
    }
    
    bool RecreateDzrQuests()
    {
        //portal array reset
        
        
        ClearDzrQuests();
        
        
        TStringArray DzrQuestFolders = GetFoldersList("$profile:\\DZR\\Quests");
        m_DzrQuests = CreateDzrQuestsFromFolders(DzrQuestFolders);
        
        
        ////Print(m_DzrQuests);
        SendDzrQuestsToPlayers();
        return true;
    }
    
    void ClearDzrQuests()
    {
        if(m_DzrQuests)
        {
            foreach(DzrQuestTrigger a_DzrQuest : m_DzrQuests)
            {
                if(a_DzrQuest)
                {
                    GetGame().ObjectDelete( a_DzrQuest );
                    ////////Print("======== Deleted ");
                    ////////Print(a_DzrQuest);
                }
                else
                {
                    ////////Print( "-----------");
                    ////////Print( a_DzrQuest);
                    ////////Print( "Does not exist");
                    ////////Print( "-----------");
                };
            }
        }
        else
        {
            "========= DzrQuest array is NULL";
        }
    }
    
    bool QuestExists(vector pos, string portalName)
    {
        array<Object> nearestObjects = new array<Object>;
        
        GetGame().GetObjectsAtPosition(pos, 3, nearestObjects, null);
        
        for (int i = 0; i < nearestObjects.Count(); i++)
        {
            if(nearestObjects.Get(i).IsKindOf("DzrQuestTrigger"))
            {
                DzrQuestTrigger theDzrQuest = DzrQuestTrigger.Cast(nearestObjects.Get(i));
                if(theDzrQuest.m_QuestName == portalName)
                {
                    return true;
                }
            }
        }
        return false;
    };
    
    bool CreateOneDzrQuest(string arg1, string arg2, vector pos, vector ori)
    {
        // vector pos;
        // vector ori;
        
        // Offset points for Garbage
        DzrQuestTrigger d_DzrQuestTrigger;
        
        
        d_DzrQuestTrigger = DzrQuestTrigger.Cast(GetGame().CreateObject( "DzrQuestTrigger", vector.Zero ));
        
        
        //vector z_newGBChernoOffsetA = z_GarbagePoint.GetPosition() + "0 50.95 0"; // 51.25 //.95 (1.13v)
        
        if(!d_DzrQuestTrigger) return false;
        //d_DzrQuestTrigger.Update();
        
        string FileContents;
        string PoralP;
        string QuestPosition;
        string QuestName;
        
        string DzrQuestFolder = arg1;
        
        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Settings.txt", FileContents, pos[0].ToString()+" "+pos[1].ToString()+" "+pos[2].ToString());
        //QuestPosition = FileContents;
        
        DzrQuestTrigger theDzrQuestTrigger = DzrQuestTrigger.Cast(d_DzrQuestTrigger);
        /*
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"QuestName.txt", FileContents, DzrQuestFolder);
            QuestName = FileContents;
            
            
            
            
            
            
            theDzrQuestTrigger.SetQuestPosition(QuestPosition);
            theDzrQuestTrigger.SetQuestName(QuestName);
            
            
            
            
            
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ActionName.txt", FileContents, "GO TO "+arg1);
            theDzrQuestTrigger.SetDzrQuestActionName(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"LoadingScreenDelay.txt", FileContents, "10");
            theDzrQuestTrigger.SetLoadingScreenDelay(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"LoadingScreenDelayWhileTeleporting.txt", FileContents, "1");
            theDzrQuestTrigger.SetLoadingScreenDelayWhileTeleporting(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Description1.txt", FileContents,"You have activated portal "+arg1);
            theDzrQuestTrigger.SetDescription1(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Description2.txt", FileContents, "Have a nice trip!");
            theDzrQuestTrigger.SetDescription2(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Description3.txt", FileContents, "Long story short. It works.");
            theDzrQuestTrigger.SetDescription3(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Target.txt", FileContents, "12918.8 64.9251 10004.7");
            theDzrQuestTrigger.SetTarget(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            
            //get portal attribute from file
            
            //get portal attribute from file
            string b_Private = arg2;
            if(arg2 == "")
            { b_Private = "0"};
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"isPrivate.txt", FileContents, b_Private);
            theDzrQuestTrigger.SetisPrivate(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"TargetOrientation.txt", FileContents, "-171 0 0");
            theDzrQuestTrigger.SetTargetOrientation(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            
            
            d_DzrQuestTrigger.SetPosition(QuestPosition.ToVector());
            
            d_DzrQuestTrigger.SetOrientation(Vector(Math.RandomInt(0, 360), 0, 0));
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"NPC.txt", FileContents);
            
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CCTV.txt", FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CCTV_Effect.txt", FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CCTV_Group.txt", FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CCTV_Controlled.txt", FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CCTV_Mic.txt", FileContents);
            
            
            
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Title.txt", FileContents);
            theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CheckItems.txt", FileContents);
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"RemoveItems.txt", FileContents);
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"GiveItems.txt", FileContents);
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ActivationsPerPlayer.txt", FileContents, "-1");
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ActivationLimitMessage.txt", FileContents, "Вы использовали портал максимальное количество раз.");
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"QuestFunctions.txt", FileContents, "RestoreHealth\r\nBoostImmunity\r\nHeal\r\nRepairGear");
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1.txt", FileContents, "YES");
            theDzrQuestTrigger.SetOption1(FileContents );
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2.txt", FileContents, "NO");
            theDzrQuestTrigger.SetOption2(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3.txt", FileContents, "BACK");
            theDzrQuestTrigger.SetOption3(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4.txt", FileContents, "0");
            theDzrQuestTrigger.SetOption3(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5.txt", FileContents, "0");
            theDzrQuestTrigger.SetOption3(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6.txt", FileContents, "0");
            theDzrQuestTrigger.SetOption3(FileContents);
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1Message.txt", FileContents, "Good you've said YES");
            //theDzrQuestTrigger.SetOption1(FileContents );
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2Message.txt", FileContents, "Sorry to hear NO");
            //theDzrQuestTrigger.SetOption2(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3Message.txt", FileContents, "Yeah, let's start from scratch.");
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4Message.txt", FileContents, "0");
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5Message.txt", FileContents, "0");
            //get portal attribute from file
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6Message.txt", FileContents, "0");
            //get portal attribute from file
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1Functions.txt", FileContents, "0");
            //theDzrQuestTrigger.SetOption1(FileContents );
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2Functions.txt", FileContents, "0");
            //theDzrQuestTrigger.SetOption2(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3Functions.txt", FileContents, "0");
            //theDzrQuestTrigger.SetOption3(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4Functions.txt", FileContents, "0");
            //theDzrQuestTrigger.SetOption3(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5Functions.txt", FileContents, "0");
            //theDzrQuestTrigger.SetOption3(FileContents);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6Functions.txt", FileContents, "0");
            //theDzrQuestTrigger.SetOption3(FileContents);
            //theDzrQuestTrigger.SetTitle(FileContents);
            //get portal attribute from file
            
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"RequiredQuests.txt", FileContents, "");
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"RestrictedQuests.txt", FileContents, DzrQuestFolder);
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ResetQuests.txt", FileContents, "");
            
        */
        
        theDzrQuestTrigger.Update();
        theDzrQuestTrigger.SetSynchDirty();
        
        
        int index = m_DzrQuests.Insert(theDzrQuestTrigger);
        
        theDzrQuestTrigger.SetDzrQuestID(index);
        
        Param1<int> p = new Param1<int>( index );
        GetRPCManager().SendRPC( "dzr_dquests", "RPCSetDzrQuestID", p, true );
        
        index = m_DzrQuestObjects.Insert(DzrQuestFolder, theDzrQuestTrigger);
        //-----------------------NEW
        
        
        
        //-----------------------NEW
        return true;
    }
    
    void CreateNPCFromFolder(string DzrQuestFolder)
    {
        
        
        Object d_DzrQuestTrigger;
        int index;
        
        vector pos;
        vector ori;
        
        // Offset points for Garbage
        // d_DzrQuestTrigger = GetGame().CreateObject( "DzrQuestTrigger", vector.Zero ); //GarbageDebug //GarbagePoint
        //vector z_newGBChernoOffsetA = z_GarbagePoint.GetPosition() + "0 50.95 0"; // 51.25 //.95 (1.13v)
        
        // CarScript tempCar;
        // tempCar.ClearNPCVehicles();
        
        
        string FileContents;
        string PoralP;
        string QuestPosition;
        string QuestName;
        
        
        DzrQuestTrigger theDzrQuestTrigger = DzrQuestTrigger.Cast(d_DzrQuestTrigger);
        
        
        
        //NPC
        
        if(FileExist("$profile:DZR\\Quests\\"+DzrQuestFolder+"\\NPC.txt"))
        {
            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"NPC.txt", FileContents);
            //Print("trying to NPC "+DzrQuestFolder);
            //Print(FileContents);
            if(FileContents != "" && FileContents != "0")
            {
                
                //Print("Not empty NPC");
                TStringArray npcArray;
                DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+DzrQuestFolder,"NPC.txt", npcArray);
                
                
                string NPC_class = npcArray.Get(0);
                string NPC_pos = npcArray.Get(1);
                string NPC_ori = npcArray.Get(2) + " 0 0" ;
                string NPC_hands = npcArray.Get(3);
                
                
                
                
                ClearNPCAtPos(NPC_pos.ToVector());
                vector NPC_pos_INIT = NPC_pos.ToVector() + "0 15 0";
                
                Print("1965 TRYING TO CREATE");
                Print(NPC_class);
                Object obj = GetGame().CreateObject( NPC_class, NPC_pos_INIT );
                Print(obj);
                
                
                if(obj.IsKindOf("CarScript"))
                {
                    
                    if(obj)
                    {
                        int b1, b2, b3, b4;
                        EntityAI theObjEnt = EntityAI.Cast(obj);
                        theObjEnt.GetPersistentID( b1, b2, b3, b4);
                        string PID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\Cleanup",PID+".txt", FileContents, theObjEnt.ToString() );
                    }
                    
                    Car theCar = Car.Cast(obj);
                    theCar.DisableSimulation(true);
                    
                    
                    //CarScript.Cast(theCar).m_DZR_Blocked = true;
                    //CarScript.Cast(theCar).m_DZR_FreshSpawn = true;
                    theCar.GetInventory().CreateInInventory("DzrCarBlocker");
                    theCar.SetLifetime( 3888000 );
                    //theCar.DisableSimulation(true);
                    
                    AllMissionCars.Insert(CarScript.Cast(theCar));
                    
                    theCar.SetOrientation(NPC_ori.ToVector());
                    
                    theCar.SetAllowDamage(false);
                    
                    if(NPC_hands != "0")
                    {
                        for (int icar = 4; icar < npcArray.Count(); icar++)
                        {
                            //ItemBase created_item = theNPC.GetInventory().CreateInInventory(npcArray.Get(icar)); 
                            theCar.GetInventory().CreateAttachment(npcArray.Get(icar));
                        }
                    }
                    else
                    {
                        theCar.OnDebugSpawn();
                    }
                    
                    
                    CarScript.Cast(theCar).DZR_SyncVal();
                    // //Print(CarScript.Cast(theCar).m_DZR_Blocked);
                    
                    CarScript tempCar2;
                    tempCar2.ClearNPCVehicles();
                    
                    //obj.SetPosition(NPC_pos.ToVector());
                    ProperCarSpawn(CarScript.Cast(theCar), NPC_pos);
                    
                }
                else
                {
                    
                    Print("-------------------- CREATING NPC ---------------------");
                    PlayerBase theNPC = PlayerBase.Cast(obj);
                    Print(theNPC);
                    theNPC.m_isDZRNPC = true;
                    
                    theNPC.SetPosition(NPC_pos.ToVector()); // prevent automatic on ground placing
                    theNPC.SetOrientation(NPC_ori.ToVector()); // prevent automatic on ground placing
                    if(NPC_hands != "0")
                    {
                        theNPC.GetHumanInventory().CreateInHands(NPC_hands);
                    }
                    
                    
                    for (int i = 4; i < npcArray.Count(); i++)
                    {
                        
                        ItemBase created_item = ItemBase.Cast(theNPC.GetInventory().CreateInInventory(npcArray.Get(i)));
                        
                        
                        
                    }
                    //obj.SetPosition(NPC_pos.ToVector());
                    Print("-------------------- ---------------------");
                    theNPC.SetSynchDirty();
                }
                
            }
            
        }
        
        
        //return DzrQuestsArray;
    }
    
    void CreateNPCFromSettingsFile(string settingsPath, string fileName, string pos, string ori)
    {
        //CREATE NPC FROM SETTINGS FILE ARRAY
        string FileContents;
        if(FileExist(settingsPath+"\\"+fileName))
        {
            DZR_PTL_GetFile(settingsPath, fileName, FileContents);
            ////////Print("trying to NPC "+DzrQuestFolder);
            //////Print(FileContents);
            if(FileContents != "" && FileContents != "0")
            {
                
                ////////Print("Not empty NPC");
                TStringArray npcArray;
                DZR_PTL_GetFileLines(settingsPath,fileName, npcArray);
                //////Print("NPC array");
                //////Print(npcArray);
                
                string NPC_class = npcArray.Get(0);
                
                string NPC_pos = pos;
                string NPC_ori = ori;//+" 0 0";;
                
                /*
                    string NPC_pos = m_Settings.LocationNPC[0]+".txt";
                    string NPC_ori = m_Settings.OrientationNPC[0]+".txt";
                */
                
                //string NPC_ori = npcArray.Get(2)+" 0 0";
                string NPC_hands = npcArray.Get(1);
                
                //npcArray.Remove(0);
                //npcArray.Remove(0);
                //npcArray.Remove(0);
                //npcArray.Remove(0);
                //npcArray.RemoveOrdered(3);
                //////Print("|||||||||||||||||||||||||||||||||||||||||||||||||||| STARTING ADDING GEAR ");
                //////Print(npcArray);
                //ClearNPCAtPos(QuestPosition.ToVector());
                ClearNPCAtPos(NPC_pos.ToVector());
                vector NPC_pos_INIT = NPC_pos.ToVector() + "0 15 0";
                
                Print("TRYING TO CREATE " + NPC_class + " at "+NPC_pos+" with orientation "+NPC_ori);
                Print(NPC_class);
                Object obj = GetGame().CreateObject( NPC_class, NPC_pos_INIT);
                Print(obj);
                
                
                if(obj.IsKindOf("CarScript"))
                {
                    
                    if(obj)
                    {
                        int b1, b2, b3, b4;
                        EntityAI theObjEnt = EntityAI.Cast(obj);
                        theObjEnt.GetPersistentID( b1, b2, b3, b4);
                        string PID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\Cleanup",PID+".txt", FileContents, theObjEnt.ToString() );
                    }
                    
                    Car theCar = Car.Cast(obj);
                    
                    
                    theCar.OnDebugSpawn();
                    
                    theCar.SetOrientation(NPC_ori.ToVector());
                    
                    theCar.DisableSimulation(true);
                    
                    
                    //CarScript.Cast(theCar).m_DZR_Blocked = true;
                    //CarScript.Cast(theCar).m_DZR_FreshSpawn = true;
                    theCar.GetInventory().CreateInInventory("DzrCarBlocker");
                    theCar.SetLifetime( 3888000 );
                    //theCar.DisableSimulation(true);
                    
                    AllMissionCars.Insert(CarScript.Cast(theCar));
                    
                    
                    theCar.SetAllowDamage(false);
                    //theCar.PlaceOnSurface();
                    
                    
                    CarScript.Cast(theCar).DZR_SyncVal();
                    // //Print(CarScript.Cast(theCar).m_DZR_Blocked);
                    
                    CarScript tempCar2;
                    tempCar2.ClearNPCVehicles();
                    
                    ProperCarSpawn( CarScript.Cast(theCar), NPC_pos);
                    
                    
                }
                else
                {
                    PlayerBase theNPC = PlayerBase.Cast(obj);
                    theNPC.m_isDZRNPC = true;
                    
                    //////Print("*****************************GetPlainId********************************");
                    //////Print(theNPC.GetIdentity());
                    //////Print("*************************************************************");
                    //////Print(theNPC);
                    theNPC.SetPosition(NPC_pos.ToVector()); // prevent automatic on ground placing
                    
                    theNPC.SetOrientation(NPC_ori.ToVector());
                    
                    
                    theNPC.SetAllowDamage( false );
                    if(NPC_hands != "0")
                    {
                        theNPC.GetHumanInventory().CreateInHands(NPC_hands);
                    }
                    
                    
                    for (int i = 2; i < npcArray.Count(); i++)
                    {
                        ItemBase created_item = ItemBase.Cast(theNPC.GetInventory().CreateInInventory(npcArray.Get(i))); 
                    }
                    theNPC.SetSynchDirty();
                }
                
                
                
            }
        }
        //NPC
        //CREATE NPC FROM SETTINGS FILE ARRAY
    };
    
    array<DzrQuestTrigger> CreateDzrQuestsFromFolders(TStringArray DzrQuestFolders)
    {
        
        
        if(!DzrQuestFolders)
        {
            return NULL;
        };
        
        Object d_DzrQuestTrigger;
        int index;
        array<DzrQuestTrigger> DzrQuestsArray= new array<DzrQuestTrigger>;;
        if(m_DzrQuestObjects.Count() != 0)
        {
            m_DzrQuestObjects.Clear();
        };
        
        int qID = 0;
        foreach (string DzrQuestFolder : DzrQuestFolders)
        {
            if(DzrQuestFolder != "Cleanup" && DzrQuestFolder != "SettingsCache" )
            {
                
                
                vector pos;
                vector ori;
                
                // Offset points for Garbage
                //TODO: Loop through locations, not just one
                //TODO: Probably here is the first schedule check
                d_DzrQuestTrigger = DzrQuestTrigger.Cast(GetGame().CreateObject( "DzrQuestTrigger", vector.Zero ));
                
                //  d_DzrQuestTrigger = GetGame().CreateObject( "DzrQuestTrigger", vector.Zero ); //GarbageDebug //GarbagePoint
                //vector z_newGBChernoOffsetA = z_GarbagePoint.GetPosition() + "0 50.95 0"; // 51.25 //.95 (1.13v)
                
                
                d_DzrQuestTrigger.Update();
                string FileContents;
                string PoralP;
                string QuestPosition;
                string QuestName;
                
                string PID = GetPersistentID(EntityAI.Cast(d_DzrQuestTrigger))
                
                Print("▃▃▃▃▃▃▃▃▃▃▃▃▃▃");
                Print(PID);
                Print("▀▀▀▀▀▀▀▀▀▀▀▀▀▀");
                
                TStringArray SettArray;
                DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+DzrQuestFolder,"Settings.txt", SettArray);
                QuestIDs.Insert(DzrQuestFolder, qID);
                QuestPIDs.Insert(qID, PID);
                FullSettingsLinesArray.InsertAll(SettArray);
                FullSettingsLinesArray.Insert("--COMPILE");
                // Print("_______________________FullSettingsLinesArray_______________________");
                // Print("______________________________________________");
                // Print(FullSettingsLinesArray);
                
                // Print("______________________________________________");
                // Print("______________________FullSettingsLinesArray________________________");
                SettArray.InsertAt(DzrQuestFolder, 0);
                
                AllSettingsArray.Insert(SettArray);
                
                //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"QuestPosition.txt", FileContents);
                
                
                //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"QuestName.txt", FileContents);
                
                
                
                DzrQuestTrigger theDzrQuestTrigger = DzrQuestTrigger.Cast(d_DzrQuestTrigger);
                DzrQuestTriggerSettings m_Settings = new DzrQuestTriggerSettings(SettArray);
                /*
                    if(DzrQuestTriggerSettings.LoadSettingsFile(DzrQuestFolder))
                    {
                    JsonFileLoader<DzrQuestTriggerSettings>.JsonLoadFile("$profile:DZR\\Quests\\SettingsCache\\"+DzrQuestFolder+"_Settings.json", m_Settings);
                    DeleteFile("$profile:DZR\\Quests\\SettingsCache\\"+DzrQuestFolder+"_Settings.json");
                    };
                */
                
                
                
                
                // string response;
                // m_Settings = DzrQuestTriggerSettings.CompileSettings(SettArray, response);
                // Print(response);
                
                int locationBlockIndex = 0;
                //TODO: Same index processing here
                Print("|||||||||||||||m_Settings.Location[locationBlockIndex][0]|||||||||||||||");
                Print(m_Settings);
                Print(m_Settings.Location);
                Print(m_Settings.Location[locationBlockIndex]);
                Print(m_Settings.Location[locationBlockIndex][0]);
                Print("=================m_Settings.Location[locationBlockIndex][0]===============");
                theDzrQuestTrigger.SetQuestPosition(m_Settings.Location[locationBlockIndex][0]);
                if(!QuestExists(m_Settings.Location[locationBlockIndex][0].ToVector(), QuestName) && m_Settings.Location[locationBlockIndex].Count() == 1)
                {
                    
                    
                    Print("|||||||||||||||PORTAL OBJECT CREATED|||||||||||||||");
                    Print(theDzrQuestTrigger);
                    Print(m_Settings.Title);
                    Print(m_Settings.TeleportTo[0]);
                    Print("|||||||||||||||||||||||||||||||||||||||||||||||||||");
                    //theDzrQuestTrigger.SetQuestName(arg1);
                    //theDzrQuestTrigger.SetPrivate(arg2.ToInt());
                    //d_DzrQuestTrigger.SetSynchDirty();
                    //d_DzrQuestTrigger.SetLifetime(3888000);
                    //AddDzrQuestSrc(arg2, arg1, arg3);
                    //TODO: Hardcoded [locationBlockIndex]
                    
                    theDzrQuestTrigger.SetQuestName(DzrQuestFolder);
                    
                    theDzrQuestTrigger.SetDzrQuestID(qID);
                    
                    Param1<int> p = new Param1<int>( qID );
                    GetRPCManager().SendRPC( "dzr_dquests", "RPCSetDzrQuestID", p, true );
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ActionName.txt", FileContents);
                    theDzrQuestTrigger.SetDzrQuestActionName(m_Settings.ActionName);
                    
                    Param1<int> pinst = new Param1<int>( m_Settings.InstantActivation.ToInt() );
                    GetRPCManager().SendRPC( "dzr_dquests", "SetInstant", pinst, true );
                    theDzrQuestTrigger.SetInstant(m_Settings.InstantActivation.ToInt());
                    
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"LoadingScreenDelay.txt", FileContents);
                    int tTotalDelay = m_Settings.DelayBeforeTeleport.ToInt() + m_Settings.DelayAfterTeleport.ToInt();
                    theDzrQuestTrigger.SetLoadingScreenDelay( tTotalDelay.ToString() );
                    // theDzrQuestTrigger.SetLoadingScreenDelayAfter(m_Settings.DelayAfterTeleport);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"LoadingScreenDelayWhileTeleporting.txt", FileContents);
                    // theDzrQuestTrigger.SetLoadingScreenDelayWhileTeleporting(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Description1.txt", FileContents);
                    theDzrQuestTrigger.SetDescription1(m_Settings.Text1);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Description2.txt", FileContents);
                    theDzrQuestTrigger.SetDescription2(m_Settings.Text2);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Description3.txt", FileContents);
                    theDzrQuestTrigger.SetDescription3(m_Settings.Text3);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Target.txt", FileContents);
                    //TODO: Hardcoded teleport coords.
                    //theDzrQuestTrigger.SetTarget(m_Settings.TeleportTo[locationBlockIndex][0]);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"isPrivate.txt", FileContents);
                    //theDzrQuestTrigger.SetisPrivate(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"TargetOrientation.txt", FileContents);
                    //theDzrQuestTrigger.SetTargetOrientation(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    
                    
                    
                    //d_DzrQuestTrigger.SetOrientation(Vector(Math.RandomInt(0, 360), 0, 0));
                    
                    //NPC
                    //ClearNPCAtPos(QuestPosition.ToVector());
                    // CarScript tempCar;
                    // tempCar.ClearNPCVehicles();
                    
                    
                    //find all npc in array
                    
                    //TODO: The first location is hardcoded until the schedule is on
                    Print("---------------PRE NPC PROTO ----------------");
                    Print(m_Settings.Title);
                    //Print(m_Settings.EnableSchedule);
                    // find related files
                    
                    int npcBlockIndex = 0;
                    
                    //TODO: Hardcoded array index
                    //QuestsSettingsArray[questID].Location[0][0].ToVector()
                    foreach(ref TStringArray aLocation : m_Settings.Location)
                    {
                        
                        foreach( string anNPCt : m_Settings.NPC[npcBlockIndex])
                        {
                            Print("________m_Settings.NPC["+npcBlockIndex+"]_________");
                            Print(anNPCt);
                            Print("_________________");
                        }
                        
                        vector qPos = m_Settings.Location[locationBlockIndex][0].ToVector();
                        d_DzrQuestTrigger.SetPosition(qPos);
                        
                        Print("m_Settings.Location[npcBlockIndex][0]");
                        Print(m_Settings.Location[npcBlockIndex][0]);
                        Print(m_Settings.NPC[npcBlockIndex]);
                        Print(aLocation[0]);
                        Print(aLocation[0][0]);
                        
                        if(m_Settings.EnableSchedule[npcBlockIndex] == "-1")
                        {
                            //TODO: Hardcoded array index
                            
                            int settingsIndex = 0;
                            ref TStringArray theNPCs = m_Settings.NPC[npcBlockIndex];
                            foreach( string anNPC : theNPCs)
                            {
                                Print("******************");
                                Print(theNPCs);
                                Print(m_Settings.NPC[npcBlockIndex][0]);
                                Print(m_Settings.NPC[npcBlockIndex][1]);
                                Print(m_Settings.LocationNPC[npcBlockIndex][0]);
                                Print("**");
                                Print(m_Settings.LocationNPC);
                                Print(m_Settings.LocationNPC[npcBlockIndex]);
                                Print("**");
                                Print(m_Settings.OrientationNPC[npcBlockIndex][0]);
                                Print(m_Settings.OrientationNPC[npcBlockIndex][1]);
                                Print(anNPC);
                                Print("******************");
                                
                                string path = "$profile:DZR\\Quests\\"+DzrQuestFolder+"\\"+anNPC+".txt";
                                Print("Checking file: "+path);
                                if(FileExist(path))
                                {
                                    Print("NPC file exists: "+ anNPC + " ("+settingsIndex+")");
                                    // read files
                                    TStringArray npcSettings;
                                    DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+DzrQuestFolder, anNPC+".txt", npcSettings);
                                    Print("---------------NPC PROTO ----------------")
                                    //TODO: Make index for multiple locations
                                    Print("----------------CreateNPC now: "+anNPC);
                                    
                                    Print(m_Settings.LocationNPC);
                                    Print(m_Settings.LocationNPC[settingsIndex]);
                                    
                                    string npcLoc = m_Settings.LocationNPC[npcBlockIndex][settingsIndex];
                                    string npcOri = m_Settings.OrientationNPC[npcBlockIndex][settingsIndex];
                                    
                                    Print(npcLoc);
                                    Print(npcOri);
                                    
                                    
                                    //TODO: NPC default disabled
                                    CreateNPCFromSettingsFile("$profile:DZR\\Quests\\"+DzrQuestFolder,anNPC+".txt", npcLoc, npcOri);
                                    Print("---------------NPC PROTO ----------------")
                                    
                                }
                                else
                                {
                                    Print(path + " ------------NO FILE "+"("+anNPC+" - "+settingsIndex+")");
                                }
                                
                                
                                settingsIndex++;
                            }
                        }
                        else
                        {
                            Print("NPC is on schedule");
                        }
                        // send to npc creating function
                        npcBlockIndex++;
                    }
                    
                    
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    // DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Title.txt", FileContents);
                    theDzrQuestTrigger.SetTitle(m_Settings.Title);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"CheckItems.txt", FileContents);
                    //theDzrQuestTrigger.SetTitle(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"RemoveItems.txt", FileContents);
                    //theDzrQuestTrigger.SetTitle(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    // DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"GiveItems.txt", FileContents);
                    //theDzrQuestTrigger.SetTitle(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    // DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ActivationsPerPlayer.txt", FileContents, "-1");
                    //theDzrQuestTrigger.SetTitle(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    // DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ActivationLimitMessage.txt", FileContents, "Вы использовали портал максимальное количество раз.");
                    //theDzrQuestTrigger.SetTitle(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    // DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"QuestFunctions.txt", FileContents, "RestoreHealth\r\nBoostImmunity\r\nHeal\r\nRepairGear");
                    //theDzrQuestTrigger.SetTitle(FileContents);
                    //get portal attribute from file
                    
                    //get portal attribute from file
                    /*
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1.txt", FileContents, "YES");
                        theDzrQuestTrigger.SetOption1(FileContents );
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2.txt", FileContents, "NO");
                        theDzrQuestTrigger.SetOption2(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3.txt", FileContents, "BACK");
                        theDzrQuestTrigger.SetOption3(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4.txt", FileContents, "0");
                        theDzrQuestTrigger.SetOption3(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5.txt", FileContents, "0");
                        theDzrQuestTrigger.SetOption3(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6.txt", FileContents, "0");
                        
                        theDzrQuestTrigger.SetOption3(FileContents);
                        //get portal attribute from file
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1Message.txt", FileContents, "Good you've said YES");
                        //theDzrQuestTrigger.SetOption1(FileContents );
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2Message.txt", FileContents, "Sorry to hear NO");
                        //theDzrQuestTrigger.SetOption2(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3Message.txt", FileContents, "Yeah, let's start from scratch.");
                        //get portal attribute from file
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4Message.txt", FileContents, "0");
                        //get portal attribute from file
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5Message.txt", FileContents, "0");
                        //get portal attribute from file
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6Message.txt", FileContents, "0");
                        //get portal attribute from file
                        
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option1Functions.txt", FileContents, "RestoreHealth\r\nBoostImmunity\r\nHeal\r\nRepairGear");
                        //theDzrQuestTrigger.SetOption1(FileContents );
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option2Functions.txt", FileContents, "RestoreHealth\r\nBoostImmunity\r\nHeal\r\nRepairGear");
                        //theDzrQuestTrigger.SetOption2(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option3Functions.txt", FileContents, "RestoreHealth\r\nBoostImmunity\r\nHeal\r\nRepairGear");
                        //theDzrQuestTrigger.SetOption3(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option4Functions.txt", FileContents, "0");
                        //theDzrQuestTrigger.SetOption3(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option5Functions.txt", FileContents, "0");
                        //theDzrQuestTrigger.SetOption3(FileContents);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"Option6Functions.txt", FileContents, "0");
                        //theDzrQuestTrigger.SetOption3(FileContents);
                        //theDzrQuestTrigger.SetTitle(FileContents);
                        //get portal attribute from file
                        
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"RequiredQuests.txt", FileContents, "");
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"RestrictedQuests.txt", FileContents, DzrQuestFolder);
                        DZR_PTL_GetFile("$profile:DZR\\Quests\\"+DzrQuestFolder,"ResetQuests.txt", FileContents, "");
                        
                    */
                    
                    theDzrQuestTrigger.Update();
                    theDzrQuestTrigger.SetSynchDirty();
                    
                    index = DzrQuestsArray.Insert(theDzrQuestTrigger);
                    index = m_DzrQuestObjects.Insert(DzrQuestFolder, theDzrQuestTrigger);
                    qID++;
                    ////////Print("New portal at index "+index.ToString() );
                }
                else
                {
                    
                    Print("----------------------------------------------- QUEST NOT CREATED");
                }
                //Send1DzrQuestToClients(DzrQuestFolder, theDzrQuestTrigger);
            }	
        }
        //foreach
        /*
            for ( int i = 0; i < m_DzrQuestObjects.Count(); ++i )
            {
            string folder = m_DzrQuestObjects.GetKey( i );
            Object aDzrQuest = m_DzrQuestObjects.Get( folder );
            Send1DzrQuestToClients(aDzrQuest);
            }
        */
        
        //SendDzrQuestsToPlayers();
        CompileGlobalArryaRPC();
        
        
        //QScheduleC.CycleEvents();
        
        return DzrQuestsArray;
    }
    
    void StartSchedule()
    {
        Print("STARTING QUEST SCHEDULE");
        QScheduleC = new QSchedule();
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater( QScheduleC.CycleEvents, 10000, true );
    }
    
    string CopyInventory(PlayerBase m_Player)
    {
        //////Print("============ CopyInventory");
        string inventoryString;
        
        //inventoryString = m_Player.GetType();
        ItemBase item_in_hands = ItemBase.Cast( m_Player.GetHumanInventory().GetEntityInHands() );
        
        if ( item_in_hands )
        {
            //////Print(item_in_hands);
            inventoryString = inventoryString + "\r\n" + item_in_hands.GetType();
            
        }
        else
        {
            inventoryString = inventoryString + "\r\n0";
        }
        
        //GameInventory inv = PlayerBase.Cast(m_Player).GetInventory();
        array<EntityAI> itemsArray = new array<EntityAI>;
        m_Player.GetInventory().EnumerateInventory(InventoryTraversalType.PREORDER, itemsArray);
        for (int i = 0; i < itemsArray.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(itemsArray.Get(i));
            if(item && !item.IsPlayer() && item != item_in_hands)
            {
                //////Print(item);
                inventoryString = inventoryString + "\r\n" + item.GetType();
            }
        }
        //////Print("============ CopyInventory END");
        return inventoryString;
        
    }
    
    static void ProperCarSpawn(CarScript theCar, string NPC_pos, int Offset = 3000)
    {
        Print("     ------PROPER CARRING---------");
        
        
        theCar.OnDebugSpawn();
        //int Offset = 60000;
        int Delay = 0;
        //vector newPos = NPC_pos.ToVector() + "1 5 1";
        vector newPos = NPC_pos.ToVector() + "0 2 0";
        
        vector ori = GetGame().GetSurfaceOrientation(NPC_pos.ToVector()[0], NPC_pos.ToVector()[2]);
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.SetOrientation, Delay, false, ori);
        Delay = Delay + 2000;
        Delay = Offset;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.SetPosition, Delay, false, newPos);
        Delay = Delay + 3000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.PlaceOnSurface, Delay, false);
        
        Delay = Delay + 1000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.DisableSimulation, Delay, false, false);
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.CreateDynamicPhysics, Delay, false,PhxInteractionLayers.DYNAMICITEM);
        Delay = Delay + 1000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.EnableDynamicCCD, Delay, false,true);
        Delay = Delay + 1000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.SetDynamicPhysicsLifeTime, Delay, false,10.0);
        Delay = Delay + 9000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.EnableDynamicCCD, Delay, false,false);
        Delay = Delay + 2000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.SetSynchDirty, Delay, false);
        
        
        Delay = Delay + 1000;
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(theCar.DisableSimulation, Delay, false,true);
        Print(newPos);
        theCar.SetLifetime( 3888000 );
        theCar.SetSynchDirty();
        Print(theCar.GetLifetime());
        Print("     ------PROPER CARRING END---------");
    }
    
    void ClearNPCAtPos(vector pos)
    {
        ////////Print("▀█ GetNearestDzrQuest █▀");
        
        array<Object> nearestObjects = new array<Object>;
        
        GetGame().GetObjectsAtPosition(pos, 1, nearestObjects, null);
        
        //Print("████████████████ TRYING TO DELETE NPC ████████████████ ");
        for (int i = 0; i < nearestObjects.Count(); i++)
        {
            Object nearestEnt = nearestObjects.Get(i);
            //Print(nearestObjects.Get(i).GetType());
            
            PlayerBase man;
            if (nearestEnt && Class.CastTo(man, nearestEnt))
            {
                //Print("Found man");
                if(!man.GetIdentity())
                {
                    if(man.m_isDZRNPC)
                    {
                        //Print(nearestEnt);
                        //Print("█████DELETING████");
                        GetGame().ObjectDelete(nearestEnt);
                    }
                }
            }
            //CarScript car;
            
            /*
                if (nearestEnt && nearestEnt.IsInherited(CarScript))
                {
                //Print("Found car");
                //Print(nearestEnt);
                // if(car.m_DZR_Blocked)
                // {
                //Print("---------------- TRYING TO CLEAR NPC CARS--------------")
                //Print(CarScript.Cast(nearestEnt).m_DZR_Blocked)
                GetGame().ObjectDelete(nearestEnt);
                //Print("---------------- --------------")
                // }
                
                }
            */
        }
        
    }
    
    private TStringArray GetFoldersList(string m_PathToFolders)
    {
        ////////////Print("GetFoldersList");
        string	file_name;
        int file_attr;
        int		flags;
        TStringArray list = new TStringArray;
        
        
        
        string path_find_pattern = m_PathToFolders+"\\*"; //*/
        FindFileHandle file_handler = FindFile(path_find_pattern, file_name, file_attr, flags);
        
        bool found = true;
        while ( found )
        {				
            if ( file_attr == FileAttr.DIRECTORY )
            {
                ////////////Print("GetFoldersList IS DIR: "+file_name);
                list.Insert(file_name);
            }
            
            found = FindNextFile(file_handler, file_name, file_attr);
        }
        
        return list;
    }
    
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
            };
            //////////Print("saved_access: "+saved_access);
            CloseFile(fhandle);				
            return true;
        };
        return false;
    };
    
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
                };
            };
            
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
            };
        };
        //Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" ERROR!");
        return false;
    }
    
    void DZR_sendPlayerMessage(PlayerBase player, string message)	
    {
        Param1<string> Msgparam;
        Msgparam = new Param1<string>(message);
        // GetGame().RPCSingleParam(player, ERPCs.RPC_USER_ACTION_MESSAGE, Msgparam, true, player.GetIdentity());
    }
    void PrintC(PlayerBase player, string message)	
    {
        Param1<string> Msgparam;
        Msgparam = new Param1<string>(message);
        //GetGame().RPCSingleParam(player, ERPCs.RPC_USER_ACTION_MESSAGE, Msgparam, true, player.GetIdentity());
    }
    
    
    override void OnEvent(EventType eventTypeId, Param params) {
        super.OnEvent(eventTypeId,params);
        
        PlayerBase     player;
        PlayerIdentity identity;
        string null_content;
        
        Object obj;
        Car theCar;
        
        switch(eventTypeId)
        {
            
            if(eventTypeId == ChatMessageEventTypeID ) {
                
                ////////Print(params);
                
                ChatMessageEventParams chat_params = ChatMessageEventParams.Cast( params );
                
                array<Man> players = new array<Man>;
                GetGame().GetPlayers(players);
                for (int i = 0; i < players.Count(); ++i)
                {
                    identity = PlayerBase.Cast(players.Get(i)).GetIdentity();
                    if (identity.GetName() == chat_params.param2 || identity.GetPlainId() == chat_params.param2 || identity.GetPlayerId() == chat_params.param2.ToInt())
                    {
                        player = PlayerBase.Cast(players.Get(i));
                    }
                }
                
                //ChatMessageEventParams chat_params = ChatMessageEventParams.Cast( params );
                //Class.CastTo(player, chat_params.param1);
                //identity = player.GetIdentity();
                //////////Print(chat_params.param1);
                //////////Print(chat_params.param2);
                //////////Print(chat_params.param3);
                //////////Print(chat_params.param4);
                //////////Print(chat_params.param5);
                //string message = chat_params.param3;
                
                //GetDayZGame().GetFelixBotAPI().newChatMessage(message, chat_params.param2);
                
                //identity = player.GetIdentity();
                
                if (chat_params.param3 != "" && player) //trigger only when channel is Global == 0, Player Name does not equal to null and entered command
                {
                    TStringArray args = new TStringArray();
                    TStringArray portalAdmins = new TStringArray();
                    chat_params.param3.Split(" ", args);
                    string cmd = args[0];
                    string arg1 = args[1];
                    string arg2 = args[2];
                    int arg3 = args[3].ToInt();
                    string arg4 = args[4];
                    
                    // Read config
                    string portalAdmin;
                    bool IsAdmin = false;
                    /*
                        if ( DZR_PTL_GetFileLines("$profile:DZR\\Quests","QuestAdmins.txt", portalAdmins )
                        {
                        
                        ////////Print("portalAdmin: "+portalAdmin);
                        //////////Print("arg4: "+arg4);
                        if(identity)
                        {
                        if(portalAdmins.Find(identity.GetPlainId()) )
                        {
                        //addname 76561198000801794 Nommer 1 123
                        //Print(portalAdmins);
                        //removename 76561198000801794 Nommer 1 123
                        //Print("[dzr_dquests] Is admin: " + identity.GetPlainId());
                        IsAdmin = true;
                        }
                        else
                        {
                        ////////Print("[dzr_dquests] Sorry, not admin: " + identity.GetPlainId());
                        }
                        }
                        }
                        // Read config
                    */
                    if (player.m_isDzrQuestAdmin)
                    {
                        vector pos;
                        vector ori;
                        
                        switch(cmd) {
                            // Add or Remove admin tag
                            case "QuestAdd":
                            ////////Print("adding portal source: "+arg1+" "+arg2);
                            if(arg1 != "")
                            {
                                pos = player.GetPosition(); // 51.25 //.95 (1.15v)
                                ori = player.GetOrientation();
                                
                                string createMess1 = "";
                                string createMess2 = "";
                                
                                
                                
                                
                                
                                bool portalCreated = CreateOneDzrQuest(arg1, arg2, pos, ori);
                                
                                
                                
                                if(portalCreated)
                                {
                                    createMess1 = "DzrQuest "+arg1+" added";
                                    createMess2 = "Now set the Target by placing your character and typing: DzrQuestTarget "+arg1;
                                    
                                }
                                else
                                {    
                                    createMess1 = "DzrQuest Not Created";
                                    createMess2 = "Show logs to mod creator.";
                                }
                                NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay,createMess1 ,createMess2 , "set:dayz_gui image:icon_flag");    
                            }
                            else
                            {
                                NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "ERROR!", "DzrQuest name cannot be empty. Use portal_add NAME>", "set:dayz_gui image:exit");
                            }
                            break;
                            
                            case "QuestTarget":
                            
                            ////////Print("adding portal Target: "+arg1+" "+arg2);
                            pos = player.GetPosition(); // 51.25 //.95 (1.15v)
                            ori = player.GetOrientation();
                            
                            
                            if(FileExist("$profile:DZR\\Quests\\"+arg1+"\\Target.txt"))
                            {
                                DeleteFile("$profile:DZR\\Quests\\"+arg1+"\\Target.txt");
                            }
                            
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"Target.txt", null_content, pos[0].ToString()+" "+pos[1].ToString()+ " "+pos[2].ToString());
                            
                            
                            if(FileExist("$profile:DZR\\Quests\\"+arg1+"\\TargetOrientation.txt"))
                            {
                                DeleteFile("$profile:DZR\\Quests\\"+arg1+"\\TargetOrientation.txt");
                            }
                            
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"TargetOrientation.txt", null_content, ori[0].ToString());
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DzrQuest "+arg1+" Target added", pos.ToString() + " "+ori.ToString()+"\r\nNow Open your profiles/DZR/DzrQuests/"+arg1+" and configure your portal." , "set:dayz_gui image:settings");
                            //AddDzrQuestDest(arg2, arg1, arg3);
                            //RecreateDzrQuests();
                            
                            
                            break;
                            
                            case "QuestAddUser":
                            ////////Print("adding portal user: "+arg1+" "+arg2);
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1+"\\Users",arg2+".txt", null_content, "1");
                            break;
                            
                            case "QuestRemoveUser":
                            ////////Print("removing portal user: "+arg1+" "+arg2);
                            string m_Path = "$profile:DZR\\Quests\\"+arg1+"\\Users\\"+arg2+".txt";
                            if(FileExist(m_Path)) {
                                DeleteFile(m_Path);
                            }
                            break;
                            
                            case "QuestRemove":
                            ////////Print("removing portal: "+arg1);
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1, "", null_content, "0", true);
                            //DeleteFile("$profile:DZR\\Quests\\"+arg1);
                            //RemoveDzrQuest(arg2, arg1, arg3);
                            break;
                            
                            case "GoToQuest":
                            ////////Print("removing portal: "+arg1);
                            //string FileContents;
                            
                            //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"QuestPosition.txt", FileContents);
                            int questID = QuestIDs.Get(arg1);
                            Print(QuestIDs);
                            //TODO: Hardcoded index
                            player.SetPosition(QuestsSettingsArray[questID].Location[0][0].ToVector());
                            //DeleteFile("$profile:DZR\\Quests\\"+arg1);
                            //RemoveDzrQuest(arg2, arg1, arg3);
                            break;
                            
                            case "BD":
                            ////////Print("removing portal: "+arg1);
                            //string FileContents;
                            
                            //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"QuestPosition.txt", FileContents);
                            questID = QuestIDs.Get("BusDriver");
                            Print(QuestIDs);
                            //TODO: Hardcoded index
                            player.SetPosition(QuestsSettingsArray[questID].Location[0][0].ToVector());
                            //DeleteFile("$profile:DZR\\Quests\\"+arg1);
                            //RemoveDzrQuest(arg2, arg1, arg3);
                            break;
                            
                            case "S1":
                            ////////Print("removing portal: "+arg1);
                            string FileContents;
                            
                            //DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"QuestPosition.txt", FileContents);
                            questID = QuestIDs.Get("SewersEnter1");
                            Print(QuestIDs);
                            //TODO: Hardcoded index
                            player.SetPosition(QuestsSettingsArray[questID].Location[0][0].ToVector());
                            //DeleteFile("$profile:DZR\\Quests\\"+arg1);
                            //RemoveDzrQuest(arg2, arg1, arg3);
                            break;
                            
                            case "QuestsUpdate":
                            ////////Print("-------------------recreating dquests");
                            ClearNPCAtPos(player.GetPosition());
                            ref Param1<string> m_Data = new Param1<string>("GO TO "+arg1);
                            
                            RecreateDzrQuests();
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DZR Quests+" , "DzrQuests recreated", "set:dayz_gui image:settings");
                            break;
                            
                            case "QuestsReset":
                            ////////Print("-------------------recreating dquests");
                            for (int iii = 0; iii < players.Count(); ++iii)
                            {
                                identity = PlayerBase.Cast(players.Get(iii)).GetIdentity();
                                player = PlayerBase.Cast(players.Get(iii));
                                Param1<int> nullData;
                                GetRPCManager().SendRPC( "dzr_dquests", "ResetOptions", nullData, true, identity); 
                                GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", nullData, true, identity); 
                                GetRPCManager().SendRPC( "dzr_dquests", "ResetQuestNames", nullData, true, identity); 
                                //SendDzrQuestsToOnePlayer(identity);
                            }
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DZR Quests+" , "DzrQuests RESET", "set:dayz_gui image:settings");
                            break;
                            
                            case "TestNPC":
                            
                            pos = player.GetPosition();
                            string NPC_pos = "12590.4 180.673 4382.5";
                            string NPC_ori = "106.16 0 0";
                            
                            obj = GetGame().CreateObject( "Truck_01_Covered_Orange", "1000 1000 1000", false, false, true );
                            
                            
                            
                            
                            theCar = Car.Cast(obj);
                            theCar.DisableSimulation(true);
                            
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "Testing NPC" , theCar.ToString(), "set:dayz_gui image:settings");
                            
                            //CarScript.Cast(theCar).m_DZR_Blocked = true;
                            //CarScript.Cast(theCar).m_DZR_FreshSpawn = true;
                            theCar.GetInventory().CreateInInventory("DzrCarBlocker");
                            theCar.SetLifetime( 3888000 );
                            //theCar.DisableSimulation(true);
                            
                            //AllMissionCars.Insert(CarScript.Cast(theCar));
                            
                            theCar.SetOrientation(NPC_ori.ToVector());
                            
                            theCar.SetAllowDamage(false);
                            
                            
                            
                            
                            
                            
                            CarScript.Cast(theCar).DZR_SyncVal();
                            
                            
                            ProperCarSpawn(CarScript.Cast(theCar), NPC_pos, 1000);
                            
                            
                            break;
                            
                            case "QuestsSend":
                            ////////Print("-------------------recreating dquests");
                            for (int ii = 0; ii < players.Count(); ++ii)
                            {
                                identity = PlayerBase.Cast(players.Get(ii)).GetIdentity();
                                player = PlayerBase.Cast(players.Get(ii));
                                // Param1<int> nullData;
                                // GetRPCManager().SendRPC( "dzr_dquests", "ResetOptions", nullData, true, identity); 
                                // GetRPCManager().SendRPC( "dzr_dquests", "ResetDzrQuestPermissions", nullData, true, identity); 
                                // GetRPCManager().SendRPC( "dzr_dquests", "ResetQuestNames", nullData, true, identity); 
                                SendDzrQuestsToOnePlayer(identity);
                            }
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DZR Quests+" , "DzrQuests SENT", "set:dayz_gui image:settings");
                            break;
                            
                            case "QuestsDebug":
                            Param1<int>nullParam;
                            GetRPCManager().SendRPC( "dzr_dquests", "DzrQuestsDebug", nullParam, true);
                            DzrQuestsDebug();
                            break;
                            
                            case "QuestData":
                            Param1<int> m_DzrQuestData = new Param1<int>(arg1.ToInt());
                            GetRPCManager().SendRPC( "dzr_dquests", "DzrQuestData", m_DzrQuestData, true, identity);
                            //DzrQuestsDebug();
                            break;
                            
                            case "QuestPlayerData":
                            string theDataName = player.GetQuestNames(arg1.ToInt());
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DZR Quests+" , theDataName, "set:dayz_gui image:settings");
                            //GetRPCManager().SendRPC( "dzr_dquests", "DzrQuestData", m_DzrQuestData, true, identity);
                            //DzrQuestsDebug();
                            break;
                            
                            case "QuestNPC":
                            
                            pos = player.GetPosition(); // 51.25 //.95 (1.15v)
                            ori = player.GetOrientation();                                                            
                            
                            if(FileExist("$profile:DZR\\Quests\\"+arg1+"\\NPC.txt"))
                            {
                                DeleteFile("$profile:DZR\\Quests\\"+arg1+"\\NPC.txt");
                            }
                            
                            
                            string inventory = CopyInventory(player);
                            
                            
                            
                            ////////Print("-------------------recreating dquests");
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"NPC.txt", FileContents, player.GetType()+"\r\n"+pos[0].ToString()+" "+pos[1].ToString()+" "+pos[2].ToString()+"\r\n"+ ori[0].ToString()+inventory);
                            //ref Param1<string> m_Data = new Param1<string>("GO TO "+arg1);
                            //GetRPCManager().SendRPC( "dzr_dquests", "ResetQuests", m_Data, true, player.GetIdentity() );
                            CreateNPCFromFolder(arg1);
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DZR Quests+" , "NPC created", "set:dayz_gui image:settings");
                            break;
                            
                            case "QuestPosition":
                            
                            //////////Print("adding portal Target: "+arg1+" "+arg2);
                            pos = player.GetPosition(); // 51.25 //.95 (1.15v)
                            ori = player.GetOrientation();
                            
                            
                            if(FileExist("$profile:DZR\\Quests\\"+arg1+"\\QuestPosition.txt"))
                            {
                                DeleteFile("$profile:DZR\\Quests\\"+arg1+"\\QuestPosition.txt");
                            }
                            
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"QuestPosition.txt", null_content, pos[0].ToString()+" "+pos[1].ToString()+ " "+pos[2].ToString());
                            
                            
                            
                            
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DzrQuest "+arg1+" relocated", pos[0].ToString()+" "+pos[1].ToString()+ " "+pos[2].ToString() , "set:dayz_gui image:settings");
                            //AddDzrQuestDest(arg2, arg1, arg3);
                            RecreateDzrQuests();
                            
                            
                            break;
                            
                            
                            
                            case "ClearNPCVehicles":
                            
                            
                            CarScript tempCar;
                            tempCar.ClearNPCVehicles();
                            
                            break;
                            
                            case "QuestNPCPosition":
                            
                            pos = player.GetPosition(); // 51.25 //.95 (1.15v)
                            ori = player.GetOrientation();                                                            
                            
                            
                            //////////Print("-------------------recreating dquests");
                            TStringArray FileContentsArray = new TStringArray;
                            DZR_PTL_GetFileLines("$profile:DZR\\Quests\\"+arg1,"NPC.txt", FileContentsArray);
                            string oldPos = FileContentsArray.Get(1);
                            ClearNPCAtPos(oldPos.ToVector());
                            if(FileExist("$profile:DZR\\Quests\\"+arg1+"\\NPC.txt"))
                            {
                                DeleteFile("$profile:DZR\\Quests\\"+arg1+"\\NPC.txt");
                            }
                            
                            string NewContents;
                            
                            NewContents = NewContents + FileContentsArray.Get(0);
                            NewContents = NewContents + "\r\n"+pos[0].ToString()+" "+pos[1].ToString()+" "+pos[2].ToString()+"\r\n"+ ori[0].ToString();
                            
                            for ( int pri = 3; pri < FileContentsArray.Count(); ++pri )
                            {
                                NewContents = NewContents + "\r\n"+FileContentsArray.Get(pri);
                            }
                            
                            DZR_PTL_GetFile("$profile:DZR\\Quests\\"+arg1,"NPC.txt", FileContents, NewContents);                        
                            
                            CreateNPCFromFolder(arg1);
                            NotificationSystem.SendNotificationToPlayerExtended(player, m_MsgDelay, "DZR Quests+" , "NPC relocated", "set:dayz_gui image:settings");
                            break;
                        }
                    }
                    
                }
                
                
            }
        }
    }
}	
