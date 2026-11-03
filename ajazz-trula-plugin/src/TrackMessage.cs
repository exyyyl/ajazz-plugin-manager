using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class TrackMessage {
    public static string Channel(string value) {
        value=(value??"").Trim().TrimStart('#').ToLowerInvariant();
        if(value.Length>0 && !Regex.IsMatch(value,"^[a-z0-9_]{1,25}$")) throw new Exception("Укажите имя канала Twitch без ссылки.");
        return value;
    }
    public static string Format(Dictionary<string,object> state,string format) {
        var track=Json.Child(state,"track");
        if(Json.Text(state,"hasTrack")!="True") throw new Exception("В Trula сейчас нет трека.");
        string title=Regex.Replace(Json.Text(track,"title"),@"[\s\p{Cc}]+"," ").Trim();
        string platform=Json.Text(track,"platform"),id=Json.Text(track,"externalId"),link="";
        if(platform=="youtube" && Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$")) link="https://youtu.be/"+id;
        if(platform=="vk" && Regex.IsMatch(id,@"^-?\d+_\d+$")) link="https://vk.com/video"+id;
        if(title.Length==0 && link.Length==0)return "Нет инфы по треку :C";
        if(link.Length==0)link="Ссылки нет :C";
        if(format=="link")return link;
        if(format!="title" && format!="both")throw new Exception("Неизвестный формат сообщения.");
        if(title.Length==0)title="Названия нет :C";
        string suffix=format=="both" && link.Length>0?" — "+link:"";
        int limit=500-"Сейчас играет: ".Length-suffix.Length;
        if(title.Length>limit) {int cut=limit-1;if(char.IsHighSurrogate(title[cut-1]))cut--;title=title.Substring(0,cut)+"…";}
        return "Сейчас играет: "+title+suffix;
    }
}
