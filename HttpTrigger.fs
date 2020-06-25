namespace Company.Function

open System
open System.IO
open Microsoft.AspNetCore.Mvc
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.AspNetCore.Http
open Newtonsoft.Json
open Microsoft.Extensions.Logging
open FSharp.Data

module HttpTrigger =
    type private RequestDto = JsonProvider<"schema/request.json", SampleIsList=true>

    module private Json =
        open Newtonsoft.Json.Serialization

        let private options () =
            JsonSerializerSettings (
                ContractResolver =
                    DefaultContractResolver (
                        NamingStrategy = SnakeCaseNamingStrategy()
                    )
            )

        let serialize obj =
            JsonConvert.SerializeObject (obj, options())

    type ResponseDto = {
        Data: DataDto
    }

    and DataDto = {
        Type: string
        Id: string
        Attributes: AttributesDto
    }

    and AttributesDto = {
        Contact: NormalizedContactDto
    }

    and NormalizedContactDto = {
        Email: string
        Phone: string
    }

    type ContactDto = {
        Email: string option
        Phone: string option
    }

    type Normalize = Normalize of (string -> string)

    module Normalize =
        let removeSpaces = Normalize (fun s -> s.Replace(" ", ""))
        let lowerCase = Normalize (fun s -> s.ToLower())
        let phoneCode = Normalize (id) (* todo - replace ^00420 -> +420 (jen na zacatku) *)

        let normalize normalizations input =
            normalizations
            |> List.fold (fun normalized (Normalize normalize) -> normalize normalized) input

        let email =
            normalize [
                removeSpaces
                lowerCase
            ]

        let phone =
            normalize [
                removeSpaces
                lowerCase
                phoneCode
            ]

    [<FunctionName("HttpTrigger")>]
    let run ([<HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)>]req: HttpRequest) (log: ILogger) =
        async {
            log.LogInformation("F# HTTP trigger function processed a request.")
(*
            match req.Headers.TryGetValue "Content-Type" with
            | true, v -> if v. = "application/vnd.api+json" then ()
            | _ -> ()
 *)
            use stream = new StreamReader(req.Body)
            let! reqBody = stream.ReadToEndAsync() |> Async.AwaitTask

            let request = reqBody |> RequestDto.Parse

            let contact = {
                Email = request.Data.Attributes.Contact.Email
                Phone = request.Data.Attributes.Contact.Phone
            }

            let response = {
                Data = {
                    Type = "normalization"
                    Id = Guid.NewGuid() |> string
                    Attributes = {
                        Contact = {
                            Email = contact.Email |> Option.map Normalize.email |> Option.defaultValue null
                            Phone = contact.Phone |> Option.map Normalize.phone |> Option.defaultValue null
                        }
                    }
                }
            }

            let response = OkObjectResult(response |> Json.serialize)

            response.ContentTypes.Clear()
            response.ContentTypes.Add("application/vnd.api+json")

            return response :> IActionResult
        }
        |> Async.StartAsTask
