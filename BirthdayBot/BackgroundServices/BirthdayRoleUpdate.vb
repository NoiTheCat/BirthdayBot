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

        ' TODO metrics for role sets, unsets, announcements - and how to do that for singles too?
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

    Private Async Function ProcessGuildAsync(guild As SocketGuild) As Task(Of Integer)
        ' Gather required information
        Dim tz As String
        Dim users As IEnumerable(Of GuildUserSettings)
        Dim role As SocketRole = Nothing
        Dim channel As SocketTextChannel = Nothing
        Dim announce As (String, String)
        Dim announceping As Boolean

        If Not BotInstance.GuildCache.ContainsKey(guild.Id) Then Return 0 ' guild not yet fully loaded; skip processing

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
        Dim correctRoleSettings = HasCorrectRoleSettings(guild, role)
        Dim gotForbidden = False

        Dim announceNames As IEnumerable(Of SocketGuildUser) = Nothing
        If correctRoleSettings Then
            Try
                announceNames = Await UpdateGuildBirthdayRoles(guild, role, birthdays)
            Catch ex As Discord.Net.HttpException
                If ex.HttpCode = HttpStatusCode.Forbidden Then
                    gotForbidden = True
                Else
                    Throw
                End If
            End Try
        End If

        ' Update warning flag
        Dim updateError = Not correctRoleSettings Or gotForbidden
        ' Quit now if the warning flag was set. Announcement data is not available.
        If updateError Then Return 0

        If announceNames.Count <> 0 Then
            ' Send out announcement message
            Await AnnounceBirthdaysAsync(announce, announceping, channel, announceNames)
        End If
        Return announceNames.Count
    End Function

    ''' <summary>
    ''' Checks if the bot may be allowed to alter roles.
    ''' </summary>
    Private Function HasCorrectRoleSettings(guild As SocketGuild, role As SocketRole) As Boolean
        If role Is Nothing Then
            ' Designated role not found or defined in guild
            Return False
        End If

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
