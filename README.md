# postgres-dotnet-operator

My homegrown Kubernetes Operator for Postgres in dotnet

## My Postgres Database operator
- [x]  ~~[**Learn dotnet (Feb 20 - Mar 6)**](https://github.com/mdrakiburrahman/exercism_dotnet)~~
- [x]  ~~[**Dotnet Operator SDK - wrapper around C# Client**](https://github.com/mdrakiburrahman/postgres-dotnet-operator/tree/main/src/OperatorSDK) (Mar 6 - Mar 15)~~
- [ ]  **Build your own Postgres operator using the Framework**
    - [x] ~~ **Project scaffold** ~~
        - [x] ~~ Operator-SDK + SQL all in one repo, remove Nuget for now (Mar 16 -) ~~
        - [x] ~~ Test in K8s with MSSQL Pod ~~
    - [ ]  **Features**
        - [ ]  Postgres Database CRD with an existing Postgres Container image and [Npgsql](https://www.nuget.org/packages/Npgsql/)
        - [ ]  Extend to include a Postgres instance CRD too
            - [ ]  Make your own Postgres compiler from Dockerfile from `src` for better control of what's inside
        - [ ]  CRD Status
        - [ ]  Database level changes
            - [ ]  **2 way sync state**
        - [ ]  ⭐ LDAP
        - [ ]  ⭐ Custom SSL
        - [ ]  Two pods in HA spec
        - [ ]  ⭐ Inject `pg_auto_failover`
        - [ ]  Backup/Restore (same logic as MI with the JSON files)
        - [ ]  Extend to Citus
        - [ ]  Store stuff in Vault
    - [ ]  **Best practices**
        - [ ]  CRD Spec validation

---

### Relevant docs

- Kubernetes API reference: https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.23/#objectmeta-v1-meta
- `lock` for C#: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
- `Task`-based Async pattern (TAP): https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
- `Task` for C#: https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task?view=net-6.0
- `Bookmark` for K8s: https://kubernetes.io/docs/reference/using-api/api-concepts/#watch-bookmarks

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
	# 172.23.46.102

	# Enable K8s features
	microk8s enable dns storage metallb ingress
	# Enter CIDR for MetalLB: 172.23.46.120-172.23.46.130
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

### Pre-Controller prep

```bash
# Create CRD
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresdb-crd.yaml
# customresourcedefinition.apiextensions.k8s.io/postgresdbs.samples.k8s-dotnet-controller-sdk created

# Create SQL Pod
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/deployment.yaml
# deployment.apps/postgres-deployment created
# service/postgres-service created
# secret/postgres-credentials created
# configmap/postgres-config created

# Make sure we can connect from this container
sqlcmd -S 172.23.46.120,1433 -U sa -P acntorPRESTO! -Q "SELECT name FROM sys.databases"
# results
```

---

### Run Controller locally

```bash
cd /workspaces/postgres-dotnet-operator/src

dotnet build
dotnet run

# Test DB CRD:
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/db1.yaml
# SSMS: we see MyFirstDB

# Edit CRD DB name
# SSMS: we see MyFirstDB_rename

# Delete in SQL
sqlcmd -S 172.23.46.120,1433 -U sa -P acntorPRESTO! -Q " DROP DATABASE MyFirstDB"

# Delete DB CRD
kubectl delete -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/db1.yaml
# SSMS: DB is gone

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

kubectl logs PostgresDB-controller-6b7c85fb4-shd2j --follow
# And the same tests above.

```
