Option Strict On
Option Explicit On
Imports BirthdayBot.CommandsCommon
Imports Discord
Imports Discord.Net
Imports Discord.WebSocket

Class BirthdayBot
    Const RoleWarningMsg As String =
        "Note: This bot does not have a role set or is unable to use the role specified. " +
        "Update the designated role with `bb.config role (role name/ID)`. This bot cannot function without it."

    Private ReadOnly _dispatchCommands As Dictionary(Of String, CommandHandler)
    Private ReadOnly _cmdsUser As UserCommands
    Private ReadOnly _cmdsHelp As HelpInfoCommands
    Private ReadOnly _cmdsMods As ManagerCommands

    Private WithEvents _client As DiscordSocketClient
    Private _cfg As Configuration
    Private ReadOnly _worker As BackgroundWorker

    Friend ReadOnly Property DiscordClient As DiscordSocketClient
        Get
            Return _client
        End Get
    End Property

    ''' <summary>SyncLock when using. The lock object is itself.</summary>
    Friend ReadOnly Property KnownGuilds As Dictionary(Of ULong, GuildSettings)

    Public Sub New(conf As Configuration, dc As DiscordSocketClient)
        _cfg = conf
        _client = dc
        KnownGuilds = New Dictionary(Of ULong, GuildSettings)

        _worker = New BackgroundWorker(Me, conf.DatabaseSettings)

        ' Command dispatch set-up
        _dispatchCommands = New Dictionary(Of String, CommandHandler)(StringComparer.InvariantCultureIgnoreCase)
        _cmdsUser = New UserCommands(Me, conf)
        For Each item In _cmdsUser.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
        _cmdsHelp = New HelpInfoCommands(Me, conf)
        For Each item In _cmdsHelp.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
        _cmdsMods = New ManagerCommands(Me, conf, _cmdsUser.Commands)
        For Each item In _cmdsMods.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
    End Sub

    Public Async Function Start() As Task
        Await _client.LoginAsync(TokenType.Bot, _cfg.BotToken)
        Await _client.StartAsync()
        _worker.Start()

        Await Task.Delay(-1)
    End Function

    ''' <summary>
    ''' Called only by CancelKeyPress handler.
    ''' </summary>
    Public Async Function Shutdown() As Task
        Await _worker.Cancel()
        Await _client.LogoutAsync()
        _client.Dispose()
    End Function

    Private Function LoadGuild(g As SocketGuild) As Task Handles _client.JoinedGuild, _client.GuildAvailable
        SyncLock KnownGuilds
            If Not KnownGuilds.ContainsKey(g.Id) Then
                Dim gi = GuildSettings.LoadSettingsAsync(_cfg.DatabaseSettings, g.Id).GetAwaiter().GetResult()
                Log("Status", $"Load information for guild {g.Id} ({g.Name})")
                KnownGuilds.Add(g.Id, gi)
            End If
        End SyncLock
        Return Task.CompletedTask
    End Function

    Private Function DiscardGuild(g As SocketGuild) As Task Handles _client.LeftGuild
        SyncLock KnownGuilds
            KnownGuilds.Remove(g.Id)
        End SyncLock
        Return Task.CompletedTask
    End Function

    Private Async Function SetStatus() As Task Handles _client.Connected
        Await _client.SetGameAsync(CommandPrefix + "help")
    End Function

    Private Async Function Dispatch(msg As SocketMessage) As Task Handles _client.MessageReceived
        If TypeOf msg.Channel Is IDMChannel Then Return
        If msg.Author.IsBot Then Return

        ' Limit 3:
        ' For all cases: base command, 2 parameters.
        ' Except this case: "bb.config", subcommand name, subcommand parameters in a single string
        Dim csplit = msg.Content.Split(" ", 3, StringSplitOptions.RemoveEmptyEntries)
        If csplit.Length > 0 Then
            If csplit(0).StartsWith(CommandPrefix, StringComparison.InvariantCultureIgnoreCase) Then
                Dim channel = CType(msg.Channel, SocketTextChannel)
                Dim author = CType(msg.Author, SocketGuildUser)

                ' Determine if it's something we're listening for.
                ' Doing this first before the block check because a block check triggers a database query.
                Dim command As CommandHandler = Nothing
                If Not _dispatchCommands.TryGetValue(csplit(0).Substring(CommandPrefix.Length), command) Then
                    Return
                End If

                ' Ban and role warning check
                Dim roleWarning As Boolean
                Dim isManager = author.GuildPermissions.ManageGuild
                SyncLock KnownGuilds
                    Dim gi = KnownGuilds(channel.Guild.Id)

                    ' Skip ban check if user is a manager
                    If Not isManager Then
                        If gi.IsUserBlockedAsync(author.Id).GetAwaiter().GetResult() Then
                            Return
                        End If
                    End If

                    roleWarning = gi.RoleWarning
                End SyncLock

                Try
                    If roleWarning Then
                        Try
                            Await channel.SendMessageAsync(RoleWarningMsg)
                        Catch ex As HttpException
                            ' Don't let this prevent the bot from continuing command execution.
                        End Try
                    End If
                    Await command(csplit, channel, author)
                Catch ex As Exception
                    If TypeOf ex Is HttpException Then Return
                    Log("Error", ex.ToString())
                    Try
                        channel.SendMessageAsync(":x: An unknown error occurred. It has been reported to the bot owner.").Wait()
                    Catch ex2 As HttpException
                        ' Fail silently.
                    End Try
                End Try
            End If
        End If
    End Function
End Class
