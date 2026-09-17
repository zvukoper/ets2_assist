
modded class MissionBase
{
	override UIScriptedMenu CreateScriptedMenu(int id)
	{
		UIScriptedMenu menu = NULL;
        menu = super.CreateScriptedMenu(id);
        if (!menu)
        {
            switch (id)
            {
                case 426181615182011219:
                menu = new DzrQuestLoading;
                break;
            }
            switch (id)
            {
                case 618161518209438723:
                menu = new DzrQuestLoading2;
                break;
            }
            if (menu)
            {
                menu.SetID(id);
            }
        }
        return menu;
	}
};
