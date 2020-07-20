namespace Company.Function

module Normalization =
    open System.Text.RegularExpressions

    type Normalize = Normalize of (string -> string)

    [<RequireQualifiedAccess>]
    module Normalize =
        let private allowedPhoneCodes = List.distinct [
            "+420"  // czech republic must be first, so it would be used for code-less numbers

            // top codes, ordered by occurrence, so it would match as fast as possible
            "+421"; "+48"; "+44"; "+91"; "+380"; "+33"; "+34"; "+375"; "+994"; "+381"; "+971"
            "+359"; "+31"; "+385"; "+353"; "+40"; "+36"; "+998"; "+41"; "+213"; "+995"; "+351"
            "+32"; "+46"; "+996"; "+358"; "+27"; "+61"; "+966"; "+972"; "+355"

            // other codes
            "+93"; "+1684"; "+376"; "+244"; "+1264"; "+672"; "+1268"; "+54"
            "+374"; "+297"; "+43"; "+1242"; "+973"; "+880"; "+1246"
            "+501"; "+229"; "+1441"; "+975"; "+591"; "+387"; "+267"; "+47"; "+55"; "+246"; "+673"
            "+226"; "+257"; "+855"; "+237"; "+1"; "+238"; "+345"; "+236"; "+235"; "+56"
            "+86"; "+57"; "+269"; "+242"; "+243"; "+682"; "+506"; "+225"
            "+53"; "+357"; "+45"; "+253"; "+1767"; "+1849"; "+593"; "+20"; "+503"; "+240"
            "+291"; "+372"; "+251"; "+500"; "+298"; "+679"; "+358"; "+594"; "+689"; "+262"
            "+241"; "+220"; "+49"; "+233"; "+350"; "+30"; "+299"; "+1473"; "+590"
            "+1671"; "+502"; "+224"; "+245"; "+592"; "+509"; "+0"; "+379"; "+504"
            "+852"; "+354"; "+62"; "+98"; "+964"
            "+39"; "+1876"; "+81"; "+962"; "+7"; "+254"; "+686"; "+850"; "+82"
            "+383"; "+965"; "+856"; "+371"; "+961"; "+266"; "+231"; "+218"; "+423"
            "+370"; "+352"; "+853"; "+389"; "+261"; "+265"; "+60"; "+960"; "+223"; "+356"
            "+692"; "+596"; "+222"; "+230"; "+262"; "+52"; "+691"; "+373"; "+377"; "+976"
            "+382"; "+1664"; "+212"; "+258"; "+95"; "+264"; "+674"; "+977"; "+599"
            "+687"; "+64"; "+505"; "+227"; "+234"; "+683"; "+672"; "+1670"; "+47"; "+968"; "+92"; "+680"
            "+970"; "+507"; "+675"; "+595"; "+51"; "+63"; "+64"; "+1939"
            "+974"; "+7"; "+250"; "+262"; "+590"; "+290"; "+1869"; "+1758"; "+590"
            "+508"; "+1784"; "+685"; "+378"; "+239"; "+221"; "+248"; "+232"
            "+65"; "+386"; "+677"; "+252"; "+211"; "+500"; "+94"
            "+249"; "+597"; "+47"; "+268"; "+963"; "+886"; "+992"; "+255"
            "+66"; "+670"; "+228"; "+690"; "+676"; "+1868"; "+216"; "+90"; "+993"; "+1649"
            "+688"; "+256"; "+1"; "+598"; "+678"; "+58"
            "+84"; "+1284"; "+1340"; "+681"; "+967"; "+260"; "+263"
        ]

        let private hasNumber (s: string) = Regex.IsMatch(s, @"\d+")
        let private hasNumbers expectedCount (s: string) = s.Length = expectedCount && Regex.IsMatch(s, sprintf @"\d{%d}" expectedCount)

        let removeSpaces = Normalize (fun s -> s.Replace(" ", ""))
        let removeWhiteSpaces = Normalize (fun s -> Regex.Replace(s, @"\p{Z}", "")) // @see https://stackoverflow.com/questions/2132348/what-does-char-160-mean-in-my-source-code
        let lowerCase = Normalize (fun s -> s.ToLower())
        let decode =
            Normalize (fun s ->
                [
                    @"%40", "@"
                    @"%2b", "+"
                    @"%2c", ","
                    @"%2f", "/"
                    @"%c2", ""
                    @"%a0", ""
                ]
                |> List.fold (fun (s: string) (find, replace) ->
                    s.Replace(find, replace)
                ) s
            )

        let phoneByLib =
            Normalize (fun phone ->
                try
                    let phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance()
                    let parsedPhone = phoneNumberUtil.Parse(phone, null)

                    if parsedPhone.IsInitialized
                        then sprintf "+%d%d" parsedPhone.CountryCode parsedPhone.NationalNumber
                        else phone
                with
                | _ -> phone
            )

        let allowedPhoneCode (allowedPhoneCodes: string list) =
            let allCodeVariantsOrderedByPriorityAndCodePosition =
                allowedPhoneCodes
                |> List.mapi (fun i code ->
                    [
                        i, 1, code, code
                        i, 5, code, code.TrimStart '+'
                        i, 10, code, code.TrimStart '+' |> sprintf @"00%s"
                        i, 15, code, code.TrimStart '+' |> sprintf @"0%s"
                        i, 20, code, code.TrimStart '+' |> sprintf @"000%s"
                        i, 25, code, code.TrimStart '+' |> sprintf @"+0%s"
                        i, 30, code, code.TrimStart '+' |> sprintf @"+00%s"
                    ]
                    |> List.collect (fun (i, priority, code, codeVariant) -> [
                        i, priority, code, codeVariant
                        i, priority + 1, code, codeVariant + "0"
                        i, priority + 2, code, codeVariant + "00"
                    ])
                )
                |> List.concat
                |> List.sortBy (fun (i, p, _, _) -> p, i)
                |> List.map (fun (_, _, code, codeVariant) -> code, codeVariant)

            Normalize (fun phone ->
                [9; 10; 8; 11]
                |> List.tryPick (fun expectedLength ->
                    allCodeVariantsOrderedByPriorityAndCodePosition
                    |> List.tryPick (fun (code, codeVariant) ->
                        let phoneWithoutCode =
                            if phone.StartsWith(codeVariant)
                                then phone.Substring(codeVariant.Length, phone.Length - codeVariant.Length)
                                else phone
                            |> fun phone -> phone.TrimStart '0'

                        if phoneWithoutCode |> hasNumbers expectedLength
                            then Some (code + phoneWithoutCode)
                            else None
                    )
                )
                |> Option.defaultValue phone
            )

        let phoneNumber =
            Normalize (fun phone ->
                let phone =
                    [
                        "."; "("; ")"; "-"; "/"; "_"                        // understandable chars in phone number
                        "´"; "`"; "¨"; "!"; ";"; "▪"; "§"; "˝"; "¸"; "'"; "ˇ"; ","    // typos probably
                        "�"                                                 // wierd chars
                    ]
                    |> List.fold (fun (phone: string) s ->
                        phone.Replace(s, "")
                    ) phone

                if phone.StartsWith "+"
                    then "+" + phone.Replace("+", "")
                    else phone.Replace("+", "")
            )

        let normalize normalizations input =
            normalizations
            |> List.fold (fun normalized (Normalize normalize) -> normalize normalized) input

        let email =
            normalize [
                removeWhiteSpaces
                lowerCase
                decode
            ]

        let phone = function
            | phone when phone |> hasNumber ->
                phone
                |> normalize [
                    removeWhiteSpaces
                    lowerCase
                    decode
                    phoneNumber
                    allowedPhoneCode allowedPhoneCodes
                    phoneByLib
                ]
            | withoutAnyNumber -> withoutAnyNumber

module HttpTrigger =
    open System
    open System.IO
    open System.Text
    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.AspNetCore.Http
    open Newtonsoft.Json
    open Microsoft.Extensions.Logging
    open FSharp.Data
    open Normalization

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

    [<FunctionName("HttpTrigger")>]
    let run ([<HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)>]req: HttpRequest) (log: ILogger) =
        async {
            use stream = new StreamReader(req.Body)
            let! reqBody = stream.ReadToEndAsync() |> Async.AwaitTask

            let request = reqBody |> RequestDto.Parse

            let contact = {
                Email = request.Data.Attributes.Contact.Email
                Phone = request.Data.Attributes.Contact.Phone
            }

            let responseBody = {
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

            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(responseBody |> Json.serialize, Encoding.UTF8, "application/vnd.api+json")

            return response
        }
        |> Async.StartAsTask
