class ActionActivateDzrQuest: ActionInteractBase
{
	DzrQuestTrigger m_DzrQuestTrigger = NULL;
	string m_QuestActionName;
	string m_QuestName;
	bool portalAvailable = false;
	ref array<EntityAI> nearestDzrQuests;
    bool CallDelay = false;
    bool m_Instant = false;
	PlayerBase g_Player;
    
	/*
		#ifdef AMS_AdditionalMedicSupplies
		protected ref AMS_TestKitReportMenu m_Report1;
		#endif
    */
	
    ref array<DzrQuestTrigger> m_DzrQuests;
    
	void ActionActivateDzrQuest(ActionData action_data)
	{
		m_CommandUID = DayZPlayerConstants.CMD_ACTIONMOD_INTERACTONCE;
		m_StanceMask = DayZPlayerConstants.STANCEMASK_ERECT | DayZPlayerConstants.STANCEMASK_CROUCH;
        
		//GetRPCManager().AddRPC("dzr_dquests", "SetDzrQuestAttributesOnClient", this, SingleplayerExecutionType.Client);
        
		GetRPCManager().AddRPC("dzr_dquests", "SetUserAllowed", this, SingleplayerExecutionType.Client);
        
		//g_Player = action_data.m_Player;
        //GetRPCManager().AddRPC( "dzr_dquests", "Save1DzrQuestFromServer", this, SingleplayerExecutionType.Both );
    }
	
	/*
		void SetUserAllowed(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
		{
		Param1<bool> params;
		if (!ctx.Read(params))
		return;
		
		m_UserAllowed = params.param1;
		}
    */
	
	void SetDzrQuestAttributesOnClient(CallType type, ref ParamsReadContext ctx, ref PlayerIdentity sender, ref Object target)
	{
		Param2<string,bool> params;
		if (!ctx.Read(params))
		return;
		
		m_QuestActionName = params.param1;
        
		////////Print(";;;;;;;;;;;;;;;;;;;;;;;;;;;; m_QuestActionName: "+params.param1);
		//m_UserAllowed = params.param2;
		////////Print(";;;;;;;;;;;;;;;;;;;;;;;;;;;; m_UserAllowed: "+params.param2);
		////////Print(";;;;;;;;;;;;;;;;;;;;;;;;;;;; portalAvailable: "+portalAvailable);
		//DZR_sendPlayerMessage(sender, "SetDzrQuestAttributesOnClient portalAvailable:"+portalAvailable+" m_UserAllowed:"+m_UserAllowed);
		
    }
	
	override void CreateConditionComponents() {
		m_ConditionItem = new CCINone;
        m_ConditionTarget = new CCTNone;
    }
	
