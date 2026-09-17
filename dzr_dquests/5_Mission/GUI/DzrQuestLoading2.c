
class DzrQuestLoading2 extends UIScriptedMenu
{
	bool 			   m_IsInitialized;
	private ButtonWidget       r_ExitButton;
    string FileContents;
    
    
	protected Widget m_DZRDzrQuests_root;	
	protected MultilineTextWidget dzrp_bstext;
    string m_LoadingSound;
    string m_CustomLayout;
    
    
    Widget r_MainAnimation;
    Widget r_SecondAnimation;
    Widget r_ExitButtonAnimation;
    Widget TitleContainer;
    MultilineTextWidget r_Header;    
    MultilineTextWidget r_Description;
    MultilineTextWidget r_AdditionalText;
    ImageWidget r_BackgroundImage;
    ImageWidget r_Icon;
    ImageWidget r_Logo;
    Widget r_ExitButtonImage;
    Widget r_ExitButtonMouseOver;
    TextWidget CloseInfo;
    Widget r_LoadingTextContainer;
    
    bool m_ShowExit = true;
    int m_GlobalTimer = 0;
    bool DebugMode;
    AbstractWave m_ActiveMus;
    
    float m_Delay=10;
    
    ref WidgetFadeTimer m_LoadingTimer1 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer2 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer3 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer4 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer5 = new WidgetFadeTimer();
    ref WidgetFadeTimer m_LoadingTimer6 = new WidgetFadeTimer();
    
    ProgressBarWidget r_LoadingBar;
    
	void DzrQuestLoading2()
    {
        /*
            #ifndef SERVER
            if (GetGame() && GetGame().GetMission() && GetGame().GetMission().GetHud())
            {
            GetGame().GetMission().GetHud().ShowHudPlayer(false);
            GetGame().GetMission().GetHud().ShowQuickbarPlayer(false);
            }
            #endif
        */
    }
    
    void ~DzrQuestLoading2()
    {
		//StopUpdateTimer();
		
		
        if(m_DZRDzrQuests_root)
        {
            m_DZRDzrQuests_root.Unlink();
        }
		
        if(layoutRoot)
        {
            layoutRoot.Unlink();
        }
    }
    
	override Widget Init()
	{
		if (!m_IsInitialized)
		{
            
            layoutRoot = GetGame().GetWorkspace().CreateWidgets( "dzr_dquests/GUI/layouts/dzr_quests_screen.layout" );
            
            
            
            
            m_IsInitialized = true;
            
        }
        
		return layoutRoot;
    }
    
    void StartLoader(Param8<string , string , string, string, string, int, string, string> LoaderData)
    {
        m_CustomLayout = LoaderData.param8;
        m_Delay = LoaderData.param6;
        
        LayoutElementsInit();
        
        r_Header.SetText(LoaderData.param1);
        
        r_Description.SetText(LoaderData.param3);
        //r_AdditionalText.SetText(LoaderData.param3);
        
        m_LoadingSound = LoaderData.param4;
        
        
        if(LoaderData.param5 == "0")
        {
            r_BackgroundImage.Show(false);
        }
        else
        {
            r_BackgroundImage.LoadImageFile(0, LoaderData.param5);
        }
        r_Icon.LoadImageFile(0, LoaderData.param7);
        
        LockCharacter();
        
        StartAnimations();
        }
    
    void LoadData(Param7<string , string , string, string, string, int, string> LoaderData)
    {
        if(LoaderData)
        {
        
        //Print(LoaderData);
        
        
    };
    }
    
