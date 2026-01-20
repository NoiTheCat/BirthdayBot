using System.ComponentModel;
using Newtonsoft.Json;

namespace WorldTime.Config;

public class DatabaseSettings {
    [JsonProperty(Required = Required.DisallowNull)]
    [Description("The host name, and optionally port, on which the PostgreSQL database is running.")]
    [DefaultValue("localhost")]
    public string Host { get; set; } = "localhost";

    [JsonProperty(Required = Required.DisallowNull)]
    [Description("The PosgreSQL to connect to. If left blank, the username is used.")]
    public string? Database { get; set; } = null;

    [JsonProperty(Required = Required.DisallowNull)]
    [Description("The PostgreSQL username to connect with.")]
    public string Username { get; set; } = null!;

    [JsonProperty(Required = Required.Always)]
    [Description("The password for the specified PostgreSQL user.")]
    public string Password { get; set; } = null!;

    internal void Validate() { }
}
