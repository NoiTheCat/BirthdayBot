Imports Discord
Imports Discord.WebSocket

Friend Class HelpInfoCommands
    Inherits CommandsCommon

    Private ReadOnly _helpEmbed As Embed
    Private ReadOnly _helpEmbedManager As Embed

    Sub New(inst As BirthdayBot, db As Configuration)
        MyBase.New(inst, db)
        Dim embeds = CreateHelpEmbed()
        _helpEmbed = embeds.Item1
        _helpEmbedManager = embeds.Item2
    End Sub

    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("help", AddressOf CmdHelp),
                ("help-tzdata", AddressOf CmdHelpTzdata),
                ("info", AddressOf CmdInfo)
            }
        End Get
    End Property

    Private Function CreateHelpEmbed() As (Embed, Embed)
        Dim cpfx = $"●`{CommandPrefix}"
        ' Normal section
        Dim cmdField As New EmbedFieldBuilder With {
            .Name = "Commands",
            .Value =
                $"{cpfx}help`, `{CommandPrefix}info`, `{CommandPrefix}help-tzdata`" + vbLf +
                $" » Various help and informational messages." + vbLf +
                $"{cpfx}set (date) [zone]`" + vbLf +
                $" » Registers your birth date. Time zone is optional." + vbLf +
                $" »» Examples: `{CommandPrefix}set jan-31`, `{CommandPrefix}set 15-aug America/Los_Angeles`." + vbLf +
                $"{cpfx}zone (zone)`" + vbLf +
                $" » Sets your local time zone. See `{CommandPrefix}help-tzdata`." + vbLf +
                $"{cpfx}remove`" + vbLf +
                $" » Removes your birthday information from this bot."
        }

        ' Manager section
        Dim mpfx = cpfx + "config "
        Dim moderatorField As New EmbedFieldBuilder With {
            .Name = "Commands for server managers and bot moderators",
            .Value =
                $"{mpfx}role (role name or ID)`" + vbLf +
                " » Sets the role to apply to users having birthdays. **Required for bot function.**" + vbLf +
                $"{mpfx}channel (channel name or ID)`" + vbLf +
                " » Sets the channel to use for announcements. Leave blank to disable." + vbLf +
                $"{mpfx}message (message)`" + vbLf +
                " » Sets a custom announcement message. The names of those celebrating birthdays are appended to it." + vbLf +
                $"{mpfx}modrole (role name or ID)`" + vbLf +
                " » Sets the designated role for bot moderators, granting them access to `bb.config` and `bb.override`." + vbLf +
                $"{mpfx}zone (time zone name)`" + vbLf +
                " » Sets the default time zone for users without their own zone." + vbLf +
                $" »» See `{CommandPrefix}help-tzdata`. Leave blank to set to default." + vbLf +
                $"{mpfx}block/unblock (user mention or ID)`" + vbLf +
                " » Prevents or allows usage of bot commands to the given user." + vbLf +
                $"{mpfx}moderated on/off`" + vbLf +
                " » Prevents or allows usage of bot commands to everyone excluding moderators." + vbLf +
                $"{cpfx}override (user mention or ID) (command w/ parameters)`" + vbLf +
                " » Performs certain commands on behalf of another given user."
        }

        Dim helpNoManager As New EmbedBuilder
        helpNoManager.AddField(cmdField)

        Dim helpModerator As New EmbedBuilder
        helpModerator.AddField(cmdField)
        helpModerator.AddField(moderatorField)

        Return (helpNoManager.Build(), helpModerator.Build())
    End Function

    Private Async Function CmdHelp(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Determine if the user asking is a moderator
        Dim showManagerCommands As Boolean
        SyncLock Instance.KnownGuilds
            showManagerCommands = Instance.KnownGuilds(reqChannel.Guild.Id).IsUserModerator(reqUser)
        End SyncLock

        Await reqChannel.SendMessageAsync(embed:=If(showManagerCommands, _helpEmbedManager, _helpEmbed))
    End Function

    Private Async Function CmdHelpTzdata(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Const tzhelp = "To have events recognized in your local time, you may specify a time zone. Time zone names " +
            "from the IANA Time Zone Database (a.k.a. Olson Database) are recognized by this bot." + vbLf + vbLf +
            "These values can be found at the following link, under the 'TZ database name' column: " +
            "https://en.wikipedia.org/wiki/List_of_tz_database_time_zones"
        Dim embed As New EmbedBuilder
        embed.AddField(New EmbedFieldBuilder() With {
            .Name = "Regarding time zone parameters",
            .Value = tzhelp
        })
        Await reqChannel.SendMessageAsync("", embed:=embed.Build())
    End Function

    Private Async Function CmdInfo(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Dim embed As New EmbedBuilder With {
            .Description = "Thanks for using Birthday Bot!" + vbLf +
            "Feel free to send feedback and/or suggestions by contacting the author." +
            vbLf + vbLf + "This bot is a work in progress. A additional features are planned to be added at a later date."
        }
        Dim verstr = Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2)
        embed.AddField(New EmbedFieldBuilder With {
            .Name = "Birthday Bot",
            .Value = $"v{verstr} - https://github.com/Noikoio/BirthdayBot"
        })
        ' TODO: Add more fun stats.
        ' Ideas: number of servers, number of total people currently having a birthday, uptime
        Await reqChannel.SendMessageAsync("", embed:=embed.Build())
    End Function
End Class
