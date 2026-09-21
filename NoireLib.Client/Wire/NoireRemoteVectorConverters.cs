using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Numerics;

namespace NoireLib.Remote;

/// <summary>
/// Writes a <see cref="Vector2"/> as <c>{"x":0,"y":0}</c> and reads either that object or a two element array.
/// </summary>
public sealed class NoireRemoteVector2Converter : JsonConverter<Vector2>
{
    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.X);
        writer.WritePropertyName("y");
        writer.WriteValue(value.Y);
        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.ReadFrom(reader);
        var components = NoireRemoteVectorComponents.Read(token, 2, "Vector2");
        return new Vector2(components[0], components[1]);
    }
}

/// <summary>
/// Writes a <see cref="Vector3"/> as <c>{"x":0,"y":0,"z":0}</c> and reads either that object or a three element array.
/// </summary>
public sealed class NoireRemoteVector3Converter : JsonConverter<Vector3>
{
    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.X);
        writer.WritePropertyName("y");
        writer.WriteValue(value.Y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.Z);
        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.ReadFrom(reader);
        var components = NoireRemoteVectorComponents.Read(token, 3, "Vector3");
        return new Vector3(components[0], components[1], components[2]);
    }
}

/// <summary>
/// Writes a <see cref="Vector4"/> as <c>{"x":0,"y":0,"z":0,"w":0}</c> and reads either that object or a four element array.
/// </summary>
public sealed class NoireRemoteVector4Converter : JsonConverter<Vector4>
{
    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.X);
        writer.WritePropertyName("y");
        writer.WriteValue(value.Y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.Z);
        writer.WritePropertyName("w");
        writer.WriteValue(value.W);
        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.ReadFrom(reader);
        var components = NoireRemoteVectorComponents.Read(token, 4, "Vector4");
        return new Vector4(components[0], components[1], components[2], components[3]);
    }
}

/// <summary>
/// Writes a <see cref="Quaternion"/> as <c>{"x":0,"y":0,"z":0,"w":1}</c> and reads either that object or a four element array.
/// </summary>
public sealed class NoireRemoteQuaternionConverter : JsonConverter<Quaternion>
{
    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.X);
        writer.WritePropertyName("y");
        writer.WriteValue(value.Y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.Z);
        writer.WritePropertyName("w");
        writer.WriteValue(value.W);
        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.ReadFrom(reader);
        var components = NoireRemoteVectorComponents.Read(token, 4, "Quaternion");
        return new Quaternion(components[0], components[1], components[2], components[3]);
    }
}

internal static class NoireRemoteVectorComponents
{
    private static readonly string[] Names = ["x", "y", "z", "w"];

    public static float[] Read(JToken token, int count, string typeName)
    {
        var components = new float[count];

        if (token.Type == JTokenType.Null)
            return components;

        if (token is JArray array)
        {
            if (array.Count != count)
                throw new JsonSerializationException("A " + typeName + " written as an array needs exactly " + count.ToString(CultureInfo.InvariantCulture) + " numbers.");

            for (var index = 0; index < count; index++)
                components[index] = array[index].Value<float>();

            return components;
        }

        if (token is JObject jsonObject)
        {
            for (var index = 0; index < count; index++)
            {
                var value = jsonObject.GetValue(Names[index], StringComparison.OrdinalIgnoreCase);

                if (value != null && value.Type != JTokenType.Null)
                    components[index] = value.Value<float>();
            }

            return components;
        }

        throw new JsonSerializationException("A " + typeName + " reads from an object or an array. It cannot read from " + token.Type + ".");
    }
}
