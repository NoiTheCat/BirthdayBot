Option Strict On
Option Explicit On
Imports Discord
Imports Discord.WebSocket

Friend Class HelpCommands
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
                ("help", AddressOf CmdHelp)
            }
        End Get
    End Property

    Private Function CreateHelpEmbed() As (EmbedBuilder, EmbedBuilder)
        Dim title = "Help & About"
        Dim description = "Birthday Bot: A utility to assist with acknowledging birthdays and other annual events." + vbLf +
            "**Currently a work in progress. There will be bugs. Features may change or be removed.**"
        Dim footer As New EmbedFooterBuilder With {
            .Text = Discord.CurrentUser.Username,
            .IconUrl = Discord.CurrentUser.GetAvatarUrl()
        }

        Dim cpfx = $"●`{CommandPrefix}"
        ' Normal section
        Dim cmdField As New EmbedFieldBuilder With {
            .Name = "Commands",
            .Value =
                $"{cpfx}help`, `{CommandPrefix}info`, `{CommandPrefix}tzdata`" + vbLf +
                $" » Various help messages." + vbLf +
                $"{cpfx}set (date) [zone]`" + vbLf +
                $" » Registers your birth date, with optional time zone." + vbLf +
                $" »» Examples: `{CommandPrefix}set jan-31 America/New_York`, `{CommandPrefix}set 15-aug Europe/Stockholm`." + vbLf +
                $"{cpfx}set-tz (zone)`" + vbLf +
                $" » Sets your local time zone. Only accepts certain values. See `{CommandPrefix}tzdata`." + vbLf +
                $"{cpfx}remove`" + vbLf +
                $" » Removes all your information from this bot."
        }

        ' Manager section
        Dim mpfx = cpfx + "config "
        Dim managerField As New EmbedFieldBuilder With {
            .Name = "Commands for server managers",
            .Value =
                $"{mpfx}role (role name or ID)`" + vbLf +
                " » Specifies which role to apply to users having birthdays." + vbLf +
                $"{mpfx}channel (channel name or ID)`" + vbLf +
                " » Sets the birthday and event announcement channel. Leave blank to disable announcements." + vbLf +
                $"{mpfx}set-tz (time zone name)`" + vbLf +
                " » Sets the default time zone to use with all dates. Leave blank to revert to default." + vbLf +
                $" » Only accepts certain values. See `{CommandPrefix}tzdata`." + vbLf +
                $"{mpfx}ban/unban (user mention or ID)`" + vbLf +
                " » Restricts or reallows access to this bot for the given user." + vbLf +
                $"{mpfx}ban-all/unban-all`" + vbLf +
                " » Restricts or reallows access to this bot for all users. Server managers are exempt." + vbLf +
                $"{cpfx}override (user ID) (regular command)`" + vbLf +
                " » Performs a command on behalf of the given user."
        }

        Dim helpNoManager As New EmbedBuilder
        With helpNoManager
            .Footer = footer
            .Title = title
            .Description = description
            .AddField(cmdField)
        End With

        Dim helpManager As New EmbedBuilder
        With helpManager
            .Footer = footer
            .Title = title
            .Description = description
            .AddField(cmdField)
            .AddField(managerField)
        End With

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
End Class
