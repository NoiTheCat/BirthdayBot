Imports System.Text.RegularExpressions
Imports Discord.WebSocket

Class UserCommands
    Inherits CommandsCommon

    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("set", AddressOf CmdSet),
                ("zone", AddressOf CmdZone),
                ("remove", AddressOf CmdRemove),
                ("when", AddressOf CmdWhen)
            }
        End Get
    End Property

    Sub New(inst As BirthdayBot, db As Configuration)
        MyBase.New(inst, db)
    End Sub

    ''' <summary>
    ''' Parses date parameter. Strictly takes dd-MMM or MMM-dd only. Eliminates ambiguity over dd/mm vs mm/dd.
    ''' </summary>
    ''' <returns>Tuple: month, day</returns>
    ''' <exception cref="FormatException">Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.</exception>
    Private Function ParseDate(dateInput As String) As (Integer, Integer)
        ' Not using DateTime.Parse. Setting it up is rather complicated, and it's probably case sensitive.
        ' Admittedly, doing it the way it's being done here probably isn't any better.
        Dim m = Regex.Match(dateInput, "^(?<day>\d{1,2})-(?<month>[A-Za-z]{3})$")
        If Not m.Success Then
            ' Flip the fields around, try again
            m = Regex.Match(dateInput, "^(?<month>[A-Za-z]{3})-(?<day>\d{1,2})$")
            If Not m.Success Then Throw New FormatException(GenericError)
        End If
        Dim day As Integer
        Try
            day = Integer.Parse(m.Groups("day").Value)
        Catch ex As FormatException
            Throw New FormatException(GenericError)
        End Try
        Dim monthVal = m.Groups("month").Value
        Dim month As Integer
        Dim dayUpper = 31 ' upper day of month check
        Select Case monthVal.ToLower()
            Case "jan"
                month = 1
            Case "feb"
                month = 2
                dayUpper = 29
            Case "mar"
                month = 3
            Case "apr"
                month = 4
                dayUpper = 30
            Case "may"
                month = 5
            Case "jun"
                month = 6
                dayUpper = 30
            Case "jul"
                month = 7
            Case "aug"
                month = 8
            Case "sep"
                month = 9
                dayUpper = 30
            Case "oct"
                month = 10
            Case "nov"
                month = 11
                dayUpper = 30
            Case "dec"
                month = 12
            Case Else
                Throw New FormatException(":x: Invalid month name. Use a three-letter month abbreviation.")
        End Select
        If day = 0 Or day > dayUpper Then Throw New FormatException(":x: The date you specified is not a valid calendar date.")

        Return (month, day)
    End Function

    Private Async Function CmdSet(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Requires one parameter. Optionally two.
        If param.Count < 2 Or param.Count > 3 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim bmonth, bday As Integer
        Dim btz As String = Nothing
        Try
            Dim res = ParseDate(param(1))
            bmonth = res.Item1
            bday = res.Item2
            If param.Length = 3 Then
                btz = ParseTimeZone(param(2))
            End If
        Catch ex As FormatException
            ' Our parse methods' FormatException has its message to send out to Discord.
            reqChannel.SendMessageAsync(ex.Message).Wait()
            Return
        End Try

        ' Parsing successful. Update user information.
        Dim known As Boolean ' Extra detail: Bot's response changes if the user was previously unknown.
        Try
            Dim user = Instance.GuildCache(reqChannel.Guild.Id).GetUser(reqUser.Id)
            known = user.IsKnown
            Await user.UpdateAsync(bmonth, bday, btz, BotConfig.DatabaseSettings)
        Catch ex As Exception
            Log("Error", ex.ToString())
            reqChannel.SendMessageAsync(":x: An unknown error occurred. The bot owner has been notified.").Wait()
            Return
        End Try
        If known Then
            Await reqChannel.SendMessageAsync(":white_check_mark: Your information has been updated.")
        Else
            Await reqChannel.SendMessageAsync(":white_check_mark: Your birthday has been recorded.")
        End If
    End Function

    Private Async Function CmdZone(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        If param.Count <> 2 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim btz As String = Nothing
        Dim user = Instance.GuildCache(reqChannel.Guild.Id).GetUser(reqUser.Id)
        If Not user.IsKnown Then
            Await reqChannel.SendMessageAsync(":x: Can't set your time zone if your birth date isn't registered.")
            Return
        End If

        Try
            btz = ParseTimeZone(param(1))
        Catch ex As Exception
            reqChannel.SendMessageAsync(ex.Message).Wait()
            Return
        End Try
        Await user.UpdateAsync(user.BirthMonth, user.BirthDay, btz, BotConfig.DatabaseSettings)

        Await reqChannel.SendMessageAsync($":white_check_mark: Your time zone has been updated to **{btz}**.")
    End Function

    Private Async Function CmdRemove(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Parameter count check
        If param.Count <> 1 Then
            Await reqChannel.SendMessageAsync(ExpectedNoParametersError)
            Return
        End If

        ' Extra detail: Send a notification if the user isn't actually known by the bot.
        Dim known As Boolean
        Dim g = Instance.GuildCache(reqChannel.Guild.Id)
        known = g.GetUser(reqUser.Id).IsKnown
        ' Delete database and cache entry
        Await g.DeleteUserAsync(reqUser.Id)
        If Not known Then
            Await reqChannel.SendMessageAsync(":white_check_mark: I don't have your information. Nothing to remove.")
        Else
            Await reqChannel.SendMessageAsync(":white_check_mark: Your information has been removed.")
        End If
    End Function

    Private Async Function CmdWhen(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' Requires a parameter
        If param.Count = 1 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim search = param(1)
        If param.Count = 3 Then
            ' param maxes out at 3 values. param(2) might contain part of the search string (if name has a space)
            search += " " + param(2)
        End If

        Dim searchTarget As SocketGuildUser = Nothing

        Dim searchId As ULong = 0
        If Not TryGetUserId(search, searchId) Then ' ID lookup
            ' name lookup without discriminator
            For Each searchuser In reqChannel.Guild.Users
                If String.Equals(search, searchuser.Username, StringComparison.OrdinalIgnoreCase) Then
                    searchTarget = searchuser
                    Exit For
                End If
            Next
        Else
            searchTarget = reqChannel.Guild.GetUser(searchId)
        End If
        If searchTarget Is Nothing Then
            Await reqChannel.SendMessageAsync(BadUserError)
            Return
        End If

        Dim users = Instance.GuildCache(reqChannel.Guild.Id).Users
        Dim searchTargetData = users.FirstOrDefault(Function(u) u.UserId = searchTarget.Id)
        If searchTargetData Is Nothing Then
            Await reqChannel.SendMessageAsync("The given user does not exist or has not set a birthday.")
            Return
        End If

        With searchTargetData
            Await reqChannel.SendMessageAsync(FormatName(searchTarget, False) + ": " +
                                          $"`{ .BirthDay.ToString("00")}-{Common.MonthNames(.BirthMonth)}`" +
                                          If(.TimeZone Is Nothing, "", $" - `{ .TimeZone}`"))
        End With
    End Function
End Class
