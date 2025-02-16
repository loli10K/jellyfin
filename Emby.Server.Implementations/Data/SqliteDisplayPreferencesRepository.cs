using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using SQLitePCL.pretty;

namespace Emby.Server.Implementations.Data
{
    /// <summary>
    /// Class SQLiteDisplayPreferencesRepository
    /// </summary>
    public class SqliteDisplayPreferencesRepository : BaseSqliteRepository, IDisplayPreferencesRepository
    {
        private readonly IFileSystem _fileSystem;

        public SqliteDisplayPreferencesRepository(ILogger<SqliteDisplayPreferencesRepository> logger, IJsonSerializer jsonSerializer, IApplicationPaths appPaths, IFileSystem fileSystem)
            : base(logger)
        {
            _jsonSerializer = jsonSerializer;
            _fileSystem = fileSystem;
            DbFilePath = Path.Combine(appPaths.DataPath, "displaypreferences.db");
        }

        /// <summary>
        /// Gets the name of the repository
        /// </summary>
        /// <value>The name.</value>
        public string Name => "SQLite";

        /// <summary>
        /// The _json serializer
        /// </summary>
        private readonly IJsonSerializer _jsonSerializer;

        public void Initialize()
        {
            try
            {
                InitializeInternal();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading database file. Will reset and retry.");

                _fileSystem.DeleteFile(DbFilePath);

                InitializeInternal();
            }
        }

        /// <summary>
        /// Opens the connection to the database
        /// </summary>
        /// <returns>Task.</returns>
        private void InitializeInternal()
        {
            using (var connection = GetConnection())
            {
                string[] queries = {

                    "create table if not exists userdisplaypreferences (id GUID NOT NULL, userId GUID NOT NULL, client text NOT NULL, data BLOB NOT NULL)",
                    "create unique index if not exists userdisplaypreferencesindex on userdisplaypreferences (id, userId, client)"
                               };

                connection.RunQueries(queries);
            }
        }

        /// <summary>
        /// Save the display preferences associated with an item in the repo
        /// </summary>
        /// <param name="displayPreferences">The display preferences.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="client">The client.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">item</exception>
        public void SaveDisplayPreferences(DisplayPreferences displayPreferences, Guid userId, string client, CancellationToken cancellationToken)
        {
            if (displayPreferences == null)
            {
                throw new ArgumentNullException(nameof(displayPreferences));
            }

            if (string.IsNullOrEmpty(displayPreferences.Id))
            {
                throw new ArgumentException("Display preferences has an invalid Id", nameof(displayPreferences));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var connection = GetConnection())
            {
                connection.RunInTransaction(db =>
                {
                    SaveDisplayPreferences(displayPreferences, userId, client, db);
                }, TransactionMode);
            }
        }

        private void SaveDisplayPreferences(DisplayPreferences displayPreferences, Guid userId, string client, IDatabaseConnection connection)
        {
            var serialized = _jsonSerializer.SerializeToBytes(displayPreferences);

            using (var statement = connection.PrepareStatement("replace into userdisplaypreferences (id, userid, client, data) values (@id, @userId, @client, @data)"))
            {
                statement.TryBind("@id", displayPreferences.Id.ToGuidBlob());
                statement.TryBind("@userId", userId.ToGuidBlob());
                statement.TryBind("@client", client);
                statement.TryBind("@data", serialized);

                statement.MoveNext();
            }
        }

        /// <summary>
        /// Save all display preferences associated with a user in the repo
        /// </summary>
        /// <param name="displayPreferences">The display preferences.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">item</exception>
        public void SaveAllDisplayPreferences(IEnumerable<DisplayPreferences> displayPreferences, Guid userId, CancellationToken cancellationToken)
        {
            if (displayPreferences == null)
            {
                throw new ArgumentNullException(nameof(displayPreferences));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var connection = GetConnection())
            {
                connection.RunInTransaction(db =>
                {
                    foreach (var displayPreference in displayPreferences)
                    {
                        SaveDisplayPreferences(displayPreference, userId, displayPreference.Client, db);
                    }
                }, TransactionMode);
            }
        }

        /// <summary>
        /// Gets the display preferences.
        /// </summary>
        /// <param name="displayPreferencesId">The display preferences id.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="client">The client.</param>
        /// <returns>Task{DisplayPreferences}.</returns>
        /// <exception cref="ArgumentNullException">item</exception>
        public DisplayPreferences GetDisplayPreferences(string displayPreferencesId, Guid userId, string client)
        {
            if (string.IsNullOrEmpty(displayPreferencesId))
            {
                throw new ArgumentNullException(nameof(displayPreferencesId));
            }

            var guidId = displayPreferencesId.GetMD5();

            using (var connection = GetConnection(true))
            {
                using (var statement = connection.PrepareStatement("select data from userdisplaypreferences where id = @id and userId=@userId and client=@client"))
                {
                    statement.TryBind("@id", guidId.ToGuidBlob());
                    statement.TryBind("@userId", userId.ToGuidBlob());
                    statement.TryBind("@client", client);

                    foreach (var row in statement.ExecuteQuery())
                    {
                        return Get(row);
                    }
                }

                return new DisplayPreferences
                {
                    Id = guidId.ToString("N", CultureInfo.InvariantCulture)
                };
            }
        }

        /// <summary>
        /// Gets all display preferences for the given user.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>Task{DisplayPreferences}.</returns>
        /// <exception cref="ArgumentNullException">item</exception>
        public IEnumerable<DisplayPreferences> GetAllDisplayPreferences(Guid userId)
        {
            var list = new List<DisplayPreferences>();

            using (var connection = GetConnection(true))
            {
                using (var statement = connection.PrepareStatement("select data from userdisplaypreferences where userId=@userId"))
                {
                    statement.TryBind("@userId", userId.ToGuidBlob());

                    foreach (var row in statement.ExecuteQuery())
                    {
                        list.Add(Get(row));
                    }
                }
            }

            return list;
        }

        private DisplayPreferences Get(IReadOnlyList<IResultSetValue> row)
            => _jsonSerializer.DeserializeFromString<DisplayPreferences>(row.GetString(0));

        public void SaveDisplayPreferences(DisplayPreferences displayPreferences, string userId, string client, CancellationToken cancellationToken)
        {
            SaveDisplayPreferences(displayPreferences, new Guid(userId), client, cancellationToken);
        }

        public DisplayPreferences GetDisplayPreferences(string displayPreferencesId, string userId, string client)
        {
            return GetDisplayPreferences(displayPreferencesId, new Guid(userId), client);
        }
    }
}
