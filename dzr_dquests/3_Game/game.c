


class dzr_DzrQuest_Trigger
{
    
	string m_QuestActionName;
	string m_LoadingScreenDelay;
	string m_LoadingScreenDelayWhileTeleporting;
	string m_Description1;
	string m_Description2;
	string m_Target;
	string m_QuestName;
	string m_isPrivate;
	string m_TargetOrientation;
	string m_QuestPosition;
	string m_Title;
	ref array<string> AllowedUsers;
    
    void dzr_DzrQuest_Trigger()
    {
        AllowedUsers = new array<string >;
    }
    
}

class dzr_DzrQuests 
{
    ref array<dzr_DzrQuest_Trigger> List = new array<dzr_DzrQuest_Trigger>;
    
    void dzr_DzrQuests()
    {
        
        
        
        List = new array<dzr_DzrQuest_Trigger >;
        
        
        
    }
}

modded class DayZGame { 
    
    protected ref dzr_DzrQuests m_DzrQuests = new dzr_DzrQuests;
    
    
    
    array<dzr_DzrQuest_Trigger> addDzrQuest(dzr_DzrQuest_Trigger aDzrQuest)
    {
        m_DzrQuests.List.Insert(aDzrQuest);
        return m_DzrQuests.List;
    };
    
    void deleteDzrQuests()
    {
        m_DzrQuests.List.Clear();
    };
    
    
    array<dzr_DzrQuest_Trigger> getDzrQuests()
    {
        
        ref dzr_DzrQuest_Trigger aDzrQuest;
        aDzrQuest = new dzr_DzrQuest_Trigger();
        aDzrQuest.AllowedUsers = new array<string>;
        aDzrQuest.AllowedUsers.Insert("000");
        
        m_DzrQuests.List.Insert(aDzrQuest);
        
        return m_DzrQuests.List;
    };
}    