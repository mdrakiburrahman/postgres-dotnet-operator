# postgres-dotnet-operator
My homegrown Kubernetes Operator for Postgres in dotnet

## Microsoft SQL Server Database operator

### Relevant docs
* Kubernetes API reference: https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.23/#objectmeta-v1-meta
* `lock` for C#: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
* `Task`-based Async pattern (TAP): https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
* `Task` for C#: https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task?view=net-6.0
* Bookmark for K8s: https://kubernetes.io/docs/reference/using-api/api-concepts/#watch-bookmarks

---

### Microk8s setup

Run these in local **PowerShell in _Admin mode_** to spin up via Multipass:

> Run with Docker Desktop turned off so `microk8s-vm` has no trouble booting up

**Multipass notes**
* `Multipassd` is the main binary available here: C:\Program Files\Multipass\bin
* Default VM files end up here: C:\Windows\System32\config\systemprofile\AppData\Roaming\multipassd


```PowerShell
# Delete old one (if any)
multipass list
multipass delete microk8s-vm
multipass purge

# Single node K8s cluster
# Latest releases: https://microk8s.io/docs/release-notes
microk8s install "--cpu=4" "--mem=6" "--disk=10" "--channel=1.22/stable" -y

# Launched: microk8s-vm
# 2022-03-05T23:05:51Z INFO Waiting for automatic snapd restart...
# ...

# Allow priveleged containers
multipass shell microk8s-vm
# This shells us in

sudo bash -c 'echo "--allow-privileged" >> /var/snap/microk8s/current/args/kube-apiserver'

exit # Exit out from Microk8s vm

# Start microk8s
microk8s status --wait-ready

# Get IP address of node for MetalLB range
microk8s kubectl get nodes -o wide
# NAME          STATUS   ROLES    AGE   VERSION                    INTERNAL-IP      EXTERNAL-IP   OS-IMAGE             KERNEL-VERSION       CONTAINER-RUNTIME
# microk8s-vm   Ready    <none>   75s   v1.22.6-3+7ab10db7034594   172.27.56.19      <none>        Ubuntu 18.04.6 LTS   4.15.0-169-generic   containerd://1.5.2

# Enable features needed for arc
microk8s enable dns storage metallb ingress
# Enter CIDR for MetalLB: 172.27.56.40-172.27.56.50
# This must be in the same range as the VM above!

# Access via kubectl in this container
$DIR = "C:\Users\mdrrahman\Documents\GitHub\dotnet-operator-sdk\microk8s"
microk8s config view > $DIR\config # Export kubeconfig
```

Turn on Docker Desktop.

Now we go into our VSCode Container:

```bash
cd /workspaces/dotnet-operator-sdk
rm -rf $HOME/.kube
mkdir $HOME/.kube
cp microk8s/config $HOME/.kube/config
dos2unix $HOME/.kube/config
cat $HOME/.kube/config

# Check kubectl works
kubectl get nodes
# NAME          STATUS   ROLES    AGE   VERSION
# microk8s-vm   Ready    <none>   29m   v1.22.6-3+7ab10db7034594
```

---

### Pre-Controller prep

```bash
# Create CRD
kubectl apply -f /workspaces/dotnet-operator-sdk/samples/postgres-db/yaml/postgres-crd.yaml
# customresourcedefinition.apiextensions.k8s.io/PostgresDBs.samples.k8s-dotnet-controller-sdk created

# Create SQL Pod
kubectl apply -f /workspaces/dotnet-operator-sdk/samples/postgres-db/yaml/deployment.yaml

# Make sure we can connect from this container
sqlcmd -S 172.27.56.40,1433 -U sa -P acntorPRESTO! -Q "SELECT name FROM sys.databases"
# results
```

---

### Run Controller locally

```bash
cd /workspaces/dotnet-operator-sdk/samples/postgres-db

dotnet build
# Build succeeded.
#     0 Warning(s)
#     0 Error(s)
# Time Elapsed 00:00:13.05

dotnet run
# 2022-03-16 01:03:30.0737 [INFO] postgres_db.PostgresController:=== PostgresController STARTING for namespace default ===
# 2022-03-16 01:03:30.2362 [INFO] postgres_db.PostgresController:=== PostgresController STARTED ===
# 2022-03-16 01:03:30.3576 [INFO] OperatorSDK.Controller`1:Reconciliation Loop for CRD PostgresDB will run every 5 seconds.

