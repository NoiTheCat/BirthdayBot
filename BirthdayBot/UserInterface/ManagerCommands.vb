Imports System.Text.RegularExpressions
Imports Discord
Imports Discord.WebSocket
Imports NodaTime

Friend Class ManagerCommands
    Inherits CommandsCommon

    Private Delegate Function ConfigSubcommand(param As String(), reqChannel As SocketTextChannel) As Task

    Private ReadOnly _subcommands As Dictionary(Of String, ConfigSubcommand)
    Private ReadOnly _usercommands As Dictionary(Of String, CommandHandler)

    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("config", AddressOf CmdConfigDispatch),
                ("override", AddressOf CmdOverride),
                ("status", AddressOf CmdStatus)
            }
        End Get
    End Property

    Sub New(inst As BirthdayBot, db As Configuration, userCommands As IEnumerable(Of (String, CommandHandler)))
        MyBase.New(inst, db)
        _subcommands = New Dictionary(Of String, ConfigSubcommand)(StringComparer.InvariantCultureIgnoreCase) From {
            {"role", AddressOf ScmdRole},
            {"channel", AddressOf ScmdChannel},
            {"modrole", AddressOf ScmdModRole},
            {"message", AddressOf ScmdAnnounceMsg},
            {"messagepl", AddressOf ScmdAnnounceMsg},
            {"ping", AddressOf ScmdPing},
            {"zone", AddressOf ScmdZone},
            {"block", AddressOf ScmdBlock},
            {"unblock", AddressOf ScmdBlock},
            {"moderated", AddressOf ScmdModerated}
        }

        ' Set up local copy of all user commands accessible by the override command
        _usercommands = New Dictionary(Of String, CommandHandler)(StringComparer.InvariantCultureIgnoreCase)
        For Each item In userCommands
            _usercommands.Add(item.Item1, item.Item2)
        Next
    End Sub

    Private Async Function CmdConfigDispatch(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Ignore those without the proper permissions.
        ' Requires either the manage guild permission or to be in the moderators role
        If Not Instance.GuildCache(reqUser.Guild.Id).IsUserModerator(reqUser) Then
            Await reqChannel.SendMessageAsync(":x: This command may only be used by bot moderators.")
            Return
        End If

        If param.Length < 2 Then
            Await reqChannel.SendMessageAsync($":x: See `{CommandPrefix}help-config` for information on how to use this command.")
            Return
        End If

        ' Special case: Restrict 'modrole' to only guild managers
        If param(1).Equals("modrole", StringComparison.OrdinalIgnoreCase) And Not reqUser.GuildPermissions.ManageGuild Then
            Await reqChannel.SendMessageAsync(":x: This command may only be used by those with the `Manage Server` permission.")
            Return
        End If

        ' Subcommands get a subset of the parameters, to make things a little easier.
        Dim confparam(param.Length - 2) As String ' subtract one extra???
        Array.Copy(param, 1, confparam, 0, param.Length - 1)
        ' confparam has at most 2 items: subcommand name, parameters in one string

        Dim h As ConfigSubcommand = Nothing
        If _subcommands.TryGetValue(confparam(0), h) Then
            Await h(confparam, reqChannel)
        End If
    End Function

#Region "Configuration sub-commands"
    ' Birthday role set
    Private Async Function ScmdRole(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(":x: A role name, role mention, or ID value must be specified.")
            Return
        End If
        Dim guild = reqChannel.Guild
        Dim role = FindUserInputRole(param(1), guild)

        If role Is Nothing Then
            Await reqChannel.SendMessageAsync(RoleInputError)
        ElseIf role.Id = reqChannel.Guild.EveryoneRole.Id Then
            Await reqChannel.SendMessageAsync(":x: You cannot set that as the birthday role.")
        Else
            Instance.GuildCache(guild.Id).UpdateRole(role.Id)
            Await reqChannel.SendMessageAsync($":white_check_mark: The birthday role has been set as **{role.Name}**.")
        End If
    End Function

    Private Async Function ScmdPing(param As String(), reqChannel As SocketTextChannel) As Task
        Const inputErr = ":x: You must specify either `off` or `on` in this setting."
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(inputErr)
            Return
        End If

        Dim input = param(1).ToLower()
        Dim setting As Boolean
        Dim result As String
        If input = "off" Then
            setting = False
            result = ":white_check_mark: Announcement pings are now **off**."
        ElseIf input = "on" Then
            setting = True
            result = ":white_check_mark: Announcement pings are now **on**."
        Else
            Await reqChannel.SendMessageAsync(inputErr)
            Return
        End If

        Instance.GuildCache(reqChannel.Guild.Id).UpdateAnnouncePing(setting)
        Await reqChannel.SendMessageAsync(result)
    End Function

    ' Announcement channel set
    Private Async Function ScmdChannel(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length = 1 Then
            ' No extra parameter. Unset announcement channel.
            Dim gi = Instance.GuildCache(reqChannel.Guild.Id)

            ' Extra detail: Show a unique message if a channel hadn't been set prior.
            If Not gi.AnnounceChannelId.HasValue Then
                reqChannel.SendMessageAsync(":x: There is no announcement channel set. Nothing to unset.").Wait()
                Return
            End If

            gi.UpdateAnnounceChannel(Nothing)
            Await reqChannel.SendMessageAsync(":white_check_mark: The announcement channel has been unset.")
        Else
            ' Determine channel from input
            Dim chId As ULong = 0

            ' Try channel mention
            Dim m = ChannelMention.Match(param(1))
            If m.Success Then
                chId = ULong.Parse(m.Groups(1).Value)
            ElseIf ULong.TryParse(param(1), chId) Then
                ' Continue...
            Else
                ' Try text-based search
                Dim res = reqChannel.Guild.TextChannels _
                    .FirstOrDefault(Function(ch) String.Equals(ch.Name, param(1), StringComparison.OrdinalIgnoreCase))
                If res IsNot Nothing Then
                    chId = res.Id ' Yeah... we are throwing the full result away only to look for it again later.
                End If
            End If

            ' Attempt to find channel in guild
            Dim chTt As SocketTextChannel = Nothing
            If chId <> 0 Then
                chTt = reqChannel.Guild.GetTextChannel(chId)
            End If
            If chTt Is Nothing Then
                Await reqChannel.SendMessageAsync(":x: Unable to find the specified channel.")
                Return
            End If

            ' Update the value
            Instance.GuildCache(reqChannel.Guild.Id).UpdateAnnounceChannel(chId)

            ' Report the success
            Await reqChannel.SendMessageAsync($":white_check_mark: The announcement channel is now set to <#{chId}>.")
        End If
    End Function

    ' Moderator role set
    Private Async Function ScmdModRole(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(":x: A role name, role mention, or ID value must be specified.")
            Return
        End If
        Dim guild = reqChannel.Guild
        Dim role = FindUserInputRole(param(1), guild)

        If role Is Nothing Then
            Await reqChannel.SendMessageAsync(RoleInputError)
        Else
            Instance.GuildCache(guild.Id).UpdateModeratorRole(role.Id)
            Await reqChannel.SendMessageAsync($":white_check_mark: The moderator role is now **{role.Name}**.")
        End If
    End Function

    ' Guild default time zone set/unset
    Private Async Function ScmdZone(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length = 1 Then
            ' No extra parameter. Unset guild default time zone.
            Dim gi = Instance.GuildCache(reqChannel.Guild.Id)

            ' Extra detail: Show a unique message if there is no set zone.
            If Not gi.AnnounceChannelId.HasValue Then
                reqChannel.SendMessageAsync(":x: A default zone is not set. Nothing to unset.").Wait()
                Return
            End If

            gi.UpdateTimeZone(Nothing)

            Await reqChannel.SendMessageAsync(":white_check_mark: The default time zone preference has been removed.")
        Else
            ' Parameter check.
            Dim zone As String
            Try
                zone = ParseTimeZone(param(1))
            Catch ex As FormatException
                reqChannel.SendMessageAsync(ex.Message).Wait()
                Return
            End Try

            ' Update value
            Instance.GuildCache(reqChannel.Guild.Id).UpdateTimeZone(zone)

            ' Report the success
            Await reqChannel.SendMessageAsync($":white_check_mark: The server's time zone has been set to **{zone}**.")
        End If
    End Function

    ' Block/unblock individual non-manager users from using commands.
    Private Async Function ScmdBlock(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim doBan As Boolean = param(0).ToLower() = "block" ' True = block, False = unblock

        Dim inputId As ULong
        If Not TryGetUserId(param(1), inputId) Then
            Await reqChannel.SendMessageAsync(BadUserError)
            Return
        End If

        Dim gi = Instance.GuildCache(reqChannel.Guild.Id)
        Dim isBanned = Await gi.IsUserBlockedAsync(inputId)
        If doBan Then
            If Not isBanned Then
                Await gi.BlockUserAsync(inputId)
                reqChannel.SendMessageAsync(":white_check_mark: User has been blocked.").Wait()
            Else
                reqChannel.SendMessageAsync(":white_check_mark: User is already blocked.").Wait()
            End If
        Else
            If isBanned Then
                Await gi.UnbanUserAsync(inputId)
                reqChannel.SendMessageAsync(":white_check_mark: User is now unblocked.").Wait()
            Else
                reqChannel.SendMessageAsync(":white_check_mark: The specified user has not been blocked.").Wait()
            End If
        End If
    End Function

    ' "moderated on/off" - Sets/unsets moderated mode.
    Private Async Function ScmdModerated(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim parameter = param(1).ToLower()
        Dim modSet As Boolean
        If parameter = "on" Then
            modSet = True
        ElseIf parameter = "off" Then
            modSet = False
        Else
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim gi = Instance.GuildCache(reqChannel.Guild.Id)
        Dim currentSet = gi.IsModerated
        gi.UpdateModeratedMode(modSet)

        If currentSet = modSet Then
            Await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode is already {parameter}.")
        Else
            Await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode has been turned {parameter}.")
        End If
    End Function

    ' Sets/unsets custom announcement message.
    Private Async Function ScmdAnnounceMsg(param As String(), reqChannel As SocketTextChannel) As Task
        Dim plural = param(0).ToLower().EndsWith("pl")

        Dim newmsg As String
        Dim clear As Boolean
        If param.Length = 2 Then
            newmsg = param(1)
            clear = False
        Else
            newmsg = Nothing
            clear = True
        End If

        Instance.GuildCache(reqChannel.Guild.Id).UpdateAnnounceMessage(newmsg, plural)
        Const report = ":white_check_mark: The {0} birthday announcement message has been {1}."
        Await reqChannel.SendMessageAsync(String.Format(report, If(plural, "plural", "singular"), If(clear, "reset", "updated")))
    End Function
#End Region

    ' Execute command as another user
    Private Async Function CmdOverride(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Moderators only. As with config, silently drop if this check fails.
        If Not Instance.GuildCache(reqUser.Guild.Id).IsUserModerator(reqUser) Then Return

        If param.Length <> 3 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        ' Second parameter: determine the user to act as
        Dim user As ULong = 0
        If Not TryGetUserId(param(1), user) Then
            Await reqChannel.SendMessageAsync(BadUserError)
            Return
        End If
        Dim overuser = reqChannel.Guild.GetUser(user)
        If overuser Is Nothing Then
            Await reqChannel.SendMessageAsync(BadUserError)
            Return
        End If

        ' Third parameter: determine command to invoke.
        ' Reminder that we're only receiving a param array of size 3 at maximum. String must be split again.
        Dim overparam = param(2).Split(" ", 3, StringSplitOptions.RemoveEmptyEntries)
        Dim cmdsearch = overparam(0)
        If cmdsearch.StartsWith(CommandPrefix) Then
            ' Strip command prefix to search for the given command.
            cmdsearch = cmdsearch.Substring(CommandPrefix.Length)
        Else
            ' Add command prefix to input, just in case.
            overparam(0) = CommandPrefix + overparam(0).ToLower()
        End If
        Dim action As CommandHandler = Nothing
        If Not _usercommands.TryGetValue(cmdsearch, action) Then
            Await reqChannel.SendMessageAsync($":x: `{cmdsearch}` is not an overridable command.")
            Return
        End If

        ' Preparations complete. Run the command.
        Await reqChannel.SendMessageAsync($"Executing `{cmdsearch.ToLower()}` on behalf of {If(overuser.Nickname, overuser.Username)}:")
        Await action.Invoke(overparam, reqChannel, overuser)
    End Function

    ' Prints a status report useful for troubleshooting operational issues within a guild
    Private Async Function CmdStatus(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Moderators only. As with config, silently drop if this check fails.
        If Not Instance.GuildCache(reqUser.Guild.Id).IsUserModerator(reqUser) Then Return

        Dim result As New EmbedBuilder
        Dim optime As DateTimeOffset
        Dim optext As String
        Dim zone As String
        Dim gi = Instance.GuildCache(reqChannel.Guild.Id)
        SyncLock gi
            Dim opstat = gi.OperationLog
            optext = opstat.GetDiagStrings() ' !!! Bulk of output handled by this method
            optime = opstat.Timestamp
            zone = If(gi.TimeZone, "UTC")
        End SyncLock
        Dim shard = Instance.DiscordClient.GetShardIdFor(reqChannel.Guild)

        ' Calculate timestamp in current zone
        Dim ts As String = "Last update:"
        Dim zonedTimeInstant = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone))
        Dim timeAgoEstimate = DateTimeOffset.UtcNow - optime

        With result
            .Title = "Background operation status"
            .Description = $"Shard: {shard}" + vbLf +
                $"Operation time: {Math.Round(timeAgoEstimate.TotalSeconds)} second(s) ago at {zonedTimeInstant}" + vbLf +
                "Report:" + vbLf +
                optext.TrimEnd()
        End With

        Await reqChannel.SendMessageAsync(embed:=result.Build())
    End Function

#Region "Common/helper methods"
    Private Const RoleInputError = ":x: Unable to determine the given role."
    Private Shared ReadOnly RoleMention As New Regex("<@?&(?<snowflake>\d+)>", RegexOptions.Compiled)

    Private Function FindUserInputRole(inputStr As String, guild As SocketGuild) As SocketRole
        ' Resembles a role mention? Strip it to the pure number
        Dim input = inputStr
        Dim rmatch = RoleMention.Match(input)
        If rmatch.Success Then
            input = rmatch.Groups("snowflake").Value
        End If

        ' Attempt to get role by ID, or Nothing
        Dim rid As ULong
        If ULong.TryParse(input, rid) Then
            Return guild.GetRole(rid)
        Else
            ' Reset the search value on the off chance there's a role name that actually resembles a role ping.
            input = inputStr
        End If

        ' If not already found, attempt to search role by string name
        For Each search In guild.Roles
            If String.Equals(search.Name, input, StringComparison.InvariantCultureIgnoreCase) Then
                Return search
            End If
        Next

        Return Nothing
    End Function
#End Region
End Class