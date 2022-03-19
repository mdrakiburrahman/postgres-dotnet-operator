using System;
using System.Collections.Generic;
using Npgsql;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
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

                // Create PostgresDeployment if not exists
                if (CreatePostgresDeployment(k8s, crd))
                {
                    Log.Info($"✅ Postgres {crd.Name()} was CREATED");
                }
                else
                {
                    Log.Error($"❌ Postgres {crd.Name()} could not be created - check warnings.");
                };

            return Task.CompletedTask;
        }

        // - - - - -
        // Pre-reqs
        // - - - - -
        // Secret: "crd.Spec.Credentials" exists with keys userid, password
        //
        // - - - - - - - - 
        // Objects created
        // - - - - - - - -
        // Service: 1 x Service
        // Deployment: 1 x Postgres pod
        // ConfigMap: Contains Postgres Endpoint and Catalog name
        public bool CreatePostgresDeployment(Kubernetes k8s, PostgresSQL crd)
        {
            // Print out CRD Spec
            Log.Info($"▶ engine.version: {crd.Spec.Engine["version"]}");
            Log.Info($"▶ services.primary.type: {crd.Spec.Services["primary"]["type"]}");
            Log.Info($"▶ credentials: {crd.Spec.Credentials}");
            Log.Info($"▶ initialCatalog: {crd.Spec.InitialCatalog}");

            // - - - - - - - -
            // Pre-reqs check
            // - - - - - - - -
            // Variables
            string pg_deployment = $"{crd.Name()}-deployment";
            string pg_internal_service = $"{crd.Name()}-internal-svc";
            string pg_external_service = $"{crd.Name()}-external-svc";

            // Check Deployment, Services
            bool deployment_flag = checkDeploymentExists(k8s, crd.Namespace(), pg_deployment);
            bool internal_service_flag = checkServiceExists(k8s, crd.Namespace(), pg_internal_service);
            bool external_service_flag = checkServiceExists(k8s, crd.Namespace(), pg_external_service);
            
            // If any of these exist, return false
            if (deployment_flag || internal_service_flag || external_service_flag)
            {
                return false;
            }
            
            // Check if secret exists with correct keys
            if (!checkSecretExists(k8s, crd.Namespace(), crd.Spec.Credentials))
            {
                Log.Error($"Secret {crd.Spec.Credentials} does not exist");
                throw new ApplicationException($"Secret '{crd.Spec.Credentials}' not found in namespace {crd.Namespace()}");
            }
            else 
            {
                Log.Info($"✔ Found secret: {crd.Spec.Credentials}");
                var secret = GetSecret(k8s, crd.Namespace(), crd.Spec.Credentials);
                if (!secret.Data.ContainsKey("userid"))
                {
                    throw new ApplicationException($"Secret '{secret.Name()}' does not contain the 'userid' data property.");
                } else {
                    Log.Info($"✔ Found key: userid");
                }
                if (!secret.Data.ContainsKey("password"))
                {
                    throw new ApplicationException($"Secret '{secret.Name()}' does not contain the 'password' data property.");
                } else {
                    Log.Info($"✔ Found key: password");
                }
            }

            // Create Postgres Deployment
            Log.Info($"▶ Creating Postgres Deployment: {pg_deployment}");
            var deployment = ConstructPostgresDeployment(crd);
            var deployment_result = k8s.CreateNamespacedDeployment(deployment, crd.Namespace());

            // Create Postgres Services - Internal and External
            var external_svc = ConstructPostgresService(crd, crd.Spec.Services["primary"]["type"], "external");
            var internal_svc = ConstructPostgresService(crd, "ClusterIP", "internal");
            Log.Info($"▶ Creating Postgres Services...");
            var external_svc_result = k8s.CreateNamespacedService(external_svc, crd.Namespace());
            var internal_svc_result = k8s.CreateNamespacedService(internal_svc, crd.Namespace());

            return true;
        }

        // Checks if a given deployment exists in the namespace
        public bool checkDeploymentExists(Kubernetes k8s, string ns, string deployment)
        {
            foreach (var depl in k8s.ListNamespacedDeployment(ns).Items)
            {
                if (depl.Metadata.Name == deployment)
                {
                    Log.Info($"Deployment {deployment} exists");
                    return true;
                }
            }
            return false;
        }

        // Checks if a given service exists in the namespace
        public bool checkServiceExists(Kubernetes k8s, string ns, string service)
        {
            foreach (var svc in k8s.ListNamespacedService(ns).Items)
            {
                if (svc.Metadata.Name == service)
                {
                    Log.Info($"Service {service} exists");
                    return true;
                }
            }
            return false;
        }

        // Checks if a given secret exists in the namespace
        public bool checkSecretExists(Kubernetes k8s, string ns, string secret)
        {
            foreach (var sec in k8s.ListNamespacedSecret(ns).Items)
            {
                if (sec.Metadata.Name == secret)
                {
                    return true;
                }
            }
            return false;
        }
        
        // Reads secret from Kubernetes
        V1Secret GetSecret(Kubernetes k8s, string ns, string secret)
        {
            try
            {
                return k8s.ReadNamespacedSecret(secret, ns);
            }
            catch (HttpOperationException hoex) when (hoex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ApplicationException($"Secret '{secret}' not found in namespace {ns}");
            }
        }

        // Creates Postgres Deployment Object based on CRD spec
        V1Deployment ConstructPostgresDeployment(PostgresSQL crd)
        {
            string pg_deployment = $"{crd.Name()}-deployment";
            string pg_service = $"{crd.Name()}-service";

            V1Deployment deployment = new V1Deployment()
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = new V1ObjectMeta()
                {
                    Name = pg_deployment,
                    NamespaceProperty = crd.Namespace(),
                    Labels = new Dictionary<string, string>()
                {
                    { "app", crd.Name() }
                }
                },
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Selector = new V1LabelSelector()
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            { "app", crd.Name() }
                        }
                    },
                    Template = new V1PodTemplateSpec()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            CreationTimestamp = null,
                            Labels = new Dictionary<string, string>
                        {
                             { "app", crd.Name() }
                        }
                        },
                        Spec = new V1PodSpec
                        {
                            Containers = new List<V1Container>()
                            {
                                new V1Container()
                                {
                                    Name = "postgres",
                                    Image = crd.Spec.Engine["version"] switch {
                                        14 => "postgres:14",
                                        13 => "postgres:13",
                                        12 => "postgres:12",
                                        _ => "postgres:14",
                                    },
                                    ImagePullPolicy = "Always",
                                    Ports = new List<V1ContainerPort> { new V1ContainerPort(5432) },
                                    Env = new List<V1EnvVar>()
                                    {   
                                        // Need 3 env vars to spin up Postgres Container
                                        new V1EnvVar("POSTGRES_DB", $"{crd.Spec.InitialCatalog}"),
                                        new V1EnvVar()
                                        {
                                            Name = "POSTGRES_USER",
                                            ValueFrom = new V1EnvVarSource()
                                            {
                                                SecretKeyRef = new V1SecretKeySelector()
                                                {
                                                    Key = "userid",
                                                    Name = crd.Spec.Credentials
                                                }
                                            }
                                        },
                                        new V1EnvVar()
                                        {
                                            Name = "POSTGRES_PASSWORD",
                                            ValueFrom = new V1EnvVarSource()
                                            {
                                                SecretKeyRef = new V1SecretKeySelector()
                                                {
                                                    Key = "password",
                                                    Name = crd.Spec.Credentials
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                Status = new V1DeploymentStatus()
                {
                    Replicas = 1
                }
            };
            return deployment;
        }

        // Creates Postgres Service Object based on CRD spec
        V1Service ConstructPostgresService(PostgresSQL crd, string type, string suffix)
        {
            V1Service service = new V1Service()
            {
                ApiVersion = "v1",
                Kind = "Service",
                Metadata = new V1ObjectMeta()
                {
                    Name = $"{crd.Name()}-{suffix}-svc",
                    NamespaceProperty = crd.Namespace(),
                    Labels = new Dictionary<string, string>()
                    {
                        { "app", crd.Name() }
                    }
                },
                Spec = new V1ServiceSpec()
                {
                    Type = type,
                    Selector = new Dictionary<string, string>()
                    {
                        { "app", crd.Name() }
                    },
                    Ports = new List<V1ServicePort>()
                    {
                        new V1ServicePort()
                        {
                            Protocol = "TCP",
                            Port = 5432,
                            TargetPort = 5432
                        }
                    }
                }
            };
            return service;
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
                Log.Info($"▶ Postgres {crd.Name()} must be DELETED)");
                
                string pg_deployment = $"{crd.Name()}-deployment";
                string pg_internal_service = $"{crd.Name()}-internal-svc";
                string pg_external_service = $"{crd.Name()}-external-svc";

                // Deployment
                if(checkDeploymentExists(k8s, crd.Namespace(), pg_deployment))
                {
                    k8s.DeleteNamespacedDeployment(pg_deployment, crd.Namespace(), new V1DeleteOptions());
                    Log.Info($"🔥 Postgres Deployment {pg_deployment} was DELETED)");
                }
                // Internal Service
                if(checkServiceExists(k8s, crd.Namespace(), pg_internal_service))
                {
                    k8s.DeleteNamespacedServiceAsync(pg_internal_service, crd.Namespace(), new V1DeleteOptions());
                    Log.Info($"🔥 Postgres {pg_internal_service} was DELETED)");
                }
                // External Service
                if(checkServiceExists(k8s, crd.Namespace(), pg_external_service))
                {
                    k8s.DeleteNamespacedServiceAsync(pg_external_service, crd.Namespace(), new V1DeleteOptions());
                    Log.Info($"🔥 Postgres {pg_external_service} was DELETED)");
                }
                Log.Info($"🔥 Postgres {crd.Name()} was DELETED)");
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
