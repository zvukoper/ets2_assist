class DzrQuestNPCAttribute extends Inventory_Base
{
    override bool DisableVicinityIcon()
	{
		return false;
    };
    
    override bool IsTakeable()
    {
		return true;
    }
};

class DzrQuestTrigger extends Inventory_Base
{
	
	int 	m_DisabledBySchedule = 0;
    
	int 	m_QuestID;
	string 	m_QuestActionName;
	string 	m_LoadingScreenDelay;
	string 	m_LoadingScreenDelayWhileTeleporting;
	string 	m_Description1;
	string 	m_Description2;
	string 	m_Description3;
	string 	m_Target;
	string 	m_QuestName;
	string 	m_isPrivate;
	string 	m_TargetOrientation;
	string 	m_QuestPosition;
	string 	m_Title;
	string 	m_Option1;
	string 	m_Option2;
	string 	m_Option3;
	string 	m_Option4;
	string 	m_Option5;
	string 	m_Option6;
	bool 	m_Instant = false;
    DzrQuestTriggerSettings m_Settings;
    
	array<string> AllowedUsers;
    
    
	static ref array<DzrQuestTrigger> dzr_AllTriggers = new array<DzrQuestTrigger>();
    
	ref array<DzrQuestTrigger> selfArray = new array<DzrQuestTrigger>();
	int selfID;
	
	void DzrQuestTrigger()
	{
		GetRPCManager().AddRPC( "dzr_dquests", "SetDzrQuestActionName", this, SingleplayerExecutionType.Both );
		GetRPCManager().AddRPC( "dzr_dquests", "SetInstant", this, SingleplayerExecutionType.Both );
		GetRPCManager().AddRPC( "dzr_dquests", "RPCSetDzrQuestID", this, SingleplayerExecutionType.Both );
		RegisterNetSyncVariableInt("m_QuestID");
        SetTakeable( false );
        selfID = selfArray.Insert(this);
    }
    
	
	void ~DzrQuestTrigger()
	{
        if (dzr_AllTriggers)
        dzr_AllTriggers.Remove(selfID);
    }
    
    void SetNewPosition(string PID, vector newpos)
    {
        Print("Setting new position for a trigger ------------------------- PID: "+PID);
        Print("Current PID ------------------------- PID: "+GetMyPID());
        
        if(GetMyPID() == PID)
        {
            Print("Found trigger ------------------------- PID: "+PID);
            vector old_pos = this.GetPosition();
            Print("Setting position trigger at "+old_pos+" to newpos: "+newpos);
            this.SetPosition(newpos);
            vector updated_pos = this.GetPosition();
            Print("updated_pos: "+updated_pos);
        }
        
        Print("_______________");
        Print(dzr_AllTriggers);
        Print("_______________");
    }
    
    string GetMyPID()
    {
        int b1, b2, b3, b4;
        this.GetPersistentID( b1, b2, b3, b4);
        return b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
    }
    
    void setSettings(DzrQuestTriggerSettings ExternalSettings)
    {
        m_Settings = ExternalSettings;
        Print("Setting ------------------------- ExternalSettings");
        Print(ExternalSettings);
        Print("Setting ------------------------- ExternalSettings");
        
    }
    
	override bool DisableVicinityIcon()
	{
		return true;
    };
    
    override bool IsTakeable()
    {
		return false;
    }
    
    void SetOption1(string value)
    {
        m_Option1 = value;
    }
    
    string GetOption1()
    {
        return m_Option1;
    }
    
    void SetOption2(string value)
    {
        m_Option2 = value;
    }
    
    string GetOption2()
    {
        return m_Option2;
    }
    
    void SetOption3(string value)
    {
        m_Option3 = value;
    }
    
    string GetOption3()
    {
        return m_Option3;
    }
    
    void SetOption4(string value)
    {
        m_Option4 = value;
    }
    
    string GetOption4()
    {
        return m_Option4;
    }
    
    void SetOption5(string value)
    {
        m_Option5 = value;
    }
    
    string GetOption5()
    {
        return m_Option5;
    }
    
    void SetOption6(string value)
    {
        m_Option6 = value;
    }
    
