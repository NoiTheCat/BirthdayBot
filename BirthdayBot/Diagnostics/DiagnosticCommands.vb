Imports Discord.WebSocket
Imports Discord.Webhook
Imports System.Text
''' <summary>
''' Implements the command used by global bot moderators to get operation info for each guild.
''' </summary>
Class DiagnosticCommands
    Inherits CommandsCommon

    Private ReadOnly _webhook As DiscordWebhookClient
    Private ReadOnly _diagChannel As ULong

    Sub New(inst As BirthdayBot, db As Configuration)
        MyBase.New(inst, db)

        _webhook = New DiscordWebhookClient(db.LogWebhook)
        _diagChannel = db.DiagnosticChannel
    End Sub

    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("diag", AddressOf CmdDiag),
                ("checkme", AddressOf CmdCheckme)
            }
        End Get
    End Property

    ' Dumps all known guild information to the given webhook
    Private Async Function CmdDiag(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Ignore if not in the correct channel
        If reqChannel.Id <> _diagChannel Then Return

        ' Requires two parameters: (cmd) (guild id)
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(":x: Usage: (command) (guild ID)")
            Return
        End If

        Dim rgid As ULong
        If Not ULong.TryParse(param(1), rgid) Then
            Await reqChannel.SendMessageAsync(":x: Cannot parse numeric guild ID")
            Return
        End If

        Dim guild = Instance.DiscordClient.GetGuild(rgid)
        If guild Is Nothing Then
            Await reqChannel.SendMessageAsync(":x: Guild is not known to the bot")
        End If

        Dim gi = Instance.GuildCache(rgid)
        If gi Is Nothing Then
            Await reqChannel.SendMessageAsync(":x: Guild is known, but information is not available.")
        End If

        Await reqChannel.SendMessageAsync(":white_check_mark: Compiling info and sending to webhook.")

        Dim report As New StringBuilder
        report.AppendLine("=-=-=-=-GUILD INFORMATION-=-=-=-=")
        ' TODO dump config info
        report.AppendLine($"{guild.Id}: {guild.Name}")
        report.AppendLine($"User count: {guild.Users.Count}")
        report.AppendLine("---")
        SyncLock gi.OperationLog
            report.Append(gi.OperationLog.GetDiagStrings())
        End SyncLock
        report.AppendLine("**Note**: Full stack traces for captured exceptions are printed to console.")

        Await _webhook.SendMessageAsync(report.ToString())
    End Function

    Private Async Function CmdCheckme(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Await _webhook.SendMessageAsync($"{reqUser.Username}#{reqUser.Discriminator}: {reqChannel.Guild.Id} checkme")
    End Function
End Class
