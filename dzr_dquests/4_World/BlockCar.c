modded class CarScript
{
    //bool m_DZR_Blocked = false;
    //bool m_DZR_FreshSpawn = false;
	//private static ref array<CarScript> m_DZR_allVehicles = new array<CarScript>;
    
    void CarScript()
	{
		//RegisterNetSyncVariableBool("m_DZR_Blocked");
		//RegisterNetSyncVariableBool("m_DZR_FreshSpawn");
        //Print("_______________ CARSCRIPT INIT_______________");
        //Print(this);
        //Print(this.GetDisplayName());
        // //Print(m_DZR_Blocked);
        // //Print(m_DZR_FreshSpawn);
        //Print("_______________ _____________ _______________");
        if(GetGame().IsClient())
        {
            Print("__________________________________________CarScript INIT CLINETSIDE");
            UpdateOnConnectVisibility_RPC();
            Print(this);
        }
        
        GetRPCManager().AddRPC("dzr_dquests", "UpdateOnConnectVisibility_RPC", this, SingleplayerExecutionType.Both);
        GetRPCManager().AddRPC("dzr_dquests", "DZR_SetNPCVisibility", this, SingleplayerExecutionType.Both);
        ClearNPCVehicles();
    }
    
    /*
        override bool CanDisplayAttachmentSlot( int slot_id )
        {
		string slot_name = InventorySlots.GetSlotName(slot_id);
        if ( this.FindAttachmentBySlotName( "DzrNpcAttribute" ) )
        {
        return false;
        }
		
		return super.CanDisplayAttachmentSlot(slot_id);
        }
    */
    
    
    void UpdateOnConnectVisibility_RPC()
    {
        int b1, b2, b3, b4;
        EntityAI theObjEnt = this;
        theObjEnt.GetPersistentID( b1, b2, b3, b4);
        string PID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
        Param1<string> p = new Param1<string>(PID);
        
        Print("____________UpdateOnConnectVisibility_RPC____________")
        //Print(PID);
        //Print("________________________")
        //GetRPCManager().SendRPC( "dzr_dquests", "CheckNPCCleanup", p, true); 
        
        CheckNPCVisibility(PID);
        
    }
    
    void CheckNPCVisibility(string PID)
    {
        Print("_______CheckNPCVisibility_________");
        
        //TODO: Request visibility from server! PIDs not matching
        
        Print(this);
        Print(PID);
        EntityAI theai =  EntityAI.Cast(this);
        string path = "$profile:DZR\\Quests\\Cleanup\\Visible\\"+PID+".txt";
        if(FileExist(path))
        {
            
            Param2<EntityAI, bool> paramvis = new Param2<EntityAI, bool>(theai, true);
            GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_SERVER", paramvis, true );
            return;
            
        };
        
        path = "$profile:DZR\\Quests\\Cleanup\\Invisible\\"+PID+".txt";
        if(FileExist(path))
        {
            
            Param2<EntityAI, bool> paramINvis = new Param2<EntityAI, bool>(theai, false);
            GetRPCManager().SendRPC( "dzr_dquests", "DZR_SetvInvisibleRPC_SERVER", paramINvis, true );
            return;
            
        };
        Print("NO FILE : " + path);
        //Print("________________");
        
    };
    
    bool IsDZRNPC()
    {
        
        array<EntityAI> itemsArray = new array<EntityAI>;
        GetInventory().EnumerateInventory(InventoryTraversalType.PREORDER, itemsArray);
        for (int i = 0; i < itemsArray.Count(); i++)
        {
            ItemBase item = ItemBase.Cast(itemsArray.Get(i));
            if(item && item.IsKindOf("DzrCarBlocker"))
            {
                //Print(item);
                return true;
            }
        }
        
        
        return false;
    }
    
	override void AfterStoreLoad()
	{
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(SendCleanupRPC, 10000, false);
        
        //Print(" AfterStoreLoad()");
        
        super.AfterStoreLoad();
        
		// int b1, b2, b3, b4, b11, b12, b13, b14;
        // this.GetPersistentID( b11, b12, b13, b14 );
        // string PID = string PID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();;
        //RCP.Send("dzr_dquests", "CheckNPCleanup", p
    }
    
    void SendCleanupRPC()
    {
        int b1, b2, b3, b4;
        EntityAI theObjEnt = this;
        theObjEnt.GetPersistentID( b1, b2, b3, b4);
        string PID = b1.ToString() +" "+ b2.ToString() +" "+ b3.ToString() +" "+ b4.ToString();
        Param1<string> p = new Param1<string>(PID);
        
        //Print("____________SendCleanupRPC____________")
        //Print(PID);
        //Print("________________________")
        //GetRPCManager().SendRPC( "dzr_dquests", "CheckNPCCleanup", p, true); 
        
        CheckNPCCleanup(PID);
        
    }
    
    
    
    void CheckNPCCleanup(string PID)
    {
        //Print("_______CheckNPCCleanup START 4_________");
        
        
        //Print("_______CheckNPCCleanup 4_________");
        //Print(PID);
        
        string path = "$profile:DZR\\Quests\\Cleanup\\"+PID+".txt";
        if(FileExist(path))
        {
            //Print("PID " +PID+ " exists. Deleting file and object "+this.ToString());
            
            DeleteFile(path);
            
            this.Delete();
        }
        
        //Print("________________");
        
    };
    
    
    
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
    
	
	static void ClearNPCVehicles()
	{
        /*
            //Print("___________________________________ CleaningCars");
            if(m_DZR_allVehicles.Count() > 1)
            {
			foreach(CarScript theCar : m_DZR_allVehicles)
            {
            if(theCar.IsDZRNPC() && !theCar.m_DZR_FreshSpawn)
            {
            //Print(theCar.GetDisplayName());
            //Print(theCar.m_DZR_Blocked);
            //Print(theCar.m_DZR_FreshSpawn);
            //Print("______________Deleted");
            //Print(theCar);
            theCar.Delete();
            }
            
            }
            }
        */  
    }
    
    /*
        override void EEInit()
        {
        super.EEInit();
        m_DZR_allVehicles.Insert(this);
        }
        
        override void EEDelete(EntityAI parent)
        {
        super.EEDelete(parent);
        m_DZR_allVehicles.RemoveItem(this);
        }
        
        override void OnStoreSave( ParamsWriteContext ctx )
        {   
        super.OnStoreSave(ctx);
        
        Param2<bool, bool> data = new Param2<bool, bool>(m_DZR_Blocked, m_DZR_FreshSpawn);
        
        ctx.Write(data);
        }
        
        
        override bool OnStoreLoad( ParamsReadContext ctx, int version )
        {
        if ( !super.OnStoreLoad( ctx, version ) )
        return false;
        
        Param2<bool, bool> data = new Param2<bool,bool>(false, false);
        if (ctx.Read(data))
        {
        m_DZR_Blocked = data.param1;
        m_DZR_FreshSpawn = false;
        }
        
        
        DZR_SyncVal();
        
        return true;
        }
    */
    override bool CanDisplayAttachmentSlot( string slot_name )
    {
        if(!super.CanDisplayAttachmentSlot(slot_name))
        return false;
        
        return !IsDZRNPC();
    }
    
    override bool CanDisplayAttachmentCategory( string category_name )
    {
        if(!super.CanDisplayAttachmentCategory(category_name))
        return false;
        
        return !IsDZRNPC();
    }
    
    void DZR_SyncVal()
    {
        if (GetGame().IsServer())
        {
            if(GetInventory())
            {
                if (IsDZRNPC())
                GetInventory().LockInventory(HIDE_INV_FROM_SCRIPT);
                else
                GetInventory().UnlockInventory(HIDE_INV_FROM_SCRIPT);
            }	        
            
            SetSynchDirty();
        }
    }
    override bool IsInventoryVisible()
    {
        if(!super.IsInventoryVisible())
        return false;
        
        return !IsDZRNPC();
    }
}	    

