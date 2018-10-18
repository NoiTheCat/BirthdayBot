Imports System.Net
Imports System.Text
Imports System.Threading
Imports Discord.WebSocket
Imports NodaTime

''' <summary>
''' BirthdayBot's periodic task. Frequently wakes up to take various actions.
''' </summary>
Class BackgroundWorker
    Private ReadOnly _bot As BirthdayBot
    Private ReadOnly _db As Database
    Private ReadOnly Property WorkerCancel As New CancellationTokenSource
    Private _workerTask As Task
    Const Interval = 45 ' How often the worker wakes up, in seconds. Adjust as needed.
    Private _clock As IClock

    Sub New(instance As BirthdayBot, dbsettings As Database)
        _bot = instance
        _db = dbsettings
        _clock = SystemClock.Instance ' can replace with FakeClock here when testing
    End Sub

    Public Sub Start()
        _workerTask = Task.Factory.StartNew(AddressOf WorkerLoop, WorkerCancel.Token,
                                            TaskCreationOptions.LongRunning, TaskScheduler.Default)
    End Sub

    Public Async Function Cancel() As Task
        WorkerCancel.Cancel()
        Await _workerTask
    End Function

    ''' <summary>
    ''' Background task. Kicks off many other tasks.
    ''' </summary>
    Private Async Function WorkerLoop() As Task
        Try
            While Not WorkerCancel.IsCancellationRequested
                ' Start background tasks.
                Dim bgTasks As New List(Of Task) From {
                    ReportServerCount(),
                    BirthdayAsync()
                }
                Await Task.WhenAll(bgTasks)

                ' All done. Wait until we have to work again.
                Await Task.Delay(Interval * 1000, WorkerCancel.Token)
            End While
        Catch ex As TaskCanceledException
            Return
        End Try
    End Function