# Test DB CRD:
kubectl apply -f /workspaces/dotnet-operator-sdk/samples/postgres-db/yaml/db1.yaml
# 2022-03-16 01:34:00.1830 [INFO] OperatorSDK.Controller`1:postgres_db.PostgresDB db1 Added on Namespace default
# 2022-03-16 01:34:00.1830 [INFO] postgres_db.PostgresDBOperationHandler:Database MyFirstDB must be created.
# 2022-03-16 01:34:00.7339 [INFO] postgres_db.PostgresDBOperationHandler:Database MyFirstDB successfully created!

# SSMS: we see MyFirstDB

# Edit CRD DB name:

# 2022-03-16 01:36:47.3682 [INFO] OperatorSDK.Controller`1:postgres_db.PostgresDB db1 Modified on Namespace default
# 2022-03-16 01:36:47.3682 [INFO] postgres_db.PostgresDBOperationHandler:PostgresDB db1 was updated. (MyFirstDB_rename)
# 2022-03-16 01:36:47.4618 [INFO] postgres_db.PostgresDBOperationHandler:Database sucessfully renamed from MyFirstDB to MyFirstDB_rename

# SSMS: we see MyFirstDB_rename

# Delete in SQL
sqlcmd -S 172.27.56.40,1433 -U sa -P acntorPRESTO! -Q " DROP DATABASE MyFirstDB_rename"

# 2022-03-16 01:41:58.5164 [WARN] postgres_db.PostgresDBOperationHandler:Database MyFirstDB_rename (db1) was not found!
# 2022-03-16 01:41:58.5164 [INFO] postgres_db.PostgresDBOperationHandler:Database MyFirstDB_rename must be created.
# 2022-03-16 01:41:58.9141 [INFO] postgres_db.PostgresDBOperationHandler:Database MyFirstDB_rename successfully created!

# Delete DB CRD
kubectl delete -f /workspaces/dotnet-operator-sdk/samples/postgres-db/yaml/db1.yaml
# 2022-03-16 01:43:11.9665 [INFO] OperatorSDK.Controller`1:postgres_db.PostgresDB db1 Deleted on Namespace default
# 2022-03-16 01:43:11.9665 [INFO] postgres_db.PostgresDBOperationHandler:PostgresDB db1 must be deleted! (MyFirstDB_rename)
# 2022-03-16 01:43:11.9916 [INFO] postgres_db.PostgresDBOperationHandler:Database MyFirstDB_rename successfully dropped!

# SSMS: DB is gone!

```
---

### Deploy Controller as a pod

```bash
# Login to docker with access token
docker login --username=mdrrakiburrahman --password=$DOCKERHUB_TOKEN

cd /workspaces/dotnet-operator-sdk/samples/postgres-db

# Build & push
docker build -t mdrrakiburrahman/PostgresDB-controller .
docker push mdrrakiburrahman/PostgresDB-controller

# Deploy to k8s and tail
kubectl apply -f /workspaces/dotnet-operator-sdk/samples/postgres-db/yaml/controller-deployment.yaml

kubectl logs PostgresDB-controller-6b7c85fb4-shd2j --follow
# And the same tests above.

```

---

## Definition

This is a controller for a newly defined `CustomResourceDefinition` (CRD) that lets you create or delete (drop) databases from a Microsoft SQL Server `Pod` running in your Kubernetes cluster.

> Compare this to the SQL MI CRD [here](https://github.com/microsoft/azure_arc/blob/1565c7ab9a141e1879585daa8b687324822a70ce/arc_data_services/deploy/yaml/custom-resource-definitions.yaml#L223).

```yaml
apiVersion: apiextensions.k8s.io/v1
kind: CustomResourceDefinition
metadata:
  name: PostgresDBs.samples.k8s-dotnet-controller-sdk
spec:
  group: samples.k8s-dotnet-controller-sdk
  scope: Namespaced
  names:
    plural: PostgresDBs
    singular: PostgresDB
    kind: PostgresDB
  # Basically versioning the CRDs
  versions:
    - name: v1
	  # https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definition-versioning/#overview
      served: true
      storage: true
      schema:
        openAPIV3Schema:
          type: object
          description: "A Microsoft SQLServer Database"
          properties:
		  	# The actual CRD Spec
            spec:
              type: object
              properties:
                dbname:
                  type: string
                configmap:
                  type: string
                credentials:
                  type: string
              required: ["dbname","configmap", "credentials"]
