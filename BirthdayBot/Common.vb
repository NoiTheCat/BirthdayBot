Imports System.Text
Imports Discord.WebSocket

Module Common
    ''' <summary>
    ''' Formats a user's name to a consistent, readable format which makes use of their nickname.
    ''' </summary>
    Public Function FormatName(member As SocketGuildUser, ping As Boolean) As String
        If ping Then Return member.Mention

        Dim escapeFormattingCharacters = Function(input As String) As String
                                             Dim result As New StringBuilder
                                             For Each c As Char In input
                                                 If c = "\"c Or c = "_"c Or c = "~"c Or c = "*"c Then
                                                     result.Append("\")
                                                 End If
                                                 result.Append(c)
                                             Next
                                             Return result.ToString()
                                         End Function

        Dim username = escapeFormattingCharacters(member.Username)
        If member.Nickname IsNot Nothing Then
            Return $"**{escapeFormattingCharacters(member.Nickname)}** ({username}#{member.Discriminator})"
        End If
        Return $"**{username}**#{member.Discriminator}"
    End Function

    Public ReadOnly Property MonthNames As New Dictionary(Of Integer, String) From {
        {1, "Jan"}, {2, "Feb"}, {3, "Mar"}, {4, "Apr"}, {5, "May"}, {6, "Jun"},
        {7, "Jul"}, {8, "Aug"}, {9, "Sep"}, {10, "Oct"}, {11, "Nov"}, {12, "Dec"}
    }
End Module
