namespace Intrinio

open Intrinio
open Serilog
open System
open System.Runtime.InteropServices
open System.Net.Http
open System.Text
open System.Text.Json
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Config

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
    [<Optional; DefaultParameterValue(null:Action<UnusualActivity>)>] onUnusualActivity: Action<UnusualActivity>) =
    let [<Literal>] heartbeatMessage : string = "{\"topic\":\"phoenix\",\"event\":\"heartbeat\",\"payload\":{},\"ref\":null}"
    let [<Literal>] heartbeatResponse : string = "{\"topic\":\"phoenix\",\"ref\":null,\"payload\":{\"status\":\"ok\",\"response\":{}},\"event\":\"phx_reply\"}"
    let [<Literal>] errorResponse : string = "\"status\":\"error\""
    let selfHealBackoffs : int[] = [| 10_000; 30_000; 60_000; 300_000; 600_000 |]
    let maxSymbolSize : int = 20
    let tradeMessageSize : int = 70 //59 used + 11 pad
    let quoteMessageSize : int = 50 //46 used + 4 pad
    let refreshMessageSize : int = 50 //42 used + 8 pad
    let unusualActivityMessageSize : int = 72 //60 used + 12 pad
    let config = LoadConfig()
    let tLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let wsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let mutable token : (string * DateTime) = (null, DateTime.Now)
    let mutable wsState: WebSocketState = new WebSocketState(null)
    let mutable dataMsgCount : uint64 = 0UL
    let mutable textMsgCount : uint64 = 0UL
    let channels : HashSet<string> = new HashSet<string>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : BlockingCollection<byte[]> = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>())
    let mutable tryReconnect : (unit -> unit) = fun () -> ()
    let httpClient : HttpClient = new HttpClient()
    
    let getPriceTypeValue (priceType: PriceType) : uint64 =
        match priceType with
        | PriceType.One                 -> 1UL
        | PriceType.Ten                 -> 10UL
        | PriceType.Hundred             -> 100UL
        | PriceType.Thousand            -> 1_000UL
        | PriceType.TenThousand         -> 10_000UL
        | PriceType.HundredThousand     -> 100_000UL
        | PriceType.Million             -> 1_000_000UL
        | PriceType.TenMillion          -> 10_000_000UL
        | PriceType.HundredMillion      -> 100_000_000UL
        | PriceType.Billion             -> 1_000_000_000UL
        | PriceType.FiveHundredTwelve   -> 512UL
        | PriceType.Zero                -> 0UL
        | _                             -> failwith ("Invalid PriceType! PriceType: " + (int32 priceType).ToString())
                
    let getScaledValueUInt64 (value: uint64, scaler: PriceType) : double =
        (double value) / (double (getPriceTypeValue(scaler)))
        
    let getScaledValueInt32 (value: int, scaler: PriceType) : double =
        (double value) / (double (getPriceTypeValue(scaler)))
        
    let getSecondsSinceUnixEpoch (timestamp : UInt64) : double =
        (double timestamp) / 1_000_000_000.0
    
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
        | Provider.OPRA
        | Provider.OPRA_FIREHOSE -> "https://realtime-options.intrinio.com/auth?api_key=" + config.ApiKey
        | Provider.MANUAL
        | Provider.MANUAL_FIREHOSE -> "http://" + config.IPAddress + "/auth?api_key=" + config.ApiKey
        | _ -> failwith "Provider not specified!"

    let getWebSocketUrl (token: string) : string =
        match config.Provider with
          | Provider.OPRA
          | Provider.OPRA_FIREHOSE -> "wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token=" + token
          | Provider.MANUAL 
          | Provider.MANUAL_FIREHOSE -> "ws://" + config.IPAddress + "/socket/websocket?vsn=1.0.0&token=" + token
          | _ -> failwith "Provider not specified!"
    
    let parseTrade (bytes: ReadOnlySpan<byte>) : Trade =
        let priceType = enum<PriceType> (int32 (bytes.Item(25))) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4)
        let underlyingPriceType = enum<PriceType> (int32 (bytes.Item(26))) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1)
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(0, maxSymbolSize))
            //EventType positionally goes here and is 1 byte = // maxSymbolSize - 1 + 1
            Price = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(21, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1)
            //PriceType positionally goes here
            //UnderlyingPriceType positionally goes here
            Size = BitConverter.ToUInt32(bytes.Slice(27, 4)) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1) + UnderlyingPriceTypeSize(1)
            Timestamp = getSecondsSinceUnixEpoch(BitConverter.ToUInt64(bytes.Slice(31, 8))) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1) + UnderlyingPriceTypeSize(1) + SizeSize(4)
            TotalVolume = BitConverter.ToUInt64(bytes.Slice(39, 8)) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1) + UnderlyingPriceTypeSize(1) + SizeSize(4) + TimestampSize(8)
            AskPriceAtExecution = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(47, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1) + UnderlyingPriceTypeSize(1) + SizeSize(4) + TimestampSize(8) + TotalVolumeSize(8)
            BidPriceAtExecution = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(51, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1) + UnderlyingPriceTypeSize(1) + SizeSize(4) + TimestampSize(8) + TotalVolumeSize(8) + AskPriceAtExecutionSize(4)
            UnderlyingPriceAtExecution = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(55, 4)), underlyingPriceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceSize(4) + PriceTypeSize(1) + UnderlyingPriceTypeSize(1) + SizeSize(4) + TimestampSize(8) + TotalVolumeSize(8) + AskPriceAtExecutionSize(4) +  + BidPriceAtExecutionSize(4)
        }

    let parseQuote (bytes: ReadOnlySpan<byte>) : Quote =
        let priceType = enum<PriceType> (int32 (bytes.Item(21))) // maxSymbolSize - 1 + 1 + EventTypeSize(1)
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(0, maxSymbolSize))
            //EventType positionally goes here and is 1 byte = // maxSymbolSize - 1 + 1
            //PriceType positionally goes here
            AskPrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(22, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1)
            AskSize = BitConverter.ToUInt32(bytes.Slice(26, 4)) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + AskPriceSize(4)
            BidPrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(30, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + AskPriceSize(4) + AskSizeSize(4)
            BidSize = BitConverter.ToUInt32(bytes.Slice(34, 4)) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + AskPriceSize(4) + AskSizeSize(4) + BidPriceSize(4)
            Timestamp = getSecondsSinceUnixEpoch(BitConverter.ToUInt64(bytes.Slice(38, 8))) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + AskPriceSize(4) + AskSizeSize(4) + BidPriceSize(4) + BidSizeSize(4)
        }

    let parseRefresh (bytes: ReadOnlySpan<byte>) : Refresh =
        let priceType = enum<PriceType> (int32 (bytes.Item(21))) // maxSymbolSize - 1 + 1 + EventTypeSize(1)
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(0, maxSymbolSize))
            //Event Type positionally goes here and is 1 byte = // maxSymbolSize - 1 + 1
            //price type positionally goes here
            OpenInterest = BitConverter.ToUInt32(bytes.Slice(22, 4)) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1)
            OpenPrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(26, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + OpenInterestSize(4)
            ClosePrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(30, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + OpenInterestSize(4) + OpenPriceSize(4)
            HighPrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(34, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + OpenInterestSize(4) + OpenPriceSize(4) + ClosePriceSize(4)
            LowPrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(38, 4)), priceType) // maxSymbolSize - 1 + 1 + EventTypeSize(1) + PriceTypeSize(1) + OpenInterestSize(4) + OpenPriceSize(4) + ClosePriceSize(4) + HighPriceSize(4)
        }

    let parseUnusualActivity (bytes: ReadOnlySpan<byte>) : UnusualActivity =
        let priceType = enum<PriceType> (int32 (bytes.Item(22))) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1)
        let underlyingPriceType = enum<PriceType> (int32 (bytes.Item(23))) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1)
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(0, maxSymbolSize))
            Type = enum<UAType> (int32 (bytes.Item(20))) // maxSymbolSize - 1 + 1
            Sentiment = enum<UASentiment> (int32 (bytes.Item(21))) // maxSymbolSize - 1 + 1 + TypeSize(1)
            //priceType positionally here
            //underlyingPriceType positionally here
            TotalValue = getScaledValueUInt64(BitConverter.ToUInt64(bytes.Slice(24, 8)), priceType) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1)
            TotalSize = BitConverter.ToUInt32(bytes.Slice(32, 4)) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1) + TotalValueSize(8)
            AveragePrice = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(36, 4)), priceType) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1) + TotalValueSize(8) + TotalSizeSize(4)
            AskAtExecution = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(40, 4)), priceType) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1) + TotalValueSize(8) + TotalSizeSize(4) + AveragePriceSize(4)
            BidAtExecution = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(44, 4)), priceType) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1) + TotalValueSize(8) + TotalSizeSize(4) + AveragePriceSize(4) + AskAtExecutionSize(4)
            UnderlyingPriceAtExecution = getScaledValueInt32(BitConverter.ToInt32(bytes.Slice(48, 4)), underlyingPriceType) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1) + TotalValueSize(8) + TotalSizeSize(4) + AveragePriceSize(4) + AskAtExecutionSize(4) + BidAtExecutionSize(4)
            Timestamp = getSecondsSinceUnixEpoch(BitConverter.ToUInt64(bytes.Slice(52, 8))) // maxSymbolSize - 1 + 1 + TypeSize(1) + SentimentSize(1) + PriceTypeSize(1) + UnderlyingPriceType(1) + TotalValueSize(8) + TotalSizeSize(4) + AveragePriceSize(4) + AskAtExecutionSize(4) + BidAtExecutionSize(4) + PriceAtExecutionSize(4)
        }

    let parseSocketMessage (bytes: byte[], startIndex: byref<int>) : unit =
        let msgType : int = int32 bytes.[startIndex + maxSymbolSize] //This works because it's startIndex + maxSymbolSize - 1 (zero based) + 1 (size of type)
        if (msgType = 1) //using if-else vs switch for hotpathing
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, quoteMessageSize)
            let quote: Quote = parseQuote(chunk)
            startIndex <- startIndex + quoteMessageSize
            if useOnQuote then onQuote.Invoke(quote)
        elif (msgType = 0)
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, tradeMessageSize)
            let trade: Trade = parseTrade(chunk)
            startIndex <- startIndex + tradeMessageSize
            if useOnTrade then onTrade.Invoke(trade)
        elif (msgType > 2)
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, unusualActivityMessageSize)
            let ua: UnusualActivity = parseUnusualActivity(chunk)
            startIndex <- startIndex + unusualActivityMessageSize
            if useOnUA then onUnusualActivity.Invoke(ua)
        elif (msgType = 2)
        then
            let chunk: ReadOnlySpan<byte> = new ReadOnlySpan<byte>(bytes, startIndex, refreshMessageSize)
            let refresh = parseRefresh(chunk)
            startIndex <- startIndex + refreshMessageSize
            if useOnRefresh then onRefresh.Invoke(refresh)
        else Log.Warning("Invalid MessageType: {0}", msgType)

    let heartbeatFn () =
        let ct = ctSource.Token
        Log.Debug("Starting heartbeat")
        while not(ct.IsCancellationRequested) do
            Thread.Sleep(20000) //send heartbeat every 20 sec
            Log.Debug("Sending heartbeat")
            wsLock.EnterReadLock()
            try
                if not(ct.IsCancellationRequested) && wsState.IsReady
                then wsState.WebSocket.Send(heartbeatMessage)
            finally wsLock.ExitReadLock()

    let heartbeat : Thread = new Thread(new ThreadStart(heartbeatFn))

    let threadFn () : unit =
        let ct = ctSource.Token
        let mutable datum : byte[] = Array.empty<byte>
        while not (ct.IsCancellationRequested) do
            try
                if data.TryTake(&datum,1000) then
                    // These are grouped (many) messages.
                    // The first byte tells us how many there are.
                    // From there, check the type at index 21 to know how many bytes each message has.
                    let cnt = datum.[0] |> int
                    let mutable startIndex = 1
                    for _ in 1 .. cnt do
                        parseSocketMessage(datum, &startIndex)
            with :? OperationCanceledException -> ()

    let threads : Thread[] = Array.init config.NumThreads (fun _ -> new Thread(new ThreadStart(threadFn)))

    let doBackoff(fn: unit -> bool) : unit =
        let mutable i : int = 0
        let mutable backoff : int = selfHealBackoffs.[i]
        let mutable success : bool = fn()
        while not success do
            Thread.Sleep(backoff)
            i <- Math.Min(i + 1, selfHealBackoffs.Length - 1)
            backoff <- selfHealBackoffs.[i]
            success <- fn()

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
                try doBackoff(trySetToken)
                finally tLock.ExitWriteLock()
                fst token
        finally tLock.ExitUpgradeableReadLock()

    let onOpen (_ : EventArgs) : unit =
        Log.Information("Websocket - Connected")
        wsLock.EnterWriteLock()
        try
            wsState.IsReady <- true
            wsState.IsReconnecting <- false
            if not heartbeat.IsAlive
            then heartbeat.Start()
            for thread in threads do
                if not thread.IsAlive
                then thread.Start()
        finally wsLock.ExitWriteLock()
        if channels.Count > 0
        then
            channels |> Seq.iter (fun (symbol: string) ->
                let sb : StringBuilder = new StringBuilder()
                if useOnTrade then sb.Append(",\"trade_data\":\"true\"") |> ignore
                if useOnQuote then sb.Append(",\"quote_data\":\"true\"") |> ignore
                if useOnRefresh then sb.Append(",\"refresh_data\":\"true\"") |> ignore
                if useOnUA then sb.Append(",\"unusual_activity_data\":\"true\"") |> ignore
                let subscriptionSelection : string = sb.ToString()
                let message : string = "{\"topic\":\"options:" + symbol + "\",\"event\":\"phx_join\"" + subscriptionSelection + ",\"payload\":{},\"ref\":null}"
                Log.Information("Websocket - Joining channel: {0:l} ({1:l})", symbol, subscriptionSelection.TrimStart(','))
                wsState.WebSocket.Send(message) )

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
        Log.Debug("Websocket - Data received")
        Interlocked.Increment(&dataMsgCount) |> ignore
        data.Add(args.Data)

    let onMessageReceived (args : MessageReceivedEventArgs) : unit =
        Log.Debug("Websocket - Message received")
        Interlocked.Increment(&textMsgCount) |> ignore
        if args.Message = heartbeatResponse then Log.Debug("Heartbeat response received")
        elif args.Message.Contains(errorResponse)
        then
            let replyDoc : JsonDocument = JsonDocument.Parse(args.Message)
            let errorMessage : string = 
                replyDoc.RootElement
                    .GetProperty("payload")
                    .GetProperty("response")
                    .GetString()
            Log.Error("Error received: {0:l}", errorMessage)

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
        if (((symbol = "lobby") || (symbol = "lobby_trades_only")) &&
            ((config.Provider <> Provider.MANUAL_FIREHOSE) && (config.Provider <> Provider.OPRA_FIREHOSE)))
        then Log.Warning("Only 'FIREHOSE' providers may join the lobby channel")
        elif (((symbol <> "lobby") && (symbol <> "lobby_trades_only")) &&
            ((config.Provider = Provider.MANUAL_FIREHOSE) || (config.Provider = Provider.OPRA_FIREHOSE)))
        then Log.Warning("'FIREHOSE' providers may only join the lobby channel")
        else
            if channels.Add(symbol)
            then 
                let sb : StringBuilder = new StringBuilder()
                if useOnTrade then sb.Append(",\"trade_data\":\"true\"") |> ignore
                if useOnQuote then sb.Append(",\"quote_data\":\"true\"") |> ignore
                if useOnRefresh then sb.Append(",\"refresh_data\":\"true\"") |> ignore
                if useOnUA then sb.Append(",\"unusual_activity_data\":\"true\"") |> ignore
                let subscriptionSelection : string = sb.ToString()
                let message : string = "{\"topic\":\"options:" + symbol + "\",\"event\":\"phx_join\"" + subscriptionSelection + ",\"payload\":{},\"ref\":null}"
                Log.Information("Websocket - Joining channel: {0:l} ({1:l})", symbol, subscriptionSelection.TrimStart(','))
                try wsState.WebSocket.Send(message)
                with _ -> channels.Remove(symbol) |> ignore

    let leave(symbol: string) : unit =
        if channels.Remove(symbol)
        then 
            let message : string = "{\"topic\":\"options:" + symbol + "\",\"event\":\"phx_leave\",\"payload\":{},\"ref\":null}"
            Log.Information("Websocket - Leaving channel: {0:l}", symbol)
            try wsState.WebSocket.Send(message)
            with _ -> ()

    do
        httpClient.Timeout <- TimeSpan.FromSeconds(5.0)
        httpClient.DefaultRequestHeaders.Add("Client-Information", "IntrinioRealtimeOptionsDotNetSDKv2.0")
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
            doBackoff(reconnectFn)
        let _token : string = getToken()
        initializeWebSockets(_token)

    member _.Join() : unit =
        if ((config.Provider = Provider.MANUAL_FIREHOSE) || (config.Provider = Provider.OPRA_FIREHOSE))
        then Log.Warning("'FIREHOSE' providers must join the lobby channel. Use the function 'JoinLobby' instead.")
        else
            while not(allReady()) do Thread.Sleep(1000)
            let symbolsToAdd : HashSet<string> = new HashSet<string>(config.Symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)

    member _.Join(symbol: string) : unit =
        if ((config.Provider = Provider.MANUAL_FIREHOSE) || (config.Provider = Provider.OPRA_FIREHOSE))
        then Log.Warning("'FIREHOSE' providers must join the lobby channel. Use the function 'JoinLobby' instead.")
        else
            if not (String.IsNullOrWhiteSpace(symbol))
            then
                while not(allReady()) do Thread.Sleep(1000)
                if not (channels.Contains(symbol))
                then join(symbol)

    member _.Join(symbols: string[]) : unit =
        if ((config.Provider = Provider.MANUAL_FIREHOSE) || (config.Provider = Provider.OPRA_FIREHOSE))
        then Log.Warning("'FIREHOSE' providers must join the lobby channel. Use the function 'JoinLobby' instead.")
        else
            while not(allReady()) do Thread.Sleep(1000)
            let symbolsToAdd : HashSet<string> = new HashSet<string>(symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)

    member _.JoinLobby() : unit =
        if ((config.Provider <> Provider.MANUAL_FIREHOSE) && (config.Provider <> Provider.OPRA_FIREHOSE))
        then Log.Warning("Only 'FIREHOSE' providers may join the lobby channel")
        elif (channels.Contains("lobby"))
        then Log.Warning("This client has already joined the lobby channel")
        else
            while not (allReady()) do Thread.Sleep(1000)
            join("lobby")

    member _.Leave() : unit =
        for channel in channels do leave(channel)

    member _.Leave(symbol: string) : unit =
        if not (String.IsNullOrWhiteSpace(symbol))
        then if channels.Contains(symbol) then leave(symbol)

    member _.Leave(symbols: string[]) : unit =
        let matchingChannels : HashSet<string> = new HashSet<string>(symbols)
        matchingChannels.IntersectWith(channels)
        for channel in matchingChannels do leave(channel)

    member _.LeaveLobby() : unit =
        if (channels.Contains("lobby"))
        then leave("lobby")

    member _.Stop() : unit =
        for channel in channels do leave(channel)
        Thread.Sleep(1000)
        wsLock.EnterWriteLock()
        try wsState.IsReady <- false
        finally wsLock.ExitWriteLock()
        ctSource.Cancel ()
        Log.Information("Websocket - Closing...");
        wsState.WebSocket.Close()
        heartbeat.Join()
        for thread in threads do thread.Join()
        Log.Information("Stopped")

    member _.GetStats() : (uint64 * uint64 * int) = (Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count)

    static member Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) = Log.Information(messageTemplate, propertyValues)


