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

SUBSCRIPTION_ID="dfea7be2-a719-4e18-b045-886013276031"
CLIENT_ID="415fb89b-4074-4532-b2ba-4c32c244ffa2"
TENANT_ID="3d8f4aa4-6211-4c04-ba38-a6198f655cb0"

#=========#
# Command #
#=========#

./fake.sh build --target appservice \
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
