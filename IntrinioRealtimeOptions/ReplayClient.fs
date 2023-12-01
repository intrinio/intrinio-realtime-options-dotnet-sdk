namespace Intrinio.Realtime.Options

open Intrinio.Realtime.Options
open Intrinio.SDK.Model
open Serilog
open System
open System.Runtime.InteropServices
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Intrinio.Realtime.Options.Config
open Serilog.Core

type ReplayClient(
    [<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>,
    [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>,
    [<Optional; DefaultParameterValue(null:Action<Refresh>)>] onRefresh: Action<Refresh>,
    [<Optional; DefaultParameterValue(null:Action<UnusualActivity>)>] onUnusualActivity: Action<UnusualActivity>,
    config : Config,
    date : DateTime,
    withSimulatedDelay : bool,
    deleteFileWhenDone : bool,
    writeToCsv : bool,
    csvFilePath : string) =
    let empty : byte[] = Array.empty<byte>
    let mutable dataMsgCount : uint64 = 0UL
    let mutable dataEventCount : uint64 = 0UL
    let mutable dataTradeCount : uint64 = 0UL
    let mutable dataQuoteCount : uint64 = 0UL
    let mutable dataRefreshCount : uint64 = 0UL
    let mutable dataUnusualActivityCount : uint64 = 0UL
    let mutable textMsgCount : uint64 = 0UL
    let channels : HashSet<(string)> = new HashSet<(string)>()
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let data : ConcurrentQueue<Tick> = new ConcurrentQueue<Tick>()
    let useOnTrade : bool = not (obj.ReferenceEquals(onTrade,null))
    let useOnQuote : bool = not (obj.ReferenceEquals(onQuote,null))
    let useOnRefresh : bool = not (obj.ReferenceEquals(onRefresh,null))
    let useOnUnusualActivity : bool = not (obj.ReferenceEquals(onUnusualActivity,null))
    let logPrefix : string = String.Format("{0}: ", config.Provider.ToString())
    let csvLock : Object = new Object()
    let lobbyChannelName : string = "$FIREHOSE";
    
    let logMessage(logLevel:LogLevel, messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
        match logLevel with
        | LogLevel.DEBUG -> Log.Debug(logPrefix + messageTemplate, propertyValues)
        | LogLevel.INFORMATION -> Log.Information(logPrefix + messageTemplate, propertyValues)
        | LogLevel.WARNING -> Log.Warning(logPrefix + messageTemplate, propertyValues)
        | LogLevel.ERROR -> Log.Error(logPrefix + messageTemplate, propertyValues)
        | _ -> failwith "LogLevel not specified!"
        
    let parseTimeReceived(bytes: ReadOnlySpan<byte>) : DateTime =
        DateTime.UnixEpoch + TimeSpan.FromTicks((int64)(BitConverter.ToUInt64(bytes) / 100UL));
    
    let parseTrade (bytes: ReadOnlySpan<byte>) : Trade =
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(26 + symbolLength))
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            TotalVolume = BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) else String.Empty
        }
        
    let parseQuote (bytes: ReadOnlySpan<byte>) : Quote =
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(22 + symbolLength))
        {
            Type = enum<QuoteType> (int32 (bytes.Item(0)))
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(23 + symbolLength, conditionLength)) else String.Empty
        }
        
    let parseRefresh (bytes: ReadOnlySpan<byte>) : Refresh =
        raise (NotImplementedException())
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(26 + symbolLength))
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            TotalVolume = BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) else String.Empty
        }
        
    let parseUnusualActivity (bytes: ReadOnlySpan<byte>) : UnusualActivity =
        raise (NotImplementedException())
        let symbolLength : int = int32 (bytes.Item(2))
        let conditionLength : int = int32 (bytes.Item(26 + symbolLength))
        {
            Symbol = Encoding.ASCII.GetString(bytes.Slice(3, symbolLength))
            Price = (float (BitConverter.ToSingle(bytes.Slice(6 + symbolLength, 4))))
            Size = BitConverter.ToUInt32(bytes.Slice(10 + symbolLength, 4))
            Timestamp = DateTime.UnixEpoch + TimeSpan.FromTicks(int64 (BitConverter.ToUInt64(bytes.Slice(14 + symbolLength, 8)) / 100UL))
            TotalVolume = BitConverter.ToUInt32(bytes.Slice(22 + symbolLength, 4))
            SubProvider = enum<SubProvider> (int32 (bytes.Item(3 + symbolLength)))
            MarketCenter = BitConverter.ToChar(bytes.Slice(4 + symbolLength, 2))
            Condition = if (conditionLength > 0) then Encoding.ASCII.GetString(bytes.Slice(27 + symbolLength, conditionLength)) else String.Empty
        }
        
    let writeRowToOpenCsvWithoutLock(row : IEnumerable<string>) : unit =
        let mutable first : bool = true
        use fs : FileStream = new FileStream(csvFilePath, FileMode.Append);
        use tw : TextWriter = new StreamWriter(fs);
        for s : string in row do
            if (not first)
            then
                tw.Write(",");
            else
                first <- false;
            tw.Write($"\"{s}\"");
        tw.WriteLine();
    
    let writeRowToOpenCsvWithLock(row : IEnumerable<string>) : unit =
        lock csvLock (fun () -> writeRowToOpenCsvWithoutLock(row))

    let doubleRoundSecRule612(value : float) : string =
        if (value >= 1.0)
        then
            value.ToString("0.00")
        else
            value.ToString("0.0000");

    let mapTradeToRow(trade : Trade) : IEnumerable<string> =
        let struct(qualifiersItem1, qualifiersItem2, qualifiersItem3, qualifiersItem4) = trade.Qualifiers;
        seq{
            yield MessageType.Trade.ToString();
            yield trade.Contract;
            yield trade.Exhange.ToString();
            yield doubleRoundSecRule612(trade.Price);
            yield trade.Size.ToString();
            yield trade.Timestamp.ToString();
            yield trade.TotalVolume.ToString();
            yield $"{qualifiersItem1}{qualifiersItem2}{qualifiersItem3}{qualifiersItem4}";
            yield doubleRoundSecRule612(trade.AskPriceAtExecution);
            yield doubleRoundSecRule612(trade.BidPriceAtExecution);
            yield doubleRoundSecRule612(trade.UnderlyingPriceAtExecution);
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
        }
        
    let writeTradeToCsv(trade : Trade) : unit =
        writeRowToOpenCsvWithLock(mapTradeToRow(trade))
        
    let mapQuoteToRow(quote : Quote) : IEnumerable<string> =
        seq{
            yield MessageType.Quote.ToString();
            yield quote.Contract;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield quote.Timestamp.ToString();
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield doubleRoundSecRule612(quote.AskPrice);
            yield quote.AskSize.ToString();
            yield doubleRoundSecRule612(quote.BidPrice);
            yield quote.BidSize.ToString();
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
        }
        
    let writeQuoteToCsv(quote : Quote) : unit =
        writeRowToOpenCsvWithLock(mapQuoteToRow(quote))
        
    let mapRefreshToRow(refresh : Refresh) : IEnumerable<string> =
        seq{
            yield MessageType.Refresh.ToString();
            yield refresh.Contract;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield refresh.OpenInterest.ToString();
            yield doubleRoundSecRule612(refresh.OpenPrice);
            yield doubleRoundSecRule612(refresh.ClosePrice);
            yield doubleRoundSecRule612(refresh.HighPrice);
            yield doubleRoundSecRule612(refresh.LowPrice);
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
        }
        
    let writeRefreshToCsv(refresh : Refresh) : unit =
        writeRowToOpenCsvWithLock(mapRefreshToRow(refresh))
        
    let mapUnusualActivityToRow(unusualActivity : UnusualActivity) : IEnumerable<string> =
        seq{
            yield MessageType.UnusualActivity.ToString();
            yield unusualActivity.Contract;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield unusualActivity.Timestamp.ToString();
            yield String.Empty;
            yield String.Empty;
            yield doubleRoundSecRule612(unusualActivity.AskPriceAtExecution);
            yield doubleRoundSecRule612(unusualActivity.BidPriceAtExecution);
            yield doubleRoundSecRule612(unusualActivity.UnderlyingPriceAtExecution);
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield String.Empty;
            yield unusualActivity.UnusualActivityType.ToString();
            yield unusualActivity.Sentiment.ToString();
            yield unusualActivity.TotalValue.ToString();
            yield unusualActivity.TotalSize.ToString();
            yield doubleRoundSecRule612(unusualActivity.AveragePrice);
        }
        
    let writeUnusualActivityToCsv(unusualActivity : UnusualActivity) : unit =
        writeRowToOpenCsvWithLock(mapUnusualActivityToRow(unusualActivity));
        
    let writeHeaderRow() : unit =
        writeRowToOpenCsvWithLock ([|"MessageType"; "Contract"; "Exchange"; "Price"; "Size"; "Timestamp"; "TotalVolume"; "Qualifiers"; "AskPriceAtExecution"; "BidPriceAtExecution"; "UnderlyingPriceAtExecution"; "AskPrice"; "AskSize"; "BidPrice"; "BidSize"; "OpenInterest"; "OpenPrice"; "ClosePrice"; "HighPRice"; "LowPrice"; "UnusualActivityType"; "Sentiment"; "TotalValue"; "TotalSize"; "AveragePrice" |]);
    
    let threadFn () : unit =
        let ct = ctSource.Token
        let mutable datum : Tick = new Tick(DateTime.Now, Option<Trade>.None, Option<Quote>.None, Option<Refresh>.None, Option<UnusualActivity>.None) //initial throw away value
        while not (ct.IsCancellationRequested) do
            try
                if data.TryDequeue(&datum) then
                    match datum.MessageType() with
                    | MessageType.Trade ->
                        if useOnTrade
                        then
                            Interlocked.Increment(&dataTradeCount) |> ignore
                            datum.Trade() |> onTrade.Invoke
                    | MessageType.Quote ->
                        if useOnQuote
                        then
                            Interlocked.Increment(&dataQuoteCount) |> ignore
                            datum.Quote() |> onQuote.Invoke
                    | MessageType.Refresh ->
                        if useOnRefresh
                        then
                            Interlocked.Increment(&dataRefreshCount) |> ignore
                            datum.Refresh() |> onRefresh.Invoke
                    | MessageType.UnusualActivity ->
                        if useOnUnusualActivity
                        then
                            Interlocked.Increment(&dataUnusualActivityCount) |> ignore
                            datum.UnusualActivity() |> onUnusualActivity.Invoke
                    | _ -> logMessage(LogLevel.WARNING, "Received invalid MessageType: {0};", [|datum.MessageType()|])
                else
                    Thread.Sleep(1)
            with
                | :? OperationCanceledException -> ()
                | exn -> logMessage(LogLevel.ERROR, "Error parsing message: {0}; {1}", [|exn.Message, exn.StackTrace|])
                
    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    let replayTickFileWithoutDelay(fullFilePath : string, byteBufferSize : int, ct : CancellationToken) : IEnumerable<Tick> =
        if File.Exists(fullFilePath)
        then            
            seq {
                use fRead : FileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.None)
                   
                if (fRead.CanRead)
                then
                    let mutable readResult : int = fRead.ReadByte() //This is message type
                    while (readResult <> -1) do
                        if not ct.IsCancellationRequested
                        then
                            let eventBuffer : byte[] = Array.zeroCreate byteBufferSize
                            let timeReceivedBuffer: byte[] = Array.zeroCreate 8
                            let eventSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(eventBuffer)
                            let timeReceivedSpanBuffer : ReadOnlySpan<byte> = new ReadOnlySpan<byte>(timeReceivedBuffer)
                            eventBuffer[0] <- (byte) readResult //This is message type
                            eventBuffer[1] <- (byte) (fRead.ReadByte()) //This is message length, including this and the previous byte.
                            let bytesRead : int = fRead.Read(eventBuffer, 2, (System.Convert.ToInt32(eventBuffer[1])-2)) //read the rest of the message
                            let timeBytesRead : int = fRead.Read(timeReceivedBuffer, 0, 8) //get the time received
                            let timeReceived : DateTime = parseTimeReceived(timeReceivedSpanBuffer)
                            
                            match (enum<MessageType> (System.Convert.ToInt32(eventBuffer[0]))) with
                            | MessageType.Trade ->
                                let trade : Trade = parseTrade(eventSpanBuffer);
                                if (channels.Contains (lobbyChannelName) || channels.Contains (trade.Contract))
                                then
                                    if writeToCsv
                                    then
                                        writeTradeToCsv trade;
                                    yield new Tick(timeReceived, Some(trade), Option<Quote>.None, Option<Refresh>.None, Option<UnusualActivity>.None);
                            | MessageType.Quote ->
                                 let quote : Quote = parseQuote(eventSpanBuffer);
                                 if (channels.Contains (lobbyChannelName) || channels.Contains (quote.Contract))
                                 then
                                    if writeToCsv
                                    then
                                        writeQuoteToCsv quote;
                                    yield new Tick(timeReceived, Option<Trade>.None, Some(quote), Option<Refresh>.None, Option<UnusualActivity>.None)
                            | MessageType.Refresh ->
                                 let refresh : Refresh = parseRefresh(eventSpanBuffer);
                                 if (channels.Contains (lobbyChannelName) || channels.Contains (refresh.Contract))
                                 then
                                    if writeToCsv
                                    then
                                        writeRefreshToCsv refresh;
                                    yield new Tick(timeReceived, Option<Trade>.None, Option<Quote>.None, Some(refresh), Option<UnusualActivity>.None)
                            | MessageType.UnusualActivity ->
                                 let unusualActivity : UnusualActivity = parseUnusualActivity(eventSpanBuffer);
                                 if (channels.Contains (lobbyChannelName) || channels.Contains (unusualActivity.Contract))
                                 then
                                    if writeToCsv
                                    then
                                        writeUnusualActivityToCsv unusualActivity;
                                    yield new Tick(timeReceived, Option<Trade>.None, Option<Quote>.None, Option<Refresh>.None, Some(unusualActivity));
                            | _ -> logMessage(LogLevel.ERROR, "Invalid MessageType: {0}", [|eventBuffer[0]|]);

                            //Set up the next iteration
                            readResult <- fRead.ReadByte();
                        else readResult <- -1;
                else
                    raise (FileLoadException("Unable to read replay file."));
            }
        else
            Array.Empty<Tick>()
                
    /// <summary>
    /// The results of this should be streamed and not ToList-ed.
    /// </summary>
    /// <param name="fullFilePath"></param>
    /// <param name="byteBufferSize"></param>
    /// <returns></returns>
    let replayTickFileWithDelay(fullFilePath : string, byteBufferSize : int, ct : CancellationToken) : IEnumerable<Tick> =
        let start : int64 = DateTime.UtcNow.Ticks;
        let mutable offset : int64 = 0L;
        seq {
            for tick : Tick in replayTickFileWithoutDelay(fullFilePath, byteBufferSize, ct) do
                if (offset = 0L)
                then
                    offset <- start - tick.TimeReceived().Ticks
                    
                if not ct.IsCancellationRequested
                then
                    System.Threading.SpinWait.SpinUntil(fun () -> ((tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks));
                    yield tick
        }
        
    let fetchReplayFileDowloadList() : Tuple<string, string>[] =
        raise (new NotImplementedException())
        // let api : Intrinio.SDK.Api.SecurityApi = new Intrinio.SDK.Api.SecurityApi()
        // if not (api.Configuration.ApiKey.ContainsKey("api_key"))
        // then
        //     api.Configuration.ApiKey.Add("api_key", config.ApiKey)
        //     
        // try
        //     let result : OptionReplayFilesResult = api.GetOptionReplayFiles(date)
        //     let decodedUrl : string = result.Url.Replace(@"\u0026", "&")
        // with | :? Exception as e ->
        //          logMessage(LogLevel.ERROR, "Error while fetching {0} file: {1}", [|subProvider.ToString(), e.Message|])
        //          null
    let fetchReplayFile(fileUrl : string, blobName : string) : string =            
        try
            let tempDir : string = System.IO.Path.GetTempPath()
            let fileName : string = Path.Combine(tempDir, blobName)
            
            use outputFile = new System.IO.FileStream(fileName,System.IO.FileMode.Create)
            (
                use httpClient = new HttpClient()
                (
                    httpClient.Timeout <- TimeSpan.FromHours(1)
                    httpClient.BaseAddress <- new Uri(fileUrl)
                    use response : HttpResponseMessage = httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead).Result
                    (
                        use streamToReadFrom : Stream = response.Content.ReadAsStreamAsync().Result
                        (
                            streamToReadFrom.CopyTo outputFile
                        )
                    )
                )
            )
            
            fileName
        with | :? Exception as e ->
                 logMessage(LogLevel.ERROR, "Error while fetching {0} file from {1}; Error: {2}", [|blobName, fileUrl, e.Message|])
                 null
                
    let fillNextTicks(enumerators : IEnumerator<Tick>[], nextTicks : Option<Tick>[]) : unit =
        for i = 0 to (nextTicks.Length-1) do
            if nextTicks.[i].IsNone && enumerators.[i].MoveNext()
            then
                nextTicks.[i] <- Some(enumerators.[i].Current)
    
    let pullNextTick(nextTicks : Option<Tick>[]) : Option<Tick> =
        let mutable pullIndex : int = 0
        let mutable t : DateTime = DateTime.MaxValue
        for i = 0 to (nextTicks.Length-1) do
            if nextTicks.[i].IsSome && nextTicks.[i].Value.TimeReceived() < t
            then
                pullIndex <- i
                t <- nextTicks.[i].Value.TimeReceived()
        
        let pulledTick = nextTicks.[pullIndex] 
        nextTicks.[pullIndex] <- Option<Tick>.None
        pulledTick
        
    let hasAnyValue(nextTicks : Option<Tick>[]) : bool =
        let mutable hasValue : bool = false
        for i = 0 to (nextTicks.Length-1) do
            if nextTicks.[i].IsSome
            then
                hasValue <- true
        hasValue
        
    let replayFileGroupWithoutDelay(tickGroup : IEnumerable<Tick>[], ct : CancellationToken) : IEnumerable<Tick> =
        seq{
            let nextTicks : Option<Tick>[] = Array.zeroCreate(tickGroup.Length)
            let enumerators : IEnumerator<Tick>[] = Array.zeroCreate(tickGroup.Length)
            for i = 0 to (tickGroup.Length-1) do
                enumerators.[i] <- tickGroup.[i].GetEnumerator()
            
            fillNextTicks(enumerators, nextTicks)
            while hasAnyValue(nextTicks) do
                let nextTick : Option<Tick> = pullNextTick(nextTicks)
                if nextTick.IsSome
                then yield nextTick.Value
                fillNextTicks(enumerators, nextTicks)
        }        
    
    let replayFileGroupWithDelay(tickGroup : IEnumerable<Tick>[], ct : CancellationToken) : IEnumerable<Tick> =
        seq {
            let start : int64 = DateTime.UtcNow.Ticks;
            let mutable offset : int64 = 0L;
            for tick : Tick in replayFileGroupWithoutDelay(tickGroup, ct) do
                if (offset = 0L)
                then
                    offset <- start - tick.TimeReceived().Ticks
                    
                if not ct.IsCancellationRequested
                then
                    System.Threading.SpinWait.SpinUntil(fun () -> ((tick.TimeReceived().Ticks + offset) <= DateTime.UtcNow.Ticks));
                    yield tick
        }
                
    let replayThreadFn () : unit =
        let ct : CancellationToken = ctSource.Token
        let replayFileUrls : Tuple<string, string>[] = fetchReplayFileDowloadList()
        let replayFilePaths : string[] = Array.zeroCreate(replayFileUrls.Length)
        let allTicks : IEnumerable<Tick>[] = Array.zeroCreate(replayFileUrls.Length)        
        
        try 
            for i = 0 to replayFileUrls.Length-1 do
                logMessage(LogLevel.INFORMATION, "Downloading Replay file for {0} on {1}...", [|replayFileUrls.[i].ToString(); date.Date.ToString()|])
                replayFilePaths.[i] <- fetchReplayFile(replayFileUrls.[i])
                logMessage(LogLevel.INFORMATION, "Downloaded Replay file to: {0}", [|replayFilePaths.[i]|])
                allTicks.[i] <- replayTickFileWithoutDelay(replayFilePaths.[i], 100, ct)
            
            let aggregatedTicks : IEnumerable<Tick> =
                if withSimulatedDelay
                then replayFileGroupWithDelay(allTicks, ct)
                else replayFileGroupWithoutDelay(allTicks, ct)
            
            for tick : Tick in aggregatedTicks do
                if not ct.IsCancellationRequested
                then
                    Interlocked.Increment(&dataEventCount) |> ignore
                    Interlocked.Increment(&dataMsgCount) |> ignore
                    data.Enqueue(tick)
            
        with | :? Exception as e -> logMessage(LogLevel.ERROR, "Error while replaying file: {0}", [|e.Message|])
        
        if deleteFileWhenDone
        then
            for deleteFilePath in replayFilePaths do
                if File.Exists deleteFilePath
                then
                    logMessage(LogLevel.INFORMATION, "Deleting Replay file: {0}", [|deleteFilePath|])
                    File.Delete(deleteFilePath)

    let threads : Thread[] = Array.init config.NumThreads (fun _ -> new Thread(new ThreadStart(threadFn)))
    
    let replayThread : Thread = new Thread(new ThreadStart(replayThreadFn))

    let join(symbol: string) : unit =
        if channels.Add((symbol))
        then
            logMessage(LogLevel.INFORMATION, "Websocket - Joining channel: {0} (trades only = {1})", [|symbol|])

    let leave(symbol: string) : unit =
        if channels.Remove((symbol))
        then 
            logMessage(LogLevel.INFORMATION, "Websocket - Leaving channel: {0} (trades only = {1})", [|symbol|])
    
    do
        config.Validate()
        for thread : Thread in threads do
            thread.Start()
        if writeToCsv
        then
            writeHeaderRow();
        replayThread.Start()
        
    new ([<Optional; DefaultParameterValue(null:Action<Trade>)>] onTrade: Action<Trade>, [<Optional; DefaultParameterValue(null:Action<Quote>)>] onQuote : Action<Quote>, [<Optional; DefaultParameterValue(null:Action<Refresh>)>] onRefresh: Action<Refresh>, [<Optional; DefaultParameterValue(null:Action<UnusualActivity>)>] onUnusualActivity: Action<UnusualActivity>, date : DateTime, withSimulatedDelay : bool, deleteFileWhenDone : bool, writeToCsv : bool, csvFilePath : string) =
        ReplayClient(onTrade, onQuote, onRefresh, onUnusualActivity, LoadConfig(), date, withSimulatedDelay, deleteFileWhenDone, writeToCsv, csvFilePath)

    interface IOptionsWebSocketClient with
        member _.Join() : unit =
            let symbolsToAdd : HashSet<string> = new HashSet<string>(config.Symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)

        member _.Join(symbol: string) : unit =
            if not (String.IsNullOrWhiteSpace(symbol))
            then
                join(symbol)

        member _.Join(symbols: string[]) : unit =
            let symbolsToAdd : HashSet<string> = new HashSet<string>(symbols)
            symbolsToAdd.ExceptWith(channels)
            for symbol in symbolsToAdd do join(symbol)

        member _.JoinLobby() : unit =
            if (channels.Contains(lobbyChannelName))
            then Log.Warning("This client has already joined the lobby channel")
            else
                join(lobbyChannelName)

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
            if (channels.Contains(lobbyChannelName))
            then leave(lobbyChannelName)
            
        member this.Stop() : unit =
            for channel in channels do leave(channel)
            ctSource.Cancel ()
            logMessage(LogLevel.INFORMATION, "Websocket - Closing...", [||])
            for thread in threads do thread.Join()
            replayThread.Join()
            logMessage(LogLevel.INFORMATION, "Stopped", [||])
            
        member this.GetStats() : ClientStats =
            new ClientStats(Interlocked.Read(&dataMsgCount), Interlocked.Read(&textMsgCount), data.Count, Interlocked.Read(&dataEventCount), Interlocked.Read(&dataTradeCount), Interlocked.Read(&dataQuoteCount), Interlocked.Read(&dataRefreshCount), Interlocked.Read(&dataUnusualActivityCount))

        [<MessageTemplateFormatMethod("messageTemplate")>]
        member this.Log(messageTemplate:string, [<ParamArray>] propertyValues:obj[]) : unit =
            Log.Information(messageTemplate, propertyValues)