modded class ActionDetach
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
    {
		if (target)
		{
			if (target.GetObject().IsInherited(CarWheel))
			{
                if(CarScript.Cast(target.GetParent()).IsDZRNPC())
                return false;
                
                
            }
        }
        
        return super.ActionCondition(player, target, item);
    }
}



modded class ActionOpenCarDoors
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        CarScript carScript;
        if (Class.CastTo(carScript, target.GetParent()))
        {
            if (CarScript.Cast(target.GetParent()).IsDZRNPC())
            {
                return false;
            }
        }
		
		return super.ActionCondition(player, target, item);
    }
}

modded class ActionGetInTransport
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        CarScript carScript;
        
        if (Class.CastTo(carScript, target.GetObject()))
        {
            if (CarScript.Cast(target.GetObject()).IsDZRNPC())
            {
                return false;
            }
        }
		
		return super.ActionCondition(player, target, item);
    }
}

modded class ActionOpenCarDoorsOutside
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        CarScript carScript;
        if (Class.CastTo(carScript, target.GetParent()))
        {
            if (CarScript.Cast(target.GetParent()).IsDZRNPC())
            {
                return false;
            }
        }
		
        return super.ActionCondition(player, target, item);
    }
}

modded class ActionPushCar
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        
        CarScript car = GetCar(target);
        if(car)
        {
            if(car.IsDZRNPC())
            {
                return false;
            }
        }
		
        return super.ActionCondition(player, target, item);
    }
}