```

This `CRD` has three properties, `dbname`, `configmap`, and `credentials`. All three of them are strings, but they all have different semantics.  

- `dbname` holds the name of the Database that will be added/delete to the SQL Server instance.
- `configmap` is the name of a [`ConfigMap`](https://kubernetes.io/docs/concepts/configuration/configmap/) with a property called `instance`. That's where the name of the [`Service`](https://kubernetes.io/docs/concepts/services-networking/service/) related to the SQL Server pod is listening.
- `credentials` is also an indirection, but in this case to a [`Secret`](https://kubernetes.io/docs/concepts/configuration/secret/) that holds both the user (`userid`) and password (`password`) to the SQL Server instance.

As you can see, these are mandatory for the controller to successfully communicate to the SQL Server instance.

So, a typical yaml for my new resource, called `PostgresDB`, will look like this

```yaml
apiVersion: samples.k8s-dotnet-controller-sdk/v1
kind: PostgresDB
metadata:
  name: db1
spec:
  dbname: MyFirstDB
  configmap: postgres-config
  credentials: postgres-credentials
```

This yaml will create (or delete) an object of kind `PostgresDB`, named db1 with the properties mentioned above. In this case, a `ConfigMap` called `postgres-config` and a `Secret` called `postgres-credentials` must exist.

### Implementation

If we first apply the first file ([`CustomResourceDefinition`](./yaml/postgres-crd.yaml)) and we then apply the second one ([db1.yaml](./yaml/db1.yaml)), we'll see that Kubernetes successfully creates the object.

```bash
kubectl apply -f .\db1.yaml  
PostgresDB.samples.k8s-cs-controller/db1 created
```

But nothing actually happens other than the API-Server saving the data in the cluster's etcd database. We need to do something that "listens" for our newly created definition and eventually would create or delete databases.

#### Base class

We need to create a class that represents our definition. For that purpose, the SDK provides a class called **`BaseCRD`** which is where your class will inherit from. Also, you must create a spec class that will hold the properties defined in your custom resource. In my case, this is what they look like:

`samples/postgres-db/PostgresDB.cs`:
```cs
public class PostgresDB : BaseCRD
{
	public PostgresDB() :
		base("samples.k8s-cs-controller", "v1", "PostgresDBs", "PostgresDB")
	{ }

	public PostgresDBSpec Spec { get; set; }
}

public class PostgresDBSpec
{
	public string DBName { get; set; }

	public string ConfigMap { get; set; }

	public string Credentials { get; set; }
}
```

Keep in mind the strings you must pass over the base class' constructor. These are the same values defined in the `CustomeResourceDefinition` file.

Then you need to create the class that will be actually creating or deleting the databases. For this purpose, create a class that implements the **`IOperationHAndler<T>`**, where **`T`** is your implementation of the **`BaseCRD`**,  in my case **`PostgresDB`**.

`samples/postgres-db/PostgresDBOperationHandler.cs`:
```cs
public interface IOperationHandler<T> where T : BaseCRD
{
	Task OnAdded(Kubernetes k8s, T crd);

	Task OnDeleted(Kubernetes k8s, T crd);

	Task OnUpdated(Kubernetes k8s, T crd);

	Task OnBookmarked(Kubernetes k8s, T crd);

	Task OnError(Kubernetes k8s, T crd);