	DzrQuestTrigger GetNearestDzrQuest(PlayerBase player)
	{
		////////Print("▀█ GetNearestDzrQuest █▀");
        
		float distance;
        bool m_UserAllowed = false;
        
		
		DzrQuestTrigger m_RetDzrQuest = NULL;
		
		array<Object> nearestObjects = new array<Object>;
		vector pos = player.GetPosition();
		GetGame().GetObjectsAtPosition(pos, 1, nearestObjects, null);
        
        
        
		////////Print("▀█ GetObjectsAtPosition █▀");
		////////Print("▀█ nearestObjects.Count() █▀");
		////////Print("▀█"+ nearestObjects.Count() +"█▀");
		//m_DzrQuestTrigger = NULL;
        
        nearestDzrQuests = new array<EntityAI>;
		for (int i = 0; i < nearestObjects.Count(); i++)
		{
			EntityAI nearestEnt = EntityAI.Cast(nearestObjects.Get(i));
			
			
			if (nearestEnt && nearestEnt.IsKindOf("DzrQuestTrigger"))
			{
				
				m_RetDzrQuest = DzrQuestTrigger.Cast(nearestEnt);
                
				//////Print("▀▀ nearestEnt ▀▀ "+m_RetDzrQuest.GetDzrQuestID());
				//////Print(nearestEnt);
				
				////////Print("▀█ ▀▀▀▀YES nearestEnt && nearestEnt.IsKindOf('DzrQuestTrigger')▀▀▀ █▀");
				////////Print("▀█ ▀▀▀▀m_RetDzrQuest▀▀▀ █▀");
				////////Print(m_RetDzrQuest);
				////Print("▀▀ m_RetDzrQuest.GetDzrQuestID() ▀▀ "+m_RetDzrQuest.GetDzrQuestID());
				
				//m_QuestActionName = m_DzrQuestTrigger.GetDzrQuestActionName();
				//send RPC with portal. Server returns portal action name
                
				ref Param1<DzrQuestTrigger> m_Data = new Param1<DzrQuestTrigger>(m_RetDzrQuest);
                
				//GetRPCManager().SendRPC( "dzr_dquests", "SendDzrQuestAttributesToClient", m_Data, true, player.GetIdentity());
				//SetText(m_QuestActionName);
                //m_UserAllowed = m_DzrQuestTrigger.isPlayerAllowed( player.GetIdentity().GetPlainId() );
				////////Print("▀█  m_RetDzrQuest.GetDzrQuestID() ▀█ ");
				////////Print( m_RetDzrQuest.GetDzrQuestID() );
				//////////Print("▀█ NAME █▀");
				//////////Print( player.QuestName( m_RetDzrQuest.GetDzrQuestID() ) );
                
                
				m_UserAllowed = player.GetDzrQuestPermissions( m_RetDzrQuest.GetDzrQuestID() );
				
                nearestDzrQuests.Insert(nearestEnt);
                
                // player.UpdateOptions();
				////////Print("▀█ return █▀");
				////////Print(portalAvailable);
				////Print(m_UserAllowed);
				//m_UserAllowed = player.PlayerNearDzrQuest( m_RetDzrQuest.GetlPosition());
				//////////Print("________________END FOR___________________");
                
				if(!m_UserAllowed)
				{
                    
					m_RetDzrQuest = NULL;
                }
                else 
                {
                    portalAvailable = true;
                    player.SetCurrentDzrQuestID( m_RetDzrQuest.GetDzrQuestID() );
                    m_QuestActionName = player.GetQuestNames( m_RetDzrQuest.GetDzrQuestID() );
                    return m_RetDzrQuest;
                }
            } 
			else
			{
				
				////////Print("▀█ NOT nearestEnt && nearestEnt.IsKindOf('DzrQuestTrigger') █▀");
                
                m_RetDzrQuest = NULL;
                
				//portalAvailable = false;
				//////////Print("________________END FOR___________________");
				//return NULL;
            }
            
            
        }
        
        if(!CallDelay)
        {
            //Print("CallDelay");
            //Print(CallDelay);
            //Print(nearestDzrQuests);
            
            foreach ( EntityAI aDzrQuest : nearestDzrQuests )
            {
                //Print("Foreach");
                if(aDzrQuest)
                {
                    //Print("Yes portal, sending to chat");
                    
                    GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(DZR_sendPlayerMessage, 1000, false,  player , aDzrQuest.ToString() );
                    
                    //Print(aDzrQuest.GetDisplayName());
                }
            }
            CallDelay = true;
            
            //Print(CallDelay);
        }
		////////Print("▀█ return █▀");
		////////Print(portalAvailable);
		////////Print(m_UserAllowed);
		//DZR_sendPlayerMessage(player, "GetNearestDzrQuest portalAvailable:"+portalAvailable+" m_UserAllowed:"+m_UserAllowed);
		//////////Print("GetNearestDzrQuest portalAvailable:"+portalAvailable+" m_UserAllowed:"+m_UserAllowed);
        
		return m_RetDzrQuest;
    }
    
	
	
	override string GetText()
	{
        if(m_DzrQuestTrigger)
        {
            
            
    		PlayerBase player = PlayerBase.Cast( GetGame().GetPlayer() );
            if ( player )
            {
                return player.GetQuestNames( m_DzrQuestTrigger.GetDzrQuestID() );
            }
            
            ////Print("██████ ██████ SERVER player.GetQuestNames( m_DzrQuestTrigger.GetDzrQuestID(): ██████ ██████");
            ////Print(player.GetQuestNames( m_DzrQuestTrigger.GetDzrQuestID() ));
            ////Print(player.GetQuestNames( 0 ));
            ////Print(player.GetQuestNames( 1 ));
            ////Print(player.GetQuestNames( 2 ));
            ////Print(player.GetQuestNames( 3 ));
            ////Print("______________________________");
            
            return m_QuestActionName;
            
            return player.GetQuestNames( m_DzrQuestTrigger.GetDzrQuestID() );
        }
        return "";
    }
	
