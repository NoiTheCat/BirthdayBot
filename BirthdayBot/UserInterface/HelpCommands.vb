Option Strict On
Option Explicit On
Imports Discord
Imports Discord.WebSocket

Friend Class HelpCommands
    Inherits CommandsCommon

    Private _helpEmbed As EmbedBuilder ' Lazily generated in the help command handler
    Private _helpManagerInfo As EmbedFieldBuilder ' Same

    Sub New(inst As BirthdayBot, db As Configuration)
        MyBase.New(inst, db)
    End Sub

    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("help", AddressOf CmdHelp)
            }
        End Get
    End Property

    Private Async Function CmdHelp(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Const FunctionMsg = "Attention server manager: A designated birthday role has not yet been set. " +
            "This bot requires the ability to be able to set and unset the specified role onto all users. " +
            "It cannot function without it." + vbLf +
            "To designate a birthday role, issue the command `{0}config role (role name/ID)`."

        If _helpEmbed Is Nothing Then
            Dim em As New EmbedBuilder
            With em
                .Footer = New EmbedFooterBuilder With {
                    .Text = Discord.CurrentUser.Username,
                    .IconUrl = Discord.CurrentUser.GetAvatarUrl()
                }
                .Title = "Help & About"
                .Description = "Birthday Bot: A utility to assist with acknowledging birthdays and other annual events.\n" +
                    "**Currently a work in progress. There will be bugs. Features may change or be removed.**"
            End With
            Dim cpfx = $"●`{CommandPrefix}"
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
            em.AddField(cmdField)
            _helpEmbed = em

            Dim mpfx = cpfx + "config "
            _helpManagerInfo = New EmbedFieldBuilder With {
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
        End If

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

        Await reqChannel.SendMessageAsync(If(useFunctionMessage, String.Format(FunctionMsg, CommandPrefix), ""),
                                          embed:=If(showManagerCommands, _helpEmbed.AddField(_helpManagerInfo), _helpEmbed))
    End Function
End Class
