using k8s.Models;
using System.Collections.Generic;

// Done
namespace customResource
{
    public class Utils
    {
        // creats a CRD definition
        public static CustomResourceDefinition MakeCRD()
        {
            var myCRD = new CustomResourceDefinition()
            {
                Kind = "PostgreSQL",
                Group = "samples.k8s-dotnet-controller-sdk",
                Version = "v1",
                PluralName = "postgresqls",
            };

            return myCRD;
        }

        // Creating a CR instance using C#
        public static CResource MakeCResource(string name, string ns, int version, string serviceType, string credentialSecret, string catalog)
        {
            var myCResource = new CResource()
            {
                Kind = "PostgreSQL",
                ApiVersion = "samples.k8s-dotnet-controller-sdk/v1",
                Metadata = new V1ObjectMeta
                {
                    Name = name,
                    NamespaceProperty = ns,
                    Labels = new Dictionary<string, string>
                    {
                        {
                            "creator", "csharp"
                        },
                    },
                },
                Spec = new CResourceSpec
                {
                    Engine = new Dictionary<string, int>
                    {
                        {
                            "version", version
                        },
                    },
                    Services = new Dictionary<string, Dictionary<string, string>>
                    {
                        {
                            "primary", new Dictionary<string, string>
                            {
                                {
                                    "type", serviceType
                                }
                            }
                        },
                    },
                    Credentials = credentialSecret,
                    InitialCatalog = catalog,
                },
            };
            return myCResource;
        }
    }
}
