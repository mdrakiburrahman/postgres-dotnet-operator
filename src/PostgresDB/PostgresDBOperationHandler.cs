using System;
using System.Collections.Generic;
using System.Data.SqlClient;
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
        const string USER_ID = "userid";
        const string PASSWORD = "password";
        const string MASTER = "master";

        // A dictionary that stores each of the matching CRDs status
        Dictionary<string, PostgresDB> m_currentState = new Dictionary<string, PostgresDB>();

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Gets Instance of the base type generated from registered factory
        /// </summary>
        /// <param name="k8s">Kubernetes Client</param>
        /// <param name="db">Database name</param>
        /// <returns>SQL Server connection object</returns>
        SqlConnection GetDBConnection(Kubernetes k8s, PostgresDB db)
        {
            var configMap = GetConfigMap(k8s, db);
            if (!configMap.Data.ContainsKey(INSTANCE))
                throw new ApplicationException($"ConfigMap '{configMap.Name()}' does not contain the '{INSTANCE}' data property.");

            string instance = configMap.Data[INSTANCE];
            
            var secret = GetSecret(k8s, db);
            if (!secret.Data.ContainsKey(USER_ID))
                throw new ApplicationException($"Secret '{secret.Name()}' does not contain the '{USER_ID}' data property.");

            if (!secret.Data.ContainsKey(PASSWORD))
                throw new ApplicationException($"Secret '{secret.Name()}' does not contain the '{PASSWORD}' data property.");

            string dbUser = ASCIIEncoding.UTF8.GetString(secret.Data[USER_ID]);
            string password = ASCIIEncoding.UTF8.GetString(secret.Data[PASSWORD]);

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = instance,
                UserID = dbUser,
                Password = password,
                InitialCatalog = MASTER
            };

            return new SqlConnection(builder.ConnectionString);
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

                using (SqlConnection connection = GetDBConnection(k8s, crd))
                {
                    connection.Open();

                    try
                    {
                        SqlCommand createCommand = new SqlCommand($"DROP DATABASE {crd.Spec.DBName};", connection);
                        int i = createCommand.ExecuteNonQuery();
                    }
                    catch (SqlException sex)
                    {
                        if (sex.Number == 3701)
                        {
                            // 0xe75	3701	SQL_3701_severity_11	Cannot %S_MSG the %S_MSG '%.*ls', because it does not exist or you do not have permission.
                            Log.Error(sex.Message);
                            return Task.CompletedTask;
                        }

                        Log.Error(sex.Message);
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
                // Loop over each of the CRD's we are tracking
                foreach (string key in m_currentState.Keys.ToList())
                {
                    // Our Database, grab by name
                    PostgresDB db = m_currentState[key];
                    using (SqlConnection connection = GetDBConnection(k8s, db))
                    {
                        // Check if DB exists in SYS.DATABASES
                        connection.Open();
                        SqlCommand queryCommand = new SqlCommand($"SELECT COUNT(*) FROM SYS.DATABASES WHERE NAME = '{db.Spec.DBName}';", connection);

                        try
                        {
                            // i to contain return flag of query execution
                            // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlcommand.executescalar?view=dotnet-plat-ext-6.0
                            // Returns null or 0 if no rows are returned
                            int i = (int)queryCommand.ExecuteScalar();

                            // Database doesn't exist
                            if (i == 0)
                            {
                                Log.Warn($"Database {db.Spec.DBName} ({db.Name()}) was not found!");
                                CreateDB(k8s, db); // So create
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

            using (SqlConnection connection = GetDBConnection(k8s, db))
            {
                connection.Open();

                try
                {
                    SqlCommand createCommand = new SqlCommand($"CREATE DATABASE {db.Spec.DBName};", connection);
                    int i = createCommand.ExecuteNonQuery();
                }
                catch (SqlException sex) when (sex.Number == 1801) // SQL Exception Database already exists
                {
                    // http://errors/
                    // 0x709	1801	SQL_1801_severity_16	Database '%.*ls' already exist
                    Log.Warn(sex.Message);
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
            string sqlCommand = @$"ALTER DATABASE {currentDB.Spec.DBName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE {currentDB.Spec.DBName} MODIFY NAME = {newDB.Spec.DBName};
ALTER DATABASE {newDB.Spec.DBName} SET MULTI_USER;";

            using (SqlConnection connection = GetDBConnection(k8s, newDB))
            {
                connection.Open();
                SqlCommand command = new SqlCommand(sqlCommand, connection);
                command.ExecuteNonQuery();
            }
        }
    }
}
