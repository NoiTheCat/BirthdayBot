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
        Dim managerField As New EmbedFieldBuilder With {
            .Name = "Commands for server managers",
            .Value =
                $"{mpfx}role (role name or ID)`" + vbLf +
                " » Configures the role to apply to users having birthdays." + vbLf +
                $"{mpfx}channel (channel name or ID)`" + vbLf +
                " » Configures the channel to use for announcements. Leave blank to disable." + vbLf +
                $"{mpfx}zone (time zone name)`" + vbLf +
                " » Sets the default time zone for all dates that don't have their own zone set." + vbLf +
                $" »» See `{CommandPrefix}help-tzdata`. Leave blank to set to UTC." + vbLf +
                $"{mpfx}block/unblock (user mention or ID)`" + vbLf +
                " » Prevents or allows usage of bot commands to the given user." + vbLf +
                $"{mpfx}moderated on/off`" + vbLf +
                " » Prevents or allows usage of bot commands to all users excluding managers." + vbLf +
                $"{cpfx}override (user mention or ID) (command)`" + vbLf +
                " » Performs a command on behalf of the given user." + vbLf +
                " »» Command may be either `set`, `zone`, or `remove` plus appropriate parameters."
        }

        Dim helpNoManager As New EmbedBuilder
        helpNoManager.AddField(cmdField)

        Dim helpManager As New EmbedBuilder
        helpManager.AddField(cmdField)
        helpManager.AddField(managerField)

        Return (helpNoManager.Build(), helpManager.Build())
    End Function

    Private Async Function CmdHelp(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Determine if an additional message about an invalid role should be added.
        Dim useFunctionMessage = False
        Dim gs As GuildSettings
        SyncLock Instance.KnownGuilds
            gs = Instance.KnownGuilds(reqChannel.Guild.Id)
        End SyncLock
        If Not gs.RoleId.HasValue Then
            useFunctionMessage = True
        End If

        ' Determine if the user asking is a manager
        Dim showManagerCommands = reqUser.GuildPermissions.ManageGuild

        Await reqChannel.SendMessageAsync("", embed:=If(showManagerCommands, _helpEmbedManager, _helpEmbed))
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
