using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Converters;

//[JsonConverter(typeof(StringEnumConverter))]
[DataContract]
public enum TextStyleModel {
[EnumMember(Value = "NONE")]
    NONE,
[EnumMember(Value = "BOLD")]
    BOLD,
[EnumMember(Value = "ITALIC")]
    ITALIC,
[EnumMember(Value = "UNDERLINE")]
    UNDERLINE,
[EnumMember(Value = "TEXT_ALIGNMENT")]
    TEXT_ALIGNMENT,
[EnumMember(Value = "QUOTE")]
    QUOTE,
[EnumMember(Value = "REFER_LINK")]
    REFER_LINK,
[EnumMember(Value = "IMAGE")]
    IMAGE,
[EnumMember(Value = "BULLET_LIST")]
    BULLET_LIST,
[EnumMember(Value = "TEXT_SIZE_LARGE")]
    TEXT_SIZE_LARGE
}