    string GetOption6()
    {
        return m_Option3;
    }
    
    override void OnStoreSave(ParamsWriteContext ctx) {
        // auto writeData = new Param1<ref array<string>>(arrayGuests);
        
        super.OnStoreSave(ctx);
        ctx.Write(m_QuestID);
    }
    
    override bool OnStoreLoad(ParamsReadContext ctx, int version) {
        Param1<ref array<string>> readData;
        
        if (!super.OnStoreLoad(ctx, version)) {
            return false;
        }
        if (!ctx.Read(m_QuestID)) {
            return false;
        }
        
        return true;
    }
    
	void SetDzrQuestActionName2(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
		////////Print("DzrQuest action name from server");
		ref Param1<string> data;
        if ( !ctx.Read( data ) )
		{
			return;	
        }
		
		
        if (type == CallType.Server)
        {
            if (data.param1)
            {
				//ref Param2<PlayerIdentity, string> m_Data = new Param2<PlayerIdentity, string>(data.param1, GetCharacterName());
				////////Print("Setting name: "+data.param1+" for "+m_QuestName);
				m_QuestActionName = data.param1;
				
            }
        }
    }
    
	bool DZR_PTL_GetFile(string folderPath, string fileName, out string fileContent, string defaults = "0", bool toDelete = false)
	{
		string m_TxtFileName = fileName;
		FileHandle fhandle;
        string pth;
        FileHandle file;
        
        if (defaults != "0" && FileExist(folderPath +"\\"+ fileName))
        {
            pth = folderPath +"\\"+ fileName;
            file = OpenFile(pth, FileMode.WRITE);
			
			if (file != 0)
			{
				FPrintln(file, defaults);
				CloseFile(file);
				//file
				fhandle	=	OpenFile(folderPath +"\\"+ fileName, FileMode.READ);
				//string server_id;
				FGets( file,  fileContent );
				
				//Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ fileName+" is OK! Contents: "+fileContent);
				CloseFile(file);
				
            }
        }
        
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
						////////Print("Could not make dirs from path: " + path);
						return false;
                    }
                }
				else
				{
					DeleteFile(path);
					////////Print("Deleted folder: "+path);
                }
            }
			
			pth = folderPath +"\\"+ m_TxtFileName;
            file = OpenFile(pth, FileMode.WRITE);
			
			if (file != 0 && !toDelete)
			{
				FPrintln(file, defaults);
				CloseFile(file);
				//file
				fhandle	=	OpenFile(folderPath +"\\"+ m_TxtFileName, FileMode.READ);
				//string server_id;
				FGets( file,  fileContent );
				
				////////Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
				CloseFile(file);
				
            }
        }
		////////Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" ERROR!");
		return false;
    }
	
	void AddAllowedUser(string SteamID)
	{
		AllowedUsers.Insert(SteamID);
    }
	
	//attribute
	string GetDzrQuestActionName()
	{
		return m_QuestActionName;
    }
	
	void SetDzrQuestActionName(string theValue)
	{
		m_QuestActionName = theValue;
		////////Print("[dzr_dquests] :::  Set portal name "+theValue+" index: "+m_QuestID);
    }

	
	void SetInstant(bool val)
	{

		m_Instant = val;
		////////Print("[dzr_dquests] :::  Set portal name "+theValue+" index: "+m_QuestID);
    }

	void SetInstant(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
		////////Print("DzrQuest action name from server");
		ref Param1<bool> data;
        if ( !ctx.Read( data ) )
		{
			return;	
        }
		m_Instant = data.param1;
		////////Print("[dzr_dquests] :::  Set portal name "+theValue+" index: "+m_QuestID);
    }
	//attribute

	//attribute
	
	//attribute
	string GetLoadingScreenDelay()
	{
		return m_LoadingScreenDelay;
    }
	
	void SetLoadingScreenDelay(string theValue)
	{
		m_LoadingScreenDelay = theValue;
    }
	//attribute
	
	//attribute
	string GetLoadingScreenDelayWhileTeleporting()
	{
		return m_LoadingScreenDelayWhileTeleporting;
    }
	
	void SetLoadingScreenDelayWhileTeleporting(string theValue)
	{
		m_LoadingScreenDelayWhileTeleporting = theValue;
    }
	//attribute
	
	//attribute
	string GetDescription1()
	{
		return m_Description1;
    }
	
	void SetDescription1(string theValue)
	{
		m_Description1 = theValue;
    }
	//attribute
	
	//attribute
	string GetDescription2()
	{
		return m_Description2;
    }
	
	void SetDescription2(string theValue)
	{
		m_Description2 = theValue;
    }
	//attribute
	
	//attribute
	string GetDescription3()
	{
		return m_Description3;
    }
	
	void SetDescription3(string theValue)
	{
		m_Description3 = theValue;
    }
	//attribute
	
	//attribute
	string GetTarget()
	{
		return m_Target;
    }
	
	void SetTarget(string theValue)
	{
		m_Target = theValue;
    }
	//attribute
	
	//attribute
	int GetDzrQuestID()
	{
		return m_QuestID;
    }
	
	//attribute
	int IsInstant()
	{
		return m_Instant;
    }
	
	void SetDzrQuestID(int val)
	{
		m_QuestID = val;
    }
	//attribute
	
	//attribute
	int DisabledBySchedule()
	{
		return m_DisabledBySchedule;
    }
	
	void SetDisabledBySchedule(int val)
	{
		m_DisabledBySchedule = val;
    }
	//attribute
	
    protected void SyncDzrQuest()
    {
        
        
        
        if (!GetGame().IsServer() || !GetGame().IsMultiplayer()) { return; }
        
        
        
		if ( GetGame().IsServer() )
		{
			Param1<int> p = new Param1<int>( m_QuestID );
            GetRPCManager().SendRPC( "dzr_dquests", "RPCSetDzrQuestID", p, true );
            
            SetSynchDirty();
            //UpdateVisuals();
			//GetGame().RPCSingleParam( this, ERPCs.RPC_BS_SKINNED_STATE, p, true );
        }
    }
    
    void RPCSetDzrQuestID(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
    {
        Param1<int> params;
        if (!ctx.Read(params))
        return;
        int RPC_DzrQuestID = params.param1;
        if ( GetGame().IsClient() )
		{
            //SetDzrQuestID(RPC_DzrQuestID);
            m_QuestID = RPC_DzrQuestID;
            SetSynchDirty();
        }
        
        
        
        if(GetGame().IsClient())
        {
            ////Print("|||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||| CLIENT SetDzrQuestID ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||");
            ////Print(RPC_DzrQuestID);
            ////Print(m_QuestID);
        }
        else
        {
            ////Print("|||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||| SERVER SetDzrQuestID ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||");
            ////Print(RPC_DzrQuestID);
            ////Print(m_QuestID);
        }
        
    }
    
	//attribute
	string GetQuestName()
	{
		return m_QuestName;
    }
	
	void SetQuestName(string theValue)
	{
		m_QuestName = theValue;
    }
	//attribute
	
	//attribute
	string GetisPrivate()
	{
		return m_isPrivate;
    }
	
	void SetisPrivate(string theValue)
	{
		m_isPrivate = theValue;
    }
	//attribute
	
	//attribute
	string GetTargetOrientation()
	{
		return m_TargetOrientation;
    }
	
	void SetTargetOrientation(string theValue)
	{
		m_TargetOrientation = theValue;
    }
	//attribute
	
	//attribute
	string GetQuestPosition()
	{
		return m_QuestPosition;
    }
	
	void SetQuestPosition(string theValue)
	{
		m_QuestPosition = theValue;
    }
	//attribute
	
	//attribute
	string GetTitle()
	{
		return m_Title;
    }
	
	void SetTitle(string theValue = "ERROR: Empty title")
	{
		m_Title = theValue;
    }
	//attribute
	
	
	bool isPlayerAllowed(string PlainID)
	{
		if(FileExist( "$profile:DZR/DzrQuests/"+m_QuestName+"/AllowedPlayers/" + PlainID ))
		{
			return true;	
        }
		return false;
    }
    
}    

class DzrQuestDummy  extends DzrQuestTrigger
{
}