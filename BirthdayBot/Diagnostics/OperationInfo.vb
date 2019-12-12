''' <summary>
''' Information regarding a single type of operation.
''' </summary>
Class OperationInfo
    ''' <summary>
    ''' The time in which the respective operation was attempted.
    ''' </summary>
    ReadOnly Property Timestamp As DateTimeOffset
    ''' <summary>
    ''' Any exception encountered during the respective operation.
    ''' </summary>
    ''' <returns>Nothing/null if the previous given operation was a success.</returns>
    ReadOnly Property Exception As Exception

    ''' <summary>
    ''' Creates an instance containing a success status.
    ''' </summary>
    Sub New()
        Timestamp = DateTimeOffset.UtcNow
    End Sub

    ''' <summary>
    ''' Creates an instance containing a captured exception
    ''' </summary>
    Sub New(ex As Exception)
        Me.New()
        Exception = ex
    End Sub

    ''' <summary>
    ''' Creates an instance containing a custom error message
    ''' </summary>
    Sub New(message As String)
        Me.New()
        Exception = New Exception(message)
    End Sub
End Class