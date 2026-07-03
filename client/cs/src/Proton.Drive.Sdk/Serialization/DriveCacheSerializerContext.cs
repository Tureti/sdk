using System.Text.Json.Serialization;
using Proton.Drive.Sdk.Volumes;

namespace Proton.Drive.Sdk.Serialization;

#pragma warning disable SA1114, SA1118 // Disable style analysis warnings due to attribute spanning multiple lines
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    RespectRequiredConstructorParameters = true)]
#pragma warning restore SA1114, SA1118
[JsonSerializable(typeof(VolumeId?))]
internal sealed partial class DriveCacheSerializerContext : JsonSerializerContext;
