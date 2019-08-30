Imports System.Data.Common
Imports Npgsql
Imports NpgsqlTypes

''' <summary>
''' Representation of a user's birthday settings within a guild.
''' Instances are held and managed by <see cref="GuildStateInformation"/>.
''' </summary>
Class GuildUserSettings
    Private _month As Integer
    Private _day As Integer
    Private _tz As String

    Public ReadOnly Property GuildId As ULong
    Public ReadOnly Property UserId As ULong
    ''' <summary>
    ''' Month of birth as a numeric value. Range 1-12.
    ''' </summary>
    Public ReadOnly Property BirthMonth As Integer
        Get
            Return _month
        End Get
    End Property
    ''' <summary>
    ''' Day of birth as a numeric value. Ranges between 1-31 or lower based on month value.
    ''' </summary>
    Public ReadOnly Property BirthDay As Integer
        Get
            Return _day
        End Get
    End Property
    Public ReadOnly Property TimeZone As String
        Get
            Return _tz
        End Get
    End Property
    Public ReadOnly Property IsKnown As Boolean
        Get
            Return _month <> 0 And _day <> 0
        End Get
    End Property

    ''' <summary>
    ''' Creates a data-less instance without any useful information.
    ''' Calling <see cref="UpdateAsync(Integer, Integer, String)"/> will create a real database entry.
    ''' </summary>
    Public Sub New(guildId As ULong, userId As ULong)
        Me.GuildId = guildId
        Me.UserId = userId
    End Sub

    ' Called by GetGuildUsersAsync. Double-check ordinals when changes are made.
    Private Sub New(reader As DbDataReader)
        GuildId = CULng(reader.GetInt64(0))
        UserId = CULng(reader.GetInt64(1))
        _month = reader.GetInt32(2)
        _day = reader.GetInt32(3)
        If Not reader.IsDBNull(4) Then _tz = reader.GetString(4)
    End Sub

    ''' <summary>
    ''' Updates user with given information.
    ''' NOTE: If there exists a tz value and the update contains none, the old tz value is retained.
    ''' </summary>
    Public Async Function UpdateAsync(month As Integer, day As Integer, newtz As String, dbconfig As Database) As Task
        Dim inserttz = If(newtz, TimeZone)

        Using db = Await dbconfig.OpenConnectionAsync()
            ' Will do a delete/insert instead of insert...on conflict update. Because lazy.
            Using t = db.BeginTransaction()
                Await DoDeleteAsync(db)
                Using c = db.CreateCommand()
                    c.CommandText = $"insert into {BackingTable} " +
                        "(guild_id, user_id, birth_month, birth_day, time_zone) values " +
                        "(@Gid, @Uid, @Month, @Day, @Tz)"
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(GuildId)
                    c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = CLng(UserId)
                    c.Parameters.Add("@Month", NpgsqlDbType.Numeric).Value = month
                    c.Parameters.Add("@Day", NpgsqlDbType.Numeric).Value = day
                    With c.Parameters.Add("@Tz", NpgsqlDbType.Text)
                        If inserttz IsNot Nothing Then
                            .Value = inserttz
                        Else
                            .Value = DBNull.Value
                        End If
                    End With
                    c.Prepare()
                    Await c.ExecuteNonQueryAsync()
                End Using
                Await t.CommitAsync()
            End Using
        End Using

        ' We didn't crash! Get the new values stored locally.
        _month = month
        _day = day
        _tz = inserttz
    End Function

    ''' <summary>
    ''' Deletes information of this user from the backing database.
    ''' The corresponding object reference should ideally be discarded after calling this.
    ''' </summary>
    Public Async Function DeleteAsync(dbconfig As Database) As Task
        Using db = Await dbconfig.OpenConnectionAsync()
            Await DoDeleteAsync(db)
        End Using
    End Function

    ' Shared between UpdateAsync and DeleteAsync
    Private Async Function DoDeleteAsync(dbconn As NpgsqlConnection) As Task
        Using c = dbconn.CreateCommand()
            c.CommandText = $"delete from {BackingTable}" +
                    " where guild_id = @Gid and user_id = @Uid"
            c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(GuildId)
            c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = CLng(UserId)
            c.Prepare()
            Await c.ExecuteNonQueryAsync()
        End Using
    End Function

#Region "Database"
    Public Const BackingTable = "user_birthdays"

    Friend Shared Sub SetUpDatabaseTable(db As NpgsqlConnection)
        Using c = db.CreateCommand()
            c.CommandText = $"create table if not exists {BackingTable} (" +
                $"guild_id bigint not null references {GuildStateInformation.BackingTable}, " +
                "user_id bigint not null, " +
                "birth_month integer not null, " +
                "birth_day integer not null, " +
                "time_zone text null, " +
                "PRIMARY KEY (guild_id, user_id)" +
                ")"
            c.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>
    ''' Gets all known birthday records from the specified guild. No further filtering is done here.
    ''' </summary>
    Shared Function GetGuildUsersAsync(dbsettings As Database, guildId As ULong) As IEnumerable(Of GuildUserSettings)
        Using db = dbsettings.OpenConnectionAsync().GetAwaiter().GetResult()
            Using c = db.CreateCommand()
                ' Take note of ordinals for use in the constructor
                c.CommandText = "select guild_id, user_id, birth_month, birth_day, time_zone " +
                    $"from {BackingTable} where guild_id = @Gid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(guildId)
                c.Prepare()
                Using r = c.ExecuteReader()
                    Dim result As New List(Of GuildUserSettings)
                    While r.Read()
                        result.Add(New GuildUserSettings(r))
                    End While
                    Return result
                End Using
            End Using
        End Using
    End Function
#End Region
End Class
