using System;
using System.Collections.Generic;
using Npgsql;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using OperatorSDK;
using NLog;
using Microsoft.Rest;

namespace POSTGRES_DB
{
    /// <summary>
	/// Implements IOperationHandler<BaseCRD> Interface
	/// </summary>
    public class PostgresDBOperationHandler : IOperationHandler<PostgresDB>
    {
        const string INSTANCE = "instance";
        const string CATALOG = "catalog";
        const string USER_ID = "userid";
        const string PASSWORD = "password";

        // A dictionary that stores each of the matching CRDs status
        Dictionary<string, PostgresDB> m_currentState = new Dictionary<string, PostgresDB>();

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Gets Instance of the base type generated from registered factory
        /// </summary>
        /// <param name="k8s">Kubernetes Client</param>
        /// <param name="db">Database name</param>
        /// <returns>SQL Server connection object</returns>
        NpgsqlConnection GetDBConnection(Kubernetes k8s, PostgresDB db)
        {
            // Pull Instance name and Catalog name from ConfigMap
            var configMap = GetConfigMap(k8s, db);
            if (!configMap.Data.ContainsKey(INSTANCE))
                throw new ApplicationException($"ConfigMap '{configMap.Name()}' does not contain the '{INSTANCE}' data property.");

            string instance = configMap.Data[INSTANCE];

            if (!configMap.Data.ContainsKey(CATALOG))
                throw new ApplicationException($"ConfigMap '{configMap.Name()}' does not contain the '{CATALOG}' data property.");

            string catalog = configMap.Data[CATALOG];
            
            var secret = GetSecret(k8s, db);
            if (!secret.Data.ContainsKey(USER_ID))
                throw new ApplicationException($"Secret '{secret.Name()}' does not contain the '{USER_ID}' data property.");

            if (!secret.Data.ContainsKey(PASSWORD))
                throw new ApplicationException($"Secret '{secret.Name()}' does not contain the '{PASSWORD}' data property.");

            string dbUser = ASCIIEncoding.UTF8.GetString(secret.Data[USER_ID]);
            string password = ASCIIEncoding.UTF8.GetString(secret.Data[PASSWORD]);

            Log.Info($"Connecting to PostgresDB: '{instance}' as '{dbUser}' with password '{password}'");

            var connString = $"Host={instance};Username={dbUser};Password={password};Database={catalog}";

            return new NpgsqlConnection(connString);
        }

        // Reads the CRD referenced configmap from K8s client
        V1ConfigMap GetConfigMap(Kubernetes k8s, PostgresDB db)
        {
            try
            {
                return k8s.ReadNamespacedConfigMap(db.Spec.ConfigMap, db.Namespace());
            }
            catch (HttpOperationException hoex) when (hoex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ApplicationException($"ConfigMap '{db.Spec.ConfigMap}' not found in namespace {db.Namespace()}");
            }
        }

        // Reads the CRD referenced secret from K8s client
        V1Secret GetSecret(Kubernetes k8s, PostgresDB db)
        {
            try
            {
                return k8s.ReadNamespacedSecret(db.Spec.Credentials, db.Namespace());
            }
            catch (HttpOperationException hoex) when (hoex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ApplicationException($"Secret '{db.Spec.Credentials}' not found in namespace {db.Namespace()}");
            }
        }

        // If a new CRD is added, add it to the current state dictionary
        public Task OnAdded(Kubernetes k8s, PostgresDB crd)
        {
            lock (m_currentState)
                CreateDB(k8s, crd);

            return Task.CompletedTask;
        }

        // Bookmark: https://kubernetes.io/docs/reference/using-api/api-concepts/#watch-bookmarks
        //  It is a special kind of event to mark that all changes up to a given resourceVersion the client is requesting have already been sent.
        public Task OnBookmarked(Kubernetes k8s, PostgresDB crd)
        {
            Log.Warn($"PostgresDB {crd.Name()} was BOOKMARKED (???)");

            return Task.CompletedTask;
        }

        // If a CRD is deleted, remove it from the current state dictionary, and delete from SQL too
        public Task OnDeleted(Kubernetes k8s, PostgresDB crd)
        {
            lock (m_currentState)
            {
                Log.Info($"PostgresDB {crd.Name()} must be deleted! ({crd.Spec.DBName})");

                using (NpgsqlConnection connection = GetDBConnection(k8s, crd))
                {
                    connection.Open();

                    try
                    {
                        var cmd = new NpgsqlCommand($"DROP DATABASE {crd.Spec.DBName};", connection);
                        int i = cmd.ExecuteNonQuery();
                    }
                    catch (NpgsqlException ex)
                    {
                        Log.Error($"PostgresDB {crd.Name()} could not be deleted! ({crd.Spec.DBName})");
                        Log.Error($"{ex.Message}");
                        return Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        Log.Fatal(ex.Message);
                        return Task.CompletedTask;
                    }

                    m_currentState.Remove(crd.Name());
                    Log.Info($"Database {crd.Spec.DBName} successfully dropped!");

                }

                return Task.CompletedTask;
            }
        }

