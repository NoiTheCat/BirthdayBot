''' <summary>
''' Automatically removes database information for guilds that have not been accessed in a long time.
''' </summary>
Class StaleDataCleaner
    Inherits BackgroundService

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
    End Sub

    Public Overrides Async Function OnTick() As Task
        ' Build a list of all values to update
        Dim updateList As New Dictionary(Of ULong, List(Of ULong))
        For Each gi In BotInstance.GuildCache
            Dim existingUsers As New List(Of ULong)()
            updateList(gi.Key) = existingUsers

            Dim guild = BotInstance.DiscordClient.GetGuild(gi.Key)
            If guild Is Nothing Then Continue For ' Have cache without being in guild. Unlikely, but...

            ' Get IDs of cached users which are currently in the guild
            Dim cachedUserIds = From cu In gi.Value.Users Select cu.UserId
            Dim guildUserIds = From gu In guild.Users Select gu.Id
            Dim existingCachedIds = cachedUserIds.Intersect(guildUserIds)
            existingUsers.AddRange(existingCachedIds)
        Next

        Using db = Await BotInstance.Config.DatabaseSettings.OpenConnectionAsync()
            ' Prepare to update a lot of last-seen values
            Dim cUpdateGuild = db.CreateCommand()
            cUpdateGuild.CommandText = $"update {GuildStateInformation.BackingTable} set last_seen = now() " +
                "where guild_id = @Gid"
            Dim pUpdateG = cUpdateGuild.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint)
            cUpdateGuild.Prepare()

            Dim cUpdateGuildUser = db.CreateCommand()
            cUpdateGuildUser.CommandText = $"update {GuildUserSettings.BackingTable} set last_seen = now() " +
                "where guild_id = @Gid and user_id = @Uid"
            Dim pUpdateGU_g = cUpdateGuildUser.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint)
            Dim pUpdateGU_u = cUpdateGuildUser.Parameters.Add("@Uid", NpgsqlTypes.NpgsqlDbType.Bigint)
            cUpdateGuildUser.Prepare()

            ' Do actual updates
            For Each item In updateList
                Dim guild = item.Key
                Dim userlist = item.Value

                pUpdateG.Value = CLng(guild)
                cUpdateGuild.ExecuteNonQuery()

                pUpdateGU_g.Value = CLng(guild)
                For Each userid In userlist
                    pUpdateGU_u.Value = CLng(userid)
                    cUpdateGuildUser.ExecuteNonQuery()
                Next
            Next

            ' Delete all old values - expects referencing tables to have 'on delete cascade'
            Using t = db.BeginTransaction()
                Using c = db.CreateCommand()
                    ' Delete data for guilds not seen in 4 weeks
                    c.CommandText = $"delete from {GuildStateInformation.BackingTable} where (now() - interval '28 days') > last_seen"
                    Dim r = c.ExecuteNonQuery()
                    If r <> 0 Then Log($"Removed {r} stale guild(s).")
                End Using
                Using c = db.CreateCommand()
                    ' Delete data for users not seen in 8 weeks
                    c.CommandText = $"delete from {GuildUserSettings.BackingTable} where (now() - interval '56 days') > last_seen"
                    Dim r = c.ExecuteNonQuery()
                    If r <> 0 Then Log($"Removed {r} stale user(s).")
                End Using
            End Using
        End Using
    End Function
End Class
