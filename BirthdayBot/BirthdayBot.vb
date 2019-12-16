Imports System.Collections.Concurrent
Imports BirthdayBot.CommandsCommon
Imports Discord
Imports Discord.Net
Imports Discord.Webhook
Imports Discord.WebSocket

Class BirthdayBot
    Private ReadOnly _dispatchCommands As Dictionary(Of String, CommandHandler)
    Private ReadOnly _cmdsUser As UserCommands
    Private ReadOnly _cmdsListing As ListingCommands
    Private ReadOnly _cmdsHelp As HelpInfoCommands
    Private ReadOnly _cmdsMods As ManagerCommands

    Private WithEvents Client As DiscordShardedClient
    Private ReadOnly _worker As BackgroundServiceRunner

    Friend ReadOnly Property Config As Configuration

    Friend ReadOnly Property DiscordClient As DiscordShardedClient
        Get
            Return Client
        End Get
    End Property
    Friend ReadOnly Property GuildCache As ConcurrentDictionary(Of ULong, GuildStateInformation)
    Friend ReadOnly Property LogWebhook As DiscordWebhookClient

    Public Sub New(conf As Configuration, dc As DiscordShardedClient)
        Config = conf
        Client = dc
        LogWebhook = New DiscordWebhookClient(conf.LogWebhook)
        GuildCache = New ConcurrentDictionary(Of ULong, GuildStateInformation)

        _worker = New BackgroundServiceRunner(Me)

        ' Command dispatch set-up
        _dispatchCommands = New Dictionary(Of String, CommandHandler)(StringComparer.InvariantCultureIgnoreCase)
        _cmdsUser = New UserCommands(Me, conf)
        For Each item In _cmdsUser.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
        _cmdsListing = New ListingCommands(Me, conf)
        For Each item In _cmdsListing.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
        _cmdsHelp = New HelpInfoCommands(Me, conf, DiscordClient)
        For Each item In _cmdsHelp.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
        _cmdsMods = New ManagerCommands(Me, conf, _cmdsUser.Commands)
        For Each item In _cmdsMods.Commands
            _dispatchCommands.Add(item.Item1, item.Item2)
        Next
    End Sub

    Public Async Function Start() As Task
        Await Client.LoginAsync(TokenType.Bot, Config.BotToken)
        Await Client.StartAsync()

#If Not DEBUG Then
        Log("Background processing", "Delaying start")
        Await Task.Delay(90000) ' TODO don't keep doing this
        Log("Background processing", "Delay complete")
#End If
        _worker.Start()

        Await Task.Delay(-1)
    End Function

    ''' <summary>
    ''' Called only by CancelKeyPress handler.
    ''' </summary>
    Public Async Function Shutdown() As Task
        Await _worker.Cancel()
        Await Client.LogoutAsync()
        Client.Dispose()
    End Function

    Private Async Function LoadGuild(g As SocketGuild) As Task Handles Client.JoinedGuild, Client.GuildAvailable
        If Not GuildCache.ContainsKey(g.Id) Then
            Dim gi = Await GuildStateInformation.LoadSettingsAsync(Config.DatabaseSettings, g.Id)
            GuildCache.TryAdd(g.Id, gi)
        End If
    End Function

    Private Function DiscardGuild(g As SocketGuild) As Task Handles Client.LeftGuild
        Dim rm As GuildStateInformation = Nothing
        GuildCache.TryRemove(g.Id, rm)
        Return Task.CompletedTask
    End Function

    Private Async Function SetStatus(shard As DiscordSocketClient) As Task Handles Client.ShardConnected
        Await shard.SetGameAsync(CommandPrefix + "help")
    End Function

    Private Async Function Dispatch(msg As SocketMessage) As Task Handles Client.MessageReceived
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

                ' Ban check
                Dim gi = GuildCache(channel.Guild.Id)
                ' Skip ban check if user is a manager
                If Not gi.IsUserModerator(author) Then
                    If gi.IsUserBlockedAsync(author.Id).GetAwaiter().GetResult() Then
                        Return
                    End If
                End If

                ' Execute the command
                Try
                    Log("Command", $"{channel.Guild.Name}/{author.Username}#{author.Discriminator}: {msg.Content}")
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

                ' Immediately check for role updates in the invoking guild
                Await _worker.BirthdayUpdater.SingleUpdateFor(channel.Guild)
            End If
        End If
    End Function
End Class
