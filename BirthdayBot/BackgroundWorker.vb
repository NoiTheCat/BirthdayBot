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
    ' NOTE: Interval greatly lowered. Raise to 45 seconds if server count goes up.
    Const Interval = 15 ' How often the worker wakes up, in seconds
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

    Private Async Function WorkerLoop() As Task
        Try
            While Not WorkerCancel.IsCancellationRequested
                Await Task.Delay(Interval * 1000, WorkerCancel.Token)
                WorkerCancel.Token.ThrowIfCancellationRequested()
                Try
                    For Each guild In _bot.DiscordClient.Guilds
                        Dim b = BirthdayWorkAsync(guild)
                        Await b
                    Next
                Catch ex As Exception
                    Log("Error", ex.ToString())
                End Try
            End While
        Catch ex As TaskCanceledException
            Return
        End Try
    End Function

#Region "Birthday handling"
    ''' <summary>
    ''' All birthday checking happens here.
    ''' </summary>
    Private Async Function BirthdayWorkAsync(guild As SocketGuild) As Task
        ' Gather required information
        Dim tz As String
        Dim users As IEnumerable(Of GuildUserSettings)
        Dim role As SocketRole = Nothing
        Dim channel As SocketTextChannel = Nothing
        SyncLock _bot.KnownGuilds
            If Not _bot.KnownGuilds.ContainsKey(guild.Id) Then Return
            Dim gs = _bot.KnownGuilds(guild.Id)
            tz = gs.TimeZone
            users = gs.Users

            If gs.AnnounceChannelId.HasValue Then channel = guild.GetTextChannel(gs.AnnounceChannelId.Value)
            If gs.RoleId.HasValue Then role = guild.GetRole(gs.RoleId.Value)
            If role Is Nothing Then
                gs.RoleWarning = True
                Return
            End If
        End SyncLock

        ' Determine who's currently having a birthday
        Dim birthdays = BirthdayCalculate(users, tz)
        ' Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.

        ' Set birthday role, get list of users now having birthdays
        Dim announceNames = Await BirthdayApplyAsync(guild, role, birthdays)
        If announceNames.Count = 0 Then Return

        ' Send out announcement message
        Await BirthdayAnnounceAsync(guild, channel, announceNames)
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
            If targetMonth = 2 And targetDay = 29 And Not DateTime.IsLeapYear(checkNow.Year) Then
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
                namedisplay.Append(name)
            Next
            result = $"Please wish our members a happy birthday!{vbLf}In no particular order: {namedisplay.ToString()}"
        End If

        Try
            Await c.SendMessageAsync(result)
        Catch ex As Discord.Net.HttpException
            ' Ignore
        End Try
    End Function
#End Region
End Class
