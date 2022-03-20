using k8s.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace customResource
{
    public class CResource : CustomResource<CResourceSpec, CResourceStatus>
    {
        public override string ToString()
        {
            var labels = "{";
            foreach (var kvp in Metadata.Labels)
            {
                labels += kvp.Key + " : " + kvp.Value + ", ";
            }

            labels = labels.TrimEnd(',', ' ') + "}";

            return $"{Metadata.Name} (Labels: {labels}), Spec: {Spec.Engine}";
        }
    }

    public class CResourceSpec
    {
        [JsonPropertyName("engine")]
        public Dictionary<string, int> Engine { get; set; }
		
		[JsonPropertyName("services")]
        public Dictionary<string, Dictionary<string, string>> Services { get; set; }

		[JsonPropertyName("credentials")]
        public string Credentials { get; set; }

		[JsonPropertyName("initialCatalog")]
		public string InitialCatalog { get; set; }

    }

    public class CResourceStatus : V1Status
    {
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; }
    }
}