	void SetText(string theActionName)
	{
		m_QuestActionName = theActionName;
		////////Print("Set Name for a portal: "+m_QuestActionName);
    }
	
	override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        
        bool returner = false;
        
        ////////Print("ActionCondition: trying  GetNearestDzrQuest");
        g_Player = player;
        
        DzrQuestTrigger returned_portal = GetNearestDzrQuest(player); 
        if(returned_portal)
        {
            //Print("---------------");
            bool isInstant;
            if(m_DzrQuestTrigger)
            {
                if(player.GetQuestInstant( m_DzrQuestTrigger.GetDzrQuestID() ))
                {
                    isInstant = player.GetQuestInstant( m_DzrQuestTrigger.GetDzrQuestID() );
                };
            };
            
            
            //Print(isInstant);
            //Print(player.IsInQuest());
            
            
            
            if( isInstant && !player.IsInQuest() )
            {
                Print("INSTANT QUEST!!!!!!!!!");
                player.SetInQuest(true);
                player.ActivateDzrQuest( returned_portal.GetDzrQuestID() );
                Print(player.IsInQuest());
            };
            //Print("---------------");
        };
        
        // TODO: Check Trigger schedule here
        /*
            if(returned_portal.DisabledBySchedule() == 1)
            {
            return false;
            }
        */
        
        //action_data.m_Target = returned_portal;
        //m_DzrQuestTrigger = returned_portal;
        
        DZR_sendPlayerMessage2(player, "PTL: "+returned_portal.ToString());
        
        ////Print(returned_portal.ToString());
        //g_Player.SetCurrentDzrQuest(returned_portal.GetDzrQuestID() );
        ////////Print("ActionCondition: returner "+returner);
        ////////Print("██████________________END ActionCondition___________________██████");
        
        if(returned_portal != NULL)
        {
            //m_QuestActionName = player.GetQuestNames(returned_portal.GetDzrQuestID());
            
            m_DzrQuestTrigger = returned_portal;
            
            player.SetShowOptions(true);
            
            // player.SetOption2(true);
            // player.SetOption3(true);
            // player.SetOption4(true);
            // player.SetOption5(true);
            // player.SetOption6(true);
            
            //player.SetSelectedOption2(0);
            
            returner = true;
        }
        else
        {
            player.SetShowOptions(false);
            
            // player.SetOption1(false);
            // player.SetOption2(false);
            // player.SetOption3(false);
            // player.SetOption4(false);
            // player.SetOption5(false);
            // player.SetOption6(false);
            
            player.SetSelectedOption2(0);
        }
        
		if ( GetGame().IsServer() && GetGame().IsMultiplayer() ) 
		return true;
        
		return returner;
    }
	
    void SetCallDelay(bool val)
    {
        CallDelay = val;
    }
    
	void DZR_sendPlayerMessage(PlayerBase player, string message)	
	{
        //Print("Sending!");
        //Print(player);
        //Print(message);
        
        CallDelay = false;
		Param1<string> Msgparam;
		Msgparam = new Param1<string>(message);
        
		//GetGame().RPCSingleParam(player, ERPCs.RPC_USER_ACTION_MESSAGE, Msgparam, true, player.GetIdentity());
        
    }
    
    void DZR_sendPlayerMessage2(PlayerBase player, string message)	
	{
		Param1<string> Msgparam;
		Msgparam = new Param1<string>(message);
        
		//GetGame().RPCSingleParam(player, ERPCs.RPC_USER_ACTION_MESSAGE, Msgparam, true, player.GetIdentity());
    }
	
    override void OnStart( ActionData action_data )
    {
		//NotificationSystem.SendNotificationToPlayerExtended(action_data.m_Player, 2, "OnStart Trying to teleport", "Trying to teleport", "set:dayz_gui image:open");
        
        super.OnStart( action_data );
    }
	
	override void OnStartClient( ActionData action_data )
	{
		action_data.m_Player.ActivateDzrQuest( m_DzrQuestTrigger.GetDzrQuestID() );
    }
	
	
};









