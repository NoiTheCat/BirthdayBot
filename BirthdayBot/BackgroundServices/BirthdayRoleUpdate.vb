Imports System.Net
Imports System.Text
Imports Discord.WebSocket
Imports NodaTime

''' <summary>
''' Periodically scans all known guilds and adjusts birthday role membership as necessary.
''' Also handles birthday announcements.
''' </summary>
Class BirthdayRoleUpdate
    Inherits BackgroundService
    Private ReadOnly Property Clock As IClock

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
        Clock = SystemClock.Instance ' can be replaced with FakeClock during testing
    End Sub

    ''' <summary>
    ''' Initial processing: Sets up a task per guild and waits on all.
    ''' </summary>
    Public Overrides Async Function OnTick(tick As Integer) As Task
        Dim tasks As New List(Of Task(Of Integer))
        For Each guild In BotInstance.DiscordClient.Guilds
            Dim t = ProcessGuildAsync(guild)
            tasks.Add(t)
        Next

        Try
            Await Task.WhenAll(tasks)
        Catch ex As Exception
            Dim exs = From task In tasks
                      Where task.Exception IsNot Nothing
                      Select task.Exception
            Log($"Encountered {exs.Count} errors during bulk guild processing.")
            For Each iex In exs
                Log(iex.ToString())
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
        If announces > 0 Then Log($"Announcing {announces} birthday(s) in {guilds} guild(s).")
    End Function

    Async Function ProcessGuildAsync(guild As SocketGuild) As Task(Of Integer)
        ' Gather required information
        Dim tz As String
        Dim users As IEnumerable(Of GuildUserSettings)
        Dim role As SocketRole = Nothing
        Dim channel As SocketTextChannel = Nothing
        Dim announce As (String, String)
        Dim announceping As Boolean
        SyncLock BotInstance.KnownGuilds
            If Not BotInstance.KnownGuilds.ContainsKey(guild.Id) Then Return 0
            Dim gs = BotInstance.KnownGuilds(guild.Id)
            tz = gs.TimeZone
            users = gs.Users
            announce = gs.AnnounceMessages
            announceping = gs.AnnouncePing

            If gs.AnnounceChannelId.HasValue Then channel = guild.GetTextChannel(gs.AnnounceChannelId.Value)
            If gs.RoleId.HasValue Then role = guild.GetRole(gs.RoleId.Value)
            If role Is Nothing Then
                gs.RoleWarning = True
                Return 0
            End If
            gs.RoleWarning = False
        End SyncLock

        ' Determine who's currently having a birthday
        Dim birthdays = GetGuildCurrentBirthdays(users, tz)
        ' Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.

        ' Set birthday roles, get list of users that had the role added
        ' But first check if we are able to do so. Letting all requests fail instead will lead to rate limiting.
        Dim announceNames As IEnumerable(Of SocketGuildUser)
        If HasCorrectRolePermissions(guild, role) Then
            Try
                announceNames = Await UpdateGuildBirthdayRoles(guild, role, birthdays)
            Catch ex As Discord.Net.HttpException
                If ex.HttpCode = HttpStatusCode.Forbidden Then
                    announceNames = Nothing
                Else
                    Throw
                End If
            End Try
        Else
            announceNames = Nothing
        End If

        If announceNames Is Nothing Then
            SyncLock BotInstance.KnownGuilds
                ' Nothing on announceNAmes signals failure to apply roles. Set the warning message.
                BotInstance.KnownGuilds(guild.Id).RoleWarning = True
            End SyncLock
            Return 0
        End If

        If announceNames.Count <> 0 Then
            ' Send out announcement message
            Await AnnounceBirthdaysAsync(announce, announceping, channel, announceNames)
        End If
        Return announceNames.Count
    End Function

    ''' <summary>
    ''' Checks if the bot may be allowed to alter roles.
    ''' </summary>
    Private Function HasCorrectRolePermissions(guild As SocketGuild, role As SocketRole) As Boolean
        If Not guild.CurrentUser.GuildPermissions.ManageRoles Then
            ' Bot user cannot manage roles
            Return False
        End If

        ' Check potential role order conflict
        If role.Position >= guild.CurrentUser.Hierarchy Then
            ' Target role is at or above bot's highest role.
            Return False
        End If

        Return True
    End Function

    ''' <summary>
    ''' Gets all known users from the given guild and returns a list including only those who are
    ''' currently experiencing a birthday in the respective time zone.
    ''' </summary>
    Private Function GetGuildCurrentBirthdays(guildUsers As IEnumerable(Of GuildUserSettings),
                                              defaultTzStr As String) As HashSet(Of ULong)
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

            Dim checkNow = Clock.GetCurrentInstant().InZone(tz)
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
    Private Async Function UpdateGuildBirthdayRoles(g As SocketGuild,
                                              r As SocketRole,
                                              names As HashSet(Of ULong)) As Task(Of IEnumerable(Of SocketGuildUser))
        ' Check members currently with the role. Figure out which users to remove it from.
        Dim roleRemoves As New List(Of SocketGuildUser)
        Dim roleKeeps As New HashSet(Of ULong)
        Dim q = 0
        For Each member In r.Members
            If Not names.Contains(member.Id) Then
                roleRemoves.Add(member)
            Else
                roleKeeps.Add(member.Id)
            End If
            q += 1
        Next

        ' TODO Can we remove during the iteration instead of after? investigate later...
        For Each user In roleRemoves
            Await user.RemoveRoleAsync(r)
        Next

        ' Apply role to members not already having it. Prepare announcement list.
        Dim newBirthdays As New List(Of SocketGuildUser)
        For Each target In names
            Dim member = g.GetUser(target)
            If member Is Nothing Then Continue For
            If roleKeeps.Contains(member.Id) Then Continue For ' already has role - do nothing
            Await member.AddRoleAsync(r)
            newBirthdays.Add(member)
        Next

        Return newBirthdays
    End Function

    Public Const DefaultAnnounce = "Please wish a happy birthday to %n!"
    Public Const DefaultAnnouncePl = "Please wish a happy birthday to our esteemed members: %n"

    ''' <summary>
    ''' Makes (or attempts to make) an announcement in the specified channel that includes all users
    ''' who have just had their birthday role added.
    ''' </summary>
    Private Async Function AnnounceBirthdaysAsync(announce As (String, String),
                                                 announcePing As Boolean,
                                                 c As SocketTextChannel,
                                                 names As IEnumerable(Of SocketGuildUser)) As Task
        If c Is Nothing Then Return

        Dim announceMsg As String
        If names.Count = 1 Then
            announceMsg = If(announce.Item1, If(announce.Item2, DefaultAnnounce))
        Else
            announceMsg = If(announce.Item2, If(announce.Item1, DefaultAnnouncePl))
        End If
        announceMsg = announceMsg.TrimEnd()
        If Not announceMsg.Contains("%n") Then announceMsg += " %n"

        ' Build sorted name list
        Dim namestrings As New List(Of String)
        For Each item In names
            namestrings.Add(FormatName(item, announcePing))
        Next
        namestrings.Sort(StringComparer.OrdinalIgnoreCase)

        Dim namedisplay As New StringBuilder()
        Dim first = True
        For Each item In namestrings
            If Not first Then
                namedisplay.Append(", ")
            End If
            first = False
            namedisplay.Append(item)
        Next

        Try
            Await c.SendMessageAsync(announceMsg.Replace("%n", namedisplay.ToString()))
        Catch ex As Discord.Net.HttpException
            ' Ignore
            ' TODO keep tabs on this somehow for troubleshooting purposes
        End Try
    End Function
End Class
