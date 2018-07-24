Option Strict On
Option Explicit On
Imports System.Data.Common
Imports Npgsql
Imports NpgsqlTypes

''' <summary>
''' Collection of GuildUserSettings instances. Holds cached information on guild users and overall
''' guild options, and provides some database abstractions regarding them all.
''' Object instances are loaded when entering a guild and discarded when the bot leaves the guild.
''' </summary>
Friend Class GuildSettings
    Public ReadOnly Property GuildId As ULong
    Private ReadOnly _db As Database
    Private _role As ULong?
    Private _channel As ULong?
    Private _tz As String
    Private _modded As Boolean
    Private _userCache As Dictionary(Of ULong, GuildUserSettings)

    ''' <summary>
    ''' Flag for notifying servers that the bot is unable to manipulate its role.
    ''' </summary>
    Public Property RoleWarning As Boolean

    ''' <summary>
    ''' Gets a list of cached users. Use sparingly.
    ''' </summary>
    Public ReadOnly Property Users As IEnumerable(Of GuildUserSettings)
        Get
            Dim items As New List(Of GuildUserSettings)
            For Each item In _userCache.Values
                items.Add(item)
            Next
            Return items
        End Get
    End Property

    ''' <summary>
    ''' Gets the guild's designated Role ID.
    ''' </summary>
    Public ReadOnly Property RoleId As ULong?
        Get
            Return _role
        End Get
    End Property

    ''' <summary>
    ''' Gets the designated announcement Channel ID.
    ''' </summary>
    Public ReadOnly Property AnnounceChannelId As ULong?
        Get
            Return _channel
        End Get
    End Property

    ''' <summary>
    ''' Gets the guild's default time zone.
    ''' </summary>
    Public ReadOnly Property TimeZone As String
        Get
            Return _tz
        End Get
    End Property

    ''' <summary>
    ''' Gets or sets if the server is in moderated mode.
    ''' Updating this value updates the database.
    ''' </summary>
    Public Property IsModerated As Boolean
        Get
            Return _modded
        End Get
        Set(value As Boolean)
            _modded = value
            UpdateDatabaseAsync()
        End Set
    End Property

    ' Called by LoadSettingsAsync. Double-check ordinals when changes are made.
    Private Sub New(reader As DbDataReader, dbconfig As Database)
        _db = dbconfig
        GuildId = CULng(reader.GetInt64(0))
        ' Weird: if using a ternary operator with a ULong?, Nothing resolves to 0 despite Option Strict On.
        If Not reader.IsDBNull(1) Then
            _role = CULng(reader.GetInt64(1))
            RoleWarning = False
        Else
            RoleWarning = True
        End If
        If Not reader.IsDBNull(2) Then _channel = CULng(reader.GetInt64(2))
        _tz = If(reader.IsDBNull(3), Nothing, reader.GetString(3))
        _modded = reader.GetBoolean(4)

        ' Get user information loaded up.
        Dim userresult = GuildUserSettings.GetGuildUsersAsync(dbconfig, GuildId)
        _userCache = New Dictionary(Of ULong, GuildUserSettings)
        For Each item In userresult
            _userCache.Add(item.UserId, item)
        Next
    End Sub

    ''' <summary>
    ''' Gets user information from this guild. If the user doesn't exist in the backing database,
    ''' a new instance is created which is capable of adding the user to the database.
    ''' </summary>
    ''' <param name="userId"></param>
    ''' <returns></returns>
    Public Function GetUser(userId As ULong) As GuildUserSettings
        If _userCache.ContainsKey(userId) Then
            Return _userCache(userId)
        End If

        ' No result. Create a blank entry and add it to the list, in case it
        ' gets referenced later regardless of if having been updated or not.
        Dim blank As New GuildUserSettings(_GuildId, userId)
        _userCache.Add(userId, blank)
        Return blank
    End Function

    ''' <summary>
    ''' Deletes the user from the backing database. Drops the locally cached entry.
    ''' </summary>
    Public Async Function DeleteUserAsync(userId As ULong) As Task
        Dim user As GuildUserSettings = Nothing
        If _userCache.TryGetValue(userId, user) Then
            Await user.DeleteAsync(_db)
        Else
            Return
        End If
        _userCache.Remove(userId)
    End Function

    ''' <summary>
    ''' Checks if the given user is banned from issuing commands.
    ''' If the server is in moderated mode, this always returns True.
    ''' </summary>
    Public Async Function IsUserBannedAsync(userId As ULong) As Task(Of Boolean)
        If IsModerated Then Return True

        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"select * from {BackingTableBans} " +
                    "where guild_id = @Gid and user_id = @Uid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = GuildId
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = userId
                c.Prepare()
                Using r = Await c.ExecuteReaderAsync()
                    If Await r.ReadAsync() Then Return True
                    Return False
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Bans the specified user from issuing commands.
    ''' Does not check if the given user is already banned.
    ''' </summary>
    Public Async Function BanUserAsync(userId As ULong) As Task
        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"insert into {BackingTableBans} (guild_id, user_id) " +
                    "values (@Gid, @Uid) " +
                    "on conflict (guild_id, user_id) do nothing"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = GuildId
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = userId
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Removes the specified user from the ban list.
    ''' Does not check if the given user was not banned to begin with.
    ''' </summary>
    Public Async Function UnbanUserAsync(userId As ULong) As Task
        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"delete from {BackingTableBans} where " +
                    "guild_id = @Gid and user_id = @Uid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = GuildId
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = userId
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function

    Public Async Function UpdateRoleAsync(roleId As ULong) As Task
        _role = roleId
        RoleWarning = False
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateAnnounceChannelAsync(channelId As ULong?) As Task
        _channel = channelId
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateTimeZoneAsync(tzString As String) As Task
        _tz = tzString
        Await UpdateDatabaseAsync()
    End Function

#Region "Database"
    Public Const BackingTable = "settings"
    Public Const BackingTableBans = "banned_users"

    Friend Shared Sub SetUpDatabaseTable(db As NpgsqlConnection)
        Using c = db.CreateCommand()
            c.CommandText = $"create table if not exists {BackingTable} (" +
                "guild_id bigint primary key, " +
                "role_id bigint null, " +
                "channel_announce_id bigint null, " +
                "time_zone text null, " +
                "moderated boolean not null default FALSE" +
                ")"
            c.ExecuteNonQuery()
        End Using
        Using c = db.CreateCommand()
            c.CommandText = $"create table if not exists {BackingTableBans} (" +
                $"guild_id bigint not null references {BackingTable}, " +
                "user_id bigint not null, " +
                "PRIMARY KEY (guild_id, user_id)" +
                ")"
            c.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>
    ''' Retrieves an object instance representative of guild settings for the specified guild.
    ''' If settings for the given guild do not yet exist, a new value is created.
    ''' </summary>
    Friend Shared Async Function LoadSettingsAsync(dbsettings As Database, guild As ULong) As Task(Of GuildSettings)
        Using db = Await dbsettings.OpenConnectionAsync()
            Using c = db.CreateCommand()
                ' Take note of ordinals for use in the constructor
                c.CommandText = "select guild_id, role_id, channel_announce_id, time_zone, moderated " +
                    $"from {BackingTable} where guild_id = @Gid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = guild
                c.Prepare()
                Using r = Await c.ExecuteReaderAsync()
                    If Await r.ReadAsync() Then
                        Return New GuildSettings(r, dbsettings)
                    End If
                End Using
            End Using

            ' If we got here, no row exists. Create it.
            Using c = db.CreateCommand()
                c.CommandText = $"insert into {BackingTable} (guild_id) values (@Gid)"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = guild
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
        ' New row created. Try this again.
        Return Await LoadSettingsAsync(dbsettings, guild)
    End Function

    ''' <summary>
    ''' Updates the backing database with values from this instance
    ''' This is a non-asynchronous operation. That may be bad.
    ''' </summary>
    Private Async Function UpdateDatabaseAsync() As Task
        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"update {BackingTable} set " +
                    "role_id = @RoleId, " +
                    "channel_announce_id = @ChannelId, " +
                    "time_zone = @TimeZone, " +
                    "moderated = @Moderated " +
                    "where guild_id = @Gid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = GuildId
                With c.Parameters.Add("@RoleId", NpgsqlDbType.Bigint)
                    If RoleId.HasValue Then
                        .Value = RoleId.Value
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                With c.Parameters.Add("@ChannelId", NpgsqlDbType.Bigint)
                    If _channel.HasValue Then
                        .Value = _channel.Value
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                With c.Parameters.Add("@TimeZone", NpgsqlDbType.Text)
                    If _tz IsNot Nothing Then
                        .Value = _tz
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                c.Parameters.Add("@Moderated", NpgsqlDbType.Boolean).Value = _modded
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function
#End Region
End Class
