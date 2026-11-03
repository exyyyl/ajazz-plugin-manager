using System;
using System.Collections.Generic;
public class TrulaTests {
    static string TestTrack(string scenario,string format) {
        string title="The Weeknd — Starboy",id="dMMUH_ZpbB0";
        if(scenario=="no-link")id="";
        else if(scenario=="no-title")title="";
        else if(scenario=="no-data") {title="";id="";}
        else if(scenario!="complete")throw new Exception("Выберите тестовую ситуацию.");
        var state=new Dictionary<string,object>{{"hasTrack",true},{"track",new Dictionary<string,object>{{"title",title},{"platform","youtube"},{"externalId",id}}}};
        return "[ТЕСТ] "+TrackMessage.Format(state,format);
    }

    static int count;
    static void Equal(object a, object b) { if(!Equals(a,b)) throw new Exception("Expected "+b+", got "+a); count++; }
    static void Reject(Action action) { try { action(); } catch { count++; return; } throw new Exception("Expected rejection"); }
    public static int Main() {
        var state=Json.Decode("{\"panel\":{\"orderVolume\":30,\"playlistVolume\":0,\"eventVolume\":75,\"isAutoplay\":true},\"source\":{\"orderVolume\":70,\"playlistVolume\":20,\"isTitleVisible\":false}}");
        var cfg=Json.Decode("{\"command\":\"mute\",\"output\":\"all\",\"media\":\"all\"}"); var restore=new Dictionary<string,object>();
        var p=Commands.Plan(state,cfg,restore);
        Equal(Json.Child(p,"panel")["orderVolume"],0); Equal(restore["source.orderVolume"],70); Equal(Json.Child(p,"panel").ContainsKey("eventVolume"),false);
        var muted=Json.Decode(Json.Encode(p)); var persisted=Json.Decode(Json.Encode(restore));
        p=Commands.Plan(muted,cfg,persisted); Equal(Json.Child(p,"panel")["orderVolume"],30); Equal(Json.Child(p,"panel")["playlistVolume"],0); Equal(Json.Child(p,"source")["playlistVolume"],20);
        Reject(()=>Commands.Plan(muted,cfg,new Dictionary<string,object>()));
        cfg["command"]="volumeDown"; cfg["step"]="100"; cfg["output"]="panel"; cfg["media"]="orders";
        p=Commands.Plan(state,cfg,restore); Equal(Json.Child(p,"panel")["orderVolume"],0); Equal(p.ContainsKey("source"),false); Equal(Json.Child(p,"panel").Count,1);
        cfg["command"]="autoplay"; p=Commands.Plan(state,cfg,restore); Equal(Json.Child(p,"panel")["isAutoplay"],false);
        cfg["command"]="titleVisible"; p=Commands.Plan(state,cfg,restore); Equal(Json.Child(p,"source")["isTitleVisible"],true);
        cfg["command"]="unknown"; Reject(()=>Commands.Plan(state,cfg,restore));
        Equal(Commands.Token("https://trula.io/cp/?token=00000000-0000-0000-0000-000000000001"),"00000000-0000-0000-0000-000000000001");
        Reject(()=>Commands.Token("https://other.example/?token=00000000-0000-0000-0000-000000000001")); Reject(()=>Commands.Token("abc\r\nHeader: x"));
        var original=Json.Decode("{\"command\":\"volumeUp\",\"output\":\"all\"}");
        var stream=Commands.Configure("com.projectdeck.trula.stream",original);
        Equal(stream["command"],"mute"); Equal(stream["output"],"source"); Equal(original["command"],"volumeUp");
        var sourceRestore=new Dictionary<string,object>();
        p=Commands.Plan(state,stream,sourceRestore);
        Equal(p.ContainsKey("panel"),false); Equal(Json.Child(p,"source")["orderVolume"],0);
        var sourceMuted=Json.Decode(Json.Encode(state)); sourceMuted["source"]=p["source"];
        p=Commands.Plan(sourceMuted,stream,sourceRestore);
        Equal(Json.Child(p,"source")["orderVolume"],70); Equal(Json.Child(p,"source")["playlistVolume"],20);
        var monitor=Commands.Configure("com.projectdeck.trula.monitor",original);
        p=Commands.Plan(state,monitor,new Dictionary<string,object>());
        Equal(p.ContainsKey("source"),false); Equal(Json.Child(p,"panel")["orderVolume"],0);
        Equal(Commands.Configure("com.projectdeck.trula.playback",original)["command"],"togglePlay");
        Equal(Commands.Configure("com.projectdeck.trula.playback",Json.Decode("{\"musicCommand\":\"nextTrack\"}"))["command"],"nextTrack");
        Equal(Commands.Configure("com.projectdeck.trula.playback",Json.Decode("{\"musicCommand\":\"prevTrack\"}"))["command"],"prevTrack");
        Equal(Commands.Configure("com.projectdeck.trula.playback",Json.Decode("{\"musicCommand\":\"unknown\"}"))["command"],"togglePlay");
        Equal(Commands.Configure("com.projectdeck.trula.next",original)["command"],"nextTrack");
        Equal(Commands.Configure("com.projectdeck.trula.previous",original)["command"],"prevTrack");
        Equal(Commands.Configure("com.projectdeck.trula.connect",original)["command"],"connect");
        Equal(Commands.ConnectionMessage(true,false,"Нет связи с OBS."),"Ссылка сохранена. Нет связи с OBS.");
        Equal(Commands.ConnectionMessage(true,true,"Ошибка"),"Trula подключена к OBS.");
        Equal(Commands.ConnectionMessage(false,true,""),"Подключите Trula кнопкой «Подключить Trula».");
        Equal(Commands.Configure("com.projectdeck.trula.chat",original)["command"],"sendTrackToChat");
        var trackState=Json.Decode("{\"hasTrack\":true,\"track\":{\"title\":\"The Weeknd \\r\\n - Starboy\",\"platform\":\"youtube\",\"externalId\":\"dMMUH_ZpbB0\"}}");
        Equal(TrackMessage.Format(trackState,"both"),"Сейчас играет: The Weeknd - Starboy — https://youtu.be/dMMUH_ZpbB0");
        Equal(TrackMessage.Format(trackState,"title"),"Сейчас играет: The Weeknd - Starboy");
        Equal(TrackMessage.Format(trackState,"link"),"https://youtu.be/dMMUH_ZpbB0");
        Equal(TrackMessage.Channel(" #My_Channel "),"my_channel");
        Reject(()=>TrackMessage.Channel("https://twitch.tv/name"));
        var noTrack=Json.Decode("{\"hasTrack\":false}");Reject(()=>TrackMessage.Format(noTrack,"both"));
        Json.Child(trackState,"track")["externalId"]="bad\r\naddress";
        Equal(TrackMessage.Format(trackState,"link"),"Ссылки нет :C");
        Equal(TrackMessage.Format(trackState,"both"),"Сейчас играет: The Weeknd - Starboy — Ссылки нет :C");
        Json.Child(trackState,"track")["title"]=new string('a',1000);
        Equal(TrackMessage.Format(trackState,"title").Length,500);
        var identity=Json.Decode("{\"client_id\":\"test-client\",\"user_id\":\"123\",\"login\":\"example\",\"scopes\":[\"user:write:chat\"]}");
        TwitchChat.ValidateIdentity(identity,"test-client");
        Reject(()=>TwitchChat.ValidateIdentity(identity,"other-client"));
        identity["scopes"]=new object[]{"chat:read"};Reject(()=>TwitchChat.ValidateIdentity(identity,"test-client"));
        identity.Remove("scopes");Reject(()=>TwitchChat.ValidateIdentity(identity,"test-client"));
        Equal(TwitchChat.DeliveryConfirmed(Json.Decode("{\"data\":[{\"is_sent\":true,\"message_id\":\"test\"}]}")),true);
        Equal(TwitchChat.DeliveryConfirmed(Json.Decode("{\"data\":[{\"is_sent\":false}]}")),false);
        Equal(TwitchChat.DeliveryConfirmed(Json.Decode("{\"data\":[]}")),false);
        Equal(TestTrack("no-link","both"),"[ТЕСТ] Сейчас играет: The Weeknd — Starboy — Ссылки нет :C");
        Equal(TestTrack("no-title","both"),"[ТЕСТ] Сейчас играет: Названия нет :C — https://youtu.be/dMMUH_ZpbB0");
        Equal(TestTrack("no-data","both"),"[ТЕСТ] Нет инфы по треку :C");
        Equal(TestTrack("no-title","title"),"[ТЕСТ] Сейчас играет: Названия нет :C");
        Equal(TestTrack("no-link","link"),"[ТЕСТ] Ссылки нет :C");
        Reject(()=>TestTrack("unknown","both"));
        var levels=Commands.Configure("com.projectdeck.trula.streamlevels",new Dictionary<string,object>());
        p=Commands.Plan(state,levels,restore);Equal(p.ContainsKey("panel"),false);Equal(Json.Child(p,"source")["orderVolume"],50);Equal(Json.Child(p,"source").Count,2);
        var changed=Json.Decode(Json.Encode(state));changed["source"]=p["source"];
        p=Commands.Plan(changed,levels,restore);Equal(Json.Child(p,"source")["playlistVolume"],10);
        changed["source"]=p["source"];p=Commands.Plan(changed,levels,restore);Equal(Json.Child(p,"source")["orderVolume"],50);
        levels["firstLevel"]=0;levels["secondLevel"]=10;
        p=Commands.Plan(state,levels,restore);Equal(Json.Child(p,"source")["orderVolume"],0);
        changed["source"]=p["source"];p=Commands.Plan(changed,levels,restore);Equal(Json.Child(p,"source")["orderVolume"],10);
        levels["secondLevel"]=0;Reject(()=>Commands.Plan(state,levels,restore));
        levels["secondLevel"]=101;Reject(()=>Commands.Plan(state,levels,restore));
        levels=Commands.Configure("com.projectdeck.trula.monitorlevels",new Dictionary<string,object>());
        p=Commands.Plan(state,levels,restore);Equal(p.ContainsKey("source"),false);Equal(Json.Child(p,"panel")["playlistVolume"],50);Equal(Json.Child(p,"panel").ContainsKey("eventVolume"),false);
        var stepCfg=Commands.Configure("com.projectdeck.trula.volumestep",new Dictionary<string,object>{{"volumeOutput","panel"},{"direction","down"},{"step",40}});
        p=Commands.Plan(state,stepCfg,restore);Equal(p.ContainsKey("source"),false);Equal(Json.Child(p,"panel")["orderVolume"],0);Equal(Json.Child(p,"panel").Count,2);
        stepCfg=Commands.Configure("com.projectdeck.trula.volumestep",new Dictionary<string,object>{{"volumeOutput","source"},{"direction","up"},{"step",90}});
        p=Commands.Plan(state,stepCfg,restore);Equal(Json.Child(p,"source")["orderVolume"],100);Equal(Json.Child(p,"source")["playlistVolume"],100);Equal(p.ContainsKey("panel"),false);
        stepCfg["step"]=0;Reject(()=>Commands.Plan(state,stepCfg,restore));
        Equal(Commands.StepState(new[]{0,0},"volumeUp"),"min");
        Equal(Commands.StepState(new[]{100,100},"volumeDown"),"max");
        Equal(Commands.StepState(new[]{0,100},"volumeUp"),"up");
        Equal(Commands.StepState(new[]{30,20},"volumeDown"),"down");
        Console.WriteLine(count+" checks passed"); return 0;
    }
}
