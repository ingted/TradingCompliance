namespace ClientServerTable

open WebSharper
open WebSharper.JavaScript
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Html
open Server
open WebSharper.Sitelets
open WebSharper.JQuery
open Import2TSS
open SqlLib
open WebSharper.Moment
open Import2TSS

[<JavaScript>]
module ClientBase =

    let getUrl (safe_url:EndPoint) =
        let router = Router.Infer()
        let doc_url = JS.Document.URL
        let url_app = doc_url.Substring(0, doc_url.LastIndexOf('/'))
        url_app + router.Link(safe_url)        
    
    let alert2entity (alert_code: string) =
        match alert_code with
        | x when x.StartsWith("A82") -> ServerModel.ByTrade
        | x when x.StartsWith("A51") -> ServerModel.ByCpty
        | _ -> ServerModel.ByCargo
    type PostParam = {param_name: string; param_id: string}
    type PostForm = {key_param_id: string; post_params: PostParam[]}
    let (|==>) (post_form: PostForm) (safe_url:EndPoint) =
        let router = Router.Infer()
        let doc_url = JS.Document.URL
        let url_app = doc_url.Substring(0, doc_url.LastIndexOf('/'))
        let url = url_app + router.Link(safe_url)
        //Console.Log("inferring url: " + url)
        form [attr.target "_blank"; attr.action url; attr.id ("form" + post_form.key_param_id); attr.method "POST" ] [
            a [ on.click (fun el ev -> 
                    let frm = JS.Document.GetElementById ("form" + post_form.key_param_id) |> As<HTMLFormElement>
                    frm.Submit()
                ) ] [text post_form.key_param_id]
            Doc.Concat 
                (post_form.post_params
                |> Array.map ( fun pp ->
                    input [attr.``type`` "hidden"; attr.name pp.param_name; attr.value pp.param_id] []
                ))
        ]

    let ToHtml (doc:Doc) : JQuery =  
       let magic = JS.Document.CreateElement "magic"  
       doc |> Doc.Run magic
       JQuery.Of(magic)


    let DeferredOfAsync (a: Async<'T>) =
        JQuery.JQuery.Deferred(As (fun (defer: JQuery.Deferred) ->
            async {
                try
                    let! res = a
                    defer.Resolve(res) |> ignore
                with exn ->
                    defer.Reject(exn) |> ignore
            }
            |> Async.Start
        ))

    let EtsSpA = ServerModel.EtsSpA 
    let EtsInc =  ServerModel.EtsInc
    let DropDownList = "MasterBookCo"

    let SearchByDropDown = "SearchByDropDown"



    let GetBookCo () = async {
        let sel = JS.Document.GetElementById(DropDownList) |> As<HTMLSelectElement>
        let idx = sel?selectedIndex |> As<int>
        let opt = sel.Item(idx) |> As<HTMLOptionElement>
        return opt.Label 
    }
    
    let addHiddenKey json = 
        let item : obj = Json.Deserialize json
        item?CompositeKey <-  item?AlertCode + "|" + item?AlertKey + "|" + item?BookCompany 
        item


    type AlertSelection = { book: string; codes: string array; status: string; 
        dateFrom: string; dateTo: string; analyst: string; alertKey: string; showClosed: bool; }
    let now = Moment() // .Locale("uk") not needed
    let initDateTo = now.Format("DD/MM/YYYY")
    let initAsofFrom = now.Format("DD/MM/YYYY")
    let initAsofTo = now.Format("DD/MM/YYYY")
    let initDateFrom = now.Add(-14,"d").Format("DD/MM/YYYY")
    let cptyFreqTradeAfter = Moment().Add(-1,"months").Format("DD/MM/YYYY")
    let cptyFreqTradeBefore = Moment().Format("DD/MM/YYYY")
    let emptyAlertSel =  { book = ""; codes = [||]; status = ""; 
        dateFrom = initDateFrom; dateTo = initDateTo; analyst = ""; alertKey = ""; showClosed = false}
    let alertSelVar = Var.Create emptyAlertSel // for alert jtable select changed
    let refreshAlerts = Submitter.Create alertSelVar.View emptyAlertSel
    let bookVar = Var.Create ""
    let refreshAnalysts = Submitter.Create bookVar.View ""
    let refreshAnalistView book =
        bookVar.Set book
        refreshAnalysts.Trigger()
    let NoLogSelected = None : System.DateTime option
    let selectedLogVar = Var.Create NoLogSelected
    type CombinedLog = {Book: string; SelectedLog: System.DateTime option }
    let combinedLogView = 
        View.Map2 (fun b s -> {Book = b; SelectedLog = s }) bookVar.View selectedLogVar.View
    let refreshLogs = Submitter.Create combinedLogView {Book = ""; SelectedLog = NoLogSelected }
    let refreshLogViewFromBook book =
        bookVar.Set book
        refreshLogs.Trigger()
    let refreshLogViewFromSelection sel =
        selectedLogVar.Set sel
        refreshLogs.Trigger()

    let doAnimation (elementId:string) (animationName:string) = 
        let element = JQuery.Of("#" + elementId)
        element.AddClass( animationName + " animated" ).Ignore
        element.On("animationend", fun el ev ->
            element.RemoveClass( animationName + " animated" ).Ignore
            element.Off("animationend").Ignore
        ).Ignore

    type AssignedBadges = NoAssignedBadges | AssignedBadges of int
    let badgeAssignedVar = Var.Create NoAssignedBadges 
    let AssignedBadgesHtmlId = "AssignedBadges"
    let ShowAssignedBadges() : Doc =
        Doc.BindView 
            (fun badges -> 
                match badges with
                | NoAssignedBadges -> Doc.Empty
                | AssignedBadges badges -> 
                span [] [text " "; span [attr.id AssignedBadgesHtmlId; attr.``class`` "badge"; on.afterRender(fun el ->
                    doAnimation AssignedBadgesHtmlId "bounceInRight"
                )] [text (string badges)]]
            ) 
            badgeAssignedVar.View    
    


    let updateAssignedBadges assigned2me = 
        async {
            match badgeAssignedVar.Value with
            | NoAssignedBadges -> ()
            | AssignedBadges badges -> 
                if (assigned2me <> badges && badges > 0) then
                    doAnimation AssignedBadgesHtmlId "bounceOutLeft"
            badgeAssignedVar.Set (if assigned2me > 0 then AssignedBadges assigned2me else NoAssignedBadges)
        } |> Async.StartImmediate

    let callUpdateAnalyst book username = 
        async {
            do! Server.UpdateAnalyst book username
            refreshAnalistView book 
        } |> Async.StartImmediate

    let callDeleteAnalyst book username = 
        async {
            do! Server.DeleteAnalyst book username
            refreshAnalistView book 
        } |> Async.StartImmediate
    
    let callInsertAnalyst book username = 
        async {
            do! Server.InsertAnalyst book username
            refreshAnalistView book 
        } |> Async.StartImmediate
    
    type AuditData = {alertCode: string; alertKey: string; bookCo: string}
    type AuditDataSel = | NoAlertSelection | AlertSelection of AuditData
    
    let auditInput = Var.Create  NoAlertSelection
    let auditSubmit = Submitter.CreateOption auditInput.View
    type AuditParams = {jtStartIndex: int; jtPageSize:int}


    let QueryString2Json(queryString:string) : string =
        JS.Inline(" var pairs = $0.split('&');
                    var rPlus = /\+/g;
                    var oilAlert = {};
                    pairs.forEach(function (pair) {
                        pair = pair.split('=');
                        oilAlert[pair[0]] = decodeURIComponent((pair[1] || '').replace( rPlus, '%20' ) );
                    });
                    return oilAlert", queryString)
    


    let RefreshHistory oilAlert bookCo =
        Console.Log("RefreshHistory AlertCode: " +  oilAlert?AlertCode + ", AlertKey: " + oilAlert?AlertKey)
        let alertCode, alertKey = string oilAlert?AlertCode, string oilAlert?AlertKey 

        auditInput.Set (AlertSelection {alertKey= alertKey; alertCode = alertCode; bookCo = bookCo })
        auditSubmit.Trigger()
        Console.Log("auditTable loaded")

    let status2style data = 
        match data with
        | str when str = Alerting.Escalated -> Some "red"
        | str when str = Alerting.OpenFromSys -> Some "aqua"
        | str when str = Alerting.CloseFromSys -> Some "white"
        | str when str = Alerting.Assigned -> Some "green"
        | str when str = Alerting.Standby -> Some "gray"
        | str when str = Alerting.TakenInCharge -> Some "orange"
        | str when str = Alerting.Resolved -> Some "gold"
        | str when str = Alerting.Closed -> Some "blue"
        | str when str = Alerting.ResolvedAfterEscalation -> Some "pink"
        | _ -> None
        |> Option.bind (fun str -> Some <| "background-color:" + str + " !important; color:black !important;")

    let colourTable data =
        let table = JQuery.Of("#AlertTable")
        let status_position = "td:eq(4)"
        data?records
        |> Array.iteri (fun i record -> 
            let curr_row  = table.Find(".jtable tbody tr:eq("+ string i + ")")
            let colorMe =  curr_row.Find status_position
            status2style record?Status
            |> Option.iter (fun css_style ->
                colorMe.Css("cssText",css_style).Ignore
            )
            curr_row.Hover(fun el ev ->
                JQuery.Of(el).Find(status_position).Css("cssText","background-color:#a8a8a8; color:white;").Ignore
            ).Ignore
            curr_row.Mouseleave(fun el ev ->
                let css_class = JQuery.Of(el).Attr("class")
                if  (css_class.EndsWith("-selected") |> not) then
                    let find_me_again = JQuery.Of(el).Find(status_position)
                    status2style (find_me_again.Text())
                    |> Option.iter (fun css_style ->
                        find_me_again.Css("cssText",css_style).Ignore)
            ).Ignore
        )
    type dateOption = | DateAndTime |DateOnly
    let dateFormat date dateOption = 
        match System.DateTime.TryParse(date) with
        | false, _ -> ""
        | true, tranDate -> 
            match dateOption with
            | DateAndTime -> (tranDate.ToShortDateString() + " " + tranDate.ToLongTimeString())
            | DateOnly -> tranDate.ToShortDateString()

    let datePicker (name:string) (format: string) (onChange: string -> unit) (value: string) =
        let datePk = JQuery.Of(name)
        JS.Inline("$0.datepicker({
                dateFormat: $1,
            }).on('change', function() {
                $2($0.val());
              });", datePk, format, onChange)
        JS.Inline("$0.datepicker('setDate',$1);",datePk, value)
    
    let dateStdFormat = "dd/mm/yy"

    type JTableOptionHelper = {DisplayText: string; Value: string}

    let analysts2options  (analysts: SqlDB.Analyst[]) =
        analysts
        |> Array.map( fun a ->
            { DisplayText = a.Name + " " + a.Surname; Value = a.UserName; }
        )

    let sel2jtable response =
        {bookcompany = response.book; user = response.analyst; code = System.String.Join(",", response.codes); status = response.status; 
         key = response.alertKey; closed = response.showClosed; dateFrom = response.dateFrom; dateTo = response.dateTo; }

    let CreateManualAlertAsync (queryString : string) = 
        async {
            let oilAlert =  QueryString2Json (queryString)
            let alertJson = JS.Inline("JSON.stringify($0)", oilAlert) 
            Console.Log(
                sprintf 
                    "Async call to CreateManualAlertAsync \nwith queryString: %s \n=> oil alert %A \n=> json %s" 
                    queryString oilAlert alertJson)
            return! Server.CreateOilAlertAsync alertJson bookVar.Value
        } |> DeferredOfAsync


    let DropDownInput // DropDownInput "Assigned To" "150px" "UserSelect" "150px" vAnalystsResp Analyst_OnChange alertSelVar.Value.analyst
        (labelTxt: string) // "Assigned To"
        (labelWidth: string) // "150px"
        (attrId: string) // "UserSelect"
        (selectWidth: string) // "150px"
        (vAnalystsResp: View<ServerAnalystResponse>)
        (analyst_OnChange: Dom.Element -> Dom.Event -> unit)
        (selectedVal: string) // alertSelVar.Value.analyst
        =
        Doc.Concat [
            span [attr.style ("width:"+ labelWidth + "; display: inline-block;")] [text labelTxt]
            vAnalystsResp
            |> View.WithInitOption
            |> Doc.BindView ( fun analystResp ->
                match analystResp with
                | None -> text "..wait.."
                | Some (AnalystError err) -> text err
                | Some (AnalystResponse (_, analysts)) ->
                    select [ attr.id attrId;
                             on.change analyst_OnChange;
                             attr.``class`` "btn btn-primary dropdown-toggle"; attr.style selectWidth] [
                        Elt.option 
                            (if analysts |> Array.forall(fun a -> selectedVal <> a.UserName) then 
                                [attr.selected ""] 
                             else [] ) [];
                        analysts
                        |> Array.map( fun a ->
                            Elt.option 
                                (if selectedVal = a.UserName then 
                                    [attr.value a.UserName; attr.selected ""] 
                                 else [attr.value a.UserName;] )
                                [text <| a.Name + " " + a.Surname] :> Doc) 
                        |> Doc.Concat                        
                    ]
            )
        ]

    let customForm (ev, dt) =
        let form :JQuery = JQuery.Of(dt?form :> obj)
        form.Css("width","600px").Ignore  
        let code_found = form.Find("select[name=AlertCode]") 
        match  dt?formType with
        | "create" ->
            code_found.Empty().Ignore
            let code_dropdown = JQuery.Of("#AlertCodeSelect option")
            Console.Log("code_dropdown", code_dropdown)
            code_dropdown.Each(fun i el -> 
                let curr_code : string = el?value
                Console.Log("create i: " + i.ToString() + " curr_code: " + curr_code + " el: ", el)
                match curr_code.Substring(0,1) with
                | "M" ->
                    code_found.Append("<option value='" + curr_code + "'>" + curr_code + "</option>").Ignore
                | _ -> ()
            ).Ignore
        | _ -> 
            let curr_alert_code : string = code_found.Val().ToString()
            Console.Log("update curr_code: ", curr_alert_code)
            match curr_alert_code.Substring(0,1) with
            | "A" ->
                form.Find("input[name=AlertKey]").Prop("readonly",true).Ignore
                code_found.Empty().Ignore
                code_found.Append("<option value='" + curr_alert_code + "'>" + curr_alert_code + "</option>").Ignore
                code_found.Val(curr_alert_code).Ignore
                Console.Log("code_found", code_found)
                form.Find("input[name=Portfolio]").Prop("readonly",true).Ignore
                form.Find("input[name=Commodity]").Prop("readonly",true).Ignore
            | _ -> 
                code_found.Empty().Ignore
                let code_dropdown = JQuery.Of("#AlertCodeSelect option")
                Console.Log("code_dropdown", code_dropdown)
                code_dropdown.Each(fun i el -> 
                    let curr_code : string = el?value
                    Console.Log("create i: " + i.ToString() + " curr_code: " + curr_code + " el: ", el)
                    match curr_code.Substring(0,1) with
                    | "M" ->
                        code_found.Append("<option value='" + curr_code + "'" + (if curr_code = curr_alert_code then " selected" else "") + ">" + curr_code + "</option>").Ignore
                    | _ -> ()
                ).Ignore
        form.Find("textarea[name=Note]").Css("width","550px").Attr("rows","6").Ignore
        form.Find("textarea[name=Outcome]").Css("width","550px").Attr("rows","6").Ignore