using System.Collections.Generic;
using OperatorSDK;

namespace POSTGRESSQL
{
	/// <summary>
	/// Class extends the BaseCRD from Operator SDK to add the PostgreSQL CRD
	/// </summary>
	public class PostgresSQL : BaseCRD
	{
		/// <summary>
		/// Constructor initiates instance of PostgresSQL class
		/// This implements the BaseCRD class with:
		/// Group: samples.k8s-dotnet-controller-sdk
		/// Version: v1
		/// Plural: postgresqls
		/// Singular: postgresql
		/// ReconciliationCheckInterval: 5 (default) - but we can override from here
		/// </summary>
		public PostgresSQL() :
			base("samples.k8s-dotnet-controller-sdk", "v1", "postgresqls", "postgresql")
		{ }

		// CRD contains the spec Class
		public PostgresSQLSpec Spec { get; set; }
		
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
	public class PostgresSQLSpec
	{
        public Dictionary<string, string> Engine { get; set; }
        public Dictionary<string, string> Service { get; set; }
        public string Credentials { get; set; }
		public string InitialCatalog { get; set; }
		
	}
}
