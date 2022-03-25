# ---------------------------------------------------------------------------------------------------------------------
# AZURE RESOURCE GROUP
# ---------------------------------------------------------------------------------------------------------------------
resource "azurerm_resource_group" "sql_migration" {
  name     = var.resource_group_name
  location = var.resource_group_location
  tags     = var.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# AZURE VIRTUAL NETWORK
# ---------------------------------------------------------------------------------------------------------------------
module "vnet" {
  depends_on = [azurerm_resource_group.sql_migration]

  source              = "./modules/vnet-module" # My local fork from https://github.com/Azure/terraform-azurerm-vnet
  vnet_name           = var.vnet_name
  resource_group_name = azurerm_resource_group.sql_migration.name
  address_space       = ["192.168.0.0/16"]
  subnet_prefixes     = ["192.168.0.0/24", "192.168.48.0/21", "192.168.144.64/27"]
  subnet_names        = ["FG-DC", "AKS", "AzureBastionSubnet"]
  dns_servers         = ["192.168.0.4", "168.63.129.16"] # FG DC, Azure DNS is required otherwise AKS fails: https://github.com/Azure/AKS/issues/2004

  tags = var.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# BASTION FOR REMOTE DESKTOP
# ---------------------------------------------------------------------------------------------------------------------
resource "azurerm_public_ip" "bastion_pip" {
  depends_on          = [azurerm_resource_group.sql_migration]
  name                = "bastion-pip"
  location            = var.resource_group_location
  resource_group_name = var.resource_group_name
  allocation_method   = "Static"
  sku                 = "Standard"

  tags = var.tags
}

resource "azurerm_bastion_host" "bastion" {
  depends_on          = [module.vnet]
  name                = "bastion"
  location            = var.resource_group_location
  resource_group_name = var.resource_group_name

  ip_configuration {
    name                 = "configuration"
    subnet_id            = lookup(module.vnet.vnet_subnets_name_id, "AzureBastionSubnet")
    public_ip_address_id = azurerm_public_ip.bastion_pip.id
  }

  tags = var.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# DOMAIN CONTROLLER
# ---------------------------------------------------------------------------------------------------------------------
# FG-DC-1
module "fg_dc_1" {
  depends_on = [module.vnet]

  source                  = "./modules/vm-module" # Local path to VM module
  prefix                  = "FG-DC-1"
  resource_group_location = var.resource_group_location
  resource_group_name     = var.resource_group_name
  subnet_id               = lookup(module.vnet.vnet_subnets_name_id, "FG-DC")
  private_ip              = "192.168.0.4"
  user_password           = var.VM_USER_PASSWORD

  tags = var.tags
}

# FG-CLIENT
module "fg_client" {
  depends_on = [module.vnet]

  source                  = "./modules/vm-module" # Local path to VM module
  prefix                  = "FG-CLIENT"
  resource_group_location = var.resource_group_location
  resource_group_name     = var.resource_group_name
  subnet_id               = lookup(module.vnet.vnet_subnets_name_id, "FG-DC")
  private_ip              = "192.168.0.5"
  user_password           = var.VM_USER_PASSWORD

  tags = var.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# AKS - WITH CNI
# ---------------------------------------------------------------------------------------------------------------------
resource "azurerm_kubernetes_cluster" "aks" {
  depends_on = [module.vnet]

  name                = "aks-cni"
  location            = var.resource_group_location
  resource_group_name = var.resource_group_name
  dns_prefix          = "akscni"

  default_node_pool {
    name                = "agentpool"
    node_count          = 1
    vm_size             = "Standard_DS3_v2"
    type                = "VirtualMachineScaleSets"
    enable_auto_scaling = false
    min_count           = null
    max_count           = null

    # Required for advanced networking
    vnet_subnet_id = lookup(module.vnet.vnet_subnets_name_id, "AKS")
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin     = "azure"
    load_balancer_sku  = "standard"
    dns_service_ip     = "192.168.64.10"
    docker_bridge_cidr = "172.17.0.1/16"
    service_cidr       = "192.168.64.0/19"
    network_policy     = "azure"
  }

  lifecycle {
    ignore_changes = [
      # Ignore changes to nodes because we have autoscale enabled
      default_node_pool[0].node_count
    ]
  }

  tags = var.tags
}
