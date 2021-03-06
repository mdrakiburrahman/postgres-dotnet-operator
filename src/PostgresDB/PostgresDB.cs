using System.Text.Json.Serialization;
using OperatorSDK;

namespace POSTGRES_DB
{
	/// <summary>
	/// Class extends the BaseCRD from Operator SDK to add the PostgresDB CRD
	/// </summary>
	public class PostgresDB : BaseCRD
	{
		/// <summary>
		/// Constructor initiates instance of PostgresDB class
		/// This implements the BaseCRD class with:
		/// Group: samples.k8s-dotnet-controller-sdk
		/// Version: v1
		/// Plural: postgresdbs
		/// Singular: postgresdb
		/// ReconciliationCheckInterval: 5 (default) - but we can override from here
		/// </summary>
		public PostgresDB() :
			base("samples.k8s-dotnet-controller-sdk", "v1", "postgresdbs", "postgresdb")
		{ }

		// CRD contains the spec Class
		public PostgresDBSpec Spec { get; set; }
		
		// Overrides spec equality comparison
		public override bool Equals(object obj)
		{
			if (obj == null)
				return false;

			return ToString().Equals(obj.ToString());
		}

		public override int GetHashCode()
		{
			return ToString().GetHashCode();
		}

		public override string ToString()
		{
			return Spec.ToString();
		}

	}
	/// <summary>
	/// 1:1 with CRD Spec
	/// </summary>
	public class PostgresDBSpec
	{
		[JsonPropertyName("instance")]
		public string Instance { get; set; }

		[JsonPropertyName("dbName")]
		public string DbName { get; set; }

		[JsonPropertyName("configMap")]
		public string ConfigMap { get; set; }

		[JsonPropertyName("credentials")]
		public string Credentials { get; set; }
		
		// Overrides string conversion
		public override string ToString()
		{
			return $"{DbName}:{ConfigMap}:{Credentials}"; 
		}
	}
}
