using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

sealed class Vector3Converter : JsonConverter
{
    public override bool CanConvert(System.Type t) => t == typeof(Vector3);

    public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
    {
        var v = (Vector3)value;
        w.WriteStartObject();
        w.WritePropertyName("x"); w.WriteValue(v.x);
        w.WritePropertyName("y"); w.WriteValue(v.y);
        w.WritePropertyName("z"); w.WriteValue(v.z);
        w.WriteEndObject();
    }

    public override object ReadJson(JsonReader r, System.Type t, object existing, JsonSerializer s)
    {
        var o = JObject.Load(r);
        return new Vector3(
            (float)(o["x"] ?? 0f),
            (float)(o["y"] ?? 0f),
            (float)(o["z"] ?? 0f));
    }
}