Imports System.Net
Imports System.Text
Imports Discord.WebSocket
Imports NodaTime

''' <summary>
''' Core automatic functionality of the bot. Manages role memberships based on birthday information,
''' and optionally sends the announcement message to appropriate guilds.
''' </summary>
Class BirthdayRoleUpdate
    Inherits BackgroundService

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
    End Sub

    ''' <summary>
    ''' Does processing on all available guilds at once.
    ''' </summary>
    Public Overrides Async Function OnTick() As Task
        Dim tasks As New List(Of Task)
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
                ' TODO probably not a good idea
                Log(iex.ToString())
            Next
        End Try

        ' TODO metrics for role sets, unsets, announcements - and how to do that for singles too?

        ' Running GC now. Many long-lasting items have likely been discarded by now.
        GC.Collect()
    End Function

    ''' <summary>
    ''' Does role and announcement processing for a single specified guild.
    ''' </summary>
    Public Async Function SingleUpdateFor(guild As SocketGuild) As Task
        Try
            Await ProcessGuildAsync(guild)
        Catch ex As Exception
            Log("Encountered an error during guild processing:")
            Log(ex.ToString())
        End Try

        ' TODO metrics for role sets, unsets, announcements - and I mentioned this above too
    End Function

    ''' <summary>
    ''' Main function where actual guild processing occurs.
    ''' </summary>
    Private Async Function ProcessGuildAsync(guild As SocketGuild) As Task
        ' Gather required information
        Dim tz As String
        Dim users As IEnumerable(Of GuildUserSettings)
        Dim role As SocketRole = Nothing
        Dim channel As SocketTextChannel = Nothing
        Dim announce As (String, String)
        Dim announceping As Boolean

        ' Skip processing of guild if local info has not yet been loaded
        If Not BotInstance.GuildCache.ContainsKey(guild.Id) Then Return

        ' Lock once to grab all info
        Dim gs = BotInstance.GuildCache(guild.Id)
        With gs
            tz = .TimeZone
            users = .Users
            announce = .AnnounceMessages
            announceping = .AnnouncePing

            If .AnnounceChannelId.HasValue Then channel = guild.GetTextChannel(gs.AnnounceChannelId.Value)
            If .RoleId.HasValue Then role = guild.GetRole(gs.RoleId.Value)
        End With

        ' Determine who's currently having a birthday
        Dim birthdays = GetGuildCurrentBirthdays(users, tz)
        ' Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.

        ' Set birthday roles, get list of users that had the role added
        ' But first check if we are able to do so. Letting all requests fail instead will lead to rate limiting.
        Dim roleCheck = CheckCorrectRoleSettings(guild, role)
        If Not roleCheck.Item1 Then
            SyncLock gs
                gs.OperationLog = New OperationStatus((OperationStatus.OperationType.UpdateBirthdayRoleMembership, roleCheck.Item2))
            End SyncLock
            Return
        End If

        Dim announcementList As IEnumerable(Of SocketGuildUser)
        Dim roleResult As (Integer, Integer) ' Role additions, removals
        ' Do actual role updating
        Try
            Dim updateResult = Await UpdateGuildBirthdayRoles(guild, role, birthdays)
            announcementList = updateResult.Item1
            roleResult = updateResult.Item2
        Catch ex As Discord.Net.HttpException
            SyncLock gs
                gs.OperationLog = New OperationStatus((OperationStatus.OperationType.UpdateBirthdayRoleMembership, ex.Message))
            End SyncLock
            If ex.HttpCode <> HttpStatusCode.Forbidden Then
                ' Send unusual exceptions to calling method
                Throw
            End If
            Return
        End Try

        Dim opResult1, opResult2 As (OperationStatus.OperationType, String)
        opResult1 = (OperationStatus.OperationType.UpdateBirthdayRoleMembership,
                     $"Success: Added {roleResult.Item1} member(s), Removed {roleResult.Item2} member(s) from target role.")

        If announcementList.Count <> 0 Then
            Dim announceOpResult = Await AnnounceBirthdaysAsync(announce, announceping, channel, announcementList)
            opResult2 = (OperationStatus.OperationType.SendBirthdayAnnouncementMessage, announceOpResult)
        Else
            opResult2 = (OperationStatus.OperationType.SendBirthdayAnnouncementMessage, "Announcement not considered.")
        End If

        SyncLock gs
            gs.OperationLog = New OperationStatus(opResult1, opResult2)
        End SyncLock
    End Function

    ''' <summary>
    ''' Checks if the bot may be allowed to alter roles.
    ''' </summary>
    Private Function CheckCorrectRoleSettings(guild As SocketGuild, role As SocketRole) As (Boolean, String)
        If role Is Nothing Then
            Return (False, "Failed: Designated role not found or defined.")
        End If

        If Not guild.CurrentUser.GuildPermissions.ManageRoles Then
            Return (False, "Failed: Bot does not contain Manage Roles permission.")
        End If

        ' Check potential role order conflict
        If role.Position >= guild.CurrentUser.Hierarchy Then
            Return (False, "Failed: Bot is beneath the designated role in the role hierarchy.")
        End If

        Return (True, Nothing)
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

            Dim checkNow = SystemClock.Instance.GetCurrentInstant().InZone(tz)
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
                                              names As HashSet(Of ULong)) As Task(Of (IEnumerable(Of SocketGuildUser), (Integer, Integer)))
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

        Return (newBirthdays, (newBirthdays.Count, roleRemoves.Count))
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
                                                  names As IEnumerable(Of SocketGuildUser)) As Task(Of String)
        If c Is Nothing Then
            Return "Announcement channel is undefined."
        End If

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
            Return $"Successfully announced {names.Count} name(s)"
        Catch ex As Discord.Net.HttpException
            Return ex.Message
        End Try
    End Function
End Class
