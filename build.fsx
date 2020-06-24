#r "paket: groupref build //"
#load "./.fake/build.fsx/intellisense.fsx"

#if !FAKE
#r "netstandard"
#r "Facades/netstandard" // https://github.com/ionide/ionide-vscode-fsharp/issues/839#issuecomment-396296095
#endif

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

type ToolDir =
    /// Global tool dir must be in PATH - ${PATH}:/root/.dotnet/tools
    | Global
    /// Just a dir name, the location will be used as: ./{LocalDirName}
    | Local of string

// ========================================================================================================
// === F# / Azure function ======================================================================== 1.0.0 =
// --------------------------------------------------------------------------------------------------------
// Options:
//  - no-lint    - lint will be executed, but the result is not validated
// --------------------------------------------------------------------------------------------------------
// Table of contents:
//      1. Information about project, configuration
//      2. Utilities, DotnetCore functions
//      3. FAKE targets
//      4. FAKE targets hierarchy
// ========================================================================================================

// --------------------------------------------------------------------------------------------------------
// 1. Information about the project to be used at NuGet and in AssemblyInfo files and other FAKE configuration
// --------------------------------------------------------------------------------------------------------

let project = "Azure Function Demo"
let summary = "Demo for azure function"

let version =
    "RELEASE_NOTES.md"
    |> System.IO.File.ReadAllLines
    |> Seq.tryPick (fun l -> if l.StartsWith "##" && not (l.Contains "Unreleased") then Some l else None)
    |> Option.bind (fun l -> match Text.RegularExpressions.Regex.Match(l, @"(\d+\.\d+\.\d+){1}") with m when m.Success -> Some m.Value | _ -> None)
    |> Option.defaultValue "0.0.0"
let gitCommit = Information.getCurrentSHA1(".")
let gitBranch = Information.getBranchName(".")

let toolsDir = Global

Target.initEnvironment ()

let funcPath = Path.getFullName "."
let deployDir = Path.getFullName "./deploy"

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

module private DotnetCore =
    let run cmd workingDir =
        let options =
            DotNet.Options.withWorkingDirectory workingDir
            >> DotNet.Options.withRedirectOutput true

        DotNet.exec options cmd ""

    let runOrFail cmd workingDir =
        run cmd workingDir
        |> tee (fun result ->
            if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir
        )
        |> ignore

    let runInRoot cmd = run cmd "."
    let runInRootOrFail cmd = runOrFail cmd "."

    let installOrUpdateTool toolDir tool =
        let toolCommand action =
            match toolDir with
            | Global -> sprintf "tool %s --global %s" action tool
            | Local dir -> sprintf "tool %s --tool-path ./%s %s" action dir tool

        match runInRoot (toolCommand "install") with
        | { ExitCode = code } when code <> 0 ->
            match runInRoot (toolCommand "update") with
            | { ExitCode = code } when code <> 0 -> Trace.tracefn "Warning: Install and update of %A has failed." tool
            | _ -> ()
        | _ -> ()

    let execute command args (dir: string) =
        let cmd =
            sprintf "%s/%s"
                (dir.TrimEnd('/'))
                command

        let processInfo = System.Diagnostics.ProcessStartInfo(cmd)
        processInfo.RedirectStandardOutput <- true
        processInfo.RedirectStandardError <- true
        processInfo.UseShellExecute <- false
        processInfo.CreateNoWindow <- true
        processInfo.Arguments <- args |> String.concat " "

        use proc =
            new System.Diagnostics.Process(
                StartInfo = processInfo
            )
        if proc.Start() |> not then failwith "Process was not started."
        proc.WaitForExit()

        if proc.ExitCode <> 0 then failwithf "Command '%s' failed in %s." command dir
        (proc.StandardOutput.ReadToEnd(), proc.StandardError.ReadToEnd())

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
    [ deployDir ]
    |> Shell.cleanDirs
)

Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        let now = DateTime.Now

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
            AssemblyInfo.Metadata("SafeTemplateVersion", "1.24.0")
        ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        (
            projectPath,
            projectName,
            System.IO.Path.GetDirectoryName(projectPath),
            (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.fsproj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (_, _, folderName, attributes) ->
        AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes
    )
)

Target.create "Build" (fun _ ->
    funcPath |> DotnetCore.runOrFail "build"
)