#Region "Birthday handling"
    ''' <summary>
    ''' Birthday tasks processing. Sets up a task per guild and waits on them.
    ''' </summary>
    Private Async Function BirthdayAsync() As Task
        Dim tasks As New List(Of Task(Of Integer))
        For Each guild In _bot.DiscordClient.Guilds
            Dim t = BirthdayGuildProcessAsync(guild)
            tasks.Add(t)
        Next
        Try
            Await Task.WhenAll(tasks)
        Catch ex As Exception
            Dim exs = From task In tasks
                      Where task.Exception IsNot Nothing
                      Select task.Exception
            Log("Error", "Encountered one or more unhandled exceptions during birthday processing.")
            For Each iex In exs
                Log("Error", iex.ToString())
            Next
        End Try

        ' Usage report: Show how many announcements were done
        Dim announces = 0
        Dim guilds = 0
        For Each task In tasks
            If task.Result > 0 Then
                announces += task.Result
                guilds += 1
            End If
        Next
        If announces > 0 Then Log("Background", $"Announcing {announces} birthday(s) in {guilds} guild(s).")
    End Function

    ''' <summary>
    ''' Birthday processing for an individual guild.
    ''' </summary>
    ''' <returns>Number of birthdays announced.</returns>
    Private Async Function BirthdayGuildProcessAsync(guild As SocketGuild) As Task(Of Integer)
        ' Gather required information
        Dim tz As String
        Dim users As IEnumerable(Of GuildUserSettings)
        Dim role As SocketRole = Nothing
        Dim channel As SocketTextChannel = Nothing
        SyncLock _bot.KnownGuilds
            If Not _bot.KnownGuilds.ContainsKey(guild.Id) Then Return 0
            Dim gs = _bot.KnownGuilds(guild.Id)
            tz = gs.TimeZone
            users = gs.Users

            If gs.AnnounceChannelId.HasValue Then channel = guild.GetTextChannel(gs.AnnounceChannelId.Value)
            If gs.RoleId.HasValue Then role = guild.GetRole(gs.RoleId.Value)
            If role Is Nothing Then
                gs.RoleWarning = True
                Return 0
            End If
        End SyncLock

        ' Determine who's currently having a birthday
        Dim birthdays = BirthdayCalculate(users, tz)
        ' Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.

        ' Set birthday role, get list of users now having birthdays
        Dim announceNames = Await BirthdayApplyAsync(guild, role, birthdays)
        If announceNames.Count <> 0 Then
            ' Send out announcement message
            Await BirthdayAnnounceAsync(guild, channel, announceNames)
        End If

        Return announceNames.Count
    End Function

    ''' <summary>
    ''' Gets all known users from the given guild and returns a list including only those who are
    ''' currently experiencing a birthday in the respective time zone.
    ''' </summary>
    Private Function BirthdayCalculate(guildUsers As IEnumerable(Of GuildUserSettings), defaultTzStr As String) As HashSet(Of ULong)
        Dim birthdayUsers As New HashSet(Of ULong)

        Dim defaultTz As DateTimeZone = Nothing
        If defaultTzStr IsNot Nothing Then
            defaultTz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(defaultTzStr)
        End If
        defaultTz = If(defaultTz, DateTimeZoneProviders.Tzdb.GetZoneOrNull("UTC"))
        ' TODO determine defaultTz from guild's voice region

        For Each item In guildUsers
            ' Determine final time zone to use for calculation
            Dim tz As DateTimeZone = Nothing
            If item.TimeZone IsNot Nothing Then
                ' Try user-provided time zone
                tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(item.TimeZone)
            End If
            tz = If(tz, defaultTz)

            Dim targetMonth = item.BirthMonth
            Dim targetDay = item.BirthDay

            Dim checkNow = _clock.GetCurrentInstant().InZone(tz)
            ' Special case: If birthday is February 29 and it's not a leap year, recognize it on March 1st
            If targetMonth = 2 And targetDay = 29 And Not Date.IsLeapYear(checkNow.Year) Then
                targetMonth = 3
                targetDay = 1
            End If
            If targetMonth = checkNow.Month And targetDay = checkNow.Day Then
                birthdayUsers.Add(item.UserId)
            End If
        Next

        Return birthdayUsers
    End Function

    ''' <summary>
    ''' Sets the birthday role to all applicable users. Unsets it from all others who may have it.
    ''' </summary>
    ''' <returns>A list of users who had the birthday role applied. Use for the announcement message.</returns>
    Private Async Function BirthdayApplyAsync(g As SocketGuild, r As SocketRole, names As HashSet(Of ULong)) As Task(Of IEnumerable(Of SocketGuildUser))
        If Not g.HasAllMembers Then Await g.DownloadUsersAsync()
        Dim newBirthdays As New List(Of SocketGuildUser)
        For Each user In g.Users
            If names.Contains(user.Id) Then
                ' User's in the list. Should have the role. Add and make note of if user does not.
                If Not user.Roles.Contains(r) Then
                    Await user.AddRoleAsync(r)
                    newBirthdays.Add(user)
                End If
            Else
                ' User's not in the list. Should remove the role.
                If user.Roles.Contains(r) Then Await user.RemoveRoleAsync(r)
            End If
        Next
        Return newBirthdays
    End Function

    ''' <summary>
    ''' Makes (or attempts to make) an announcement in the specified channel that includes all users
    ''' who have just had their birthday role added.
    ''' </summary>
    Private Async Function BirthdayAnnounceAsync(g As SocketGuild, c As SocketTextChannel, names As IEnumerable(Of SocketGuildUser)) As Task
        If c Is Nothing Then Return

        Dim result As String

        If names.Count = 1 Then
            ' Single birthday. No need for tricks.
            Dim name As String
            If names(0).Nickname IsNot Nothing Then
                name = names(0).Nickname
            Else
                name = names(0).Username
            End If
            result = $"Please wish a happy birthday to our esteemed member, **{name}**."
        Else
            ' Build name list
            Dim namedisplay As New StringBuilder()
            Dim first = True
            For Each item In names
                If Not first Then
                    namedisplay.Append(", ")
                End If
                first = False
                Dim name As String
                If item.Nickname IsNot Nothing Then
                    name = item.Nickname
                Else
                    name = item.Username
                End If
                namedisplay.Append("**" + name + "**")
            Next
            result = $"Please wish our esteemed members a happy birthday!{vbLf}In no particular order: {namedisplay.ToString()}"
        End If

        Try
            Await c.SendMessageAsync(result)
        Catch ex As Discord.Net.HttpException
            ' Ignore
        End Try
    End Function
#End Region

#Region "Activity reporting"
    Private _activityCount As Integer = 0
    Private Async Function ReportServerCount() As Task
        ' Once roughly every 6 hours, with an initial ~2 minute delay. Be mindful of the interval value.
        _activityCount += 1
        If _activityCount Mod 400 <> 2 Then Return

        Dim count = _bot.DiscordClient.Guilds.Count
        Log("Activity Report", $"Currently in {count} guild(s).")

        Dim dtok = _bot._cfg.DBotsToken
        If dtok IsNot Nothing Then
            Const dUrl As String = "https://bots.discord.pw/api/bots/{0}/stats"

            Using client As New WebClient()
                Dim uri = New Uri(String.Format(dUrl, CType(_bot.DiscordClient.CurrentUser.Id, String)))
                Dim data = "{ ""server_count"": " + CType(count, String) + " }"
                client.Headers(HttpRequestHeader.Authorization) = dtok
                client.Headers(HttpRequestHeader.ContentType) = "application/json"
                Try
                    Await client.UploadStringTaskAsync(uri, data)
                    Log("Activity Report", "Count sent to Discord Bots.")
                Catch ex As WebException
                    Log("Activity Report", "Encountered error on sending to Discord Bots: " + ex.Message)
                End Try
            End Using
        End If
    End Function
#End Region
End Class
