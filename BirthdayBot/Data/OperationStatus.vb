Imports System.Text
''' <summary>
''' Holds information regarding the previous updating operation done on a guild including success/error information.
''' </summary>
Class OperationStatus
    Private ReadOnly _log As New Dictionary(Of OperationType, String)

    Public ReadOnly Property Timestamp As DateTimeOffset

    Sub New(ParamArray statuses() As (OperationType, String))
        Timestamp = DateTimeOffset.UtcNow
        For Each status In statuses
            _log(status.Item1) = status.Item2
        Next
    End Sub

    ''' <summary>
    ''' Prepares known information in a displayable format.
    ''' </summary>
    Public Function GetDiagStrings() As String
        Dim report As New StringBuilder
        For Each otype As OperationType In [Enum].GetValues(GetType(OperationType))
            Dim prefix = $"`{[Enum].GetName(GetType(OperationType), otype)}`: "

            Dim info As String = Nothing

            If Not _log.TryGetValue(otype, info) Then
                report.AppendLine(prefix + "No data")
                Continue For
            End If

            If info Is Nothing Then
                report.AppendLine(prefix + "Success")
            Else
                report.AppendLine(prefix + info)
            End If
        Next
        Return report.ToString()
    End Function

    ''' <summary>
    ''' Specifies the type of operation logged. These enum values are publicly displayed in the specified order.
    ''' </summary>
    Public Enum OperationType
        UpdateBirthdayRoleMembership
        SendBirthdayAnnouncementMessage
    End Enum
End Class
