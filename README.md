Azure Function Demo
===================

[![Build Status](https://dev.azure.com/MortalFlesh/azure-function-demo/_apis/build/status/MortalFlesh.azure-function-demo)](https://dev.azure.com/MortalFlesh/azure-function-demo/_build/latest?definitionId=1)
[![Build Status](https://api.travis-ci.com/MortalFlesh/azure-function-demo.svg?branch=master)](https://travis-ci.com/MortalFlesh/azure-function-demo)

> Demo repository for trying out Azure Functions with F#

Inspired by https://www.aaron-powell.com/posts/2020-01-13-creating-azure-functions-in-fsharp/
More information https://docs.microsoft.com/cs-cz/azure/azure-functions/functions-reference-fsharp

## Azure function

```
dotnet new func --language F# --name MFAzureFuncDemo
dotnet new http --language F# --name HttpTrigger
```

## Locally

### Tools
```
npm install -g azure-functions-core-tools
```

### Run
> Did not work for F#?

```
func start
```

## Deploy
> Fill credentials

```
./release.sh
```
