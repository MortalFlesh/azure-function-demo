#load ".fake/build.fsx/intellisense.fsx"

// ========================================================================================================
// === F# / Azure function ======================================================================== 2.0.0 =
// --------------------------------------------------------------------------------------------------------
// Options:
//  - no-clean   - disables clean of dirs in the first step (required on CI)
//  - no-lint    - lint will be executed, but the result is not validated
// --------------------------------------------------------------------------------------------------------
// Table of contents:
//      1. Information about project, configuration
//      2. Utilities, DotnetCore functions
//      3. FAKE targets
//      4. FAKE targets hierarchy
// ========================================================================================================

open System

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Tools.Git
#load @"paket-files/build/CompositionalIT/fshelpers/src/FsHelpers/ArmHelper/ArmHelper.fs"
open Cit.Helpers.Arm
open Cit.Helpers.Arm.Parameters
open Microsoft.Azure.Management.ResourceManager.Fluent.Core

// --------------------------------------------------------------------------------------------------------
// 1. Information about the project to be used at NuGet and in AssemblyInfo files and other FAKE configuration
// --------------------------------------------------------------------------------------------------------

let project = "Azure Function Demo"
let summary = "Demo for azure function"
let releaseDir = Path.getFullName "./deploy"

let changeLog = None
let gitCommit = Information.getCurrentSHA1(".")
let gitBranch = Information.getBranchName(".")

[<RequireQualifiedAccess>]
module ProjectSources =
    let release =
        !! "./*.fsproj"
        ++ "src/**/*.fsproj"

    let tests =
        !! "tests/**/*.fsproj"

    let all =
        release
        ++ "tests/**/*.fsproj"

// --------------------------------------------------------------------------------------------------------
// 2. Utilities, DotnetCore functions, etc.
// --------------------------------------------------------------------------------------------------------

let platformTool tool winTool =
    let tool = if Environment.isUnix then tool else winTool
    match ProcessUtils.tryFindFileOnPath tool with
    | Some t -> t
    | _ ->
        let errorMsg =
            tool + " was not found in path. " +
            "Please install it and make sure it's available from your path. " +
            "See https://safe-stack.github.io/docs/quickstart/#install-pre-requisites for more info"
        failwith errorMsg

let nodeTool = platformTool "node" "node.exe"
let npmTool = platformTool "npm" "npm.cmd"
let npxTool = platformTool "npx" "npx.cmd"

let runTool cmd args workingDir =
    let arguments = args |> String.split ' ' |> Arguments.OfArgs
    Command.RawCommand (cmd, arguments)
    |> CreateProcess.fromCommand
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

let openBrowser url =
    //https://github.com/dotnet/corefx/issues/10361
    Command.ShellCommand url
    |> CreateProcess.fromCommand
    |> CreateProcess.ensureExitCodeWithMessage "opening browser failed"
    |> Proc.run
    |> ignore

[<AutoOpen>]
module private Utils =
    let tee f a =
        f a
        a

    let skipOn option action p =
        if p.Context.Arguments |> Seq.contains option
        then Trace.tracefn "Skipped ..."
        else action p

    let createProcess exe arg dir =
        CreateProcess.fromRawCommandLine exe arg
        |> CreateProcess.withWorkingDirectory dir
        |> CreateProcess.ensureExitCode

    let run proc arg dir =
        proc arg dir
        |> Proc.run
        |> ignore

    let orFail = function
        | Error e -> raise e
        | Ok ok -> ok

    let envVar name =
        if Environment.hasEnvironVar(name)
            then Environment.environVar(name) |> Some
            else None

    let stringToOption = function
        | null | "" -> None
        | string -> Some string

    [<RequireQualifiedAccess>]
    module Option =
        let mapNone f = function
            | Some v -> v
            | None -> f None

        let bindNone f = function
            | Some v -> Some v
            | None -> f None

[<RequireQualifiedAccess>]
module Dotnet =
    let dotnet = createProcess "dotnet"

    let run command dir = try run dotnet command dir |> Ok with e -> Error e
    let runInRoot command = run command "."
    let runOrFail command dir = run command dir |> orFail
    let runInRootOrFail command = run command "." |> orFail

// --------------------------------------------------------------------------------------------------------
// 3. Targets for FAKE
// --------------------------------------------------------------------------------------------------------

Target.create "Clean" (fun _ ->
    !! "./**/bin/Release"
    ++ "./**/bin/Debug"
    ++ "./**/obj"
    ++ "./**/.ionide"
    |> Shell.cleanDirs
)

Target.create "SafeClean" (fun _ ->
    [ releaseDir ]
    |> Shell.cleanDirs
)

Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        let now = DateTime.Now

        let version =
            changeLog
            |> Option.bind (fun changeLog ->
                changeLog
                |> System.IO.File.ReadAllLines
                |> Seq.tryPick (fun l -> if l.StartsWith "##" && not (l.Contains "Unreleased") then Some l else None)
                |> Option.bind (fun l -> match Text.RegularExpressions.Regex.Match(l, @"(\d+\.\d+\.\d+){1}") with m when m.Success -> Some m.Value | _ -> None)
            )
            |> Option.defaultValue "0.0.0"

        let gitValue fallbackEnvironmentVariableNames initialValue =
            initialValue
            |> String.replace "NoBranch" ""
            |> stringToOption
            |> Option.bindNone (fun _ -> fallbackEnvironmentVariableNames |> List.tryPick envVar)
            |> Option.defaultValue "unknown"

        [
            AssemblyInfo.Title projectName
            AssemblyInfo.Product project
            AssemblyInfo.Description summary
            AssemblyInfo.Version version
            AssemblyInfo.FileVersion version
            AssemblyInfo.InternalsVisibleTo "tests"
            AssemblyInfo.Metadata("gitbranch", gitBranch |> gitValue [ "GIT_BRANCH"; "branch" ])
            AssemblyInfo.Metadata("gitcommit", gitCommit |> gitValue [ "GIT_COMMIT"; "commit" ])
            AssemblyInfo.Metadata("buildNumber", "BUILD_NUMBER" |> envVar |> Option.defaultValue "-")
            AssemblyInfo.Metadata("createdAt", now.ToString("yyyy-MM-dd HH:mm:ss"))
        ]

    let getProjectDetails (projectPath: string) =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        (
            projectPath,
            projectName,
            System.IO.Path.GetDirectoryName(projectPath),
            (getAssemblyInfoAttributes projectName)
        )

    ProjectSources.all
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (_, _, folderName, attributes) ->
        AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes
    )
)

