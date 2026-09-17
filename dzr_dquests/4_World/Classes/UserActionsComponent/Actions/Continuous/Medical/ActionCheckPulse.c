
modded class ActionCheckPulseTarget
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
		if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
		return super.ActionCondition(player, target, item);
    }
}

modded class ActionForceConsumeSingle
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceABiteCan
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceDrinkDisinfectant
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionDrinkDisinfectant2
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceConsume
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceDrink
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if(!ntarget)
        {
            return false;
        };
        
        if (ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceFeed
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
    if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
    {
        return false;
    }
    
    return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceFeedSmall
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceDrinkAlcohol
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceFeedCan
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}


modded class ActionForceFeedMeat
{
    override bool ActionCondition( PlayerBase player, ActionTarget target, ItemBase item )
	{
        PlayerBase ntarget = PlayerBase.Cast(target.GetObject());
        if ( ntarget.GetIdentity() == NULL || ntarget.m_isDZRNPC)
        {
            return false;
        }
        
        return super.ActionCondition(player, target, item);
    }
}

