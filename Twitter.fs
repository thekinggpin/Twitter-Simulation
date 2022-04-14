open System
open System.Collections.Generic
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

open Suave.Writers
open Newtonsoft.Json
open Akka.Actor

open Akka.FSharp

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

let system = ActorSystem.Create("Tweeter")

type Follower = 
    {
        UserName: string
        Following: string
    }

type NewTweet =
    {
        Tweet: string
        UserName: string
    }
type NewAnswer =
    {
        Text: string
    }
type Answer = 
    {
        Text: string
        AnswerId: int
    }


type RespMsg =
    {
        Comment: string
        Content: list<string>
        status: int
        error: bool
    }
      
type LiveUserHandlerMsg =
    | SendTweet of WebSocket*NewTweet
    | SendMention of WebSocket* NewTweet
    | SelfTweet of WebSocket * NewTweet
    | Following of WebSocket * string

type Register =
    {
        UserName: string
        Password: string
    }
type tweetHandlerMsg =
    | AddTweetMsg of NewTweet
    | AddTweetToFollowersMsg of NewTweet
    | TweetParserMsg of NewTweet


type Login =
    {
        UserName: string
        Password: string
    }

type Logout =
    {
        UserName: string
    }
let setCORSHeaders =
    setHeader  "Access-Control-Allow-Origin" "*"
    >=> setHeader "Access-Control-Allow-Headers" "content-type"

let mutable users = Map.empty
let mutable activeUsers = Map.empty
let mutable tweetOwner = Map.empty
let mutable followers = Map.empty
let mutable mentions = Map.empty
let mutable hashTags = Map.empty
let mutable websockmap = Map.empty






let build_response_in_bytes (message:string) =
    message
    |> System.Text.Encoding.ASCII.GetBytes
    |> ByteSegment


let adding_a_user (user: Register) =
    let temp = users.TryFind(user.UserName)
    if temp = None then
        users <- users.Add(user.UserName,user.Password)
        {Comment = "Congratulations 🥳 Registration is Succesful!";Content=[];status=1;error=false}
    else
        {Comment = "User Already Exsists, please Log In with the same credentials.";Content=[];status=1;error=true}

let login_user (user: Login) = 
    printfn "A wild Login Request from %s as %A appears." user.UserName user
    let temp = users.TryFind(user.UserName)
    if temp = None then
        {Comment = "User Does not exsist 🚫. Please Register your self!";Content=[];status=0;error=true}
    else
        if temp.Value.CompareTo(user.Password) = 0 then
            let temp1 = activeUsers.TryFind(user.UserName)
            if temp1 = None then
                activeUsers <- activeUsers.Add(user.UserName,true)
                {Comment = "Logged In Succesfully, Let's get Ill. 🎉🎊🥳";Content=[];status=2;error=false}
            else
                {Comment = "Already Logged In";Content=[];status=2;error=true}
        else
            {Comment = "Wrong Password! You look sus. 🕵️ ";Content=[];status=1;error=true}

let logout_user (user:Logout) = 
    printfn "Received a known logout Request from %s as %A" user.UserName user
    let temp = users.TryFind(user.UserName)
    if temp = None then
        {Comment = "User Doesn't exsist!!. Please Register ";Content=[];status=0;error=true}
    else
        let temp1 = activeUsers.TryFind(user.UserName)
        if temp1 = None then
            {Comment = "User Not Logged In";Content=[];status=1;error=true}
        else
            activeUsers <- activeUsers.Remove(user.UserName)
            {Comment = "User Logged out Succesfully";Content=[];status=1;error=false}

let user_logged_in_bool username = 
    let temp = activeUsers.TryFind(username)
    if temp <> None then
        1 // Logged In
    else
        let temp1 = users.TryFind(username)
        if temp1 = None then
            -1 // User Doesn't Exsist
        else
            0 // User Exsists but not logged in 

let check_user_if_exist username =
    let temp = users.TryFind(username)
    temp <> None





let liveUserHandler (mailbox:Actor<_>) = 
    let rec loop() = actor{
        let! msg = mailbox.Receive()
        match msg with
        |SelfTweet(ws,tweet)->  let response = "You have tweeted '"+tweet.Tweet+"'"
                                let byteResponse = build_response_in_bytes response
                                
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        |SendTweet(ws,tweet)->
                                let response = tweet.UserName+" has tweeted '"+tweet.Tweet+"'"
                                let byteResponse = build_response_in_bytes response
                                
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
                                // printfn "%A" err
        |SendMention(ws,tweet)->
                                let response = tweet.UserName+" mentioned you in tweet '"+tweet.Tweet+"'"
                                let byteResponse = build_response_in_bytes response
                                // printfn "Sending data to %A" ws 
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        |Following(ws,msg)->
                                let response = msg
                                let byteResponse = build_response_in_bytes response
                                // printfn "Sending data to %A" ws 
                                let s =socket{
                                                do! ws.send Text byteResponse true
                                                }
                                Async.StartAsTask s |> ignore
        return! loop()
    }
    loop()
    
