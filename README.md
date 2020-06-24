Azure Function Demo
===================

> Demo repository for trying out Azure Functions with F#

Inspired by https://www.aaron-powell.com/posts/2020-01-13-creating-azure-functions-in-fsharp/

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
