using System;
using System.Collections.Generic;
public class TrulaTests {
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
        Console.WriteLine(count+" checks passed"); return 0;
    }
}