let liveUserHandlerRef = spawn system "luref" liveUserHandler

let tweetParser (tweet:NewTweet) =
    let splits = (tweet.Tweet.Split ' ')
    for i in splits do
        if i.StartsWith "@" then
            let temp = i.Split '@'
            if check_user_if_exist temp.[1] then
                let temp1 = mentions.TryFind(temp.[1])
                if temp1 = None then
                    let mutable mp = Map.empty
                    let tlist = new List<string>()
                    tlist.Add(tweet.Tweet)
                    mp <- mp.Add(tweet.UserName,tlist)
                    mentions <- mentions.Add(temp.[1],mp)
                else
                    let temp2 = temp1.Value.TryFind(tweet.UserName)
                    if temp2 = None then
                        let tlist = new List<string>()
                        tlist.Add(tweet.Tweet)
                        let mutable mp = temp1.Value
                        mp <- mp.Add(tweet.UserName,tlist)
                        mentions <- mentions.Add(temp.[1],mp)
                    else
                        temp2.Value.Add(tweet.Tweet)
                let temp3 = websockmap.TryFind(temp.[1])
                if temp3<>None then
                    liveUserHandlerRef <! SendMention(temp3.Value,tweet)
        elif i.StartsWith "#" then
            let temp1 = i.Split '#'
            let temp = hashTags.TryFind(temp1.[1])
            if temp = None then
                let lst = List<string>()
                lst.Add(tweet.Tweet)
                hashTags <- hashTags.Add(temp1.[1],lst)
            else
                temp.Value.Add(tweet.Tweet)


let addFollower (follower: Follower) =
    printfn "Received Follower Request from %s as %A" follower.UserName follower
    let status = user_logged_in_bool follower.UserName
    if status = 1 then
        if (check_user_if_exist follower.Following) then
            let temp = followers.TryFind(follower.Following)
            let temp1 = websockmap.TryFind(follower.UserName)
            if temp = None then
                let lst = new List<string>()
                lst.Add(follower.UserName)
                followers <- followers.Add(follower.Following,lst)
                if temp1 <> None then
                    liveUserHandlerRef <! Following(temp1.Value,"You are now following: "+follower.Following)
                {Comment = "Sucessfully Added to the Following list";Content=[];status=2;error=false}
            else
                if temp.Value.Exists( fun x -> x.CompareTo(follower.UserName) = 0 ) then
                    if temp1 <> None then
                        liveUserHandlerRef <! Following(temp1.Value,"You are already following: "+follower.Following)
                    {Comment = "You are already Following"+follower.Following;Content=[];status=2;error=true}
                else
                    temp.Value.Add(follower.UserName)
                    if temp1 <> None then
                        liveUserHandlerRef <! Following(temp1.Value,"You are now following: "+follower.Following)
                    {Comment = "Sucessfully Added to the Following list";Content=[];status=2;error=false}
        else
            {Comment = "Follower "+follower.Following+" doesn't exsist";Content=[];status=2;error=true}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "User Doesn't Exsist!!Please Register";Content=[];status=0;error=true}

let addTweet (tweet: NewTweet) =
    let temp = tweetOwner.TryFind(tweet.UserName)
    if temp = None then
        let lst = new List<string>()
        lst.Add(tweet.Tweet)
        tweetOwner <- tweetOwner.Add(tweet.UserName,lst)
    else
        temp.Value.Add(tweet.Tweet)
    



let addTweetToFollowers (tweet: NewTweet) = 
    let temp = followers.TryFind(tweet.UserName)
    if temp <> None then
        for i in temp.Value do
            let temp1 = {Tweet=tweet.Tweet;UserName=i}
            addTweet temp1
            let temp2 = websockmap.TryFind(i)
            printfn "%s" i
            if temp2 <> None then
                liveUserHandlerRef <! SendTweet(temp2.Value,tweet)



let tweetHandler (mailbox:Actor<_>) =
    let rec loop() = actor{
        let! msg = mailbox.Receive()
        match msg with 
        | AddTweetMsg(tweet) -> addTweet(tweet)
                                let temp = websockmap.TryFind(tweet.UserName)
                                if temp <> None then
                                    liveUserHandlerRef <! SelfTweet(temp.Value,tweet)
        | AddTweetToFollowersMsg(tweet) ->  addTweetToFollowers(tweet)
        | TweetParserMsg(tweet) -> tweetParser(tweet)
        return! loop()
    }
    loop()

let tweetHandlerRef = spawn system "thref" tweetHandler

let addTweetToUser (tweet: NewTweet) =
    let status = user_logged_in_bool tweet.UserName
    if status = 1 then
        tweetHandlerRef <! AddTweetMsg(tweet)
        // addTweet tweet
        tweetHandlerRef <! AddTweetToFollowersMsg(tweet)
        // addTweetToFollowers tweet
        tweetHandlerRef <! TweetParserMsg(tweet)
        // tweetParser tweet
        {Comment = "Tweeted Succesfully";Content=[];status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "User Doesn't Exsist!!Please Register";Content=[];status=0;error=true}

