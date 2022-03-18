# postgres-dotnet-operator

My homegrown Kubernetes Operator for Postgres in dotnet

## My Postgres Database operator
- [x]  ~~[**Learn dotnet (Feb 20 - Mar 6)**](https://github.com/mdrakiburrahman/exercism_dotnet)~~
- [x]  ~~[**Dotnet Operator SDK - wrapper around C# Client**](https://github.com/mdrakiburrahman/postgres-dotnet-operator/tree/main/src/OperatorSDK) (Mar 6 - Mar 15)~~
- [ ]  **Build your own Postgres operator using the Framework**
    - [x] ~~**Project scaffold**~~
        - [x] ~~Operator-SDK + SQL all in one repo, remove Nuget for now (Mar 16 -)~~
        - [x] ~~Test in K8s with MSSQL Pod~~
    - [ ]  **Features**
        - [X]  ~~Postgres Database CRD with an existing Postgres Container image and [Npgsql](https://www.nuget.org/packages/Npgsql/)~~
        - [ ]  Extend to include a Postgres instance CRD too
			- [ ] Make with vanilla Postgres 14 image
			- [ ] Make your own Postgres pod image in a Dockerfile from `src` for better control of what's inside
			- [ ] Deploy `StatefulSet` 
			- [ ] Deploy with `PVC`
		- [ ]  Two pods in HA spec
        - [ ]  ⭐ Inject `pg_auto_failover`
        - [ ]  CRD Status
		- [ ]  ⭐ LDAP
        - [ ]  ⭐ Custom SSL
        - [ ]  Database level changes
            - [ ]  **2 way sync state DB <> CRD**
			> I'm not sure how this would work, only `status` is supposed to be updated ...if the Controller tries to change it's own CRD's `spec` does it go into a recursion since that generates a modified event?
        - [ ]  Backup/Restore (same logic as MI with the JSON files)
        - [ ]  Extend to Citus
        - [ ]  Vault CSI
    - [ ]  **Best practices**
        - [ ] CRD Spec validation
		- [ ] Queue up events if Controller is down

---

### Relevant docs

- Kubernetes API reference: https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.23/#objectmeta-v1-meta
- `lock` for C#: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
- `Task`-based Async pattern (TAP): https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
- `Task` for C#: https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task?view=net-6.0
- `Bookmark` for K8s: https://kubernetes.io/docs/reference/using-api/api-concepts/#watch-bookmarks
- `Npgsql`: https://zetcode.com/csharp/postgresql/
- `ownerReference`: https://stackoverflow.com/questions/51068026/when-exactly-do-i-set-an-ownerreferences-controller-field-to-true
- `CRD`: https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/
	- `x-kubernetes-preserve-unknown-fields: true`: https://kubernetes.io/blog/2019/06/20/crd-structural-schema/#extensions (basically doesn't prune CRD)
- `OpenAPIv3`: https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#validation
- Amazing video series on `Async` and `await`:
	- https://www.youtube.com/watch?v=FIZVKteEFyk 
	- https://www.youtube.com/watch?v=S49dpEwMSUY
	- https://www.youtube.com/watch?v=By2HlOKIZxs
---

### [SRS notes](https://krazytech.com/projects/sample-software-requirements-specificationsrs-report-airline-database)

#### Instance and DB CRD
* 2 Options:
	* **Option 1**: Make the Database a child resource of the Instance
		* **Pros**:
			* Natural model
			* Can use GitOps
			* If DB is deleted via T-SQL, does Controller go in and edit the CRD
		* **Cons**:
			* Deleting Databases becomes a pain via `kubectl delete`, can do via `edit`
			* One Control mechanism, events for DB vs Instance are coupled, Controller becomes bloated
	* **Option 2**: Instance and DB CRDs are seperate, use [`OwnerReference`](https://kubernetes.io/docs/concepts/overview/working-with-objects/owners-dependents/) on the DB
		* **Pros**:
			* Can use GitOps
			* Easy to seperate out controllers
		* **Cons**:
			* Unnatural model
			* If DB is deleted via T-SQL, does Controller go in and delete the CRD it is tracking? Does that trigger more events where it tries to connect to the DB?

> **March 18:** For now, let's leave use Option 2 - i.e. keep the DB CRD seperate since it's non-critical to learning path. Can always come back and merge into **Option 1**

---

<details>
  <summary>Microk8s setup</summary>
  
  	Run these in local **PowerShell in _Admin mode_** to spin up via Multipass:

	Run with Docker Desktop turned off so `microk8s-vm` has no trouble booting up

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

	# Allow priveleged containers
	multipass shell microk8s-vm
	# This shells us in

	sudo bash -c 'echo "--allow-privileged" >> /var/snap/microk8s/current/args/kube-apiserver'

	exit # Exit out from Microk8s vm

	# Start microk8s
	microk8s status --wait-ready

	# Get IP address of node for MetalLB range
	microk8s kubectl get nodes -o wide
	# INTERNAL-IP
	# 172.23.101.27

	# Enable K8s features
	microk8s enable dns storage metallb ingress
	# Enter CIDR for MetalLB: 172.23.101.50-172.23.101.60
	# This must be in the same range as the VM above!

	# Access via kubectl in this container
	$DIR = "C:\Users\mdrrahman\Documents\GitHub\postgres-dotnet-operator\microk8s"
	microk8s config view > $DIR\config # Export kubeconfig
	```

	Now we go into our VSCode Container:

	```bash
	cd /workspaces/postgres-dotnet-operator
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

</details>

---

### Instance CRD creation

```bash
# Create Instance CRD
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresql-crd.yaml

# Create Instance Resource
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresql1.yaml
```

---

### Pre-Controller prep

```bash
# Create Database CRD
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresdb-crd.yaml
# customresourcedefinition.apiextensions.k8s.io/postgresdbs.samples.k8s-dotnet-controller-sdk created

# Create Postgres Pod
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/deployment.yaml
# deployment.apps/postgres-deployment created
# service/postgres-service created
# secret/postgres-credentials created
# configmap/postgres-config created

# Grab LoadBalancer IP
export lb_ip=$(kubectl get svc postgres-service -o json | jq -r .status.loadBalancer.ingress[0].ip)

# Make sure we can connect from this container
export PGPASSWORD='acntorPRESTO!'
pgcli -h $lb_ip -U boor -p 5432 -d postgres
SELECT table_name FROM information_schema.tables LIMIT 5;
# results visible

# Ctrl+D to exit
```

---

### Run Controller locally

```bash
cd /workspaces/postgres-dotnet-operator/src

dotnet build
dotnet run

# Test DB CRD:
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/db1.yaml
# Data Studio: we see MyFirstDB

# Edit CRD DB name
# Data Studio: we see MyFirstDB_rename

# Delete in pgcli
pgcli -h $lb_ip -U boor -p 5432 -d postgres
DROP DATABASE myfirstdb;
exit
# Operator auto-reconciles

# Delete DB CRD
kubectl delete -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/db1.yaml
# Data Studio: DB is gone

```

---

### Deploy Controller as a pod

```bash
# Login to docker with access token
docker login --username=mdrrakiburrahman --password=$DOCKERHUB_TOKEN

cd /workspaces/postgres-dotnet-operator/src

# Build & push
docker build -t mdrrakiburrahman/postgresdb-controller .
docker push mdrrakiburrahman/postgresdb-controller

# Deploy to k8s and tail
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/controller-deployment.yaml

kubectl logs postgresdb-controller-84f849dfc-bf5kp --follow
# And the same tests above.

```
