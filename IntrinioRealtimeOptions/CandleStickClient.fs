namespace Intrinio

open Intrinio
open Serilog
open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Config
open FSharp.NativeInterop
open System.Runtime.CompilerServices

module private CandleStickClientInline =

    [<SkipLocalsInit>]
    let inline private stackalloc<'a when 'a: unmanaged> (length: int): Span<'a> =
        let p = NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr
        Span<'a>(p, length)
     
type internal ContractBucket =
    val mutable TradeCandleStick : TradeCandleStick option
    val mutable AskCandleStick : QuoteCandleStick option
    val mutable BidCandleStick : QuoteCandleStick option
    val Locker : ReaderWriterLockSlim
    
    new (tradeCandleStick : TradeCandleStick option, askCandleStick : QuoteCandleStick option, bidCandleStick : QuoteCandleStick option) =
        {
            TradeCandleStick = tradeCandleStick
            AskCandleStick = askCandleStick
            BidCandleStick = bidCandleStick
            Locker = new ReaderWriterLockSlim()
        }
        
type CandleStickClient(
    [<Optional; DefaultParameterValue(null:Action<TradeCandleStick>)>] onTradeCandleStick : Action<TradeCandleStick>,
    [<Optional; DefaultParameterValue(null:Action<QuoteCandleStick>)>] onQuoteCandleStick : Action<QuoteCandleStick>,
    CandleStickSeconds : float) =
    
    let ctSource : CancellationTokenSource = new CancellationTokenSource()
    let useOnTradeCandleStick : bool = not (obj.ReferenceEquals(onTradeCandleStick,null))
    let useOnQuoteCandleStick : bool = not (obj.ReferenceEquals(onQuoteCandleStick,null))
    let initialDictionarySize : int = 3_601_579 //a close prime number greater than 2x the max expected size.  There are usually around 1.5m option contracts.
    let contractsLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    let contracts : Dictionary<string, ContractBucket> = new Dictionary<string, ContractBucket>(initialDictionarySize)
    
    let getSlot(key : string) : ContractBucket =
        match contracts.TryGetValue(key) with
        | (true, value) -> value
        | (false, _) ->
            contractsLock.EnterWriteLock()
            try
                match contracts.TryGetValue(key) with
                | (true, value) -> value
                | (false, _) ->
                    let bucket : ContractBucket = new ContractBucket(Option.None, Option.None, Option.None)
                    contracts.Add(key, bucket)
                    bucket
            finally contractsLock.ExitWriteLock()
    
    let onAsk(quote: Quote, bucket: ContractBucket) : unit =
        if (bucket.AskCandleStick.IsSome && not (Double.IsNaN(quote.AskPrice)))
        then
            if (bucket.AskCandleStick.Value.OpenTimestamp + CandleStickSeconds < quote.Timestamp)
            then
                onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                bucket.AskCandleStick <- Some(new QuoteCandleStick(quote.Contract, quote.AskPrice, QuoteType.Ask, quote.Timestamp, quote.Timestamp + CandleStickSeconds))
            elif (bucket.AskCandleStick.Value.OpenTimestamp <= quote.Timestamp)
            then
                bucket.AskCandleStick.Value.Update(quote.AskPrice)
            //else This is a late event.  We already shipped the candle, so ignore
        elif (bucket.AskCandleStick.IsNone && not (Double.IsNaN(quote.AskPrice)))
        then
            bucket.AskCandleStick <- Some(new QuoteCandleStick(quote.Contract, quote.AskPrice, QuoteType.Ask, quote.Timestamp, quote.Timestamp + CandleStickSeconds))
    
    let onBid(quote: Quote, bucket : ContractBucket) : unit =        
        if (bucket.BidCandleStick.IsSome && not (Double.IsNaN(quote.BidPrice)))
        then
            if (bucket.BidCandleStick.Value.OpenTimestamp + CandleStickSeconds < quote.Timestamp)
            then
                onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                bucket.BidCandleStick <- Some(new QuoteCandleStick(quote.Contract, quote.BidPrice, QuoteType.Bid, quote.Timestamp, quote.Timestamp + CandleStickSeconds))
            elif (bucket.BidCandleStick.Value.OpenTimestamp <= quote.Timestamp)
            then
                bucket.BidCandleStick.Value.Update(quote.BidPrice)
            //else This is a late event.  We already shipped the candle, so ignore
        elif (bucket.BidCandleStick.IsNone && not (Double.IsNaN(quote.BidPrice)))
        then
            bucket.BidCandleStick <- Some(new QuoteCandleStick(quote.Contract, quote.BidPrice, QuoteType.Bid, quote.Timestamp, quote.Timestamp + CandleStickSeconds))
            
    let getCurrentTimestamp() : float =
        (DateTime.UtcNow - DateTime.UnixEpoch.ToUniversalTime()).TotalSeconds
            
    let flushFn () : unit =
        Log.Information("Starting candlestick expiration watcher...")
        let ct = ctSource.Token
        while not (ct.IsCancellationRequested) do
            try                
                contractsLock.EnterReadLock()
                let mutable keys : string list = []
                for key in contracts.Keys do
                    keys <- key::keys
                contractsLock.ExitReadLock()
                for key in keys do
                    let bucket : ContractBucket = getSlot(key)
                    let currentTime : float = getCurrentTimestamp()
                    bucket.Locker.EnterWriteLock()
                    try
                        if (useOnQuoteCandleStick && bucket.TradeCandleStick.IsSome && (bucket.TradeCandleStick.Value.OpenTimestamp + CandleStickSeconds < currentTime))
                        then
                            onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                            bucket.TradeCandleStick <- Option.None
                        if (useOnQuoteCandleStick && bucket.AskCandleStick.IsSome && (bucket.AskCandleStick.Value.OpenTimestamp + CandleStickSeconds < currentTime))
                        then
                            onQuoteCandleStick.Invoke(bucket.AskCandleStick.Value)
                            bucket.AskCandleStick <- Option.None
                        if (useOnQuoteCandleStick && bucket.BidCandleStick.IsSome && (bucket.BidCandleStick.Value.OpenTimestamp + CandleStickSeconds < currentTime))
                        then
                            onQuoteCandleStick.Invoke(bucket.BidCandleStick.Value)
                            bucket.BidCandleStick <- Option.None
                    finally
                        bucket.Locker.ExitWriteLock()
                if not (ct.IsCancellationRequested)
                then
                    Thread.Sleep 1000    
            with :? OperationCanceledException -> ()
        Log.Information("Stopping candlestick expiration watcher...")
            
    let flushThread : Thread = new Thread(new ThreadStart(flushFn))
        
    member _.OnTrade(trade: Trade) : unit =
        try
            if useOnTradeCandleStick
            then
                let bucket : ContractBucket = getSlot(trade.Contract)
                try          
                    bucket.Locker.EnterWriteLock()
                    if (bucket.TradeCandleStick.IsSome)
                    then
                        if (bucket.TradeCandleStick.Value.OpenTimestamp + CandleStickSeconds < trade.Timestamp)
                        then
                            onTradeCandleStick.Invoke(bucket.TradeCandleStick.Value)
                            bucket.TradeCandleStick <- Some(new TradeCandleStick(trade.Contract, trade.Size, trade.Price, trade.Timestamp, trade.Timestamp + CandleStickSeconds))
                        elif (bucket.TradeCandleStick.Value.OpenTimestamp <= trade.Timestamp)
                        then
                            bucket.TradeCandleStick.Value.Update(trade.Size, trade.Price)                    
                        //else This is a late trade.  We already shipped the candle, so ignore
                    else
                        bucket.TradeCandleStick <- Some(new TradeCandleStick(trade.Contract, trade.Size, trade.Price, trade.Timestamp, trade.Timestamp + CandleStickSeconds))
                finally bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling trade in CandleStick Client: {0}", ex.Message)
        
    member _.OnQuote(quote: Quote) : unit =
        try
            if useOnQuoteCandleStick
            then
                let bucket : ContractBucket = getSlot(quote.Contract)
                try          
                    bucket.Locker.EnterWriteLock()            
                    onAsk(quote, bucket)
                    onBid(quote, bucket)
                finally bucket.Locker.ExitWriteLock()
        with ex ->
            Log.Warning("Error on handling trade in CandleStick Client: {0}", ex.Message)
            
    member _.Start() : unit =
        if not flushThread.IsAlive
                then flushThread.Start()
            
    member _.Stop() : unit = 
        ctSource.Cancel()