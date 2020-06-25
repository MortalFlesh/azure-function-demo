#!/bin/bash

# see https://safe-stack.github.io/docs/template-appservice/
# todo
#   - move this logic to build.fsx
#   - depends on connectionStrings, ...

#==========#
# Defaults #
#==========#

LOCATION="westeurope"

#=======#
# Azure #
#=======#

echo "Fill values first, then remove this line and the exit!"
exit

SUBSCRIPTION_ID="<fill>"
CLIENT_ID="<fill>"
TENANT_ID="<fill>"

#=========#
# Command #
#=========#

./fake.sh build --target AzureFunction \
    -e subscriptionId="$SUBSCRIPTION_ID" \
    -e clientId="$CLIENT_ID" \
    -e tenantId="$TENANT_ID" \
    -e location="$LOCATION"       \
    -e environment="prod" \
    ;

#==========#
# Optional #
#==========#

# environment is an optional environment name that will be appended to all Azure resources created, which allows you to create entire dev / test environments quickly and easily. This defaults to a random GUID.
# -------------------------------
# -e environment="$ENVIRONMENT" \

# pricingTier is the pricing tier of the app service that hosts your SAFE app. This defaults to F1 (free); the full list can be viewed https://azure.microsoft.com/en-us/pricing/details/app-service/windows/.
# -------------------------------
