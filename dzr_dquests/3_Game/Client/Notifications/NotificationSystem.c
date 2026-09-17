/*
modded class NotificationRuntimeData
{
    
	static void SendNotificationToPlayerIdentityExtended( PlayerIdentity player, float show_time, string title_text, string detail_text = "", string icon = "" )
	{
        //Not modded
        ScriptRPC rpc = new ScriptRPC();
		
		rpc.Write(show_time);
		rpc.Write(title_text);
		rpc.Write(detail_text);
		rpc.Write(icon);
        
		rpc.Send(null, ERPCs.RPC_SEND_NOTIFICATION_EXTENDED, true, player);
        //Not modded
        
        FileHandle fhandle;
        string path = "$profile:DZR";
        if(!FileExist(path)) MakeDirectory(path);
        
        path = "$profile:DZR\\Quests";
        if(!FileExist(path)) MakeDirectory(path);
        
        int hour;
		int minute;
		int second;
		int year;
		int month;
		int day;
		GetHourMinuteSecond(hour, minute, second);
		GetYearMonthDay(year, month, day);
		string timestamp = year.ToString()+"."+month.ToString()+"."+day.ToString()+" "+hour.ToString()+":"+minute.ToString()+":"+second.ToString();
        
        path = "$profile:DZR\\Quests\\ClientLog.txt";
        if(!FileExist(path)) 
        {
  			fhandle	=	OpenFile(path, FileMode.WRITE);
            FPrintln(fhandle, "|||||||| DZR Quests: Message Log ||||||\r\n" + timestamp+" || Initialized.");
            CloseFile(fhandle);
        }
        else
        {
            fhandle	=	OpenFile(path, FileMode.APPEND);
            FPrintln(fhandle, timestamp+" ||"+player.GetPlainId()+"|| "+title_text+" : "+detail_text);
            CloseFile(fhandle);
        };
        
        
        
        
    }
}
*/