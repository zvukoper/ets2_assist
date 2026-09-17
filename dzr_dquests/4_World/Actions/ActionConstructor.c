modded class ActionConstructor
{
    override void RegisterActions(TTypenameArray actions)
    {
		actions.Insert(ActionActivateDzrQuest);
		actions.Insert(ActionOption1);
		actions.Insert(ActionOption2);
		actions.Insert(ActionOption3);
		actions.Insert(ActionOption4);
		actions.Insert(ActionOption5);
		actions.Insert(ActionOption6);
        super.RegisterActions(actions);
       
    }
}