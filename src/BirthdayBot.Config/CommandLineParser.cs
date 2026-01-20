using CommandLine;

namespace WorldTime.Config;

public class CommandLineParser {
    [Option('c', "config")]
    public string? ConfigFile { get; set; }

    [Option("shardtotal")]
    public int? ShardTotal { get; set; }

    [Option("shardrange")]
    public string? ShardRange { get; set; }

    public static CommandLineParser? Parse(string[] args) {
        CommandLineParser? result = null;

        new Parser(settings => {
            settings.IgnoreUnknownArguments = true;
            settings.AutoHelp = false;
            settings.AutoVersion = false;
        }).ParseArguments<CommandLineParser>(args)
            .WithParsed(p => result = p)
            .WithNotParsed(e => { /* ignore */ });
        return result;
    }
}