Target.create "Build" (fun _ ->
    ProjectSources.all
    |> Seq.iter (DotNet.build id)
)

Target.create "Lint" <| skipOn "no-lint" (fun _ ->
    ProjectSources.all
    ++ "./Build.fsproj"
    |> Seq.iter (fun fsproj ->
        match Dotnet.runInRoot (sprintf "fsharplint lint %s" fsproj) with
        | Ok () -> Trace.tracefn "Lint %s is Ok" fsproj
        | Error e -> raise e
    )
)

Target.create "Tests" (fun _ ->
    if ProjectSources.tests |> Seq.isEmpty
    then Trace.tracefn "There are no tests yet."
    else Dotnet.runOrFail "run" "tests"
)

Target.create "Release" (fun _ ->
    releaseDir
    |> sprintf "publish -c Release -o %s"
    |> Dotnet.runInRootOrFail
)

Target.create "Watch" (fun _ ->
    Dotnet.runInRootOrFail "watch run"
)

Target.create "Run" (fun _ ->
    Dotnet.runInRootOrFail "run"
)

type ArmOutput = {
    WebAppName : ParameterValue<string>
    WebAppPassword : ParameterValue<string>
}
let mutable deploymentOutputs : ArmOutput option = None

Target.create "ArmTemplate" (fun _ ->
    let environment = Environment.environVarOrDefault "environment" (Guid.NewGuid().ToString().ToLower().Split '-' |> Array.head)
    let armTemplate = @"arm-template.json"
    let resourceGroupName = "fun-" + environment

    let authCtx =
        // You can safely replace these with your own subscription and client IDs hard-coded into this script.
        let subscriptionId = try Environment.environVar "subscriptionId" |> Guid.Parse with _ -> failwith "Invalid Subscription ID. This should be your Azure Subscription ID."
        let clientId = try Environment.environVar "clientId" |> Guid.Parse with _ -> failwith "Invalid Client ID. This should be the Client ID of an application registered in Azure with permission to create resources in your subscription."
        let tenantId =
            try Environment.environVarOrNone "tenantId" |> Option.map Guid.Parse
            with _ -> failwith "Invalid TenantId ID. This should be the Tenant ID of an application registered in Azure with permission to create resources in your subscription."

        Trace.tracefn "Deploying template '%s' to resource group '%s' in subscription '%O'..." armTemplate resourceGroupName subscriptionId
        subscriptionId
        |> authenticateDevice Trace.trace { ClientId = clientId; TenantId = tenantId }
        |> Async.RunSynchronously

    let deployment =
        let location = Environment.environVarOrDefault "location" Region.EuropeWest.Name
        {
            DeploymentName = "AZfun-template-deploy"
            ResourceGroup = New(resourceGroupName, Region.Create location)
            ArmTemplate = IO.File.ReadAllText armTemplate
            Parameters =
                Simple [
                    "environment", ArmString environment
                    "location", ArmString location
                ]
            DeploymentMode = Incremental
        }

    deployment
    |> deployWithProgress authCtx
    |> Seq.iter(function
        | DeploymentInProgress (state, operations) -> Trace.tracefn "State is %s, completed %d operations." state operations
        | DeploymentError (statusCode, message) -> Trace.traceError <| sprintf "DEPLOYMENT ERROR: %s - '%s'" statusCode message
        | DeploymentCompleted d -> deploymentOutputs <- d)
)

open System.Net

// https://github.com/SAFE-Stack/SAFE-template/issues/120
// https://stackoverflow.com/a/6994391/3232646
type TimeoutWebClient() =
    inherit WebClient()
    override this.GetWebRequest uri =
        let request = base.GetWebRequest uri
        request.Timeout <- 30 * 60 * 1000
        request

Target.create "AzureFunction" (fun _ ->
    let zipFile = "deploy.zip"
    IO.File.Delete zipFile
    Zip.zip releaseDir zipFile !!(releaseDir + @"\**\**")

    let appName = deploymentOutputs.Value.WebAppName.value
    let appPassword = deploymentOutputs.Value.WebAppPassword.value

    let destinationUri = sprintf "https://%s.scm.azurewebsites.net/api/zipdeploy" appName
    let client = new TimeoutWebClient(Credentials = NetworkCredential("$" + appName, appPassword))
    Trace.tracefn "Uploading %s to %s" zipFile destinationUri
    client.UploadData(destinationUri, IO.File.ReadAllBytes zipFile) |> ignore
)

// --------------------------------------------------------------------------------------------------------
// 4. FAKE targets hierarchy
// --------------------------------------------------------------------------------------------------------

open Fake.Core.TargetOperators

"SafeClean"
    ==> "AssemblyInfo"
    ==> "Build"
    ==> "Lint"
    ==> "Tests"
    ==> "Release"
    ==> "ArmTemplate"
    ==> "AzureFunction"

"Build"
    ==> "Run" <=> "Watch"

Target.runOrDefaultWithArguments "Build"
