/*
    class AttachActionData : ActionData
    {
	int m_AttSlot;
    }
*/

class ActionOptionBase: ActionSingleUseBase
{
    string QuestName;
    int thisOptionID;
    PlayerBase g_Player;
    
	void ActionOptionBase()
	{
        thisOptionID = -1;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
	override void CreateConditionComponents() 
	{
		m_ConditionItem = new CCINone;
        m_ConditionTarget = new CCTNone;
    } 
	
	override bool UseMainItem()
	{
		return false;
    }
	
	override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
		
		bool returner = false;
		g_Player = player;
        returner = player.ShowOptions();
        
        //////Print("==================================== player.GetSelectedOption() " +player.GetSelectedOption());
        
		if (returner)
        {
            
            QuestName = player.CurrentDzrQuest();
            int CurrentDzrQuestID = player.GetCurrentDzrQuestID();
            m_Text = GetOptionText(player, CurrentDzrQuestID);
            
            //int m_UserAllowed = player.GetDzrQuestPermissions( CurrentDzrQuestID );
            
            if(m_Text == "0" || m_Text == "" ) return false;

            if(player.GetSelectedOption() == thisOptionID)
            {
                m_Text = "[ " + m_Text + " ]";
            }
            else
            {
                if(player.GetSelectedOption() != 0 && player.GetSelectedOption() != 51)
                {
                    returner = false;
                }
            }
        }
        
        if ( GetGame().IsServer() && GetGame().IsMultiplayer() ) 
		return true;
        

		return returner;
    }
    
    string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return player.GetOptions1( CurrentDzrQuestID );
    }
    
	override void OnExecuteServer(ActionData action_data)
	{
        //Print("TRYING TO SELECT");
        
        ref Param1<int> m_Data = new Param1<int>(thisOptionID);
        
        //QuestName = action_data.m_Player.CurrentDzrQuest();
        
        string OptionMessage;
        
        // DZR_PTLFUNC.DZR_PTL_GetFile("$profile:DZR\\Quests\\"+QuestName+"\\","Option1Message.txt", OptionMessage);
        //NotificationSystem.SendNotificationToPlayerExtended(action_data.m_Player, 2, "Торговец", "Выбор 1", "set:dayz_gui image:open");
        
        GetRPCManager().SendRPC( "dzr_dquests", "SetSelectedOption", m_Data, true, g_Player.GetIdentity() ); 
        
        //Print("SELECTING OPTION: "+thisOptionID);

		
    }
    
    	override void OnExecuteClient(ActionData action_data)
	{
        action_data.m_Player.SetSelectedOption2(thisOptionID);
    }
	
}

class ActionOption1: ActionOptionBase
{
    void ActionOption1()
	{
        thisOptionID = 1;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
    override string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return player.GetOptions1( CurrentDzrQuestID );
    }
	
}


class ActionOption2: ActionOptionBase
{
    void ActionOption2()
	{
        thisOptionID = 2;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
    override string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return player.GetOptions2( CurrentDzrQuestID );
    }
	
}


class ActionOption3: ActionOptionBase
{
    void ActionOption3()
	{
        thisOptionID = 3;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
    override string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return  player.GetOptions3(CurrentDzrQuestID );
    }
    
}


class ActionOption4: ActionOptionBase
{
    void ActionOption4()
	{
        thisOptionID = 4;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
    override string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return  player.GetOptions4(CurrentDzrQuestID );
    }
    
}


class ActionOption5: ActionOptionBase
{
    void ActionOption5()
	{
        thisOptionID = 5;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
    override string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return  player.GetOptions5(CurrentDzrQuestID );
    }
    
}


class ActionOption6: ActionOptionBase
{
    void ActionOption6()
	{
        thisOptionID = 6;
        m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_Text = "Вариант "+ thisOptionID;
    }
    
    override string GetOptionText(PlayerBase player, int CurrentDzrQuestID)
    {
        return  player.GetOptions6(CurrentDzrQuestID);
    }
    
}



class DZR_PTLFUNC
{
    
    static bool DZR_PTL_GetFile(string folderPath, string fileName, out string fileContent, string defaults = "0", bool toDelete = false)
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
                
                //////Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ fileName+" is OK! Contents: "+fileContent);
                
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
            
            //////Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
            
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
                        //////Print("Could not make dirs from path: " + path);
                        
                        return false;
                    }
                }
                else
                {
                    DeleteFile(path);
                    
                    //////Print("Deleted folder: "+path);
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
                
                //////Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" is OK! Contents: "+fileContent);
                
                CloseFile(file);
                
            }
        }
        //////Print("[dzr_dquests] :::  Config "+folderPath +"\\"+ m_TxtFileName+" ERROR!");
        
        return false;
    }
    
}