Target.create "Lint" <| skipOn "no-lint" (fun _ ->
    DotnetCore.installOrUpdateTool toolsDir "dotnet-fsharplint"

    let checkResult (messages: string list) =
        let rec check: string list -> unit = function
            | [] -> failwithf "Lint does not yield a summary."
            | head :: rest ->
                if head.Contains "Summary" then
                    match head.Replace("= ", "").Replace(" =", "").Replace("=", "").Replace("Summary: ", "") with
                    | "0 warnings" -> Trace.tracefn "Lint: OK"
                    | warnings -> failwithf "Lint ends up with %s." warnings
                else check rest
        messages
        |> List.rev
        |> check

    !! "src/**/*.fsproj"
    |> Seq.map (fun fsproj ->
        match toolsDir with
        | Global ->
            DotnetCore.runInRoot (sprintf "fsharplint lint %s" fsproj)
            |> fun (result: ProcessResult) -> result.Messages
        | Local dir ->
            DotnetCore.execute "dotnet-fsharplint" ["lint"; fsproj] dir
            |> fst
            |> tee (Trace.tracefn "%s")
            |> String.split '\n'
            |> Seq.toList
    )
    |> Seq.iter checkResult
)

Target.create "Tests" (fun _ ->
    if !! "tests/*.fsproj" |> Seq.isEmpty
    then Trace.tracefn "There are no tests yet."
    else DotnetCore.runOrFail "run" "tests"
)

Target.create "Run" (fun _ ->
    funcPath |> DotnetCore.runOrFail "watch run"
)

Target.create "Bundle" (fun _ ->
    funcPath |> DotnetCore.runOrFail (sprintf "publish -c Release -o \"%s\"" deployDir)
)

type ArmOutput =
    { WebAppName : ParameterValue<string>
      WebAppPassword : ParameterValue<string> }
let mutable deploymentOutputs : ArmOutput option = None

Target.create "ArmTemplate" (fun _ ->
    let environment = Environment.environVarOrDefault "environment" (Guid.NewGuid().ToString().ToLower().Split '-' |> Array.head)
    let armTemplate = @"arm-template.json"
    let resourceGroupName = "safe-" + environment   // todo change

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
        { DeploymentName = "SAFE-template-deploy"
          ResourceGroup = New(resourceGroupName, Region.Create location)
          ArmTemplate = IO.File.ReadAllText armTemplate
          Parameters =
              Simple
                  [ "environment", ArmString environment
                    "location", ArmString location ]
          DeploymentMode = Incremental }

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
(* 
Target.create "AppService" (fun _ ->
    let zipFile = "deploy.zip"
    IO.File.Delete zipFile
    Zip.zip deployDir zipFile !!(deployDir + @"\**\**")

    let appName = deploymentOutputs.Value.WebAppName.value
    let appPassword = deploymentOutputs.Value.WebAppPassword.value

    let destinationUri = sprintf "https://%s.scm.azurewebsites.net/api/zipdeploy" appName
    let client = new TimeoutWebClient(Credentials = NetworkCredential("$" + appName, appPassword))
    Trace.tracefn "Uploading %s to %s" zipFile destinationUri
    client.UploadData(destinationUri, IO.File.ReadAllBytes zipFile) |> ignore)
 *)
Target.create "AzureFunction" (fun _ ->
    let zipFile = "deploy.zip"
    IO.File.Delete zipFile
    Zip.zip deployDir zipFile !!(deployDir + @"\**\**")

    let appName = deploymentOutputs.Value.WebAppName.value
    let appPassword = deploymentOutputs.Value.WebAppPassword.value

    let destinationUri = sprintf "https://%s.scm.azurewebsites.net/api/zipdeploy" appName
    let client = new TimeoutWebClient(Credentials = NetworkCredential("$" + appName, appPassword))
    Trace.tracefn "Uploading %s to %s" zipFile destinationUri
    client.UploadData(destinationUri, IO.File.ReadAllBytes zipFile) |> ignore)

// --------------------------------------------------------------------------------------------------------
// 4. FAKE targets hierarchy
// --------------------------------------------------------------------------------------------------------

open Fake.Core.TargetOperators

"SafeClean"
    ==> "AssemblyInfo"
    ==> "InstallClient"
    ==> "Build"
    ==> "Lint"
    ==> "Tests"
    ==> "Bundle"
    ==> "ArmTemplate"
    ==> "AzureFunction"

"SafeClean"
    ==> "InstallClient"
    ==> "Run"

Target.runOrDefaultWithArguments "Build"
