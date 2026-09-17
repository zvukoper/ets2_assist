class QSchedule 
{
    bool debug_mode = true;
    int globalSeconds;
    
    //ref array<int> events = new array<int>;
    ref array<bool> aActive = new array<bool>;
    ref array<bool> aCreated = new array<bool>;
    ref array<bool> aDeleted = new array<bool>;
    ref TStringArray aQuestName = new TStringArray;
    ref TStringArray aQuestPID = new TStringArray;
    ref TStringArray aLocation = new TStringArray;
    ref array<TStringArray> aNPC = new array<TStringArray>;
    ref TStringArray aEnableSchedule = new TStringArray;
    ref TStringArray aTimeAvailable = new TStringArray;
    ref TStringArray aWeekAvailable = new TStringArray;
    ref TStringArray aDateAvailable = new TStringArray;
    ref array<TStringArray> aLocationNPC = new array<TStringArray>;
    ref array<TStringArray> aOrientationNPC = new array<TStringArray>;
    ref TStringArray aRemoveNPCWhenUnavailable = new TStringArray;
    
    void QSchedule()
    {
        if(debug_mode) { Print("========================================= QSchedule INIT ========================================="); };
        TStringArray aLocation = new TStringArray;
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater( this.CycleEvents, 10000, true );
        
    };
    
    void RegisterEvent(string Location, TStringArray NPC, string EnableSchedule, string TimeAvailable, string  WeekAvailable, string DateAvailable, TStringArray LocationNPC, TStringArray OrientationNPC, string RemoveNPCWhenUnavailable, string QuestName, string QuestPID)
    {
        if(debug_mode) { Print("--------- QSchedule RegisterEvent ======= "+Location+" ---------"); };
        //string repeatEvent = from_time +" : "+to_time;
        aLocation.Insert(Location);
        aNPC.Insert(NPC);
        
        aEnableSchedule.Insert(EnableSchedule);
        aTimeAvailable.Insert(TimeAvailable);
        aWeekAvailable.Insert(WeekAvailable);
        aDateAvailable.Insert(DateAvailable);
        
        aActive.Insert(false);
        aCreated.Insert(false);
        aDeleted.Insert(false);
        
        aLocationNPC.Insert(LocationNPC);
        aOrientationNPC.Insert(OrientationNPC);
        aQuestName.Insert(QuestName);
        
        aQuestPID.Insert(QuestPID);
        //GetPersistentID
        aRemoveNPCWhenUnavailable.Insert(RemoveNPCWhenUnavailable);
        
        //Print(events[0]);
        // SCRIPT       : array<string> qLocation = 0x0000000077fa4630 {'14447.6 526.152 460.851'}
        // SCRIPT       : array<string> qNPC = 0x0000000077fa49f0 {'BusDriverNPC','ExpansionBusNPC'}
        // SCRIPT       : string qEnableSchedule = '1'
        // SCRIPT       : string qTimeAvailable = 'Game 10:30-12:30'
        // SCRIPT       : string qWeekAvailable = 'Game 1 2 3 4 5 6 7'
        // SCRIPT       : string qDateAvailable = 'Game 19.12.23-01.05.24'
        // SCRIPT       : array<string> qLocationNPC = 0x0000000077fa4df0 {'2511.548584 190.159912 5285.504395','12590.3 180.683 4380.71'}
        // SCRIPT       : array<string> qOrientationNPC = NULL
        // SCRIPT       : string qRemoveNPCWhenUnavailable = ''
        
    };
    
    string GetPersistentID( EntityAI entity )
    {
        int b1, b2, b3, b4;
        entity.GetPersistentID( b1, b2, b3, b4);
        return b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
    }
    
    void CheckEvent()
    {
        
    };
    
    void IsEventWeekDay()
    {
        
    };
    
    void IsEventTime()
    {
        
    };
    
    void CycleEvents()
    {
        int ts_seconds = CF_Date.Now(false).GetTimestamp();
        //int ts_seconds2 = CF_Date.Now(true).GetTimestamp();
        string curdate = CF_Date.Now(false).DateToString();
        //string curdate2 =   CF_Date.Now(true).DateToString();
        
        //game date
        int year, month, day, hour, minute;
        GetGame().GetWorld().GetDate( year, month, day, hour, minute );
        
        //current y-m-d
        int dyear, dmonth, dday, dhour, dminute, dsecond;
        CF_Date.TimestampToDate(ts_seconds, year, month, day, dhour, dminute, dsecond);
        
        //current ymd but game time
        ts_seconds = CF_Date.Timestamp( year,  month,  day,  hour,  minute, 0);
        
        curdate = CF_Date.Epoch(ts_seconds).DateToString();
        
        //if(debug_mode) { Print("========================================= Cycling through events ==================== "+ts_seconds+"=:="+curdate+" ==================="); };
        //if(debug_mode) { Print("========================================= Cycling through events 2 ==================== "+ts_seconds2+"=:="+curdate2+" ==================="); };
        
        if(aEnableSchedule.Count() == 0)
        {
            if(debug_mode) { Print("----NO EVENTS---"); };
        }
        else
        {
            if(debug_mode) { 
                int i = 0;
                foreach( string Enabled : aEnableSchedule )
                {
                    if(Enabled == "1")
                    {
                        Print("--------- EVENT "+i+" ---------");
                        Print(aLocation[i]);
                        Print(aNPC[i]);
                        
                        Print(aEnableSchedule[i]);
                        
                        Print(aTimeAvailable[i]);
                        TStringArray TimeArr = new TStringArray; 
                        aTimeAvailable[i].Split(" ",TimeArr );
                        
                        TStringArray TimeArr2 = new TStringArray; 
                        TimeArr[1].Split("-",TimeArr2 );
                        
                        TStringArray FromArr = new TStringArray; 
                        TStringArray ToArr = new TStringArray; 
                        TimeArr2[0].Split(":", FromArr) ;
                        TimeArr2[1].Split(":", ToArr) ;
                        
                        string FromHour = FromArr[0]; 
                        string FromMinute = FromArr[1]; 
                        
                        string ToHour = ToArr[0]; 
                        string ToMinute = ToArr[1]; 
                        
                        
                        if(TimeArr[0] == "System")
                        {
                            //int curstamp = curdate.GetTimestamp();
                            
                            CF_Date fromdate = CF_Date.Now(true);
                            fromdate.SetHours(FromHour.ToInt());
                            fromdate.SetMinutes(FromMinute.ToInt());
                            
                            CF_Date todate = CF_Date.Now(true);
                            todate.SetHours(ToHour.ToInt());
                            todate.SetMinutes(ToMinute.ToInt());
                            
                            int fromts = fromdate.GetTimestamp();
                            int tots = todate.GetTimestamp();
                            
                            /*
                                Print("---TS---");
                                Print(ts_seconds);
                                Print(fromts);
                                Print(tots);
                            */
                            
                            
                            
                            
                            if( (ts_seconds > fromts) && (ts_seconds < tots) )
                            {
                                Print("]]]]]]]]]]]]]]]]]]]]] ACTIVE QUEST [[[[[[[[[[[[[[[[[[[");
                                aActive[i] = true;
                                
                                //create 
                                
                                if(!aDeleted[i] && !aCreated[i])
                                {
                                    ClearQuest(i);
                                    aCreated[i] = false;
                                    aDeleted[i] = true;
                                }
                                else
                                {
                                    if(!aCreated[i])
                                    {
                                        
                                        RelocateQuestObject(i);
                                        CreateNPCFromSettingsFile(i);
                                        aCreated[i] = true;
                                        aDeleted[i] = false;
                                    };
                                };
                            }
                            else
                            {
                                
                                if(!aDeleted[i])
                                {
                                    ClearQuest(i);
                                    aCreated[i] = false;
                                    aDeleted[i] = true;
                                };
                                aActive[i] = false;
                                
                                //clear npc
                                
                                
                            }
                            Print("---xx---");
                        }
                        
                        /*
                            Print(aWeekAvailable[i]);
                            Print(aDateAvailable[i]);
                            
                            Print(aLocationNPC[i]);
                            Print(aOrientationNPC[i]);
                            
                            Print(aRemoveNPCWhenUnavailable[i]);
                            Print("------ xxxxx ---------");
                        */
                    } 
                    else
                    {
                       // Print("Empty Event "+i);
                    };
                    i++;
                };
            };
            
        };
    };
    
    void RelocateQuestObject2(int id)
    {
        
        vector new_pos;
        //new_pos = aLocation[id];
        Print(aLocation[id]);
        
        /*
            new_pos = aLocation[id].ToVector();
            Print(new_pos);
            new_pos = aLocation[id][0] +" "+aLocation[id][1]+" "+aLocation[id][2] ;
            Print(new_pos);
            string PID = aQuestPID[id]; 
            
            DzrQuestTrigger.SetNewPosition(PID, newpos);
        */
    }
    
    void RelocateQuestObject(int id)
    {
        //TODO: Get objects at loc. Compare pids. Remove;
        array<Object> nearestObjects = new array<Object>;
        Print("██r██");
        vector new_pos = aLocation[id].ToVector();
        string PID = aQuestPID[id];
        Print(new_pos);
        Print(PID);
        
        //GetGame().GetObjectsAtPosition(current_pos, 1, nearestObjects, null);
        Object triggerOB = GetGame().CreateObject( "DzrQuestTrigger", vector.Zero, true, false, false );
        DzrQuestTrigger trigger = DzrQuestTrigger.Cast(triggerOB);
        trigger.SetNewPosition(PID, new_pos);
         GetGame().ObjectDelete(trigger);
        Print("████████████████ TRYING TO RELOCATE QUEST ████████████████ ");
        /*
            for (int i = 0; i < nearestObjects.Count(); i++)
            {
            Object nearestEnt = nearestObjects.Get(i);
            Print(nearestObjects.Get(i).GetType());
            
            
            if (nearestEnt && Class.CastTo(trigger, nearestEnt))
            {
            
            if(GetPersistentID(trigger) == PID)
            {
            
            Print("Found trigger "+GetPersistentID(trigger));
            
            Print(nearestEnt);
            nearestEnt.SetPosition(new_pos);
            }
            
            }
            
            Print("▀█ --------------- █▀");
            
            };
        */
    };
    
    void CreateNPCFromSettingsFile(int id)
    {
        Print("___________trying to creating npc "+id+"__________");
        string folder_root = "$profile:DZR\\Quests\\"
        string settingsPath = folder_root + aQuestName[id];
        
        //Print(settingsPath);
        
        TStringArray allNPCs = aNPC[id];
        Print(allNPCs);
        
        int npcCounter = 0;
        foreach ( string anNpc : allNPCs)
        {
            
            
            string fileName = anNpc+".txt";
            string pos = aLocationNPC[id][0] +" "+aLocationNPC[id][1]+" "+aLocationNPC[id][2] ;//[npcCounter];
            
            pos.Replace("<", "");
            pos.Replace(",", " ");
            pos.Replace("  ", " ");
            
            string ori = aOrientationNPC[id][npcCounter];
            
            Print(fileName);
            Print(pos);
            //Print(ori);
            
            //CREATE NPC FROM SETTINGS FILE ARRAY
            string FileContents;
            if(FileExist(settingsPath+"\\"+fileName))
            {
                MissionServer.DZR_PTL_GetFile(settingsPath, fileName, FileContents);
                Print(FileContents);
                ////////Print("trying to NPC "+DzrQuestFolder);
                //////Print(FileContents);
                if(FileContents != "" && FileContents != "0")
                {
                    
                    ////////Print("Not empty NPC");
                    TStringArray npcArray;
                    MissionServer.DZR_PTL_GetFileLines(settingsPath,fileName, npcArray);
                    //////Print("NPC array");
                    //////Print(npcArray);
                    //TODO: NPC Dummy override
                    string NPC_class = npcArray.Get(0);
                    
                    /*
                        if(NPC_class != "SurvivorM_Seth")
                        {
                        return;
                        };
                    */
                    //string NPC_class = "DzrQuestDummy"; 
                    //Print("Creating dummy instead of " +npcArray.Get(0));
                    
                    string NPC_pos = pos;
                    string NPC_ori = ori;
                    
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
                    ClearQuest(id);
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
                            MissionServer.DZR_PTL_GetFile("$profile:DZR\\Quests\\Cleanup",PID+".txt", FileContents, theObjEnt.ToString() );
                            
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
                        
                        MissionServer.AllMissionCars.Insert(CarScript.Cast(theCar));
                        
                        
                        theCar.SetAllowDamage(false);
                        //theCar.PlaceOnSurface();
                        
                        
                        CarScript.Cast(theCar).DZR_SyncVal();
                        // //Print(CarScript.Cast(theCar).m_DZR_Blocked);
                        
                        CarScript tempCar2;
                        tempCar2.ClearNPCVehicles();
                        
                        MissionServer.ProperCarSpawn( CarScript.Cast(theCar), NPC_pos);
                        
                        
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
            else
            {
                Print("Could not find "+settingsPath+"\\"+fileName);
            }
            npcCounter++;
        }
        Print("_________DONE Creating npc____________");
        //NPC
        //CREATE NPC FROM SETTINGS FILE ARRAY
    };
    
    void ClearQuest(int id)
    {
        
        if(aDeleted[id] == false)
        {
            Print("▀█DELETING npc "+id+"__________█▀");
            string folder_root = "$profile:DZR\\Quests\\"
            string settingsPath = folder_root + aQuestName[id];
            
            Print(settingsPath);
            vector qtpos = aLocation[id].ToVector();
            TStringArray allNPCs = aNPC[id];
            Print(allNPCs);
            array<Object> nearestObjects = new array<Object>;
            Object nearestEnt;
            // TRIGGER CLEAR
            GetGame().GetObjectsAtPosition(qtpos, 0.5, nearestObjects, null);
            
            Print("████████████████ TRYING TO DELETE trigger ████████████████ ");
            for (int i2 = 0;i2 < nearestObjects.Count(); i2++)
            {
                Object qnearestEnt = nearestObjects.Get(i2);
                Print(nearestObjects.Get(i2).GetType());
                
                DzrQuestTrigger trigger;
                if (qnearestEnt && DzrQuestTrigger.CastTo(trigger, nearestEnt))
                {
                    Print("Found trigger "+GetPersistentID(trigger));
                    
                    
                    if(GetPersistentID(trigger) == aQuestPID[id])
                    {
                        
                        Print("Found trigger "+GetPersistentID(trigger));
                        
                        Print(nearestEnt);
                        GetGame().ObjectDelete(nearestEnt);
                    };
                };
                
                
                
                Print("▀█ --------------- █▀");
            };
            
            int npcCounter = 0;
            foreach ( string anNpc : allNPCs)
            {
                
                
                string fileName = anNpc+".txt";
                string spos = aLocationNPC[id][0] +" "+aLocationNPC[id][1]+" "+aLocationNPC[id][2] ;//[npcCounter];//[npcCounter];
                //string sori = aOrientationNPC[id][npcCounter];
                
                spos.Replace("<", "");
                spos.Replace(",", " ");
                spos.Replace("  ", " ");
                
                Print("▀█ GetNearestNPC "+fileName+" ("+npcCounter+") at "+spos+" █▀");
                vector pos = spos.ToVector();
                
                Print(pos);
                GetGame().GetObjectsAtPosition(pos, 1, nearestObjects, null);
                
                //Print("████████████████ TRYING TO DELETE NPC ████████████████ ");
                for (int i = 0; i < nearestObjects.Count(); i++)
                {
                    nearestEnt = nearestObjects.Get(i);
                    Print(nearestObjects.Get(i).GetType());
                    
                    PlayerBase man;
                    if (nearestEnt && Class.CastTo(man, nearestEnt))
                    {
                        Print("Found man");
                        if(!man.GetIdentity())
                        {
                            if(man.m_isDZRNPC)
                            {
                                Print(nearestEnt);
                                Print("█████DELETING████");
                                GetGame().ObjectDelete(nearestEnt);
                                aCreated[id] = false;
                                aDeleted[id] = true;
                            };
                        };
                    };
                    
                    
                    CarScript car;
                    
                    
                    if (nearestEnt && nearestEnt.IsInherited(CarScript))
                    {
                        Print("Found car");
                        Print(nearestEnt);
                        if(CarScript.Cast(nearestEnt).IsDZRNPC())
                        {
                            Print("---------------- TRYING TO CLEAR NPC CARS--------------")
                            Print(CarScript.Cast(nearestEnt).IsDZRNPC())
                            GetGame().ObjectDelete(nearestEnt);
                            Print("---------------- --------------")
                        };
                        
                    };
                    
                    npcCounter++;  
                };
            };
            
        }
        else
        {
            
            Print("▀█ --------ALREADY DELETED------- █▀");
        };
    };
    
    string GetRealDay()
    {
        int year, month, day;	
        GetYearMonthDay(year, month, day);
        string date = day.ToStringLen(2) + "." + month.ToStringLen(2) + "." + year.ToStringLen(2);
        return date;
    };
    
    string GetRealTime()
    {
        int hour, minute, second;
        GetHourMinuteSecond(hour, minute, second);
        string time = hour.ToStringLen(2) + "." + minute.ToStringLen(2) + "." + second.ToStringLen(2);
        return time;
    }	
}                                                                    