class CfgPatches
{
	class dzr_dquests
	{
		requiredAddons[] = {"DZ_Data","DZ_Radio","JM_CF_Scripts"};
		units[] = {};
		weapons[] = {};
    };
};

class CfgMods
{
	class dzr_dquests
	{
		type = "mod";
		author = "DayZRussia.com";
		description = "Quest System.";
		dir = "dzr_dquests";
		name = "DZR Quests";
		inputs = "dzr_dquests/Data/Inputs.xml";
		dependencies[] = {"Core", "Game", "World", "Mission"};
		class defs
		{
			class engineScriptModule
			{
				files[] = {"dzr_dquests/Common",  "dzr_dquests/1_Core"};
            };
			class gameLibScriptModule
			{
				files[] = {"dzr_dquests/Common",  "dzr_dquests/2_GameLib"};
            };
			class gameScriptModule
			{
				files[] = {"dzr_dquests/Common",  "dzr_dquests/3_Game"};
            };
			class worldScriptModule
			{
				files[] = {"dzr_dquests/Common", "dzr_dquests/4_World"};
            };
			class missionScriptModule
			{
				files[] = {"dzr_dquests/Common", "dzr_dquests/5_Mission"};
            };
			
        };
    };
};


class CfgVehicles
{

    
    class PersonalRadio;
    class PublicDzrQuestDevice: PersonalRadio
    {
        displayName="Public DzrQuest Creator";
        descriptionShort="Creates Public DzrQuest";
        simulation="itemTransmitter";
        inputRange[]={};
        range=0;
        attachments[]=
        {
        };
        repairableWithKits[]={7};
        repairCosts[]={25};
        class EnergyManager
        {
        };
    };
    class PrivateDzrQuestDevice: PublicDzrQuestDevice
    {
        displayName="Private DzrQuest Creator";
        descriptionShort="Creates Private DzrQuest, adds the creator to it. Edited in the profile/DZR/DZR Quests folder";
    };
    class Inventory_Base;
    
    class DzrQuestTrigger: Inventory_Base
    {
        scope = 2;
        //model = "\dzr_dquests\sphere.p3d";
        storageCategory = 10;

        class DamageSystem
        {
            class GlobalHealth
            {
                class Health
                {
                    hitpoints = 100;
                    healthLevels[] = {{1,{}},{0.7,{}},{0.5,{}},{0.3,{}},{0,{}}};
                };
            };
        };
    };
    
    class DzrQuestTriggerDEBUG: Inventory_Base
    {
        scope = 2;
        storageCategory = 10;
        model = "\dzr_dquests\sphere.p3d";
        class DamageSystem
        {
            class GlobalHealth
            {
                class Health
                {
                    hitpoints = 100;
                    healthLevels[] = {{1,{}},{0.7,{}},{0.5,{}},{0.3,{}},{0,{}}};
                };
            };
        };
    };


    class DzrCarBlocker: Inventory_Base
    {
        scope = 2;
        descriptionShort = "Place in the car to block all actions";
        //storageCategory = 10;
        model = "\DZ\gear\radio\WalkieTalkie.p3d";

    };
};

class CfgSoundShaders
{
    class DZR_DzrQuests_PlayMusic1SoundShader
    {
        samples[] = {{"\dzr_dquests\data\Music1",1.0}};
        volume = 1.9;
        
    };
    
    class DZR_DzrQuests_CarTravelSoundShader
    {
        samples[] = {{"\dzr_dquests\data\CarTravel",1.0}};
        volume = 1.9;
        
    };
    class DZR_DzrQuests_TruckTravelSoundShader
    {
        samples[] = {{"\dzr_dquests\data\TruckTravel",1.0}};
        volume = 1.9;
        
        
    };
};

class CfgSoundSets
{
    class DZR_DzrQuests_PlayMusic1
    {
        volumeFactor = 1;
        frequencyFactor = 1;
        spatial = 0;
        soundShaders[] = {"DZR_DzrQuests_PlayMusic1SoundShader"};
    };
    class DZR_DzrQuests_TruckTravel
    {
        volumeFactor = 1;
        frequencyFactor = 1;
        spatial = 0;
        soundShaders[] = {"DZR_DzrQuests_TruckTravelSoundShader"};
    };
    class DZR_DzrQuests_CarTravel
    {
        volumeFactor = 1;
        frequencyFactor = 1;
        spatial = 0;
        soundShaders[] = {"DZR_DzrQuests_CarTravelSoundShader"};
    };
};                