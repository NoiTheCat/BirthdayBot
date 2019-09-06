Imports System.IO
Imports System.Text
Imports Discord.WebSocket

''' <summary>
''' Commands for listing upcoming and all birthdays.
''' </summary>
Class ListingCommands
    Inherits CommandsCommon
    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("list", AddressOf CmdList),
                ("upcoming", AddressOf CmdUpcoming),
                ("recent", AddressOf CmdUpcoming)
            }
        End Get
    End Property

    Sub New(inst As BirthdayBot, db As Configuration)
        MyBase.New(inst, db)
    End Sub

    ' Creates a file with all birthdays.
    Private Async Function CmdList(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' For now, we're restricting this command to moderators only. This may turn into an option later.
        If Not Instance.GuildCache(reqChannel.Guild.Id).IsUserModerator(reqUser) Then
            Await reqChannel.SendMessageAsync(":x: Only bot moderators may use this command.")
            Return
        End If

        Dim useCsv = False
        ' Check for CSV option
        If param.Length = 2 Then
            If (param(1).ToLower() = "csv") Then
                useCsv = True
            Else
                Await reqChannel.SendMessageAsync(":x: That is not available as an export format.")
                Return
            End If
        ElseIf param.Length > 2 Then
            Await reqChannel.SendMessageAsync(GenericError)
            Return
        End If

        Dim bdlist = Await LoadList(reqChannel.Guild, False)

        Dim filepath = Path.GetTempPath() + "birthdaybot-" + reqChannel.Guild.Id.ToString()
        Dim fileoutput As String
        If useCsv Then
            fileoutput = ListExportCsv(reqChannel, bdlist)
            filepath += ".csv"
        Else
            fileoutput = ListExportNormal(reqChannel, bdlist)
            filepath += ".txt"
        End If
        Await File.WriteAllTextAsync(filepath, fileoutput, Encoding.UTF8)

        Try
            Await reqChannel.SendFileAsync(filepath, $"Exported {bdlist.Count} birthdays to file.")
        Catch ex As Discord.Net.HttpException
            reqChannel.SendMessageAsync(":x: Unable to send list due to a permissions issue. Check the 'Attach Files' permission.").Wait()
        Catch ex As Exception
            Log("Listing", ex.ToString())
            reqChannel.SendMessageAsync(":x: An internal error occurred. It has been reported to the bot owner.").Wait()
        Finally
            File.Delete(filepath)
        End Try
    End Function

    ' "Recent and upcoming birthdays"
    ' The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    Private Async Function CmdUpcoming(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Dim now = DateTimeOffset.UtcNow
        Dim search = DateIndex(now.Month, now.Day) - 4 ' begin search 4 days prior to current date UTC
        If search <= 0 Then search = 366 - Math.Abs(search)

        Dim query = Await LoadList(reqChannel.Guild, True)
        If query.Count = 0 Then
            Await reqChannel.SendMessageAsync("There are currently no recent or upcoming birthdays.")
            Return
        End If

        Dim output As New StringBuilder()
        output.AppendLine("Recent and upcoming birthdays:")
        For count = 1 To 11 ' cover 11 days total (3 prior, current day, 7 upcoming
            Dim results = From item In query
                          Where item.DateIndex = search
                          Select item

            ' push up search by 1 now, in case we back out early
            search += 1
            If search > 366 Then search = 1 ' wrap to beginning of year

            If results.Count = 0 Then Continue For ' back out early

            ' Build sorted name list
            Dim names As New List(Of String)
            For Each item In results
                names.Add(item.DisplayName)
            Next
            names.Sort(StringComparer.InvariantCultureIgnoreCase)

            Dim first = True
            output.AppendLine()
            output.Append($"● `{MonthNames(results(0).BirthMonth)}-{results(0).BirthDay.ToString("00")}`: ")
            For Each item In names
                If first Then
                    first = False
                Else
                    output.Append(", ")
                End If
                output.Append(item)
            Next
        Next

        Await reqChannel.SendMessageAsync(output.ToString())
    End Function

    ''' <summary>
    ''' Fetches all guild birthdays and places them into an easily usable structure.
    ''' Users currently not in the guild are not included in the result.
    ''' </summary>
    Private Async Function LoadList(guild As SocketGuild, escapeFormat As Boolean) As Task(Of List(Of ListItem))
        Dim ping = Instance.GuildCache(guild.Id).AnnouncePing

        Using db = Await BotConfig.DatabaseSettings.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = "select user_id, birth_month, birth_day from " + GuildUserSettings.BackingTable +
                    " where guild_id = @Gid order by birth_month, birth_day"
                c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint).Value = CLng(guild.Id)
                c.Prepare()
                Using r = Await c.ExecuteReaderAsync()
                    Dim result As New List(Of ListItem)
                    While Await r.ReadAsync()
                        Dim id = CULng(r.GetInt64(0))
                        Dim month = r.GetInt32(1)
                        Dim day = r.GetInt32(2)

                        Dim guildUser = guild.GetUser(id)
                        If guildUser Is Nothing Then Continue While ' Skip users not in guild

                        result.Add(New ListItem With {
                            .BirthMonth = month,
                            .BirthDay = day,
                            .DateIndex = DateIndex(month, day),
                            .UserId = guildUser.Id,
                            .DisplayName = FormatName(guildUser, False)
                        })
                    End While
                    Return result
                End Using
            End Using
        End Using
    End Function

    Private Function ListExportNormal(channel As SocketGuildChannel, list As IEnumerable(Of ListItem)) As String
        'Output: "● Mon-dd: (user ID) Username [ - Nickname: (nickname)]"
        Dim result As New StringBuilder()
        With result
            .AppendLine("Birthdays in " + channel.Guild.Name)
            .AppendLine()
            For Each item In list
                Dim user = channel.Guild.GetUser(item.UserId)
                If user Is Nothing Then Continue For ' User disappeared in the instant between getting list and processing
                .Append($"● {MonthNames(item.BirthMonth)}-{item.BirthDay.ToString("00")}: ")
                .Append(item.UserId)
                .Append(" " + user.Username + "#" + user.Discriminator)
                If user.Nickname IsNot Nothing Then
                    .Append(" - Nickname: " + user.Nickname)
                End If
                .AppendLine()
            Next
        End With
        Return result.ToString()
    End Function

    Private Function ListExportCsv(channel As SocketGuildChannel, list As IEnumerable(Of ListItem)) As String
        ' Output: User ID, Username, Nickname, Month-Day, Month, Day
        Dim result As New StringBuilder()
        With result
            ' Conforming to RFC 4180
            ' With header.
            result.Append("UserID,Username,Nickname,MonthDayDisp,Month,Day")
            result.Append(vbCrLf) ' crlf is specified by the standard
            For Each item In list
                Dim user = channel.Guild.GetUser(item.UserId)
                If user Is Nothing Then Continue For ' User disappeared in the instant between getting list and processing
                .Append(item.UserId)
                .Append(",")
                .Append(CsvEscape(user.Username + "#" + user.Discriminator))
                .Append(",")
                If user.Nickname IsNot Nothing Then .Append(user.Nickname)
                .Append(",")
                .Append($"{MonthNames(item.BirthMonth)}-{item.BirthDay.ToString("00")}")
                .Append(",")
                .Append(item.BirthMonth)
                .Append(",")
                .Append(item.BirthDay)
                .Append(vbCrLf)
            Next
        End With
        Return result.ToString()
    End Function

    Private Function CsvEscape(input As String) As String
        Dim result As New StringBuilder
        result.Append("""")
        For Each ch In input
            If ch = """"c Then result.Append(""""c)
            result.Append(ch)
        Next
        result.Append("""")
        Return result.ToString()
    End Function

    Private Function DateIndex(month As Integer, day As Integer) As Integer
        DateIndex = 0
        ' Add month offset
        If month > 1 Then DateIndex += 31 ' Offset January
        If month > 2 Then DateIndex += 29 ' Offset February (incl. leap day)
        If month > 3 Then DateIndex += 31 ' etc
        If month > 4 Then DateIndex += 30
        If month > 5 Then DateIndex += 31
        If month > 6 Then DateIndex += 30
        If month > 7 Then DateIndex += 31
        If month > 8 Then DateIndex += 31
        If month > 9 Then DateIndex += 30
        If month > 10 Then DateIndex += 31
        If month > 11 Then DateIndex += 30
        DateIndex += day
    End Function

    Private Structure ListItem
        Public Property DateIndex As Integer
        Public Property BirthMonth As Integer
        Public Property BirthDay As Integer
        Public Property UserId As ULong
        Public Property DisplayName As String
    End Structure
End Class
