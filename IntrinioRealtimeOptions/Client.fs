namespace Intrinio.Realtime.Options

open Serilog
open System
open System.Runtime.InteropServices
open System.Net.Http
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open FSharp.NativeInterop
open FSharp.Core.LanguagePrimitives
open System.Runtime.CompilerServices

module private ClientInline =

    let [<Literal>] internal SYMBOL_SIZE : int = 22
    let [<Literal>] internal TRADE_MESSAGE_SIZE : int = 72
    let [<Literal>] internal QUOTE_MESSAGE_SIZE : int = 52
    let [<Literal>] internal REFRESH_MESSAGE_SIZE : int = 52
    let [<Literal>] internal UNUSUAL_ACTIVITY_MESSAGE_SIZE : int = 74

    let internal SELF_HEAL_BACKOFFS : int[] = [| 10_000; 30_000; 60_000; 300_000; 600_000 |]
    
    [<SkipLocalsInit>]
    let inline private stackalloc<'a when 'a: unmanaged> (length: int): Span<'a> =
        let p = NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr
        Span<'a>(p, length)
    
    let inline internal FormatContract (alternateFormattedChars: ReadOnlySpan<byte>) : string =
        //Transform from server format to normal format
        //From this: AAPL_201016C100.00 or ABC_201016C100.003
        //To this:   AAPL__201016C00100000 or ABC___201016C00100003
        
        let contractChars : Span<byte> = stackalloc<byte>(21)
        contractChars[0] <- (byte)'_'
        contractChars[1] <- (byte)'_'
        contractChars[2] <- (byte)'_'
        contractChars[3] <- (byte)'_'
        contractChars[4] <- (byte)'_'
        contractChars[5] <- (byte)'_'
        contractChars[6] <- (byte)'2'
        contractChars[7] <- (byte)'2'
        contractChars[8] <- (byte)'0'
        contractChars[9] <- (byte)'1'
        contractChars[10] <- (byte)'0'
        contractChars[11] <- (byte)'1'
        contractChars[12] <- (byte)'C'
        contractChars[13] <- (byte)'0'
        contractChars[14] <- (byte)'0'
        contractChars[15] <- (byte)'0'
        contractChars[16] <- (byte)'0'
        contractChars[17] <- (byte)'0'
        contractChars[18] <- (byte)'0'
        contractChars[19] <- (byte)'0'
        contractChars[20] <- (byte)'0'
        
        let underscoreIndex : int = alternateFormattedChars.IndexOf((byte)'_')
        let decimalIndex : int = alternateFormattedChars.Slice(9).IndexOf((byte)'.') + 9 //ignore decimals in tickersymbol

        alternateFormattedChars.Slice(0, underscoreIndex).CopyTo(contractChars) //copy symbol        
        alternateFormattedChars.Slice(underscoreIndex + 1, 6).CopyTo(contractChars.Slice(6)) //copy date
        alternateFormattedChars.Slice(underscoreIndex + 7, 1).CopyTo(contractChars.Slice(12)) //copy put/call
        alternateFormattedChars.Slice(underscoreIndex + 8, decimalIndex - underscoreIndex - 8).CopyTo(contractChars.Slice(18 - (decimalIndex - underscoreIndex - 8))) //whole number copy
        alternateFormattedChars.Slice(decimalIndex + 1).CopyTo(contractChars.Slice(18)) //decimal number copy
        
        Encoding.ASCII.GetString(contractChars)

    let inline internal ParseTrade (bytes: ReadOnlySpan<byte>) : Trade =
        Trade
            (FormatContract(bytes.Slice(1, int bytes[0])),
             EnumOfValue<char,Exchange>(char(bytes[65])),
             bytes[23],
             bytes[24],
             BitConverter.ToInt32(bytes.Slice(25, 4)),
             BitConverter.ToUInt32(bytes.Slice(29, 4)),
             BitConverter.ToUInt64(bytes.Slice(33, 8)),
             BitConverter.ToUInt64(bytes.Slice(41, 8)),
             struct(bytes[61], bytes[62], bytes[63], bytes[64]),
             BitConverter.ToInt32(bytes.Slice(49, 4)),
             BitConverter.ToInt32(bytes.Slice(53, 4)),
             BitConverter.ToInt32(bytes.Slice(57, 4)))

    let inline internal ParseQuote (bytes: ReadOnlySpan<byte>) : Quote =
        Quote
            (FormatContract(bytes.Slice(1, int bytes[0])),
             bytes[23],
             BitConverter.ToInt32(bytes.Slice(24, 4)),
             BitConverter.ToUInt32(bytes.Slice(28, 4)),
             BitConverter.ToInt32(bytes.Slice(32, 4)),
             BitConverter.ToUInt32(bytes.Slice(36, 4)),
             BitConverter.ToUInt64(bytes.Slice(40, 8)))

    let inline internal ParseRefresh (bytes: ReadOnlySpan<byte>) : Refresh =
        Refresh
            (FormatContract(bytes.Slice(1, int bytes[0])),
             bytes[23],
             BitConverter.ToUInt32(bytes.Slice(24, 4)),
             BitConverter.ToInt32(bytes.Slice(28, 4)),
             BitConverter.ToInt32(bytes.Slice(32, 4)),
             BitConverter.ToInt32(bytes.Slice(36, 4)),
             BitConverter.ToInt32(bytes.Slice(40, 4)))

    let inline internal ParseUnusualActivity (bytes: ReadOnlySpan<byte>) : UnusualActivity =
        UnusualActivity
            (FormatContract(bytes.Slice(1, int bytes[0])),
             enum<UAType> (int bytes[22]),
             enum<UASentiment> (int bytes[23]),
             bytes[24],
             bytes[25],             
             BitConverter.ToUInt64(bytes.Slice(26, 8)),
             BitConverter.ToUInt32(bytes.Slice(34, 4)),
             BitConverter.ToInt32(bytes.Slice(38, 4)),
             BitConverter.ToInt32(bytes.Slice(42, 4)),
             BitConverter.ToInt32(bytes.Slice(46, 4)),
             BitConverter.ToInt32(bytes.Slice(50, 4)),
             BitConverter.ToUInt64(bytes.Slice(54, 8)))

    let inline internal SetUsesTrade(bitmask: uint8) : uint8 = bitmask ||| 1uy
    let inline internal SetUsesQuote(bitmask: uint8) : uint8 = bitmask ||| 2uy 
    let inline internal SetUsesRefresh(bitmask: uint8) : uint8 = bitmask ||| 4uy 
    let inline internal SetUsesUA(bitmask: uint8) : uint8 = bitmask ||| 8uy

    let inline internal DoBackoff(fn: unit -> bool) : unit =
        let mutable i : int = 0
        let mutable backoff : int = SELF_HEAL_BACKOFFS.[i]
        let mutable success : bool = fn()
        while not success do
            Thread.Sleep(backoff)
            i <- Math.Min(i + 1, SELF_HEAL_BACKOFFS.Length - 1)
            backoff <- SELF_HEAL_BACKOFFS.[i]
            success <- fn()

