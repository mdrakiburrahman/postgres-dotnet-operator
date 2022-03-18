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

namespace POSTGRESSQL
{
    /// <summary>
	/// Implements IOperationHandler<BaseCRD> Interface
	/// </summary>
    public class PostgreSQLOperationHandler : IOperationHandler<PostgresSQL>
    {
        // A dictionary that stores each of the matching CRDs status
        Dictionary<string, PostgresSQL> m_currentState = new Dictionary<string, PostgresSQL>();
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // If a new CRD is added, add it to the current state dictionary
        public Task OnAdded(Kubernetes k8s, PostgresSQL crd)
        {
            lock (m_currentState)
                Log.Info($"PostgresSQL {crd.Name()} was ADDED)");
            return Task.CompletedTask;
        }

        // Bookmark: https://kubernetes.io/docs/reference/using-api/api-concepts/#watch-bookmarks
        //  It is a special kind of event to mark that all changes up to a given resourceVersion the client is requesting have already been sent.
        public Task OnBookmarked(Kubernetes k8s, PostgresSQL crd)
        {
            Log.Warn($"PostgresDB {crd.Name()} was BOOKMARKED (???)");
            return Task.CompletedTask;
        }

        // If a CRD is deleted, remove it from the current state dictionary, and delete from SQL too
        public Task OnDeleted(Kubernetes k8s, PostgresSQL crd)
        {
            lock (m_currentState)
            {
                Log.Info($"PostgresDB {crd.Name()} must be deleted! ({crd.Name()})");
            }
                return Task.CompletedTask;
        }

        public Task OnError(Kubernetes k8s, PostgresSQL crd)
        {
            Log.Error($"ERROR on {crd.Name()}");

            return Task.CompletedTask;
        }

        // Checks what was updated in the CRD
        public Task OnUpdated(Kubernetes k8s, PostgresSQL crd)
        {
            Log.Info($"PostgresDB {crd.Name()} was updated. ({crd.Name()})");

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
                    // Our Instance, grab by name
                    PostgresSQL pg = m_currentState[key];

                    Log.Info($"Checking PostgresDB {pg.Name()}");
                }
            }
            return Task.CompletedTask;
        }
    }
}