    void StartAnimations()
    {
        PlayerBase player = PlayerBase.Cast(GetGame().GetPlayer());
        ProgressAsync.DestroyAllPendingProgresses();
        GetGame().GetMission().GetHud().Show(false);
        m_LoadingTimer1.FadeIn(r_MainAnimation, 0.6, false);
        layoutRoot.Show(true);
        ProgressAsync.SetProgressData(r_LoadingBar);
        //GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer2.FadeIn, 500, false, TitleContainer, 1, false);
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer6.FadeIn, 1200, false, r_BackgroundImage, 10, false);
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(player.SetInvisible, 3000, false, true);
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(player.SetDZRQVisibility, 3000, false, true);
        
        
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer4.FadeIn, 4000, false, r_SecondAnimation, 1, false);
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(SetProgress, 5000, false);
        
        // GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer5.FadeIn, m_Delay*1000+3000, false, r_ExitButtonAnimation, 0.5, false);
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(GetGame().GetUIManager().ShowUICursor, m_Delay*1000+3000, false, true);
        
    }
    
    void LockCharacter()
    {
        GetGame().GetMission().GetHud().Show(false);
        BlockPlayer(true); 
    }
    
    void LayoutElementsInit()
    {
        
        Widget layoutRootInner = GetGame().GetWorkspace().CreateWidgets( m_CustomLayout, layoutRoot );
        //Widget layoutRootInner = GetGame().GetWorkspace().CreateWidgets( "dzr_dquests/GUI/layouts/dzr_inner.layout", layoutRoot );
        
        r_LoadingBar = ProgressBarWidget.Cast(layoutRoot.FindAnyWidget("r_LoadingBar"));
        //layoutRoot = GetGame().GetWorkspace().CreateWidgets(  );
        r_MainAnimation = Widget.Cast( layoutRoot.FindAnyWidget("r_MainAnimation") );
        r_ExitButtonAnimation = Widget.Cast( layoutRoot.FindAnyWidget("r_ExitButtonAnimation") );
        r_BackgroundImage = ImageWidget.Cast( layoutRootInner.FindAnyWidget("r_BackgroundImage") );
        r_Logo = ImageWidget.Cast( layoutRootInner.FindAnyWidget("r_Logo") );
        r_Icon = ImageWidget.Cast( layoutRootInner.FindAnyWidget("r_Icon") );
        r_ExitButtonImage = Widget.Cast( layoutRootInner.FindAnyWidget("r_ExitButtonImage") );
        r_ExitButtonMouseOver = Widget.Cast( layoutRootInner.FindAnyWidget("r_ExitButtonMouseOver") );
        r_Header = MultilineTextWidget.Cast( layoutRootInner.FindAnyWidget("r_Header") );
        r_Description = MultilineTextWidget.Cast( layoutRootInner.FindAnyWidget("r_Description") );
        r_AdditionalText = MultilineTextWidget.Cast( layoutRootInner.FindAnyWidget("r_AdditionalText") );
        r_LoadingTextContainer = Widget.Cast( layoutRootInner.FindAnyWidget("r_LoadingTextContainer") );
        r_ExitButton = ButtonWidget.Cast(layoutRootInner.FindAnyWidget("r_ExitButton"));
        r_SecondAnimation = Widget.Cast(layoutRootInner.FindAnyWidget("r_SecondAnimation"));
        //TitleContainer = Widget.Cast(layoutRootInner.FindAnyWidget("TitleContainer"));
        
        //r_ExitButton.SetText("#dzrptl_close");
        //r_LoadingBar.SetColor(Colors.COLOR_RUINED);
        r_ExitButtonMouseOver.Show(false);
        r_SecondAnimation.Show(false);
        //TitleContainer.Show(false);
        r_BackgroundImage.Show(false);
        r_ExitButtonAnimation.Show(false);
        r_LoadingBar.SetCurrent(0);
        
        r_LoadingTextContainer.Show(true);
    }
    
    
    
    void BlockPlayer(bool val)
    {
        
        
        PlayerBase player = PlayerBase.Cast(GetGame().GetPlayer());
        
        if(player)
        {
            player.SetAllowDamage(!val);
            player.DisableSimulation(val);
            player.SetInvisible( val );
            player.ControlsOff(val);
            player.PhysicsEnableGravity( !val );
        }
        
        if(val)
        {
            //m_LoadingSound = "SewersEnterSoundSet";
            PlayMusic(m_LoadingSound);
            GetGame().GetSoundScene().SetSoundVolume(0.1,3);
            GetGame().GetSoundScene().SetMusicVolume(1,3);
        }
        
    }
    
    void PlayMusic(string soundSetName)
    {
        
        
        if (m_ActiveMus) {
            return;
        }
        
        SoundParams soundParams			= new SoundParams(soundSetName);
        SoundObjectBuilder soundBuilder	= new SoundObjectBuilder( soundParams );
        SoundObject soundObject			= soundBuilder.BuildSoundObject();
        soundObject.SetKind( WaveKind.WAVEMUSIC );
        m_ActiveMus = GetGame().GetSoundScene().Play2D(soundObject, soundBuilder);
        m_ActiveMus.Play();
        
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
        this.Close();
        
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
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(SetProgress, tick, false);
        }
        else
        {
            PrintD("---------------------------- SetProgress 341 ------------------------============ m_GlobalTimer > total ");
            
            m_LoadingTimer1.FadeIn(r_ExitButtonAnimation, 0.5, false);
            
            PrintD(m_GlobalTimer.ToString());
            PrintD(total.ToString());
            ProgressAsync.SetProgressData(null);
            ProgressAsync.DestroyAllPendingProgresses();
            m_GlobalTimer = 0;
            r_LoadingTextContainer.Show(false);
        }
        
        PrintD("---------------------------- SetProgress 344 ------------------------============");
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
        
        float m_FadeOutTime = 800.0;
        float m_FadeOutTick = 15.0;
        if(m_ActiveMus)
        {
            float increment = m_ActiveMus.GetVolume() / (m_FadeOutTime / m_FadeOutTick);
            TickVolume(increment, m_FadeOutTick);
        }
        
        PlayerBase player = PlayerBase.Cast(GetGame().GetPlayer());
        player.SetInvisible(false);
        player.SetDZRQVisibility(false);
        BlockPlayer(false);
        player.SetInQuest(false);
        GetGame().GetUIManager().ShowUICursor( false );
        
        
        
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(m_LoadingTimer1.FadeOut, 500, false, r_MainAnimation, 1, false);
        
        GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(this.Exit, 1800, false);
        
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
            float thevalue = m_ActiveMus.GetVolume() - increment;
            if(thevalue < 0)
            { 
                thevalue = 0.01;
            }
            m_ActiveMus.SetVolume(thevalue);
            GetGame().GetCallQueue(CALL_CATEGORY_GAMEPLAY).CallLater(TickVolume, tick, false, increment, tick);
        }
        PrintD("QuestLoading.c TickVolume 389");
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
    
    void PrintD(string text)
    {
        if(DebugMode)
        {
            Print("[DZR_QUESTS ClientGUI DzrQuestLoading Debug] :::: "+ text);
        }
    }
    
}