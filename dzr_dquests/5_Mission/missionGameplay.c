

modded class MissionGameplay
{
    
	//protected Widget m_DZRDzrQuests_root;	
	protected MultilineTextWidget dzrp_bstext;
    
    ref DzrQuestLoading2 m_DZRDzrQuests_root2;
    ref DzrQuestLoading m_DZRDzrQuests_root;
    
    Widget r_MainAnimation;
    MultilineTextWidget r_Header;
    MultilineTextWidget r_Description;
    MultilineTextWidget r_AdditionalText;
    ImageWidget r_BackgroundImage;
    TextWidget CloseInfo;
    bool DebugMode;
    bool m_DzrQuestLoadingShown = false;
    
    ref WidgetFadeTimer m_LoadingTimer = new WidgetFadeTimer();
    
	string dzrp_FadeMode = "";
	
	void MissionGameplay()
	{
		Print("[dzr_dquests][Client] ::: MissionGameplay ::: Initiated");
		GetRPCManager().AddRPC( "dzr_dquests", "RequestLoadingScreen", this, SingleplayerExecutionType.Both );	
		GetRPCManager().AddRPC( "dzr_dquests", "PassLoadingScreenResources_RPC", this, SingleplayerExecutionType.Both );	
		GetRPCManager().AddRPC( "dzr_dquests", "SetPlayerActiveLayout", this, SingleplayerExecutionType.Both );	
        
		GetRPCManager().AddRPC( "dzr_dquests", "RequestLoadingScreenClose", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC( "dzr_dquests", "DZR_SetvInvisibleRPC", this, SingleplayerExecutionType.Both );
        GetRPCManager().AddRPC("dzr_dquests", "DZR_SetvInvisibleRPC_CLIENT", this, SingleplayerExecutionType.Both);
        
        /*
            m_DZRDzrQuests_root = GetGame().GetWorkspace().CreateWidgets( "dzr_dquests/GUI/layouts/dzr_quests_screen.layout" );
            r_MainAnimation = Widget.Cast( m_DZRDzrQuests_root.FindAnyWidget("r_MainAnimation") );
            r_BackgroundImage = Widget.Cast( m_DZRDzrQuests_root.FindAnyWidget("r_BackgroundImage") );
            r_Header = MultilineTextWidget.Cast( m_DZRDzrQuests_root.FindAnyWidget("r_Header") );
            r_Description = MultilineTextWidget.Cast( m_DZRDzrQuests_root.FindAnyWidget("r_Description") );
            r_AdditionalText = MultilineTextWidget.Cast( m_DZRDzrQuests_root.FindAnyWidget("r_AdditionalText") );
            CloseInfo = TextWidget.Cast( m_DZRDzrQuests_root.FindAnyWidget("CloseInfo") );
            CloseInfo.SetText("#dzrptl_close");
            m_DZRDzrQuests_root.Show(false);
        */
		//m_DZRDzrQuests_root.Show(true);
        
        string FileContents;
        DZR_PTL_GetFile("$profile:DZR\\Quests","Debug.txt", FileContents, "0");
        DZR_PTL_GetFile("$profile:DZR\\Quests","DebugTeleport.txt", FileContents, "1");
        DebugMode = FileContents.ToInt();
        
    }
    
    void NewObjectVisibility_RPC(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        
        //if (!GetGame().IsClient())
        //return;
        
        Param2<EntityAI, bool> params;
        Print("_______NewObjectVISIBILITY_RPC_________");
        
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
        objt.ClearFlags(FLAGS_INVIS, true);
        Print("objt.IsFlagSet(EntityFlags.VISIBLE)");
        if (objt.IsFlagSet(EntityFlags.VISIBLE))
        {
            Print(objt.IsFlagSet(EntityFlags.VISIBLE));
			objt.SetInvisible(true);
			objt.OnInvisibleSet(true);
			objt.SetInvisibleRecursive(true,objt);
        }
        else
        {
            Print("objt.IsFlagSet(EntityFlags.VISIBLE)");
            Print(objt.IsFlagSet(EntityFlags.VISIBLE));
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
    
    void PassLoadingScreenResources_RPC()
    {
        
    };
    
    void PrintD(string text)
    {
        if(DebugMode)
        {
            Print("[DZR_QUESTS Client Debug] :::: "+ text);
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
    
	void ~MissionGameplay()
	{
        /*
            m_DZRDzrQuests_root.Show(false);
            m_DZRDzrQuests_root = null;
        */
    }
    
    
    
	void RequestLoadingScreen(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
        PrintD("Trying RequestLoadingScreen NEW2");
        Param8<string , string , string, string, string, int, string, string> LoaderData;
		if ( !ctx.Read( LoaderData ) ) return;
        PrintD("RequestLoadingScreen2 173");
        m_DZRDzrQuests_root2 = DzrQuestLoading2.Cast( g_Game.GetUIManager().EnterScriptedMenu(618161518209438723, NULL) );
        m_DZRDzrQuests_root2.StartLoader(LoaderData);
        
        
        
        
    }
    
    
	void RequestLoadingScreen2(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
        PrintD("Trying RequestLoadingScreen NEW");
		Param9<string , string , string , string , string , float, string, string, string > data;
		if ( !ctx.Read( data ) ) return;
        PrintD("RequestLoadingScreen 70");
        m_DZRDzrQuests_root = DzrQuestLoading.Cast( g_Game.GetUIManager().EnterScriptedMenu(426181615182011219, NULL) );
        
		//TODO: try Create scripted menu, set it up, then Show\Enter
        
        PrintD("RequestLoadingScreen 74");
        string bs_text1 = data.param2;
        PrintD(bs_text1);
        string bs_text2 = data.param3;
        PrintD(bs_text2);
        string bs_text3 = data.param4;
        PrintD(bs_text3);
        
        //int bs_color = data.param5;
        
        float bs_delay = data.param6;
        
        //PrintD(bs_color);
        //Print(bs_delay);
        //bool aclose;
        //Print(bs_delay);
        
        string mImage= data.param7;
        string mSound= data.param8;
        string mLayout= data.param9;
        string aclose = data.param5;
        PrintD("RequestLoadingScreen 92");
        m_DZRDzrQuests_root.SetDzrLayout(mLayout);
        PrintD("RequestLoadingScreen 94");
        m_DZRDzrQuests_root.SetDelay(bs_delay);
        PrintD("RequestLoadingScreen 96");
        m_DZRDzrQuests_root.SetMedia(mImage, mSound);
        PrintD("RequestLoadingScreen 97");
        m_DZRDzrQuests_root.SetTexts(bs_text1, bs_text2, bs_text3, aclose );
        PrintD("RequestLoadingScreen 99");
        m_DZRDzrQuests_root.Run();
        PrintD("RequestLoadingScreen 102");
        
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
    
	void RequestLoadingScreenClose(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
        
        PrintTime("Trying RequestLoadingScreen NEW");
        m_DZRDzrQuests_root.AnimateExit();
        
        
    }
    
    static PlayerBase dzrGetPlayerByIdentity(PlayerIdentity identity)
    {
		if(identity)
		{
			int high, low;
			if (!GetGame().IsMultiplayer())
			{
				return PlayerBase.Cast(GetGame().GetPlayer());
            }
			
			GetGame().GetPlayerNetworkIDByIdentityID(identity.GetPlayerId(), low, high);
			return PlayerBase.Cast(GetGame().GetObjectByNetworkId(low, high));
        }
		return NULL;
    }
    
	void SetPlayerActiveLayout(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
        PrintD("Trying SetPlayerActiveLayout NEW");
		Param1<string> data;
		if ( !ctx.Read( data ) ) return;
        
        //PlayerBase player = dzrGetPlayerByIdentity(sender);
        
        PlayerBase player = PlayerBase.Cast( GetGame().GetPlayer() );
        if(player)
        {
            player.SetCurrentLayout(data.param1);
            Print(sender);
            Print(data);
            Print(data.param1);
            player.SetSynchDirty();
        }
        
    }
    
	
	void dzrp_fadeInPlayer(PlayerBase player){
        
		//m_DZRDzrQuests_root.Show(false);
        //player.COTSetInvisibility(JMInvisibilityType.DisableSimulation, false);
        
        player.SetAllowDamage(true);
        player.DisableSimulation(false);
        player.SetInvisible( false );
        m_DzrQuestLoadingShown = false;
    }
	
	void dzrp_fadeOutPlayer(PlayerBase player){
        
		//m_DZRDzrQuests_root.Show(true);
        
        m_DzrQuestLoadingShown = true;
        
        player.SetAllowDamage(false);
        player.DisableSimulation(true);
        player.SetInvisible( true );
        GetGame().GetUIManager().ShowUICursor( true );
        
    }
    
    void RayCastForPlayer()
	{
		PlayerBase player = PlayerBase.Cast( GetGame().GetPlayer() );
		
		vector rayStart = GetGame().GetCurrentCameraPosition() + GetGame().GetCurrentCameraDirection() * 0.47;
		vector rayEnd = GetGame().GetCurrentCameraPosition() + GetGame().GetCurrentCameraDirection() * (10 + 1);		
		Object m_CursorObject;
		float distance;
		vector position;
		vector contact_dir;
		int contact_component;
		ref set<Object> hitObjects = new set<Object>;
		
        
        DayZPhysics.RaycastRV(rayStart, rayEnd, position, contact_dir, contact_component, hitObjects, null, player, false, false, ObjIntersectView, 0.1);
		
		
		if ( hitObjects.Count() > 0 )
		{
			m_CursorObject = hitObjects.Get(0);
			
        } 
		else 
		{
			m_CursorObject = null;
        }
		
		GetGame().Chat(m_CursorObject.GetType(), "colorFriendly" );
		
        
    }
    
    void NearestTrigger(vector offset, bool AtCursor = false)
	{
        float TELEPORT_DISTANCE_MAX = 25;
        
        //GetGame().Chat(offset.ToString() + " - " + AtCursor , "colorAction" );
        
        ref array<Object> m_ObjectList = new array<Object>; 
        ref array<CargoBase> m_ObjectCargoList = new array<CargoBase>;
        
        PlayerBase player = PlayerBase.Cast( GetGame().GetPlayer() );
        vector newOri;
        vector oldOri;
        
        // vector rayStart = GetGame().GetCurrentCameraPosition() + GetGame().GetCurrentCameraDirection() * 0.47;
		// vector rayEnd = GetGame().GetCurrentCameraPosition() + GetGame().GetCurrentCameraDirection() * (10 + 1);
        
		GetGame().GetObjectsAtPosition3D( player.GetPosition() , 25.0 , m_ObjectList , m_ObjectCargoList );
		for ( int i = 0 ; i < m_ObjectList.Count(); i++ )
		{ 
            
			Object FoundObject = m_ObjectList.Get(i);
			if ( FoundObject.GetType() == "DzrQuestTrigger" )
			{	
                vector newCoord = FoundObject.GetPosition() + offset;
                oldOri = FoundObject.GetOrientation();
                
                //GetGame().Chat(oldOri.ToString() + " - " + newOri.ToString(), "colorFriendly" );
                // SEND RPC
                
                if(AtCursor)
                {
                    vector rayStart = GetGame().GetCurrentCameraPosition();
                    vector rayEnd = rayStart + GetGame().GetCurrentCameraDirection() * 1000;		
                    vector hitPos;
                    vector hitNormal;
                    int hitComponentIndex;		
                    DayZPhysics.RaycastRV(rayStart, rayEnd, hitPos, hitNormal, hitComponentIndex, NULL, NULL, player);
                    float distance = vector.Distance( player.GetPosition(), hitPos );
                    
                    if ( distance < TELEPORT_DISTANCE_MAX )
                    {
                        newCoord = hitPos;
                    }
                    else
                    {
                        newCoord =  rayStart + GetGame().GetCurrentCameraDirection() * TELEPORT_DISTANCE_MAX;
                    }
                    newOri = player.GetOrientation();
                }
                
                Param3<EntityAI, vector, vector> params =  new Param3<EntityAI, vector, vector>(EntityAI.Cast(FoundObject), newCoord, Vector(oldOri[0], newOri[1], oldOri[2]) );
                GetRPCManager().SendRPC( "dzr_dquests", "NewObjectPosition_RPC", params, true, player.GetIdentity() );
				break;
            }	 
        }	
		
		
		
        
    };
    
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
    
    void DZR_SetvInvisibleRPC_CLIENT(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Print("DZR_SetvInvisibleRPC_CLIENT");
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
            theai.ClearFlags(EntityFlags.VISIBLE|EntityFlags.SOLID|EntityFlags.ACTIVE, true);
        }
        else
        {
            GetGame().Chat("EntityFlags.VISIBLE = false", "colorFriendly" );
            Print("EntityFlags.VISIBLE = false");
            theai.SetFlags(EntityFlags.VISIBLE|EntityFlags.SOLID|EntityFlags.ACTIVE, true);
        };
        
        if(PlayerBase.Cast(theai))
        {
            PlayerBase.Cast(theai).m_isDZRVisible = is_visible;
            
            PlayerBase.Cast(theai).DZR_SetHeadInvisible(!is_visible);
            
        }
        
        GetGame().Chat(theai.GetType() + ": visible = "+is_visible, "colorFriendly" );
        Print(theai.GetType() + ": visible = "+is_visible);
        // theai.Update();
        // theai.SetSynchDirty();
        
        theai.SetInvisible(is_visible);
        theai.OnInvisibleSet(is_visible);
        theai.SetInvisibleRecursive(is_visible,theai);
        
        EntityAI tgt_parent = EntityAI.Cast( theai.GetParent() );
        
        
        
        // tgt_parent.SetInvisible(is_visible);
        // tgt_parent.OnInvisibleSet(is_visible);
        
        if(!is_visible)
        {
            theai.SetScale(0);
        }
        else
        {
            theai.SetScale(1);
        };
        
        // tgt_parent.SetInvisibleRecursive(is_visible,theai);
        
        theai.Update();
        theai.SetSynchDirty();
    }
    
    void DZRNearestObject(bool AtCursor = false, int setInvisible = -1)
	{
        float TELEPORT_DISTANCE_MAX = 25;
        
        //GetGame().Chat(offset.ToString() + " - " + AtCursor , "colorAction" );
        
        ref array<Object> m_ObjectList = new array<Object>; 
        ref array<CargoBase> m_ObjectCargoList = new array<CargoBase>;
        
        PlayerBase player = PlayerBase.Cast( GetGame().GetPlayer() );
        vector newCoord;
        vector oldOri;
        
        
        
        // vector rayStart = GetGame().GetCurrentCameraPosition() + GetGame().GetCurrentCameraDirection() * 0.47;
        // vector rayEnd = GetGame().GetCurrentCameraPosition() + GetGame().GetCurrentCameraDirection() * (10 + 1);
        //Param2 <EntityAI, bool> paramvis;
        GetGame().GetObjectsAtPosition3D( player.GetPosition() , 5.0 , m_ObjectList , m_ObjectCargoList );
        for ( int i = 0 ; i < m_ObjectList.Count(); i++ )
        { 
            
            Object FoundObject = m_ObjectList.Get(i);
            string theOType = FoundObject.GetType();
            //CarScript acar = GetCar(FoundObject);
            //PlayerBase aplayer = GetCar(FoundObject);
            //            Print(theOType);
            EntityAI theai =  EntityAI.Cast(FoundObject);
            
            
            if(theOType == "SurvivorM_Seth"   ||  theOType == "ExpansionBus" )
            {
                if(theai)
                {
                    Print(theOType);
                    GetGame().Chat(theOType, "colorFriendly" );
                    
                    /*
                        
                        bool is_visible = false;
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
                        
                        SetInvisible(is_visible);
                        OnInvisibleSet(is_visible);
                        SetInvisibleRecursive(is_visible,theai);
                        
                        theai.Update();
                        theai.SetSynchDirty();
                    */
                    bool is_visible = theai.IsFlagSet(EntityFlags.VISIBLE);
                    Param2<EntityAI, bool> paramvis = new Param2<EntityAI, bool>(theai, !is_visible);
                    GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_SERVER", paramvis, true );
                    
                }
            }
            /*
                if(theOType == "SurvivorM_Seth" )  // theOType == "ExpansionBus" ||  
                {
                
                Print("theOType == ExpansionBus ||  theOType == SurvivorM_Seth");
                
                //EntityAI FoundObjectEA = EntityAI.Cast(FoundObject);
                SurvivorBase FoundObjectEA = SurvivorBase.Cast(FoundObject);
                oldOri = FoundObject.GetOrientation();
                
                if(setInvisible == 1)
                {
                paramvis = new Param2<EntityAI, bool>(EntityAI.Cast(FoundObject), true);
                GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC", paramvis, true, player.GetIdentity() );
                break;
                //FoundObjectEA.SetInvisible(true);
                };
                
                if(setInvisible == 0)
                {
                paramvis = new Param2<EntityAI, bool>(EntityAI.Cast(FoundObject), false);
                GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC", paramvis, true, player.GetIdentity() );
                break;
                //FoundObjectEA.SetInvisible(false);
                };
                
                if(AtCursor)
                {
                vector rayStart = GetGame().GetCurrentCameraPosition();
                vector rayEnd = rayStart + GetGame().GetCurrentCameraDirection() * 1000;		
                vector hitPos;
                vector hitNormal;
                int hitComponentIndex;		
                DayZPhysics.RaycastRV(rayStart, rayEnd, hitPos, hitNormal, hitComponentIndex, NULL, NULL, player);
                float distance = vector.Distance( player.GetPosition(), hitPos );
                
                if ( distance < TELEPORT_DISTANCE_MAX )
                {
                newCoord = hitPos;
                }
                
                Param3<EntityAI, vector, vector> params =  new Param3<EntityAI, vector, vector>(EntityAI.Cast(FoundObject), newCoord, Vector(oldOri[0], oldOri[1], oldOri[2]) );
                GetRPCManager().SendRPC( "dzr_dquests", "NewObjectPosition_RPC", params, true, player.GetIdentity() );
                break;
                }
                }
            */
        }
    }	
    
    
    
    
    
    
    override void OnUpdate(float timeslice)
    {
        super.OnUpdate( timeslice );
        PlayerBase player = PlayerBase.Cast( GetGame().GetPlayer() );
        float offset = 0.25;
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerFwd" ).LocalPress() )
        {
            //GetGame().Chat("UAUIDZR_TriggerFwd", "colorFriendly" );
            NearestTrigger(Vector(offset, 0, 0));
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerBck" ).LocalPress() )
        {
            //GetGame().Chat("UAUIDZR_TriggerBck", "colorFriendly" );
            NearestTrigger(Vector(-offset, 0, 0));
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerUp" ).LocalPress() )
        {
            //GetGame().Chat("UAUIDZR_TriggerUp", "colorFriendly" );
            NearestTrigger(Vector(0, offset, 0));
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerDwn" ).LocalPress() )
        {
            //GetGame().Chat("UAUIDZR_TriggerDwn", "colorFriendly" );
            NearestTrigger(Vector(0, -offset, 0));
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerLt" ).LocalPress() )
        {
            //GetGame().Chat("UAUIDZR_TriggerLt", "colorFriendly" );
            NearestTrigger(Vector(0, 0, offset));
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerRt" ).LocalPress() )
        {
            //GetGame().Chat("UAUIDZR_TriggerRt", "colorFriendly" );
            NearestTrigger(Vector(0, 0, -offset));
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerSetInvis" ).LocalPress() )
        {
            GetGame().Chat("UAUIDZR_TriggerSetInvis", "colorFriendly" );
            DZRNearestObject(false, 0);
        };
        
        if ( GetUApi().GetInputByName( "UAUIDZR_TriggerSetVis" ).LocalPress() )
        {
            GetGame().Chat("UAUIDZR_TriggerSetVis", "colorFriendly" );
            DZRNearestObject(false, 1);
        };
        
        //CTRL+P Show name to all
        
        
        
    }	
    /*
        override void OnKeyPress(int key) {
        
        
        if (key == KeyCode.KC_ESCAPE) {
        GetUIManager().CloseAll();
        }
        }
    */
    
}
