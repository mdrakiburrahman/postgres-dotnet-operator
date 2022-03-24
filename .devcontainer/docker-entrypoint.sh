#!/bin/bash

echo "Configuring wakatime..."
printf "\n[settings]\napi_key = $WAKA_TIME_API_KEY\n" > ~/.wakatime.cfg;

echo "Logging into Azure..."

az login --service-principal --username $spnClientId --password $spnClientSecret --tenant $spnTenantId
az account set --subscription $subscriptionId

exec "$@"