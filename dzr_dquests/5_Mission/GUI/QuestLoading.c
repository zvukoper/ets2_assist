
class DzrQuestLoading extends UIScriptedMenu
{
	bool 			   m_IsInitialized;
	protected Widget m_DZRDzrQuests_root;	
	protected MultilineTextWidget dzrp_bstext;
    string m_LoadingSound;
    private ButtonWidget       r_ExitButton;
    
    Widget r_MainAnimation;
    Widget r_SecondAnimation;
    Widget r_ExitButtonAnimation;
    Widget TitleContainer;
    MultilineTextWidget r_Header;
    
    //MultilineTextWidget r_Description;
    
    MultilineTextWidget r_Description;
    MultilineTextWidget r_AdditionalText;
    ImageWidget r_BackgroundImage;
    ImageWidget r_ExitButtonImage;
    ImageWidget r_ExitButtonMouseOver;
    TextWidget CloseInfo;
    bool m_ShowExit = true;
    int m_GlobalTimer = 0;
    bool DebugMode;
    protected AbstractWave m_ActiveMus;
    
    float m_Delay=3;
    
    ref WidgetFadeTimer m_LoadingTimer1 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer2 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer3 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer4 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer5 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer6 = new WidgetFadeTimer();
    
    ProgressBarWidget r_LoadingBar;
    
    string DisableProgress = "0";
    string DisableBlocking = "0";
    string DisableCustomLayouts = "0";
    
	void DzrQuestLoading()
    {
        string FileContents;
        DZR_PTL_GetFile("$profile:DZR\\Quests","Debug.txt", FileContents, "0");
        DebugMode = FileContents.ToInt();
        PrintD("_____________________________");
        PrintD("DebugMode");
        PrintD(FileContents);
        
        DZR_PTL_GetFile("$profile:DZR\\Quests","DisableProgress.txt", DisableProgress, "0");
        
        //DisableProgress = FileContents.ToInt();
        
        PrintD("_____________________________");
        PrintD(DisableProgress);
        
        
        DZR_PTL_GetFile("$profile:DZR\\Quests","DisableBlocking.txt", DisableBlocking, "0");
        PrintD("_____________________________");
        PrintD(DisableBlocking);
        
        
        DZR_PTL_GetFile("$profile:DZR\\Quests","DisableCustomLayouts.txt", DisableCustomLayouts, "0");
        PrintD("_____________________________");
        PrintD(DisableCustomLayouts);
        
		#ifndef SERVER
            if (GetGame() && GetGame().GetMission() && GetGame().GetMission().GetHud())
            {
                GetGame().GetMission().GetHud().ShowHudPlayer(false);
                GetGame().GetMission().GetHud().ShowQuickbarPlayer(false);
            }
        #endif
        
        
    }
    
	void ~DzrQuestLoading()
    {
        layoutRoot = NULL;
        m_GlobalTimer = 0;
        m_IsInitialized = false;
        ProgressAsync.SetProgressData(null);
        ProgressAsync.DestroyAllPendingProgresses();
        
		#ifndef SERVER
            if (GetGame() && GetGame().GetMission() && GetGame().GetMission().GetHud())
            {
                GetGame().GetMission().GetHud().ShowHudPlayer(true);
                GetGame().GetMission().GetHud().ShowQuickbarPlayer(true);
            }
        #endif
        
		
		if (GetGame() && GetGame().GetMission())
		{
			GetGame().GetMission().EnableAllInputs(true);
        }
        
        PrintD("______|||||||||||||________LOADING SCREEN IS PROPERLY DISPOSED_______||||||||||||||________");
    }
    
