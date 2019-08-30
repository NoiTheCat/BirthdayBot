Imports Npgsql

''' <summary>
''' Some database abstractions.
''' </summary>
Class Database
    ' Database storage in this project, explained:
    ' Each guild gets a row in the settings table. This table is referred to when doing most things.
    ' Within each guild, each known user gets a row in the users table with specific information specified.
    ' Users can override certain settings in global, such as time zone.

    Private ReadOnly Property DBConnectionString As String

    Sub New(connString As String)
        DBConnectionString = connString

        ' Database initialization happens here as well.
        SetupTables()
    End Sub

    Public Async Function OpenConnectionAsync() As Task(Of NpgsqlConnection)
        Dim db As New NpgsqlConnection(DBConnectionString)
        Await db.OpenAsync()
        Return db
    End Function

    Private Sub SetupTables()
        Using db = OpenConnectionAsync().GetAwaiter().GetResult()
            GuildStateInformation.SetUpDatabaseTable(db) ' Note: Call this first. (Foreign reference constraints.)
            GuildUserSettings.SetUpDatabaseTable(db)
        End Using
    End Sub
End Class
