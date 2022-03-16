using System;
using System.Threading.Tasks;
using OperatorSDK;
using NLog;

namespace POSTGRES_DB
{
	// This is our Controller Class
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

				Controller<PostgresDB>.ConfigLogger();

				Log.Info($"=== {nameof(PostgresController)} STARTING for namespace {k8sNamespace} ===");

				// Our main CRD Interface
				PostgresDBOperationHandler handler = new PostgresDBOperationHandler();

				// Initiates the Contorller for the CRD
				Controller<PostgresDB> controller = new Controller<PostgresDB>(new PostgresDB(), handler, k8sNamespace);

				// Initiates controller in Async, there's a typo in the SDK - should be StartAsync
				Task reconciliation = controller.StartAsync();

				Log.Info($"=== {nameof(PostgresController)} STARTED ===");

				// Continues to run tasks
				// https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.configureawait
				reconciliation.ConfigureAwait(false).GetAwaiter().GetResult();

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