    void PrintD(string text)
    {
        if(DebugMode)
        {
            Print("[DZR_QUESTS ClientGUI DzrQuestLoading Debug] :::: "+ text);
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
    
	override Widget Init()
	{
		if (!m_IsInitialized)
		{
            
            layoutRoot = GetGame().GetWorkspace().CreateWidgets( "dzr_dquests/GUI/layouts/dzr_quests_screen.layout" );

            //GetRPCManager().AddRPC("dzr_dquests", "SetActiveLayout", this, SingleplayerExecutionType.Server);
        }
		return layoutRoot;
    }
    
    void SetDzrLayout(string lt)
    {
        PrintD("SetLayout 70");
        
        if(lt == "" || lt == "0" || DisableCustomLayouts == "1")
        {
            lt = "dzr_dquests/GUI/layouts/dzr_inner.layout";
        }
        
        // Print("CREATING CUSTOM LAYOUT");
        // Print(lt);
        // Print("CREATING CUSTOM LAYOUT_");
        
        //ProgressAsync.SetProgressData(r_LoadingBar);
        
        Widget layoutRootInner = GetGame().GetWorkspace().CreateWidgets( lt, layoutRoot );
        
        r_LoadingBar = ProgressBarWidget.Cast(layoutRoot.FindAnyWidget("LoadingBar"));
        
        r_MainAnimation = Widget.Cast( layoutRoot.FindAnyWidget("MainAnimationGroup") );
        r_ExitButtonAnimation = Widget.Cast( layoutRoot.FindAnyWidget("ExitAnimationGroup") );
        r_BackgroundImage = ImageWidget.Cast( layoutRootInner.FindAnyWidget("ImageBackground") );
        r_ExitButtonImage = ImageWidget.Cast( layoutRootInner.FindAnyWidget("ExitIcon") );
        r_ExitButtonMouseOver = ImageWidget.Cast( layoutRootInner.FindAnyWidget("ExitIcon_hover") );
        r_Header = MultilineTextWidget.Cast( layoutRootInner.FindAnyWidget("QuestLabel") );
        r_Description = MultilineTextWidget.Cast( layoutRootInner.FindAnyWidget("QuestText") );
        r_AdditionalText = MultilineTextWidget.Cast( layoutRootInner.FindAnyWidget("QuestText2") );
        r_ExitButton = ButtonWidget.Cast(layoutRootInner.FindAnyWidget("ExitButton"));
        r_SecondAnimation = Widget.Cast(layoutRootInner.FindAnyWidget("r_SecondAnimation"));
        TitleContainer = Widget.Cast(layoutRootInner.FindAnyWidget("TitleContainer"));
        
        //CloseInfo = TextWidget.Cast( layoutRoot.FindAnyWidget("CloseInfo") );
        
        PrintD("SetLayout 98");
        r_ExitButton.SetText("#dzrptl_close");
        PrintD("SetLayout 100");
        layoutRootInner.Show(true);
        PrintD("SetLayout 102");
        
        //m_LoadingTimer.FadeIn(layoutRoot, 4, false);
        
        r_BackgroundImage.LoadImageFile(0, "dzr_dquests/data/Drive1.edds");
        PrintD("SetLayout 105");
        m_LoadingSound = "DZR_DzrQuests_PlayMusic1";
        PrintD("SetLayout 108");
        r_ExitButtonMouseOver.Show(false);
        r_SecondAnimation.Show(false);
        TitleContainer.Show(false);
        
        //r_Header.Show(false);
        //r_Description.Show(false);
        //r_AdditionalText.Show(false);
        
        //r_Header.SetLineBreakingOverride(LinebreakOverrideMode.LINEBREAK_WESTERN);
        //r_Description.SetLineBreakingOverride(LinebreakOverrideMode.LINEBREAK_WESTERN);
        //r_AdditionalText.SetLineBreakingOverride(LinebreakOverrideMode.LINEBREAK_WESTERN);
        
        r_BackgroundImage.Show(false);
        r_ExitButtonAnimation.Show(false);
        PrintD("SetLayout 121");
    }
    
    void SetTexts(string text1, string text2, string text3, string is_autoclosed)
    {
        if(r_Header)
        {
            r_Header.SetText(text1);
        };
        
        if(r_Description)
        {
            r_Description.SetText(text2);
        };
        
        if(r_AdditionalText)
        {
            r_AdditionalText.SetText(text3);
        };
        
        if(is_autoclosed == "1")
        {
            m_ShowExit = false;
        };
    }
    
    void SetMedia(string Image, string Sound)
    {
        //Print("|----------------------------------------------------------------------------------- MEDIA");
        //Print(Image);
        //Print(Sound);
        //Print("|-----------------------------------------------------------------------------------------");
        
        if(Image != "0")
        {
            r_BackgroundImage.LoadImageFile(0, Image);
        }
        
        if(Sound != "0")
        {
            m_LoadingSound = Sound;
        }
    }
    
    void Run()
    {
        
        
        ProgressAsync.DestroyAllPendingProgresses();
        PrintD("QuestLoading.c Run 260");
        GetGame().GetMission().GetHud().Show(false);
        PrintD("QuestLoading.c Run 262");
        m_LoadingTimer1.FadeIn(r_MainAnimation, 2, false);
        PrintD("QuestLoading.c Run 256 ---- BLOCKING NEXT");
        if(DisableBlocking == "0")
        {   PrintD("QuestLoading.c Run BLOCKING EXECUTED 266");
            BlockPlayer(true);
            
            //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(BlockPlayer, 1000, false, true); 
        }
        PrintD("QuestLoading.c Run 269");
        
        if(DisableProgress == "0")
        {
            ProgressAsync.SetProgressData(r_LoadingBar);
            SetProgress();
            
            //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(SetProgress, 3000, false);
        }
        //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer2.FadeIn, 500, false, TitleContainer, 1, false);
        
        PrintD("QuestLoading.c Run 261");
        
        //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer4.FadeIn, 4500, false, r_SecondAnimation, 3, false);
        
        m_LoadingTimer4.FadeIn(r_SecondAnimation, 3, false);
        PrintD("QuestLoading.c Run 263");
        if(m_ShowExit)
        {
            //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer5.FadeIn, m_Delay*1000+3000, false, r_ExitButtonAnimation, 0.5, false);
            
            m_LoadingTimer5.FadeIn(r_ExitButtonAnimation, 0.5, false);
        }
        PrintD("QuestLoading.c Run 268");
        
        //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(GetGame().GetUIManager().ShowUICursor, m_Delay*1000+3000, false, true);
        
        GetGame().GetUIManager().ShowUICursor(true);
        PrintD("QuestLoading.c Run 270");
        
        //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer6.FadeIn, 2700, false, r_BackgroundImage, 9, false);
        
        m_LoadingTimer6.FadeIn(r_BackgroundImage, 9, false);
        PrintD("QuestLoading.c Run 272");
        m_IsInitialized = true;
        
    }
    
    void SetProgress()
    {
        
        PrintD("---------------------------- SetProgress 318 ------------------------============");
        float tick = 100;
        float time = m_Delay*1000;
        float increment = time/tick/100;
        float multiplier = 3;
        
        tick = (m_Delay+0.5)*1000/(100*multiplier);
        float total = 100*multiplier;
        
        if(m_GlobalTimer < total)
        {
            m_GlobalTimer++;
            
            r_LoadingBar.SetCurrent(m_GlobalTimer/multiplier);
            PrintD("---------------------------- SetProgress 332 ------------------------============");
            int gmult = m_GlobalTimer/multiplier;
            PrintD("m_GlobalTimer/multiplier" + gmult.ToString());
            PrintD( "r_LoadingBar.GetCurrent()" + r_LoadingBar.GetCurrent().ToString() );
            PrintD("tick: " +tick.ToString());
            PrintD("m_GlobalTimer: " +m_GlobalTimer.ToString());
            PrintD("QuestLoading.c SetProgress CALLING LATER NEXT");
            //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(SetProgress, tick, false);
        }
        else
        {
            PrintD("---------------------------- SetProgress 341 ------------------------============ m_GlobalTimer > total ");
            PrintD(m_GlobalTimer.ToString());
            PrintD(total.ToString());
            ProgressAsync.SetProgressData(null);
            ProgressAsync.DestroyAllPendingProgresses();
            m_GlobalTimer = 0;
        }
        
        PrintD("---------------------------- SetProgress 344 ------------------------============");
    }
    
    void SetDelay(float time)
    {
        //Print("Delay SET " + time)
        
        m_Delay = time;
    }
    
	static void ForceDisableInputs(bool state, inout TIntArray skipIDs = null)
	{
        //PrintD("QuestLoading.c ForceDisableInputs 230");
        
		if (!skipIDs)
        skipIDs = new TIntArray;
        
        skipIDs.Insert(UAUIBack);
        
        TIntArray inputIDs = new TIntArray;
        
        GetUApi().GetActiveInputs(inputIDs);
        
        foreach (int inputID: inputIDs)
        {
            if (skipIDs.Find(inputID) == -1)
            {
                GetUApi().GetInputByID(inputID).ForceDisable(state);
            }
        }
        
        //PrintD("QuestLoading.c ForceDisableInputs 247");
    }    
    
    
	void PlayMusic(string soundSetName)
	{
        
        PrintD("QuestLoading.c PlayMusic 254");
		if (m_ActiveMus) {
			return;
        }
		
		SoundParams soundParams			= new SoundParams(soundSetName);
		SoundObjectBuilder soundBuilder	= new SoundObjectBuilder( soundParams );
		SoundObject soundObject			= soundBuilder.BuildSoundObject();
		soundObject.SetKind( WaveKind.WAVEMUSIC );
		m_ActiveMus = GetGame().GetSoundScene().Play2D(soundObject, soundBuilder);
		m_ActiveMus.Play();
        PrintD("QuestLoading.c PlayMusic 265");
    }
	
	void StopMusic()
	{
		if (m_ActiveMus)
		{
            
            //m_ActiveMus.SetFadeOutFactor(0);
            //m_ActiveMus.SetVolume(0);
            //m_ActiveMus.SetSoundAutodestroy( true );  
            
			m_ActiveMus.Stop();
			m_ActiveMus = NULL;
        }
    }
	
	void ForcePlayMusic(string sound)
	{
		StopMusic();
		PlayMusic(sound);
    }
    
    void BlockPlayer(bool val)
    {
        PrintD("QuestLoading.c BlockPlayer 405");
        PrintD(DisableBlocking);
        PlayerBase player = PlayerBase.Cast(GetGame().GetPlayer());
        
        if(player)
        {
            player.SetAllowDamage(!val);
            player.DisableSimulation(val);
            player.SetInvisible( val);
            player.ControlsOff(val);
        }
        
        
        if(val)
        {
            
            PlayMusic(m_LoadingSound);
            GetGame().GetSoundScene().SetSoundVolume(0.1,3);
            GetGame().GetSoundScene().SetMusicVolume(1,3);
        }
        
        
        PrintD("QuestLoading.c BlockPlayer 427");
    }
    
    void Exit()
    {
        
        PrintD("QuestLoading.c Exit 346");
        
        // PlayerBase player = GetGame().GetPlayer();
        // player.ControlsOff(false);
        
        GetGame().GetSoundScene().SetSoundVolume(g_Game.m_volume_sound,3);
        GetGame().GetSoundScene().SetMusicVolume(g_Game.m_volume_music,3);
        
        m_IsInitialized = false;
        
        m_LoadingTimer1.Stop();
        m_LoadingTimer2.Stop();
        m_LoadingTimer3.Stop();
        m_LoadingTimer4.Stop();
        m_LoadingTimer5.Stop();
        m_LoadingTimer6.Stop();
        
        StopMusic();
        layoutRoot.Show(false);
        GetGame().GetMission().GetHud().Show(true);
        GetGame().GetUIManager().ShowUICursor( false );
        GetGame().GetUIManager().HideScriptedMenu(this);
        PrintD("QuestLoading.c Exit 366");
        this.Close();
        
    }
    
	override void LockControls()
	{
		//GetGame().GetUIManager().ShowUICursor( true );
    }
    
    void TickVolume(float increment, float tick)
    {
        PrintD("QuestLoading.c TickVolume 378");
        if(m_ActiveMus)
        {
            if(m_ActiveMus.GetVolume() <=0)
            {
                return;
            }
            
            //Print(m_ActiveMus.GetVolume());
            
            m_ActiveMus.SetVolume(m_ActiveMus.GetVolume() - increment);
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(TickVolume, tick, false, increment, tick);
        }
        PrintD("QuestLoading.c TickVolume 389");
    }
	
	override bool OnClick(Widget w, int x, int y, int button)
	{
		
        // Print("OnClick: " + w.GetName());
        
		if(w == r_ExitButton)
		{
            r_ExitButtonImage.Show(true);
            r_ExitButtonMouseOver.Show(false);
            AnimateExit();
            
            //Exit();
            
            return true;
            
            
        }
        return super.OnClick(w, x, y, button);
    }
    
    override bool OnMouseEnter( Widget w, int x, int y ) {        
		if ( w == r_ExitButton ) {
        
            //Print("r_ExitButtonAnimation enter");
            
            r_ExitButtonImage.Show(false);
            r_ExitButtonMouseOver.Show(true);
            return true;
        }
		if ( w.GetName() == "r_MainAnimation" ) {
        
            //Print("r_ExitButtonAnimation enter");
            
            r_ExitButtonImage.Show(true);
            r_ExitButtonMouseOver.Show(false);
            return true;
        }
		return false;
    }
    
    
    void AnimateExit()
    {
        PrintD("QuestLoading.c AnimateExit 429");
        float m_FadeOutTime = 4000.0;
        float m_FadeOutTick = 100.0;
        if(m_ActiveMus)
        {
            float increment = m_ActiveMus.GetVolume() / (m_FadeOutTime / m_FadeOutTick);
            TickVolume(increment, m_FadeOutTick);
        }
        
        PrintD("QuestLoading.c AnimateExit 437 ---------- UNBLOCKING NEXT");
        
        GetGame().GetUIManager().ShowUICursor( false );
        if(DisableBlocking == "0")
        {
            PrintD("QuestLoading.c AnimateExit 437 ---------- UNBLOCKING NOW");
            BlockPlayer(false);
            PrintD("QuestLoading.c AnimateExit 437 ---------- UNBLOCKED");
        };
        
        PrintD("QuestLoading.c AnimateExit 440");
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer1.FadeOut, 2000, false, r_MainAnimation, 2, false);
        PrintD("QuestLoading.c AnimateExit 443");
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(this.Exit, 4000, false);
        PrintD("QuestLoading.c AnimateExit 445");
    }
	
}
