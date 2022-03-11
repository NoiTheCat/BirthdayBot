using System.Text;

namespace BirthdayBot.TextCommands;

internal class CommandDocumentation {
    public string[] Commands { get; }
    public string Usage { get; }
    public string? Examples { get; }

    public CommandDocumentation(IEnumerable<string> commands, string usage, string? examples) {
        var cmds = new List<string>();
        foreach (var item in commands) cmds.Add(CommandsCommon.CommandPrefix + item);
        if (cmds.Count == 0) throw new ArgumentException(null, nameof(commands));
        Commands = cmds.ToArray();
        Usage = usage ?? throw new ArgumentException(null, nameof(usage));
        Examples = examples;
    }

    /// <summary>
    /// Returns a string that can be inserted into a help or usage message.
    /// </summary>
    public string Export() {
        var result = new StringBuilder();
        foreach (var item in Commands) result.Append(", `" + item + "`");
        result.Remove(0, 2);
        result.Insert(0, '●');
        result.AppendLine();
        result.Append("» " + Usage);
        if (Examples != null) {
            result.AppendLine();
            result.Append("» Examples: " + Examples);
        }
        return result.ToString();
    }

    /// <summary>
    /// Creates an embeddable message containing the command documentation.
    /// </summary>
    public Embed UsageEmbed => new EmbedBuilder() {
        Author = new EmbedAuthorBuilder() { Name = "Usage" },
        Description = Export()
    }.Build();
}
