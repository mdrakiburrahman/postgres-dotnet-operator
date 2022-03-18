using System;
using System.Threading.Tasks;
using OperatorSDK;
using NLog;
using POSTGRES_DB;
using POSTGRESSQL;

namespace ControllerService
{
	// Controller class for all things Postgres
	class PostgresController
	{
		// Initiates logger
		private static readonly Logger Log = LogManager.GetCurrentClassLogger();

		// Controller entrypoint
		static void Main(string[] args)
		{
			try
			{
				string k8sNamespace = "default";
				if (args.Length > 1) // If an arg was passed in it is namespace, so override it
					k8sNamespace = args[0];

				Log.Info($"=== {nameof(PostgresController)} STARTING for namespace {k8sNamespace} ===");

				// - - - - -
				// Instance
				// - - - - -
				Controller<PostgresSQL>.ConfigLogger();
				PostgreSQLOperationHandler sql_handler = new PostgreSQLOperationHandler();

				// Initiates the Controller for the Database CRD in Async mode
				Controller<PostgresSQL> sql_controller = new Controller<PostgresSQL>(new PostgresSQL(), sql_handler, k8sNamespace);
				Task sql_reconciliation = sql_controller.StartAsync();
				Log.Info($"=== INSTANCE: {nameof(PostgresController)} STARTED ===");

				// - - - - 
				// Database
				// - - - - 
				// Database CRD Interface
				Controller<PostgresDB>.ConfigLogger();
				PostgresDBOperationHandler db_handler = new PostgresDBOperationHandler();

				// Initiates the Controller for the Database CRD in Async mode
				Controller<PostgresDB> db_controller = new Controller<PostgresDB>(new PostgresDB(), db_handler, k8sNamespace);
				Task db_reconciliation = db_controller.StartAsync();
				Log.Info($"=== DATABASE: {nameof(PostgresController)} STARTED ===");

				// Continues to run tasks
				// https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.configureawait
				sql_reconciliation.ConfigureAwait(false).GetAwaiter().GetResult();
				db_reconciliation.ConfigureAwait(false).GetAwaiter().GetResult();

			}
			catch (Exception ex)
			{
				Log.Fatal(ex);
					throw;
			}
			finally
			{
				// If controller is killed
				Log.Warn($"=== {nameof(PostgresController)} TERMINATING ===");
			}
		}
	}
}