	Task CheckCurrentState(Kubernetes k8s);
}
```
The implementation is pretty straight forward, you need to implement the **`On_Action_`** methods. These methods are the ones that will communicate with the SQL Server instance and will create or delete the databases. So whenever somebody uses `kubectl` to create, apply or delete an object, these methods will be called.

But what happens if somebody or something connects to your SQL Server instance and deletes the databases? Here's where the **`CheckCurrentState`** method comes into play. This method, in my case, is checking every 5 seconds if the **`PostgresDB`** objects created in my cluster are actually created as databases in the SQL Server instance. If they are not, it will try to recreate them.

### Start your engines!

Ok, now it's time to start and try everything.

In my case it's a .NET Core console application where I start the controller. (I've also seen ASP.NET Hosted Services)

```cs
static void Main(string[] args)
{
	try
	{
		string k8sNamespace = "default";
		if (args.Length > 1)
			k8sNamespace = args[0];

		Controller<PostgresDB>.ConfigLogger();

		Log.Info($"=== {nameof(PostgresController)} STARTING for namespace {k8sNamespace} ===");

		PostgresDBOperationHandler handler = new PostgresDBOperationHandler();
		Controller<PostgresDB> controller = new Controller<PostgresDB>(new PostgresDB(), handler);
		Task reconciliation = controller.StartAsync(k8sNamespace);

		Log.Info($"=== {nameof(PostgresController)} STARTED ===");

		reconciliation.ConfigureAwait(false).GetAwaiter().GetResult();

	}
	catch (Exception ex)
	{
		Log.Fatal(ex);
	}
	finally
	{
		Log.Warn($"=== {nameof(PostgresController)} TERMINATING ===");
	}
}
```
Here you can see that I first create the handler and pass it over to the controller instance. This **`Controller`** is given by the SDK and it's the one checking on the objects created by the kubernetes-apiserver. I then start the controller, the handler for the current state, and that's it!.

### Take it for a spin

Start your console application and see what happens.

```log
2020-10-04 12:26:22.2833 [INFO] postgres_db.PostgresController:=== PostgresController STARTING for namespace default ===
2020-10-04 12:26:23.0727 [INFO] postgres_db.PostgresController:=== PostgresController STARTED ===
2020-10-04 12:26:29.5139 [INFO] K8sControllerSDK.Controller`1:Reconciliation Loop for CRD PostgresDB will run every 5 seconds.
2020-10-04 12:26:29.5954 [INFO] K8sControllerSDK.Controller`1:postgres_db.PostgresDB db1 Added on Namespace default
2020-10-04 12:26:29.6158 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB will be ADDED
2020-10-04 12:26:30.4513 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB successfully created!
2020-10-04 12:26:39.6329 [INFO] K8sControllerSDK.Controller`1:postgres_db.PostgresDB db1 Deleted on Namespace default
2020-10-04 12:26:39.6339 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB will be DELETED!
2020-10-04 12:26:39.7343 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB successfully deleted!
2020-10-04 12:26:45.7297 [INFO] K8sControllerSDK.Controller`1:postgres_db.PostgresDB db1 Added on Namespace default
2020-10-04 12:26:45.7297 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB will be ADDED
2020-10-04 12:26:47.0061 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB successfully created!
2020-10-04 12:26:59.7036 [WARN] postgres_db.PostgresDBOperationHandler:Database MyFirstDB was not found!
2020-10-04 12:26:59.7036 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB will be ADDED
2020-10-04 12:27:01.3013 [INFO] postgres_db.PostgresDBOperationHandler:DATABASE MyFirstDB successfully created!
```

Here's the log of the execution. The first thing I did was created the first db (all these yaml files are in the [yaml](./yaml) folder)

`kubectl apply -f .\db1.yaml`

I then deleted the object, and the database was successfully deleted:

`kubectl delete -f .\db1.yaml`

I then created it again via YAML, and connected to the pod running the SQL Server and dropped the MyFirstDB database, thus, you see that `Database MyFirstDB was not found!` message.

Also, in the log shown above, you'll notice some messages seem to have the same info, but they actually come from two sources. One from the controller engine itself (from inside the SDK) and some form my own PostgresDB implementation.

### Run it in your container

This msqldb controller is also available as a Docker image in my personal Docker Hub repository under [sebagomez/k8s-PostgresDB](https://hub.docker.com/repository/docker/sebagomez/k8s-PostgresDB). 

In the [yaml](./samples/msssql-db/yaml) folder there are a few files that can be used to try the controller.

File|Description
---|---
[deployment.yaml](./samples/msssql-db/yaml/deployment.yaml)|Sets up a `Pod` with an instance of MS SQL Server, a `Service` to access the `Pod` and the `ConfigMap` and `Secret` needed to access the SQL Server instance.
[postgres-crd.yaml](./samples/msssql-db/yaml/postgres-crd.yaml)|The `CustomResourceDefinition` for this new resource called `PostgresDB`.
[controller-deployment.yaml](./samples/msssql-db/yaml/controller-deployment.yaml)|Spins up a `Pod` with the controller itself.
[db1.yaml](./samples/msssql-db/yaml/db1.yaml)|A sample PostgresDB that creates a database called 'MyFirstDB'

Apply those scripts in the order described above. Also, you can play around renaming the SQL Database instance modifiyng `dbname` value of the `db1.yaml` file.

But, is it working?

I've added a little script at [sqlcmd.sh](./samples/msssql-db/yaml/sqlcmd.sh) that will spin a `Pod` with the SqlCmd utility.  
Once you run the script you can connect to your SQL Server instance with running the following command

`sqlcmd -S postgres-service -U sa -P MyNotSoSecuredPa55word!`


Once connected, you can check the existent databases like this

```sql
select name from sys.databases;
go
```

Want to make interesting? Drop the database created by Kubernetes and see what happens

```sql
drop database MyFirstDB;
go
``` 

If you're fast enough, you will see that the database is gone. But after a few seconds (5 max) the database is once again created. This is because the reconciliation loop realized that the actual state of the cluster is not consistent with the desired state, so it will try to change that.

Have fun with it and let me know what you think.
