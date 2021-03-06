# postgres-dotnet-operator

My homegrown Kubernetes Operator for Postgres in dotnet.

## Progress

- [x] ~~[**Learn dotnet (Feb 20 - Mar 6)**](https://github.com/mdrakiburrahman/exercism_dotnet)~~
- [x] ~~[**Dotnet Operator SDK - wrapper around C# Client**](https://github.com/mdrakiburrahman/postgres-dotnet-operator/tree/main/src/OperatorSDK) (Mar 6 - Mar 15)~~
- [ ] **Features**
  - [x] ~~**Project scaffold**~~
    - [x] ~~Operator-SDK + SQL all in one repo, remove Nuget for now (Mar 16 -)~~
    - [x] ~~Test in K8s with MSSQL Pod~~
  - [ ] **Features**
    - [x] ~~Postgres Database CRD with an existing Postgres Container image and [Npgsql](https://www.nuget.org/packages/Npgsql/)~~
    - [x] ~~Extend to include a Postgres instance CRD as `deployment`~~
      - [x] Make with vanilla Postgres 14 image
        - [x] ~~`CREATE`~~
        - [x] ~~`DELETE`~~
      - [x] ~~Expose `ClusterIP` and `LoadBalancer`/`NodePort`~~
      - [x] ~~Update to latest C# Client to stay up to speed with examples~~
      - [x] ~~Update to .NET 6.0~~
      - [x] ~~Represent Instance CRD as C# object so we can query it~~
      - [x] ~~For Database Operator, remove dependency from `ConfigMap`, read straight from CRD and `LoadBalancer`/`ClusterIp` svc - depending on where Controller is running~~
        - [x] ~~Test Controller locally and internal to cluster~~
      - [x] ~~Test multiple instance and database deployments to ensure no conflicts~~
    - [x] ~~Make your own Postgres pod image in a Dockerfile from `src` for better control of what's inside~~
      - [x] ~~Create a modular pattern for injecting `*.sql` and `*.sh` startup scripts~~
    - [ ] ⭐ SSO via LDAP or GSSAPI (aka Kerberos)
      - [ ] Use CoreDNS to have PG Pod use DNS resolution from AD, similar to Arc
      - [ ] Use `ActiveDirectoryConnector` spec similar to Arc
      - [ ] Make
    - [ ] Two pods in HA spec
      - [ ] ⭐ Inject `pg_auto_failover` per container
    - [ ] ⭐ Custom SSL
    - [ ] CRD Status with a "health"
      - [ ] Database level changes
      - [ ] Add `ownerReference` with Instance CRD - [ ] **2 way sync state DB <> CRD**
        > I'm not sure how this would work, only `status` is supposed to be updated ...if the Controller tries to change it's own CRD's `spec` does it go into a recursion since that generates a modified event?
    - [ ] Backup/Restore (same logic as MI with the JSON files)
    - [ ] Deploy with `StatefulSet` and `PVC` instead of `deployment`
    - [ ] Extend to Citus
    - [ ] Vault CSI
  - [ ] **Best practices**
    - [ ] Unit tests with `XUnit`
    - [ ] Allow Controller restart to pick up new events only/ignore existing resources in healthy state
      - [ ] CRD Spec validation (e.g. supported Postgres Versions)
    - [ ] CRD `UPDATE` in place (e.g. Postgres extensions)
    - [ ] Queue up events if Controller is down
  - [ ] **Extras**
    - [ ] Docs/Deck, Diagrams, Demo

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
- Kerberos:
  - https://www.enterprisedb.com/blog/how-set-kerberos-authentication-using-active-directory-postgresql-database
  - https://dev.to/robbmanes/running-coredns-as-a-dns-server-in-a-container-1d0
---

### [SRS notes](https://krazytech.com/projects/sample-software-requirements-specificationsrs-report-airline-database)

#### Instance and DB CRD

- 2 Options:
  - **Option 1**: Make the Database a child resource of the Instance
    - **Pros**:
      - Natural model
      - Can use GitOps
      - If DB is deleted via T-SQL, does Controller go in and edit the CRD
    - **Cons**:
      - Deleting Databases becomes a pain via `kubectl delete`, can do via `edit`
      - One Control mechanism, events for DB vs Instance are coupled, Controller becomes bloated
  - **Option 2**: Instance and DB CRDs are seperate, use [`OwnerReference`](https://kubernetes.io/docs/concepts/overview/working-with-objects/owners-dependents/) on the DB
    - **Pros**:
      - Can use GitOps
      - Easy to seperate out controllers
    - **Cons**:
      - Unnatural model
      - If DB is deleted via T-SQL, does Controller go in and delete the CRD it is tracking? Does that trigger more events where it tries to connect to the DB?

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
    # 172.31.244.248

    # Enable K8s features
    microk8s enable dns storage metallb ingress
    # Enter CIDR for MetalLB: 172.31.244.210-172.31.244.220
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

<details>
  <summary>AKS setup with Domain Controller</summary>

  ```bash
  # ---------------------
  # ENVIRONMENT VARIABLES
  # For Terraform
  # ---------------------
  # Secrets
  export TF_VAR_SPN_CLIENT_ID=$spnClientId
  export TF_VAR_SPN_CLIENT_SECRET=$spnClientSecret
  export TF_VAR_SPN_TENANT_ID=$spnTenantId
  export TF_VAR_SPN_SUBSCRIPTION_ID=$subscriptionId
  export TF_VAR_VM_USER_PASSWORD=$localPassword # RDP password for VMs

  # Module specific
  export TF_VAR_resource_group_name='raki-pg-operator-rg'

  # ---------------------
  # DEPLOY TERRAFORM
  # ---------------------
  cd terraform
  terraform init
  terraform plan
  terraform apply -auto-approve

  # ---------------------
  # ‼ DESTROY ENVIRONMENT
  # ---------------------
  terraform destory

  # ---------------------
  # ‼ AKS Kubeconfig
  # ---------------------
  export aksName='aks-cni'
  az aks get-credentials --resource-group $TF_VAR_resource_group_name --name $aksName

  ```

  **Create Domain Controller `FG-DC-1-vm`:**
  ```powershell
  # Configure the Domain Controller
  $domainName = 'fg.contoso.com'
  $domainAdminPassword = "acntorPRESTO!"
  $secureDomainAdminPassword = $domainAdminPassword | ConvertTo-SecureString -AsPlainText -Force

  Install-WindowsFeature -Name AD-Domain-Services -IncludeManagementTools

  # Create Active Directory Forest
  Install-ADDSForest `
      -DomainName "$domainName" `
      -CreateDnsDelegation:$false `
      -DatabasePath "C:\Windows\NTDS" `
      -DomainMode "7" `
      -DomainNetbiosName $domainName.Split('.')[0].ToUpper() ` # FG
      -ForestMode "7" `
      -InstallDns:$true `
      -LogPath "C:\Windows\NTDS" `
      -NoRebootOnCompletion:$false `
      -SysvolPath "C:\Windows\SYSVOL" `
      -Force:$true `
      -SafeModeAdministratorPassword $secureDomainAdminPassword
  ```

  **Join to domain `FG-CLIENT-vm`:**
  ```powershell
  # Join to FG Domain
  $user = "FG\boor"
  $domainAdminPassword = "acntorPRESTO!"
  $domainName = 'fg.contoso.com'
  $pass = $domainAdminPassword | ConvertTo-SecureString -AsPlainText -Force
  $Credential = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $user, $pass
  add-computer –domainname $domainName -Credential $Credential -restart –force

  # Reboot, and login with Domain Admin

  # Install chocolatey
  iex ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))

  # Install apps
  $chocolateyAppList = 'vscode,grep,microsoft-edge,dbeaver'

  $appsToInstall = $chocolateyAppList -split "," | foreach { "$($_.Trim())" }

  foreach ($app in $appsToInstall)
  {
      Write-Host "Installing $app"
      & choco install $app /y -Force| Write-Output
  }

  # Turn of firewall on both VMs
  Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False

  ```
  After the reboot, we can login via Bastion as our Domain Admin boor@fg.contoso.com.

  </details>

---

### Create Custom Postgres Image from Dockerfile

```bash
cd /workspaces/postgres-dotnet-operator/postgres/14

# Convert all to unix
for f in *; do
	dos2unix $f
done

# Login to docker with access token
docker login --username=mdrrakiburrahman --password=$DOCKERHUB_TOKEN

# Build & push
docker build --no-cache -t mdrrakiburrahman/postgres-14 .
docker push mdrrakiburrahman/postgres-14
```

To add any Custom init scripts - we follow the pattern [here](https://github.com/docker-library/docs/blob/master/postgres/README.md#initialization-scripts).

Currently any `*.sql` or `*.sh` scripts under `postgres/14/init` will get copied into the container and run at Container bootup:

```text
...
...
/usr/local/bin/docker-entrypoint.sh: sourcing /docker-entrypoint-initdb.d/get-db.sh
--> All existing Databases:
  datname
-----------
 postgres
 template1
 template0
(3 rows)


/usr/local/bin/docker-entrypoint.sh: sourcing /docker-entrypoint-initdb.d/test-hello.sh
Hello from Raki!
...
...
```

---

### Define CRDs

```bash
# Instance CRD
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresql-crd.yaml

# Database CRD
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresdb-crd.yaml

```

---

### Run Controller locally

```bash
cd /workspaces/postgres-dotnet-operator/src

dotnet build
dotnet run
```

---

### Test Instance and DB

```bash
# Apply Instance CRD:
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresql.yaml

# Apply DB CRD:
kubectl apply -f /workspaces/postgres-dotnet-operator/kubernetes/yaml/postgresdb.yaml
# We have a Database now

# Connect to Database
export lb_ip=$(kubectl get svc pg1-external-svc -o json | jq -r .status.loadBalancer.ingress[0].ip)
export PGPASSWORD='acntorPRESTO!'

# Connect to Postgres
pgcli -h $lb_ip -U boor -p 5432 -d postgres

# Check Database
SELECT datname FROM pg_database;

# Delete Database
DROP DATABASE myfirstdb WITH (FORCE);
exit
# Gets recreated in 5 seconds

```

---

### Run Controller in Kubernetes

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

### Kerberos setup: GSSAPI, CoreDNS
	
#### CoreDNS setup in K8s
