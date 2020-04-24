Imports Npgsql
''' <summary>
''' Automatically removes database information for guilds that have not been accessed in a long time.
''' </summary>
Class StaleDataCleaner
    Inherits BackgroundService

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
    End Sub

    Public Overrides Async Function OnTick() As Task
        Using db = Await BotInstance.Config.DatabaseSettings.OpenConnectionAsync()
            ' Update only for all guilds the bot has cached
            Using c = db.CreateCommand()
                c.CommandText = $"update {GuildStateInformation.BackingTable} set last_seen = now() " +
                    "where guild_id = @Gid"
                Dim updateGuild = c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint)
                c.Prepare()

                Dim list As New List(Of ULong)(BotInstance.GuildCache.Keys)
                For Each id In list
                    updateGuild.Value = CLng(id)
                    c.ExecuteNonQuery()
                Next
            End Using

            ' Delete all old values - expects referencing tables to have 'on delete cascade'
            Using t = db.BeginTransaction()
                Using c = db.CreateCommand()
                    ' Delete data for guilds not seen in 2 weeks
                    c.CommandText = $"delete from {GuildStateInformation.BackingTable} where (now() - interval '28 days') > last_seen"
                    Dim r = c.ExecuteNonQuery()
                    If r <> 0 Then Log($"Removed {r} stale guild(s).")
                End Using
            End Using
        End Using
    End Function
End Class
