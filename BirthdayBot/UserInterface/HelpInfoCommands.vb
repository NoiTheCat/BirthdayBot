Option Strict On
Option Explicit On
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

    Private Function CreateHelpEmbed() As (EmbedBuilder, EmbedBuilder)
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
                $" » Removes your information from this bot."
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

        Const betamsg = "Birthday Bot is still in active development and may be a little rough around the edges. " +
            "If you encounter problems or have suggestions on improving existing features, please send detailed(!) " +
            "information to `Noi#7890`." + vbLf + "Thank you for giving this bot a try!"
        helpManager.Description = betamsg
        helpNoManager.Description = betamsg

        Return (helpNoManager, helpManager)
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
        Const tzhelp = "To ensure that events are recognized at your local time, you may specify the time " +
            "zone in which events take place. Time zone parameters take values from the IANA Time Zone Database, " +
            "also known as the Olson Database." + vbLf + vbLf +
            "A list of values can be found at the following link: https://en.wikipedia.org/wiki/List_of_tz_database_time_zones" + vbLf +
            "Most zone names within this list are supported."
        Dim embed As New EmbedBuilder
        embed.AddField(New EmbedFieldBuilder() With {
            .Name = "About time zone parameters",
            .Value = tzhelp
        })
        Await reqChannel.SendMessageAsync("", embed:=embed)
    End Function

    Private Async Function CmdInfo(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Dim embed As New EmbedBuilder
        embed.AddField(New EmbedFieldBuilder With {
            .Name = "BirthdayBot",
            .Value = "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2)
        })
        ' TODO: Add more fun stats.
        ' Ideas: number of servers, number of current birthdays, uptime
        Await reqChannel.SendMessageAsync("", embed:=embed)
    End Function
End Class
