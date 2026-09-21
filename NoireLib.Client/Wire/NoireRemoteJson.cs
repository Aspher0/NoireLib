using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NoireLib.Remote;

/// <summary>
/// The single definition of how anything on the NoireRemote wire is serialized. The listener and the caller both read
/// and write through it. The protocol is whatever this class says it is.
/// </summary>
public static class NoireRemoteJson
{
    /// <summary>
    /// Builds the settings the wire is defined by. Every call returns a fresh instance. A caller may adjust a copy
    /// without reshaping the protocol for anyone else.
    /// </summary>
    /// <returns>Settings with type name handling off, camelCase names, string enums and the four vector converters.</returns>
    public static JsonSerializerSettings CreateSettings()
    {
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            FloatParseHandling = FloatParseHandling.Double,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            // Members are camelCase on the wire. Dictionary keys cross as written.
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = true,
                },
            },
        };

        settings.Converters.Add(new StringEnumConverter());
        settings.Converters.Add(new NoireRemoteVector2Converter());
        settings.Converters.Add(new NoireRemoteVector3Converter());
        settings.Converters.Add(new NoireRemoteVector4Converter());
        settings.Converters.Add(new NoireRemoteQuaternionConverter());

        return settings;
    }

    // Never a parameterless JsonConvert overload. Those merge JsonConvert.DefaultSettings, a process-global any plugin can assign.
    private static readonly JsonSerializer ValueSerializer = JsonSerializer.Create(CreateSettings());

    // Also refuses content after the top-level value. A separate instance because ToObject(Type, JsonSerializer) toggles CheckAdditionalContent on the serializer it is handed.
    private static readonly JsonSerializer DocumentSerializer = CreateDocumentSerializer();

    private static JsonSerializer CreateDocumentSerializer()
    {
        var serializer = JsonSerializer.Create(CreateSettings());
        serializer.CheckAdditionalContent = true;
        return serializer;
    }

    /// <summary>
    /// Gets the serializer used for a value inside a document, such as one argument or one result.
    /// </summary>
    public static JsonSerializer Serializer => ValueSerializer;

    /// <summary>
    /// Gets the serializer used for a whole document, such as a request envelope or a directory record.
    /// </summary>
    public static JsonSerializer Documents => DocumentSerializer;

    /// <summary>
    /// Serializes a whole document to a JSON string.
    /// </summary>
    /// <param name="value">The document to write.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(object? value)
    {
        var builder = new StringBuilder(256);

        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var jsonWriter = new JsonTextWriter(stringWriter))
        {
            DocumentSerializer.Serialize(jsonWriter, value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Serializes a whole document to UTF-8 bytes.
    /// </summary>
    /// <param name="value">The document to write.</param>
    /// <returns>The JSON text encoded as UTF-8, with no byte order mark.</returns>
    public static byte[] WriteBytes(object? value)
        => new UTF8Encoding(false).GetBytes(Write(value));

    /// <summary>
    /// Reads a whole document from JSON text.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="json">The JSON text.</param>
    /// <returns>The document, or null when the text is empty.</returns>
    /// <exception cref="JsonException">If the text is not valid JSON or carries trailing content.</exception>
    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var stringReader = new StringReader(json!);
        using var jsonReader = new JsonTextReader(stringReader);

        return DocumentSerializer.Deserialize<T>(jsonReader);
    }

    /// <summary>
    /// Reads a whole document from JSON text. Malformed input returns null.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="json">The JSON text.</param>
    /// <returns>The document, or null when the text is empty or malformed.</returns>
    public static T? TryRead<T>(string? json) where T : class
    {
        try
        {
            return Read<T>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a value to a JSON token using the wire settings.
    /// </summary>
    /// <param name="value">The value to convert. Null becomes a null token.</param>
    /// <returns>The token.</returns>
    public static JToken ToToken(object? value)
        => value == null ? JValue.CreateNull() : JToken.FromObject(value, ValueSerializer);

    /// <summary>
    /// Converts a JSON token into a known type using the wire settings.
    /// </summary>
    /// <param name="token">The token to convert. Null returns the type's default.</param>
    /// <param name="type">The type to materialize.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="JsonException">If the token does not fit the type.</exception>
    public static object? FromToken(JToken? token, Type type)
    {
        if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;

        return token.ToObject(type, ValueSerializer);
    }

    /// <summary>
    /// Converts a JSON token into a known type using the wire settings.
    /// </summary>
    /// <typeparam name="T">The type to materialize.</typeparam>
    /// <param name="token">The token to convert.</param>
    /// <returns>The converted value.</returns>
    public static T? FromToken<T>(JToken? token)
        => (T?)FromToken(token, typeof(T));
}