        public Task OnError(Kubernetes k8s, PostgresDB crd)
        {
            Log.Error($"ERROR on {crd.Name()}");

            return Task.CompletedTask;
        }

        // Checks what was updated in the CRD
        public Task OnUpdated(Kubernetes k8s, PostgresDB crd)
        {
            Log.Info($"PostgresDB {crd.Name()} was updated. ({crd.Spec.DBName})");

            // The specific CRD that was updated
            PostgresDB currentDb = m_currentState[crd.Name()];

            // Checks if name was changed
            if (currentDb.Spec.DBName != crd.Spec.DBName)
            {
                try
                {
                    // Renames in SQL Server
                    RenameDB(k8s, currentDb, crd);
                    Log.Info($"Database sucessfully renamed from {currentDb.Spec.DBName} to {crd.Spec.DBName}");
                    m_currentState[crd.Name()] = crd;
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex);
                    throw;
                }
            }
            else
                // Name wasn't updated - so either the configMap name or the Creds secret name was updated
                m_currentState[crd.Name()] = crd;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Checks Database state inside the SQL Server instance
        /// If Database isn't there, it's going to try and create
        ///
        /// Note it does not come back to K8s and create something that isn't there - but that would be cool!
        /// </summary>
        public Task CheckCurrentState(Kubernetes k8s)
        {
            // Locks the dictionary of CRDs
            lock (m_currentState)
            {
                // Loop over each of the Database CRD's we are tracking
                foreach (string key in m_currentState.Keys.ToList())
                {
                    // Our Database, grab by name
                    PostgresDB db = m_currentState[key];

                    using (NpgsqlConnection connection = GetDBConnection(k8s, db))
                    {
                        // Check if DB exists in SYS.DATABASES
                        connection.Open();
                        var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM pg_database WHERE datname = '{db.Spec.DBName}';", connection);

                        try
                        {
                            // i to contain return flag of query execution
                            // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlcommand.executescalar?view=dotnet-plat-ext-6.0
                            // Returns null or 0 if no rows are returned
                            int i = (int)(long)cmd.ExecuteScalar();

                            // Database doesn't exist
                            if (i == 0)
                            {
                                Log.Warn($"Database {db.Spec.DBName} ({db.Name()}) was not found!");
                                CreateDB(k8s, db); // Create Database
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Message);
                        }
                    }
                }
            }

            return Task.CompletedTask; // Returns the completed Task: https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.completedtask?view=net-6.0#system-threading-tasks-task-completedtask
        }

        // Creates a new Database with the given CRD definition
        void CreateDB(Kubernetes k8s, PostgresDB db)
        {
            Log.Info($"Database {db.Spec.DBName} must be created.");

            using (NpgsqlConnection connection = GetDBConnection(k8s, db))
            {
                connection.Open();

                try
                {
                    var cmd = new NpgsqlCommand($"CREATE DATABASE {db.Spec.DBName};", connection);
                    int i = cmd.ExecuteNonQuery();
                }
                catch (NpgsqlException ex) // Postgres exception
                {
                    Log.Warn(ex.Message);
                    m_currentState[db.Name()] = db; // Update the dictionary to store the database object
                    return;
                }
                catch (Exception ex) // Something else other than Database existing went wrong
                {
                    Log.Fatal(ex.Message);
                    throw;
                }

                m_currentState[db.Name()] = db; // Created new dataase, update the dictionary to store the database object
                Log.Info($"Database {db.Spec.DBName} successfully created!");
            }
        }

        // Renames a Database with CRD Spec
        void RenameDB(Kubernetes k8s, PostgresDB currentDB, PostgresDB newDB)
        {
            // https://www.postgresqltutorial.com/postgresql-rename-database/
            string sqlCommand = @$"SELECT pg_terminate_backend (pid) FROM pg_stat_activity WHERE datname = '{currentDB.Spec.DBName}';
            ALTER DATABASE {currentDB.Spec.DBName} RENAME TO {newDB.Spec.DBName};";
            
            using (NpgsqlConnection connection = GetDBConnection(k8s, newDB))
            {
                connection.Open();
                var cmd = new NpgsqlCommand(sqlCommand, connection);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
