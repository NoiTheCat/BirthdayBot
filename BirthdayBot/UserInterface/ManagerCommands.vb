Imports System.Text.RegularExpressions
Imports Discord.WebSocket

Friend Class ManagerCommands
    Inherits CommandsCommon
    Private Delegate Function ConfigSubcommand(param As String(), reqChannel As SocketTextChannel) As Task

    Private _subcommands As Dictionary(Of String, ConfigSubcommand)
    Private _usercommands As Dictionary(Of String, CommandHandler)

    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("config", AddressOf CmdConfigDispatch),
                ("override", AddressOf CmdOverride)
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
        Dim allowed As Boolean
        SyncLock Instance.KnownGuilds
            allowed = Instance.KnownGuilds(reqUser.Guild.Id).IsUserModerator(reqUser)
        End SyncLock
        If Not allowed Then
            Await reqChannel.SendMessageAsync(":x: This command may only be used by bot moderators.")
            Return
        End If

        If param.Length <> 3 Then
            Await reqChannel.SendMessageAsync(GenericError)
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
        Else
            SyncLock Instance.KnownGuilds
                Instance.KnownGuilds(guild.Id).UpdateRoleAsync(role.Id).Wait()
            End SyncLock
            Await reqChannel.SendMessageAsync($":white_check_mark: The birthday role has been set as **{role.Name}**.")
        End If
    End Function

    ' Announcement channel set
    Private Async Function ScmdChannel(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length = 1 Then
            ' No extra parameter. Unset announcement channel.
            SyncLock Instance.KnownGuilds
                Dim gi = Instance.KnownGuilds(reqChannel.Guild.Id)

                ' Extra detail: Show a unique message if a channel hadn't been set prior.
                If Not gi.AnnounceChannelId.HasValue Then
                    reqChannel.SendMessageAsync(":x: There is no announcement channel set. Nothing to unset.").Wait()
                    Return
                End If

                gi.UpdateAnnounceChannelAsync(Nothing).Wait()
            End SyncLock

            Await reqChannel.SendMessageAsync(":white_check_mark: The announcement channel has been unset.")
        Else
            ' Parameter check: This needs a channel mention to function.
            Dim m = ChannelMention.Match(param(1))
            If Not m.Success Then
                Await reqChannel.SendMessageAsync(":x: The given parameter must be a channel. (The channel name must be clickable.)")
                Return
            End If

            Dim chId = ULong.Parse(m.Groups(1).Value)
            ' Check if the channel isn't in the local guild.
            Dim chInst = reqChannel.Guild.GetTextChannel(chId)
            If chInst Is Nothing Then
                Await reqChannel.SendMessageAsync(":x: Unable to find the specified channel on this server.")
                Return
            End If

            ' Update the value
            SyncLock Instance.KnownGuilds
                Dim gi = Instance.KnownGuilds(reqChannel.Guild.Id)
                gi.UpdateAnnounceChannelAsync(chId).Wait()
            End SyncLock

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
            SyncLock Instance.KnownGuilds
                Instance.KnownGuilds(guild.Id).UpdateModeratorRoleAsync(role.Id).Wait()
            End SyncLock
            Await reqChannel.SendMessageAsync($":white_check_mark: The moderator role is now **{role.Name}**.")
        End If
    End Function

    ' Guild default time zone set/unset
    Private Async Function ScmdZone(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length = 1 Then
            ' No extra parameter. Unset guild default time zone.
            SyncLock Instance.KnownGuilds
                Dim gi = Instance.KnownGuilds(reqChannel.Guild.Id)

                ' Extra detail: Show a unique message if there is no set zone.
                If Not gi.AnnounceChannelId.HasValue Then
                    reqChannel.SendMessageAsync(":x: A default zone is not set. Nothing to unset.").Wait()
                    Return
                End If

                gi.UpdateTimeZoneAsync(Nothing).Wait()
            End SyncLock

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
            SyncLock Instance.KnownGuilds
                Dim gi = Instance.KnownGuilds(reqChannel.Guild.Id)
                gi.UpdateTimeZoneAsync(zone).Wait()
            End SyncLock

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

        SyncLock Instance.KnownGuilds
            Dim gi = Instance.KnownGuilds(reqChannel.Guild.Id)
            Dim isBanned = gi.IsUserBlockedAsync(inputId).GetAwaiter().GetResult()

            If doBan Then
                If Not isBanned Then
                    gi.BlockUserAsync(inputId).Wait()
                    reqChannel.SendMessageAsync(":white_check_mark: User has been blocked.").Wait()
                Else
                    reqChannel.SendMessageAsync(":white_check_mark: User is already blocked.").Wait()
                End If
            Else
                If isBanned Then
                    gi.UnbanUserAsync(inputId).Wait()
                    reqChannel.SendMessageAsync(":white_check_mark: User is now unblocked.").Wait()
                Else
                    reqChannel.SendMessageAsync(":white_check_mark: The specified user has not been blocked.").Wait()
                End If
            End If
        End SyncLock
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

        Dim currentSet As Boolean

        SyncLock Instance.KnownGuilds
            Dim gi = Instance.KnownGuilds(reqChannel.Guild.Id)
            currentSet = gi.IsModerated
            gi.UpdateModeratedModeAsync(modSet).Wait()
        End SyncLock

        If currentSet = modSet Then
            Await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode is already {parameter}.")
        Else
            Await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode has been turned {parameter}.")
        End If
    End Function

    ' Sets/unsets custom announcement message.
    Private Async Function ScmdAnnounceMsg(param As String(), reqChannel As SocketTextChannel) As Task
        If param.Length <> 2 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim plural = param(0).ToLower().EndsWith("pl")

        SyncLock Instance.KnownGuilds
            If plural Then
                Instance.KnownGuilds(reqChannel.Guild.Id).UpdateAnnounceMessagePlAsync(param(1)).Wait()
            Else
                Instance.KnownGuilds(reqChannel.Guild.Id).UpdateAnnounceMessageAsync(param(1)).Wait()
            End If
        End SyncLock
        Dim report = $":white_check_mark: The {If(plural, "plural", "singular")} birthday announcement message has been updated."
        Await reqChannel.SendMessageAsync(report)
    End Function
#End Region

    ' Execute command as another user
    Private Async Function CmdOverride(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Moderators only. As with config, silently drop if this check fails.
        SyncLock Instance.KnownGuilds
            If Not Instance.KnownGuilds(reqUser.Guild.Id).IsUserModerator(reqUser) Then Return
        End SyncLock

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