type internal WebSocketState(ws: WebSocket) =
    let mutable webSocket : WebSocket = ws
    let mutable isReady : bool = false
    let mutable isReconnecting : bool = false
    let mutable lastReset : DateTime = DateTime.Now

    member _.WebSocket
        with get() : WebSocket = webSocket
        and set (ws:WebSocket) = webSocket <- ws

    member _.IsReady
        with get() : bool = isReady
        and set (ir:bool) = isReady <- ir

    member _.IsReconnecting
        with get() : bool = isReconnecting
        and set (ir:bool) = isReconnecting <- ir

    member _.LastReset : DateTime = lastReset

    member _.Reset() : unit = lastReset <- DateTime.Now

type Client(
    [<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
    [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
    [<Optional; DefaultParameterValue(null:Action<Refresh>)>] onRefresh: Action<Refresh>,
    [<Optional; DefaultParameterValue(null:Action<UnusualActivity>)>] onUnusualActivity: Action<UnusualActivity>,
    config : Config.Config) =
    
    let tLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let wsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let mutable token : (string * DateTime) = (null, DateTime.Now)
    let mutable wsState: WebSocketState = new WebSocketState(null)
    let mutable dataMsgCount : uint64 = 0UL
    let mutable dataEventCount : uint64 = 0UL
    let mutable dataTradeCount : uint64 = 0UL
    let mutable dataQuoteCount : uint64 = 0UL
    let mutable dataRefreshCount : uint64 = 0UL
    let mutable dataUnusualActivityCount : uint64 = 0UL
    let mutable textMsgCount : uint64 = 0UL
    let channels : HashSet<string> = new HashSet<string>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : ConcurrentQueue<byte[]> = new ConcurrentQueue<byte[]>()
    let mutable tryReconnect : (unit -> unit) = fun () -> ()
    let httpClient : HttpClient = new HttpClient()
    
    let clientInfoHeaderKey : string = "Client-Information"
    let clientInfoHeaderValue : string = "IntrinioRealtimeOptionsDotNetSDKv6.0"
    let delayHeaderKey : string = "delay"
    let delayHeaderValue : string = "true"

    let useOnTrade : bool = not (obj.ReferenceEquals(onTrade,null))
    let useOnQuote : bool = not (obj.ReferenceEquals(onQuote,null))
    let useOnRefresh : bool = not (obj.ReferenceEquals(onRefresh,null))
    let useOnUA : bool = not (obj.ReferenceEquals(onUnusualActivity,null))

    let allReady() : bool = 
        wsLock.EnterReadLock()
        try wsState.IsReady
        finally wsLock.ExitReadLock()

    let getAuthUrl () : string =
        match config.Provider with
        | Provider.OPRA -> "https://realtime-options.intrinio.com/auth?api_key=" + config.ApiKey
        | Provider.MANUAL -> "http://" + config.IPAddress + "/auth?api_key=" + config.ApiKey
        | _ -> failwith "Provider not specified!"

    let getWebSocketUrl (token: string) : string =
        match config.Provider with
          | Provider.OPRA -> "wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token=" + token + (if config.Delayed then "&delayed=true" else String.Empty)
          | Provider.MANUAL -> "ws://" + config.IPAddress + "/socket/websocket?vsn=1.0.0&token=" + token + (if config.Delayed then "&delayed=true" else String.Empty)
          | _ -> failwith "Provider not specified!"
    
    let parseSocketMessage (bytes: byte[], startIndex: byref<int>) : unit =
        let msgType : uint8 = bytes[startIndex + ClientInline.SYMBOL_SIZE] //This works because it's startIndex + maxSymbolSize - 1 (zero based) + 1 (size of type)
        if (msgType = 1uy) //using if-else vs switch for hotpathing
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, ClientInline.QUOTE_MESSAGE_SIZE)
            let quote: Quote = ClientInline.ParseQuote(chunk)
            Interlocked.Increment(&dataQuoteCount) |> ignore
            startIndex <- startIndex + ClientInline.QUOTE_MESSAGE_SIZE
            if useOnQuote then onQuote.Invoke(quote)
        elif (msgType = 0uy)
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, ClientInline.TRADE_MESSAGE_SIZE)
            let trade: Trade = ClientInline.ParseTrade(chunk)
            Interlocked.Increment(&dataTradeCount) |> ignore
            startIndex <- startIndex + ClientInline.TRADE_MESSAGE_SIZE
            if useOnTrade then onTrade.Invoke(trade)
        elif (msgType > 2uy)
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, ClientInline.UNUSUAL_ACTIVITY_MESSAGE_SIZE)
            let ua: UnusualActivity = ClientInline.ParseUnusualActivity(chunk)
            Interlocked.Increment(&dataUnusualActivityCount) |> ignore
            startIndex <- startIndex + ClientInline.UNUSUAL_ACTIVITY_MESSAGE_SIZE
            if useOnUA then onUnusualActivity.Invoke(ua)
        elif (msgType = 2uy)
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, ClientInline.REFRESH_MESSAGE_SIZE)
            let refresh = ClientInline.ParseRefresh(chunk)
            Interlocked.Increment(&dataRefreshCount) |> ignore
            startIndex <- startIndex + ClientInline.REFRESH_MESSAGE_SIZE
            if useOnRefresh then onRefresh.Invoke(refresh)
        else Log.Warning("Invalid MessageType: {0}", msgType)

    let threadFn () : unit =
        Thread.CurrentThread.Priority <- ThreadPriority.Lowest; //We shouldn't mess with user's thread priority, but we can do our best to make sure our threads run lower than the socket thread feeding the queue.
        let ct = ctSource.Token
        let mutable datum : byte[] = Array.empty<byte>
        while not (ct.IsCancellationRequested) do
            try
                if data.TryDequeue(&datum) then
                    // These are grouped (many) messages.
                    // The first byte tells us how many there are.
                    // From there, check the type at index 22 to know how many bytes each message has.
                    let cnt = datum[0] |> uint64
                    Interlocked.Add(&dataEventCount, cnt) |> ignore
                    let mutable startIndex = 1
                    for _ in 1UL .. cnt do
                        parseSocketMessage(datum, &startIndex)
            with
                | :? OperationCanceledException -> ()
                | exn -> Log.Error("Parse data failure: {0}", exn.Message)

    let threads : Thread[] = Array.init config.NumThreads (fun _ -> new Thread(new ThreadStart(threadFn)))

    let trySetToken() : bool =
        Log.Information("Authorizing...")
        let authUrl : string = getAuthUrl()
        async {
            try
                let! response = httpClient.GetAsync(authUrl) |> Async.AwaitTask
                if (response.IsSuccessStatusCode)
                then
                    let! _token = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    Interlocked.Exchange(&token, (_token, DateTime.Now)) |> ignore
                    Log.Information("Authorization successful")
                    return true
                else
                    Log.Warning("Authorization Failure. Authorization server status code = {0}", response.StatusCode) 
                    return false
            with
            | :? System.InvalidOperationException as exn ->
                Log.Error("Authorization Failure (bad URI): {0:l}", exn.Message)
                return false
            | :? System.Net.Http.HttpRequestException as exn ->
                Log.Error("Authoriztion Failure (bad network connection): {0:l}", exn.Message)
                return false
            | :? System.Threading.Tasks.TaskCanceledException as exn ->
                Log.Error("Authorization Failure (timeout): {0:l}", exn.Message)
                return false
        } |> Async.RunSynchronously

    let getToken() : string =
        tLock.EnterUpgradeableReadLock()
        try
            if (DateTime.Now - TimeSpan.FromDays(1.0)) > (snd token)
            then (fst token)
            else
                tLock.EnterWriteLock()
                try ClientInline.DoBackoff(trySetToken)
                finally tLock.ExitWriteLock()
                fst token
        finally tLock.ExitUpgradeableReadLock()

    let onOpen (_ : EventArgs) : unit =
        Log.Information("Websocket - Connected")
        wsLock.EnterWriteLock()
        try
            wsState.IsReady <- true
            wsState.IsReconnecting <- false
            for thread in threads do
                if not thread.IsAlive
                then thread.Start()
        finally wsLock.ExitWriteLock()
        if channels.Count > 0
        then
            channels |> Seq.iter (fun (symbol: string) ->
                let mutable mask : uint8 = 0uy
                if useOnTrade then mask <- ClientInline.SetUsesTrade(mask)
                if useOnQuote then mask <- ClientInline.SetUsesQuote(mask)
                if useOnRefresh then mask <- ClientInline.SetUsesRefresh(mask)
                if useOnUA then mask <- ClientInline.SetUsesUA(mask)
                let message : byte[] = Array.zeroCreate<byte> (symbol.Length + 2)
                message[0] <- 74uy
                message[1] <- mask
                Array.Copy(Encoding.ASCII.GetBytes(symbol), 0, message, 2, symbol.Length)
                Log.Information("Websocket - Joining channel: {0:l}", symbol)
                wsState.WebSocket.Send(message, 0, message.Length) )

    let onClose (_ : EventArgs) : unit =
        wsLock.EnterUpgradeableReadLock()
        try 
            if not wsState.IsReconnecting
            then
                Log.Information("Websocket - Closed")
                wsLock.EnterWriteLock()
                try wsState.IsReady <- false
                finally wsLock.ExitWriteLock()
                if (not ctSource.IsCancellationRequested)
                then Task.Factory.StartNew(tryReconnect) |> ignore
        finally wsLock.ExitUpgradeableReadLock()

    let (|Closed|Refused|Unavailable|Other|) (input:exn) =
        if (input.GetType() = typeof<SocketException>) &&
            input.Message.StartsWith("A connection attempt failed because the connected party did not properly respond after a period of time")
        then Closed
        elif (input.GetType() = typeof<SocketException>) &&
            (input.Message = "No connection could be made because the target machine actively refused it.")
        then Refused
        elif input.Message.StartsWith("HTTP/1.1 503")
        then Unavailable
        else Other

    let onError (args : SuperSocket.ClientEngine.ErrorEventArgs) : unit =
        let exn = args.Exception
        match exn with
        | Closed -> Log.Warning("Websocket - Error - Connection failed")
        | Refused -> Log.Warning("Websocket - Error - Connection refused")
        | Unavailable -> Log.Warning("Websocket - Error - Server unavailable")
        | _ -> Log.Error("Websocket - Error - {0}:{1}", exn.GetType(), exn.Message)

    let onDataReceived (args: DataReceivedEventArgs) : unit =
        //Log.Debug("Websocket - Data received")
        Interlocked.Increment(&dataMsgCount) |> ignore
        data.Enqueue(args.Data)

    let onMessageReceived (args : MessageReceivedEventArgs) : unit =
        Log.Debug("Websocket - Message received")
        Interlocked.Increment(&textMsgCount) |> ignore
        if (args.Message.Length > 0)
        then Log.Error("Error received: {0:l}", args.Message)

    let resetWebSocket(token: string) : unit =
        Log.Information("Websocket - Resetting")
        let wsUrl : string = getWebSocketUrl(token)
        let ws : WebSocket = new WebSocket(wsUrl)
        ws.Opened.Add(onOpen)
        ws.Closed.Add(onClose)
        ws.Error.Add(onError)
        ws.DataReceived.Add(onDataReceived)
        ws.MessageReceived.Add(onMessageReceived)
        wsLock.EnterWriteLock()
        try
            wsState.WebSocket <- ws
            wsState.Reset()
        finally wsLock.ExitWriteLock()
        ws.Open()

    let initializeWebSockets(token: string) : unit =
        wsLock.EnterWriteLock()
        try
            Log.Information("Websocket - Connecting...")
            let wsUrl : string = getWebSocketUrl(token)
            let ws: WebSocket = new WebSocket(wsUrl)
            ws.Opened.Add(onOpen)
            ws.Closed.Add(onClose)
            ws.Error.Add(onError)
            ws.DataReceived.Add(onDataReceived)
            ws.MessageReceived.Add(onMessageReceived)
            wsState <- new WebSocketState(ws)                
        finally wsLock.ExitWriteLock()
        wsState.WebSocket.Open()

    let join(symbol: string) : unit =
        let translatedSymbol : string = Config.TranslateContract(symbol)
        if channels.Add(translatedSymbol)
        then
            let mutable mask : uint8 = 0uy
            if useOnTrade then mask <- ClientInline.SetUsesTrade(mask)
            if useOnQuote then mask <- ClientInline.SetUsesQuote(mask)
            if useOnRefresh then mask <- ClientInline.SetUsesRefresh(mask)
            if useOnUA then mask <- ClientInline.SetUsesUA(mask)
            let message : byte[] = Array.zeroCreate<byte> (translatedSymbol.Length + 2)
            message[0] <- 74uy
            message[1] <- mask
            Array.Copy(Encoding.ASCII.GetBytes(translatedSymbol), 0, message, 2, translatedSymbol.Length)
            Log.Information("Websocket - Joining channel: {0:l}", translatedSymbol)
            wsState.WebSocket.Send(message, 0, message.Length)

    let leave(symbol: string) : unit =
        let translatedSymbol : string = Config.TranslateContract(symbol)
        if channels.Remove(translatedSymbol)
        then
            let message : byte[] = Array.zeroCreate<byte> (translatedSymbol.Length + 2)
            message[0] <- 76uy
            Array.Copy(Encoding.ASCII.GetBytes(translatedSymbol), 0, message, 2, translatedSymbol.Length)
            Log.Information("Websocket - Leaving channel: {0:l}", translatedSymbol)
            wsState.WebSocket.Send(message, 0, message.Length)

    do
        Thread.CurrentThread.Priority <- ThreadPriority.Highest // Ensure pulling from the network has priority over worker threads.
        config.Validate()
        Log.Information("useOnTrade: {0}, useOnQuote: {1}, useOnRefresh: {2}, useOnUA: {3}", useOnTrade, useOnQuote, useOnRefresh, useOnUA)
        httpClient.Timeout <- TimeSpan.FromSeconds(5.0)
        httpClient.DefaultRequestHeaders.Add(clientInfoHeaderKey, clientInfoHeaderValue)
        tryReconnect <- fun () ->
            let reconnectFn () : bool =
                Log.Information("Websocket - Reconnecting...")
                if wsState.IsReady then true
                else
                    wsLock.EnterWriteLock()
                    try wsState.IsReconnecting <- true
                    finally wsLock.ExitWriteLock()
                    if (DateTime.Now - TimeSpan.FromDays(5.0)) > (wsState.LastReset)
                    then
                        let _token : string = getToken()
                        resetWebSocket(_token)
                    else
                        try
                            wsState.WebSocket.Open()
                        with _ -> ()
                    false
            ClientInline.DoBackoff(reconnectFn)
        let _token : string = getToken()
        initializeWebSockets(_token)
        
    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
         [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
         [<Optional; DefaultParameterValue(null:Action<Refresh>)>] onRefresh: Action<Refresh>,
         [<Optional; DefaultParameterValue(null:Action<UnusualActivity>)>] onUnusualActivity: Action<UnusualActivity>) =
        Client(onTrade, onQuote, onRefresh, onUnusualActivity, Config.LoadConfig())
        
    interface IOptionsWebSocketClient with
    
        member _.Join() : unit =
            while not(allReady()) do Thread.Sleep(1000)
            let symbolsToAdd : HashSet<string> = new HashSet<string>(config.Symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)

        member _.Join(symbol: string) : unit =
            if not (String.IsNullOrWhiteSpace(symbol))
            then
                while not(allReady()) do Thread.Sleep(1000)
                join(symbol)

        member _.Join(symbols: string[]) : unit =
            while not(allReady()) do Thread.Sleep(1000)
            let symbolsToAdd : HashSet<string> = new HashSet<string>(symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)

        member _.JoinLobby() : unit =
            if (channels.Contains("$FIREHOSE"))
            then Log.Warning("This client has already joined the lobby channel")
            else
                while not (allReady()) do Thread.Sleep(1000)
                join("$FIREHOSE")

        member _.Leave() : unit =
            for channel in channels do leave(channel)

        member _.Leave(symbol: string) : unit =
            if not (String.IsNullOrWhiteSpace(symbol))
            then leave(symbol)

        member _.Leave(symbols: string[]) : unit =
            let matchingChannels : HashSet<string> = new HashSet<string>(symbols)
            matchingChannels.IntersectWith(channels)
            for channel in matchingChannels do leave(channel)

        member _.LeaveLobby() : unit =
            if (channels.Contains("$FIREHOSE"))
            then leave("$FIREHOSE")

        member _.Stop() : unit =
            for channel in channels do leave(channel)
            Thread.Sleep(1000)
            wsLock.EnterWriteLock()
            try wsState.IsReady <- false
            finally wsLock.ExitWriteLock()
            ctSource.Cancel ()
            Log.Information("Websocket - Closing...");
            wsState.WebSocket.Close()
            for thread in threads do thread.Join()
            Log.Information("Stopped")

        member this.GetStats() : ClientStats =
            new ClientStats(Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count, Interlocked.Read(&dataEventCount), Interlocked.Read(&dataTradeCount), Interlocked.Read(&dataQuoteCount), Interlocked.Read(&dataRefreshCount), Interlocked.Read(&dataUnusualActivityCount))

        member this.Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
            Log.Information(messageTemplate, propertyValues)