Imports System.Text
''' <summary>
''' Holds information regarding previous operations done on a guild and their most recent success/error status.
''' </summary>
Class OperationStatus
    Private ReadOnly _log As New Dictionary(Of OperationType, OperationInfo)

    Default Public Property Item(otype As OperationType) As OperationInfo
        Get
            Dim o As OperationInfo = Nothing
            If Not _log.TryGetValue(otype, o) Then
                Return Nothing
            End If
            Return o
        End Get
        Set(value As OperationInfo)
            If value Is Nothing Then
                _log.Remove(otype)
            Else
                _log(otype) = value
            End If
        End Set
    End Property

    ''' <summary>
    ''' Prepares known information in a displayable format.
    ''' </summary>
    Public Function GetDiagStrings() As String
        Dim report As New StringBuilder
        For Each otype As OperationType In [Enum].GetValues(GetType(OperationType))
            Dim prefix = $"`{[Enum].GetName(GetType(OperationType), otype)}`: "

            Dim info = Item(otype)
            If info Is Nothing Then
                report.AppendLine(prefix + "No data")
                Continue For
            End If
            prefix += info.Timestamp.ToString("u") + " "

            If info.Exception Is Nothing Then
                report.AppendLine(prefix + "Success")
            Else
                Log("OperationStatus", prefix + info.Exception.ToString())
                report.AppendLine(prefix + info.Exception.Message)
            End If
        Next
        Return report.ToString()
    End Function
End Class
