# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/main/containers/dotnet

# [Choice] .NET Core version: 3.1 / 3.1-focal, 5.0 / 5.0-focal, 6.0 /6.0-bullseye, 6.0-focal, 5.0-bullseye, 3.1-bullseye
ARG VARIANT="6.0-focal"
FROM mcr.microsoft.com/vscode/devcontainers/dotnet:${VARIANT}

# https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli
# https://stackoverflow.com/a/59814147/8954538
ENV PATH $PATH:/root/.dotnet/tools
RUN dotnet tool install -g dotnet-ef

# [Choice] Node.js version: none, lts/*, 16, 14, 12, 10
ARG NODE_VERSION="none"
RUN if [ "${NODE_VERSION}" != "none" ]; then su vscode -c "umask 0002 && . /usr/local/share/nvm/nvm.sh && nvm install ${NODE_VERSION} 2>&1"; fi

# Docker Compose version
ARG COMPOSE_VERSION=1.24.0

# Helm Version
ARG HELM_VERSION=3.6.3

# Terraform Version: https://github.com/hashicorp/terraform/releases
ARG TF_Version="1.1.5"

# Avoid warnings by switching to noninteractive
ENV DEBIAN_FRONTEND=noninteractive

# Configure apt and install packages
RUN apt-get update \
    && apt-get -y install --no-install-recommends apt-utils dialog 2>&1 \
    && apt-get -y install git iproute2 procps bash-completion

# Install the Azure CLI && aks-preview extension
RUN apt-get install -y apt-transport-https gnupg2 lsb-release \
    && echo "deb [arch=amd64] https://packages.microsoft.com/repos/azure-cli/ $(lsb_release -cs) main" > /etc/apt/sources.list.d/azure-cli.list \
    && curl -sL https://packages.microsoft.com/keys/microsoft.asc | apt-key add - 2>/dev/null \
    && apt-get update \
    && apt-get install -y azure-cli \
    && az extension add -n aks-preview

# Add Azure CLI extensions
RUN az config set extension.use_dynamic_install=yes_without_prompt

# Install Docker CE CLI
RUN apt-get install -y apt-transport-https ca-certificates curl gnupg-agent software-properties-common lsb-release \
    && curl -fsSL https://download.docker.com/linux/$(lsb_release -is | tr '[:upper:]' '[:lower:]')/gpg | (OUT=$(apt-key add - 2>&1) || echo $OUT) \
    && add-apt-repository "deb [arch=amd64] https://download.docker.com/linux/$(lsb_release -is | tr '[:upper:]' '[:lower:]') $(lsb_release -cs) stable" \
    && apt-get update \
    && apt-get install -y docker-ce-cli

# Install Docker Compose
RUN curl -sSL "https://github.com/docker/compose/releases/download/${COMPOSE_VERSION}/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose \
    && chmod +x /usr/local/bin/docker-compose

# Install Kubectl
RUN echo "deb https://apt.kubernetes.io/ kubernetes-xenial main" > /etc/apt/sources.list.d/kubernetes.list \
    && curl -sL https://packages.cloud.google.com/apt/doc/apt-key.gpg | apt-key add - 2>/dev/null \
    && apt-get update \
    && apt-get install -y kubectl

# Install kubectx
ENV VERSION "0.9.4"
RUN echo "Installing kubectx" \
    && curl -L -o /usr/local/bin/kubectx https://raw.githubusercontent.com/ahmetb/kubectx/v${VERSION}/kubectx \
	&& curl -L -o /usr/local/bin/kubens https://raw.githubusercontent.com/ahmetb/kubectx/v${VERSION}/kubens \
    && chmod +x /usr/local/bin/kubectx \
    && chmod +x /usr/local/bin/kubens

# Install Helm
RUN mkdir -p /tmp/downloads/helm \
    && curl -sL -o /tmp/downloads/helm.tar.gz https://get.helm.sh/helm-v${HELM_VERSION}-linux-amd64.tar.gz \
    && tar -C /tmp/downloads/helm -zxvf /tmp/downloads/helm.tar.gz \
    && mv /tmp/downloads/helm/linux-amd64/helm /usr/local/bin

# Install Terraform
RUN apt-get install -y \
    wget \
    unzip \
  && rm -rf /var/lib/apt/lists/*
RUN wget --quiet https://releases.hashicorp.com/terraform/${TF_Version}/terraform_${TF_Version}_linux_amd64.zip \
  && unzip terraform_${TF_Version}_linux_amd64.zip \
  && mv terraform /usr/bin \
  && rm terraform_${TF_Version}_linux_amd64.zip

# Install Python
USER root
RUN apt-get update -y \
    && apt-get install -y python3.9 \
    && apt-get install -y python3.9-venv \
    && apt-get install -y python3-pip \
    && apt-get install  -y libpq-dev python-dev \
    && pip install pgcli

# Install Linux stuff
RUN apt-get update \
    && apt-get install tree -y \
    && apt-get install make -y \
    && apt-get install kafkacat -y \
    && apt-get install dos2unix -y

# Clean up
RUN apt-get autoremove -y \
    && apt-get clean -y \
    && rm -rf /var/lib/apt/lists/* \
    && rm -rf /tmp/downloads

# Switch back to dialog for any ad-hoc use of apt-get
ENV DEBIAN_FRONTEND=dialog

# Startup
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD [ "sleep", "infinity" ]