let getTweets username =
    let status = user_logged_in_bool username
    if status = 1 then
        let temp = tweetOwner.TryFind(username)
        if temp = None then
            {Comment = "No Tweets";Content=[];status=2;error=false}
        else
            let len = Math.Min(10,temp.Value.Count)
            let res = [for i in 1 .. len do yield(temp.Value.[i-1])] 
            {Comment = "Get Tweets done Succesfully";Content=res;status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "User Doesn't Exsist!!Please Register";Content=[];status=0;error=true}

let getMentions username = 
    let status = user_logged_in_bool username
    if status = 1 then
        let temp = mentions.TryFind(username)
        if temp = None then
            {Comment = "No Mentions";Content=[];status=2;error=false}
        else
            let res = new List<string>()
            for i in temp.Value do
                for j in i.Value do
                    res.Add(j)
            let len = Math.Min(10,res.Count)
            let res1 = [for i in 1 .. len do yield(res.[i-1])] 
            {Comment = "Get Mentions done Succesfully";Content=res1;status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "User Doesn't Exsist!!Please Register";Content=[];status=0;error=true}

let getHashTags username hashtag =
    let status = user_logged_in_bool username
    if status = 1 then
        printf "%s" hashtag
        let temp = hashTags.TryFind(hashtag)
        if temp = None then
            {Comment = "No Tweets with this hashtag found";Content=[];status=2;error=false}
        else
            let len = Math.Min(10,temp.Value.Count)
            let res = [for i in 1 .. len do yield(temp.Value.[i-1])] 
            {Comment = "Get Hashtags done Succesfully";Content=res;status=2;error=false}
    elif status = 0 then
        {Comment = "Please Login";Content=[];status=1;error=true}
    else
        {Comment = "User Doesn't Exsist!!Please Register";Content=[];status=0;error=true}

let registerNewUser (user: Register) =
    printfn "Received Register Request from %s as %A" user.UserName user
    adding_a_user user

let respTweet (tweet: NewTweet) =
    printfn "Received Tweet Request from %s as %A" tweet.UserName tweet
    addTweetToUser tweet

//low level functions
let getString (rawForm: byte[]) =
    // printfn "%A" (System.Text.Encoding.UTF8.GetString(rawForm))
    System.Text.Encoding.UTF8.GetString(rawForm)

let fromJson<'a> json =
    JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a



let gettweets username =
    printfn "Received GetTweets Request from %s " username
    getTweets username
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let getmentions username =
    printfn "Received GetMentions Request from %s " username
    getMentions username
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let gethashtags username hashtag =
    printfn "Received GetHashTag Request from %s for hashtag %A" username hashtag
    getHashTags username hashtag
    |> JsonConvert.SerializeObject
    |> OK
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let register =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Register>
    |> registerNewUser
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let login =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Login>
    |> login_user
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let logout =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Logout>
    |> logout_user
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let newTweet = 
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<NewTweet>
    |> respTweet
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders

let follow =
    request (fun r ->
    r.rawForm
    |> getString
    |> fromJson<Follower>
    |> addFollower
    |> JsonConvert.SerializeObject
    |> OK
    )
    >=> setMimeType "application/json"
    >=> setCORSHeaders




let websocketHandler (webSocket : WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true

        while loop do
              let! msg = webSocket.read()

              match msg with
              | (Text, data, true) ->
                let str = UTF8.toString data 
                
                if str.StartsWith("UserName:") then
                    let uname = str.Split(':').[1]
                    websockmap <- websockmap.Add(uname,webSocket)
                    printfn "connected to %s websocket" uname
                else
                    let response = sprintf "response to %s" str
                    let byteResponse = build_response_in_bytes response
                    do! webSocket.send Text byteResponse true

              | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
                loop <- false
              | _ -> ()
    }



let allow_cors : WebPart =
    choose [
        OPTIONS >=>
            fun context ->
                context |> (
                    setCORSHeaders
                    >=> OK " approved by CORS" )
    ]

//setup app routes
let app =
    choose
        [ 
            path "/websocket" >=> handShake websocketHandler 
            allow_cors
            GET >=> choose
                [ 
                path "/" >=> OK "Hello World" 
                pathScan "/gethashtags/%s/%s" (fun (username,hashtag) -> (gethashtags username hashtag))
                pathScan "/gettweets/%s" (fun username -> (gettweets username))
                pathScan "/getmentions/%s" (fun username -> (getmentions username))
                ]

            POST >=> choose
                [   
                path "/login" >=> login
                path "/logout" >=> logout
                path "/follow" >=> follow
                path "/newtweet" >=> newTweet 
                path "/register" >=> register
              ]

            PUT >=> choose
                [ ]

            DELETE >=> choose
                [ ]
        ]

[<EntryPoint>]
let main argv =
    startWebServer defaultConfig app
    0
