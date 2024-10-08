namespace Intrinio.Realtime.Options

open System
open System.Globalization
open System.Text
open FSharp.NativeInterop
open FSharp.Core.LanguagePrimitives
open System.Runtime.CompilerServices

type Provider =
    | NONE = 0
    | OPRA = 1
    | MANUAL = 2

module private TypesInline =
    
    [<SkipLocalsInit>]
    let inline internal stackalloc<'a when 'a: unmanaged> (length: int): Span<'a> =
        let p = NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr
        Span<'a>(p, length)
        
    let private priceTypeDivisorTable : double[] =
        [|
            1.0
            10.0
            100.0
            1000.0
            10000.0
            100000.0
            1000000.0
            10000000.0
            100000000.0
            1000000000.0
            512.0
            0.0
            0.0
            0.0
            0.0
            Double.NaN
        |]
                
    let inline internal ScaleUInt64Price (price: uint64, priceType: uint8) : double =
        (double price) / priceTypeDivisorTable[int priceType]
        
    let inline internal ScaleInt32Price (price: int, priceType: uint8) : double =
        (double price) / priceTypeDivisorTable[int priceType]
        
    let inline internal ScaleTimestampToSeconds (nanoseconds : UInt64) : double =
        (double nanoseconds) / 1_000_000_000.0
        
type QuoteType =
    | Ask = 0
    | Bid = 1
    
type MessageType =
    | Trade = 0
    | Quote = 1    
    | Refresh = 2
    | UnusualActivity = 3
    
type LogLevel =
    | DEBUG = 0
    | INFORMATION = 1
    | WARNING = 2
    | ERROR = 3
    
type IntervalType =
    | OneMinute = 60
    | TwoMinute = 120
    | ThreeMinute = 180
    | FourMinute = 240
    | FiveMinute = 300
    | TenMinute = 600
    | FifteenMinute = 900
    | ThirtyMinute = 1800
    | SixtyMinute = 3600

/// <summary>
/// A 'Quote' is a unit of data representing an individual market bid or ask event.
/// </summary>
/// <param name="Symbol">The id of the option contract (e.g. AAPL_210305C350.00).</param>
/// <param name="AskPrice">The dollar price of the last ask.</param>
/// <param name="AskSize">The number of contacts for the ask.</param>
/// <param name="BidPrice">The dollars price of the last bid.</param>
/// <param name="BidSize">The number of contacts for the bid.</param>
/// <param name="Timestamp">The time that the Quote was made (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
type Quote internal
    (cont: string,
     pt: uint8,
     aPrice: int32,
     aSize: uint32,
     bPrice: int32,
     bSize: uint32,
     ts: uint64) =

    member _.Contract with get() : string = cont
    member _.AskPrice with get() : float =
        if (aPrice = Int32.MaxValue) || (aPrice = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(aPrice, pt)
    member _.AskSize with get() : uint32 = aSize
    member _.BidPrice with get() : float =
        if (bPrice = Int32.MaxValue) || (bPrice = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(bPrice, pt)
    member _.BidSize with get() : uint32 = bSize
    member _.Timestamp with get() : float = TypesInline.ScaleTimestampToSeconds(ts)

    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Contract.[13] - uint16 '0') * 10_000us + (uint16 this.Contract.[14] - uint16 '0') * 1000us + (uint16 this.Contract.[15] - uint16 '0') * 100us + (uint16 this.Contract.[16] - uint16 '0') * 10us + (uint16 this.Contract.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Contract.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Contract.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Contract.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Contract.[12] = 'P'

    member this.IsCall() : bool = this.Contract.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        sprintf "Quote (Contract: %s, AskPrice: %s, AskSize: %s, BidPrice: %s, BidSize: %s, Timestamp: %s)"
            this.Contract
            (this.AskPrice.ToString("f3"))
            (this.AskSize.ToString())
            (this.BidPrice.ToString("f3"))
            (this.BidSize.ToString())
            (this.Timestamp.ToString("f6"))
            
    static member CreateUnitTestObject(contract : string, askPrice : float, askSize : uint32, bidPrice : float, bidSize : uint32, nanoSecondsSinceUnixEpoch : uint64) : Quote =
        let priceType : uint8 = (uint8)4
        let unscaledAskPrice : int32 = System.Convert.ToInt32(askPrice * 10000.0)
        let unscaledBidPrice : int32 = System.Convert.ToInt32(bidPrice * 10000.0)
        let ts : uint64 = nanoSecondsSinceUnixEpoch;
        let quote = new Quote(contract, priceType, unscaledAskPrice, askSize, unscaledBidPrice, bidSize, ts)        
        quote

type Exchange =
    | UNKNOWN = '?'
    | NYSE_AMERICAN = 'A'
    | BOSTON = 'B'
    | CBOE = 'C'
    | MIAMI_EMERALD = 'D'
    | BATS_EDGX = 'E'
    | ISE_GEMINI = 'H'
    | ISE = 'I'
    | MERCURY = 'J'
    | MIAMI = 'M'
    | NYSE_ARCA = 'N'
    | MIAMI_PEARL = 'O'
    | NYSE_ARCA_DEPRECIATED_CODE = 'P'
    | NASDAQ = 'Q'
    | MIAX_SAPPHIRE = 'S'
    | NASDAQ_BX = 'T'
    | MEMX = 'U'
    | CBOE_C2 = 'W'
    | PHLX = 'X'
    | BATS_BZX = 'Z'

/// <summary>
/// A 'Trade' is a unit of data representing an individual market trade event.
/// </summary>
/// <param name="Symbol">The id of the option contract (e.g. AAPL_210305C350.00).</param>
/// <param name="Exchange">The specific exchange through which the trade occurred.</param>
/// <param name="Price">The dollar price of the last trade.</param>
/// <param name="Size">The number of contacts for the trade.</param>
/// <param name="Timestamp">The time that the trade was executed (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
/// <param name="TotalVolume">The running total trade volume for this contract today.</param>
/// <param name="AskPriceAtExecution">The dollar price of the best ask at execution.</param>
/// <param name="BidPriceAtExecution">The dollar price of the best bid at execution.</param>
/// <param name="UnderlyingPriceAtExecution">The dollar price of the underlying security at the time of execution.</param>
type Trade internal
    (cont: string,
     ex: Exchange,
     pt: uint8,
     upt: uint8,
     p: int32,
     s: uint32,
     ts: uint64,
     tv: uint64,
     qual: struct(byte*byte*byte*byte),
     ape: int32,
     bpe: int32,
     upe: int32) =
    member _.Contract with get() : string = cont
    member _.Exhange with get() : Exchange = ex
    member _.Price with get() : float =
        if (p = Int32.MaxValue) || (p = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(p, pt)
    member _.Size with get() : uint32 = s
    member _.Timestamp with get() : float = TypesInline.ScaleTimestampToSeconds(ts)
    member _.TotalVolume with get() : uint64 = tv
    member _.Qualifiers with get() : struct(byte*byte*byte*byte) = qual
    member _.AskPriceAtExecution with get() : float =
        if (ape = Int32.MaxValue) || (ape = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(ape, pt)
    member _.BidPriceAtExecution with get() : float =
        if (bpe = Int32.MaxValue) || (bpe = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(bpe, pt)
    member _.UnderlyingPriceAtExecution with get() : float =
        if (upe = Int32.MaxValue) || (upe = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(upe, upt)
    
    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Contract.[13] - uint16 '0') * 10_000us + (uint16 this.Contract.[14] - uint16 '0') * 1000us + (uint16 this.Contract.[15] - uint16 '0') * 100us + (uint16 this.Contract.[16] - uint16 '0') * 10us + (uint16 this.Contract.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Contract.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Contract.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Contract.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Contract.[12] = 'P'

    member this.IsCall() : bool = this.Contract.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        sprintf "Trade (Contract: %s, Exchange: %s, Price: %s, Size: %s, Timestamp: %s, TotalVolume: %s, Qualifiers: %s, AskPriceAtExecution: %s, BidPriceAtExecution: %s, UnderlyingPrice: %s)"
            this.Contract
            (this.Exhange.ToString())
            (this.Price.ToString("f3"))
            (this.Size.ToString())
            (this.Timestamp.ToString("f6"))
            (this.TotalVolume.ToString())
            (this.Qualifiers.ToString())
            (this.AskPriceAtExecution.ToString("f3"))
            (this.BidPriceAtExecution.ToString("f3"))
            (this.UnderlyingPriceAtExecution.ToString("f3"))
            
    static member CreateUnitTestObject(contract : string, exchange: Exchange, price : float, size : uint32, nanoSecondsSinceUnixEpoch : uint64, totalVolume : uint64, qualifiers : struct(byte*byte*byte*byte), askPriceAtExecution : float, bidPriceAtExecution : float, underlyingPriceAtExecution : float) : Trade =
        let priceType : uint8 = (uint8)4        
        let unscaledPrice : int32 = System.Convert.ToInt32(price * 10000.0)
        let ts : uint64 = nanoSecondsSinceUnixEpoch;
        let unscaledAskPriceAtExecution = System.Convert.ToInt32(askPriceAtExecution * 10000.0)
        let unscaledBidPriceAtExecution = System.Convert.ToInt32(bidPriceAtExecution * 10000.0)
        let unscaledUnderlyingPriceAtExecution = System.Convert.ToInt32(underlyingPriceAtExecution * 10000.0)
        let trade = new Trade(contract, exchange, priceType, priceType, unscaledPrice, size, ts, totalVolume, qualifiers, unscaledAskPriceAtExecution, unscaledBidPriceAtExecution, unscaledUnderlyingPriceAtExecution)
        trade

/// <summary>
/// A 'Refresh' is an event that periodically sends updated values for open interest and high/low/open/close.
/// </summary>
/// <param name="Symbol">The id of the option contract (e.g. AAPL_210305C350.00).</param>
/// <param name="OpenInterest">Number of total active contracts for this contract.</param>
/// <param name="OpenPrice">The opening price for this contract for the day.</param>
/// <param name="ClosePrice">The closing price for this contract for the day.</param>
/// <param name="HighPrice">The running high price for this contract today.</param>
/// <param name="LowPrice">The running low price for this contract today.</param>
type Refresh internal
    (cont: string,
     pt: uint8,
     oi: uint32,
     o: int32,
     c: int32,
     h: int32,
     l: int32) =
    member _.Contract with get() : string = cont
    member _.OpenInterest with get() : uint32 = oi
    member _.OpenPrice with get() : float =
        if (o = Int32.MaxValue) || (o = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(o, pt)
    member _.ClosePrice with get() : float =
        if (c = Int32.MaxValue) || (c = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(c, pt)
    member _.HighPrice with get() : float =
        if (h = Int32.MaxValue) || (h = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(h, pt)
    member _.LowPrice with get() : float =
        if (l = Int32.MaxValue) || (l = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(l, pt)

    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Contract.[13] - uint16 '0') * 10_000us + (uint16 this.Contract.[14] - uint16 '0') * 1000us + (uint16 this.Contract.[15] - uint16 '0') * 100us + (uint16 this.Contract.[16] - uint16 '0') * 10us + (uint16 this.Contract.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Contract.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Contract.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Contract.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Contract.[12] = 'P'

    member this.IsCall() : bool = this.Contract.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, 6).TrimEnd('_')
    
    override this.ToString() : string =
        sprintf "Refresh (Contract: %s, OpenInterest: %s, OpenPrice: %s, ClosePrice: %s, HighPrice: %s, LowPrice: %s)"
            this.Contract
            (this.OpenInterest.ToString())
            (this.OpenPrice.ToString("f3"))
            (this.ClosePrice.ToString("f3"))
            (this.HighPrice.ToString("f3"))
            (this.LowPrice.ToString("f3"))
            
    static member CreateUnitTestObject(contract : string, openInterest : uint32, openPrice : float, closePrice : float, highPrice : float, lowPrice : float) : Refresh =
        let priceType : uint8 = (uint8)4        
        let unscaledOpenPrice : int32 = System.Convert.ToInt32(openPrice * 10000.0)
        let unscaledClosePrice : int32 = System.Convert.ToInt32(closePrice * 10000.0)
        let unscaledHighPrice : int32 = System.Convert.ToInt32(highPrice * 10000.0)
        let unscaledLowPrice : int32 = System.Convert.ToInt32(lowPrice * 10000.0)
        let refresh = new Refresh(contract, priceType, openInterest, unscaledOpenPrice, unscaledClosePrice, unscaledHighPrice, unscaledLowPrice)
        refresh

/// <summary>
/// Unusual activity type.
/// </summary>
type UAType =
    | Block = 3
    | Sweep = 4
    | Large = 5
    | UnusualSweep = 6

/// <summary>
/// Unusual activity sentiment.
/// </summary>
type UASentiment =
    | Neutral = 0
    | Bullish = 1
    | Bearish = 2
    
/// <summary>
/// An 'UnusualActivity' is an event that indicates unusual trading activity.
/// </summary>
/// <param name="Symbol">The id of the option contract (e.g. AAPL_210305C350.00).</param>
/// <param name="UnusualActivityType">The type of unusual activity.</param>
/// <param name="Sentiment">Bullish or Bearish.</param>
/// <param name="TotalValue">The total value in dollars of the unusual trading activity.</param>
/// <param name="TotalSize">The total number of contracts of the unusual trading activity.</param>
/// <param name="AveragePrice">The average executed trade price of the unusual activity.</param>
/// <param name="AskAtExecution">The best ask of this contract at the time of execution.</param>
/// <param name="BidAtExecution">The best bid of this contract at the time of execution.</param>
/// <param name="UnderlyingPriceAtExecution">The dollar price of the underlying security at the time of execution.</param>
/// <param name="Timestamp">The time that the unusual activity began (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
type UnusualActivity internal
    (cont: string,
     uat: UAType,
     s: UASentiment,
     pt: uint8,
     upt: uint8,     
     tv: uint64,
     ts: uint32,
     ap: int32,
     ape: int32,
     bpe: int32,
     upe: int32,
     t: uint64) =
    member _.Contract with get() : string = cont
        
    member _.UnusualActivityType with get() : UAType = uat
    member _.Sentiment with get() : UASentiment = s
    member _.TotalValue with get() : float =
        if (tv = UInt64.MaxValue) || (tv = 0UL) then Double.NaN else TypesInline.ScaleUInt64Price(tv, pt)
    member _.TotalSize with get() : uint32 = ts
    member _.AveragePrice with get() : float =
        if (ap = Int32.MaxValue) || (ap = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(ap, pt)
    member _.AskPriceAtExecution with get() : float =
        if (ape = Int32.MaxValue) || (ape = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(ape, pt)
    member _.BidPriceAtExecution with get() : float =
        if (bpe = Int32.MaxValue) || (bpe = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(bpe, pt)
    member _.UnderlyingPriceAtExecution with get() : float =
        if (upe = Int32.MaxValue) || (upe = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(upe, upt)
    member _.Timestamp with get() : float = TypesInline.ScaleTimestampToSeconds(t)

    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Contract.[13] - uint16 '0') * 10_000us + (uint16 this.Contract.[14] - uint16 '0') * 1000us + (uint16 this.Contract.[15] - uint16 '0') * 100us + (uint16 this.Contract.[16] - uint16 '0') * 10us + (uint16 this.Contract.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Contract.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Contract.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Contract.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Contract.[12] = 'P'

    member this.IsCall() : bool = this.Contract.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        sprintf "UnusualActivity (Contract: %s, Type: %s, Sentiment: %s, TotalValue: %s, TotalSize: %s, AveragePrice: %s, AskAtExecution: %s, BidAtExecution: %s, UnderlyingPriceAtExecution: %s, Timestamp: %s)"
            this.Contract
            (this.UnusualActivityType.ToString())
            (this.Sentiment.ToString())
            (this.TotalValue.ToString("f3"))
            (this.TotalSize.ToString())
            (this.AveragePrice.ToString("f3"))
            (this.AskPriceAtExecution.ToString("f3"))
            (this.BidPriceAtExecution.ToString("f3"))
            (this.UnderlyingPriceAtExecution.ToString("f3"))
            (this.Timestamp.ToString("f6"))
            
    static member CreateUnitTestObject(contract : string, unusualActivityType : UAType, sentimentType : UASentiment, totalValue : float, totalSize : uint32, averagePrice : float, askPriceAtExecution : float, bidPriceAtExecution : float, underlyingPriceAtExecution : float, nanoSecondsSinceUnixEpoch : uint64) : UnusualActivity =
        let priceType : uint8 = (uint8)4
        let unscaledTotalValue : uint64 = System.Convert.ToUInt64(totalValue * 10000.0)
        let unscaledAveragePrice : int32 = System.Convert.ToInt32(averagePrice * 10000.0)
        let unscaledAskPriceAtExecution = System.Convert.ToInt32(askPriceAtExecution * 10000.0)
        let unscaledBidPriceAtExecution = System.Convert.ToInt32(bidPriceAtExecution * 10000.0)
        let unscaledUnderlyingPriceAtExecution = System.Convert.ToInt32(underlyingPriceAtExecution * 10000.0)        
        let ts : uint64 = nanoSecondsSinceUnixEpoch; 
        let ua = new UnusualActivity(contract, unusualActivityType, sentimentType, priceType, priceType, unscaledTotalValue, totalSize, unscaledAveragePrice, unscaledAskPriceAtExecution, unscaledBidPriceAtExecution, unscaledUnderlyingPriceAtExecution, ts)
        ua
            
type TradeCandleStick =
    val Contract: string
    val mutable Volume: uint32
    val mutable High: float
    val mutable Low: float
    val mutable Close: float
    val mutable Open: float
    val OpenTimestamp: float
    val CloseTimestamp: float
    val mutable FirstTimestamp: float
    val mutable LastTimestamp: float
    val mutable Complete: bool
    val mutable Average: float
    val mutable Change: float
    val Interval: IntervalType
    
    new(contract: string, volume: uint32, price: float, openTimestamp: float, closeTimestamp : float, interval : IntervalType, tradeTime : float) =
        {
            Contract = contract
            Volume = volume
            High = price
            Low = price
            Close = price
            Open = price
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = tradeTime
            LastTimestamp = tradeTime
            Complete = false
            Average = price
            Change = 0.0
            Interval = interval
        }
        
    new(contract: string, volume: uint32, high: float, low: float, closePrice: float, openPrice: float, openTimestamp: float, closeTimestamp : float, firstTimestamp: float, lastTimestamp: float, complete: bool, average: float, change: float, interval : IntervalType) =
        {
            Contract = contract
            Volume = volume
            High = high
            Low = low
            Close = closePrice
            Open = openPrice
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = firstTimestamp
            LastTimestamp = lastTimestamp
            Complete = complete
            Average = average
            Change = change
            Interval = interval
        }
        
    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Contract.[13] - uint16 '0') * 10_000us + (uint16 this.Contract.[14] - uint16 '0') * 1000us + (uint16 this.Contract.[15] - uint16 '0') * 100us + (uint16 this.Contract.[16] - uint16 '0') * 10us + (uint16 this.Contract.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Contract.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Contract.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Contract.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Contract.[12] = 'P'

    member this.IsCall() : bool = this.Contract.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, 6).TrimEnd('_')
    
    override this.Equals(other: Object) : bool =
        ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
        || (
            (not (Object.ReferenceEquals(other, null)))
            && (not (Object.ReferenceEquals(this, other)))
            && (other :? TradeCandleStick)
            && (this.Contract.Equals((other :?> TradeCandleStick).Contract))
            && (this.Interval.Equals((other :?> TradeCandleStick).Interval))
            && (this.OpenTimestamp.Equals((other :?> TradeCandleStick).OpenTimestamp))
           )
    
    override this.GetHashCode() : int =
        this.Contract.GetHashCode() ^^^ this.Interval.GetHashCode() ^^^ this.OpenTimestamp.GetHashCode()
        
    interface IEquatable<TradeCandleStick> with
        member this.Equals(other: TradeCandleStick) : bool =
            ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
            || (
                (not (Object.ReferenceEquals(other, null)))
                && (not (Object.ReferenceEquals(this, other)))
                && (this.Contract.Equals(other.Contract))
                && (this.Interval.Equals(other.Interval))
                && (this.OpenTimestamp.Equals(other.OpenTimestamp))
               )
            
    interface IComparable with
        member this.CompareTo(other: Object) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match (other :? TradeCandleStick) with
                    | false -> -1
                    | true -> 
                        match this.Contract.CompareTo((other :?> TradeCandleStick).Contract) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 ->
                            match this.Interval.CompareTo((other :?> TradeCandleStick).Interval) with
                            | x when x < 0 -> -1
                            | x when x > 0 -> 1
                            | 0 -> this.OpenTimestamp.CompareTo((other :?> TradeCandleStick).OpenTimestamp)
                    
    interface IComparable<TradeCandleStick> with
        member this.CompareTo(other: TradeCandleStick) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match this.Contract.CompareTo(other.Contract) with
                    | x when x < 0 -> -1
                    | x when x > 0 -> 1
                    | 0 ->
                        match this.Interval.CompareTo(other.Interval) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 -> this.OpenTimestamp.CompareTo(other.OpenTimestamp)

    override this.ToString() : string =
        sprintf "TradeCandleStick (Contract: %s, Volume: %s, High: %s, Low: %s, Close: %s, Open: %s, OpenTimestamp: %s, CloseTimestamp: %s, AveragePrice: %s, Change: %s, Complete: %s)"
            this.Contract
            (this.Volume.ToString())
            (this.High.ToString("f3"))
            (this.Low.ToString("f3"))
            (this.Close.ToString("f3"))
            (this.Open.ToString("f3"))
            (this.OpenTimestamp.ToString("f6"))
            (this.CloseTimestamp.ToString("f6"))
            (this.Average.ToString("f3"))
            (this.Change.ToString("f6"))
            (this.Complete.ToString())
            
    member this.Merge(candle: TradeCandleStick) : unit =
        this.Average <- ((System.Convert.ToDouble(this.Volume) * this.Average) + (System.Convert.ToDouble(candle.Volume) * candle.Average)) / (System.Convert.ToDouble(this.Volume + candle.Volume))
        this.Volume <- this.Volume + candle.Volume
        this.High <- if this.High > candle.High then this.High else candle.High
        this.Low <- if this.Low < candle.Low then this.Low else candle.Low
        this.Close <- if this.LastTimestamp > candle.LastTimestamp then this.Close else candle.Close
        this.Open <- if this.FirstTimestamp < candle.FirstTimestamp then this.Open else candle.Open
        this.FirstTimestamp <- if candle.FirstTimestamp < this.FirstTimestamp then candle.FirstTimestamp else this.FirstTimestamp
        this.LastTimestamp <- if candle.LastTimestamp > this.LastTimestamp then candle.LastTimestamp else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
            
    member internal this.Update(volume: uint32, price: float, time: float) : unit = 
        this.Average <- ((System.Convert.ToDouble(this.Volume) * this.Average) + (System.Convert.ToDouble(volume) * price)) / (System.Convert.ToDouble(this.Volume + volume)) 
        this.Volume <- this.Volume + volume
        this.High <- if price > this.High then price else this.High
        this.Low <- if price < this.Low then price else this.Low
        this.Close <- if time > this.LastTimestamp then price else this.Close
        this.Open <- if time < this.FirstTimestamp then price else this.Open
        this.FirstTimestamp <- if time < this.FirstTimestamp then time else this.FirstTimestamp
        this.LastTimestamp <- if time > this.LastTimestamp then time else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
        
    member internal this.MarkComplete() : unit =
        this.Complete <- true
        
    member internal this.MarkIncomplete() : unit =
        this.Complete <- false

type QuoteCandleStick =
    val Contract: string
    val mutable High: float
    val mutable Low: float
    val mutable Close: float
    val mutable Open: float
    val QuoteType: QuoteType
    val OpenTimestamp: float
    val CloseTimestamp: float
    val mutable FirstTimestamp: float
    val mutable LastTimestamp: float
    val mutable Complete: bool
    val mutable Change: float
    val Interval: IntervalType
    
    new(contract: string,
        price: float,
        quoteType: QuoteType,
        openTimestamp: float,
        closeTimestamp: float,
        interval: IntervalType,
        tradeTime: float) =
        {
            Contract = contract
            High = price
            Low = price
            Close = price
            Open = price
            QuoteType = quoteType
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = tradeTime
            LastTimestamp = tradeTime
            Complete = false
            Change = 0.0
            Interval = interval
        }
        
    new(contract: string,
        high: float,
        low: float,
        closePrice: float,
        openPrice: float,
        quoteType: QuoteType,
        openTimestamp: float,
        closeTimestamp: float,
        firstTimestamp: float,
        lastTimestamp: float,
        complete: bool,
        change: float,
        interval: IntervalType) =
        {
            Contract = contract
            High = high
            Low = low
            Close = closePrice
            Open = openPrice
            QuoteType = quoteType
            OpenTimestamp = openTimestamp
            CloseTimestamp = closeTimestamp
            FirstTimestamp = firstTimestamp
            LastTimestamp = lastTimestamp
            Complete = complete
            Change = change
            Interval = interval
        }
        
    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Contract.[13] - uint16 '0') * 10_000us + (uint16 this.Contract.[14] - uint16 '0') * 1000us + (uint16 this.Contract.[15] - uint16 '0') * 100us + (uint16 this.Contract.[16] - uint16 '0') * 10us + (uint16 this.Contract.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Contract.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Contract.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Contract.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Contract.[12] = 'P'

    member this.IsCall() : bool = this.Contract.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, 6).TrimEnd('_')
    
    override this.Equals(other: Object) : bool =
        ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
        || (
            (not (Object.ReferenceEquals(other, null)))
            && (not (Object.ReferenceEquals(this, other)))
            && (other :? QuoteCandleStick)
            && (this.Contract.Equals((other :?> QuoteCandleStick).Contract))
            && (this.Interval.Equals((other :?> QuoteCandleStick).Interval))
            && (this.QuoteType.Equals((other :?> QuoteCandleStick).QuoteType))
            && (this.OpenTimestamp.Equals((other :?> QuoteCandleStick).OpenTimestamp))            
           )
    
    override this.GetHashCode() : int =
        this.Contract.GetHashCode() ^^^ this.Interval.GetHashCode() ^^^ this.OpenTimestamp.GetHashCode() ^^^ this.QuoteType.GetHashCode()
        
    interface IEquatable<QuoteCandleStick> with
        member this.Equals(other: QuoteCandleStick) : bool =
            ((not (Object.ReferenceEquals(other, null))) && Object.ReferenceEquals(this, other))
            || (
                (not (Object.ReferenceEquals(other, null)))
                && (not (Object.ReferenceEquals(this, other)))
                && (this.Contract.Equals(other.Contract))
                && (this.Interval.Equals(other.Interval))
                && (this.QuoteType.Equals(other.QuoteType))
                && (this.OpenTimestamp.Equals(other.OpenTimestamp))
               )
            
    interface IComparable with
        member this.CompareTo(other: Object) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match (other :? QuoteCandleStick) with
                    | false -> -1
                    | true -> 
                        match this.Contract.CompareTo((other :?> QuoteCandleStick).Contract) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 ->
                            match this.Interval.CompareTo((other :?> QuoteCandleStick).Interval) with
                            | x when x < 0 -> -1
                            | x when x > 0 -> 1
                            | 0 ->
                                match this.QuoteType.CompareTo((other :?> QuoteCandleStick).QuoteType) with
                                | x when x < 0 -> -1
                                | x when x > 0 -> 1
                                | 0 -> this.OpenTimestamp.CompareTo((other :?> QuoteCandleStick).OpenTimestamp)
                    
    interface IComparable<QuoteCandleStick> with
        member this.CompareTo(other: QuoteCandleStick) : int =
            match this.Equals(other) with
            | true -> 0
            | false ->
                match Object.ReferenceEquals(other, null) with
                | true -> 1
                | false ->
                    match this.Contract.CompareTo(other.Contract) with
                    | x when x < 0 -> -1
                    | x when x > 0 -> 1
                    | 0 ->
                        match this.Interval.CompareTo(other.Interval) with
                        | x when x < 0 -> -1
                        | x when x > 0 -> 1
                        | 0 ->
                            match this.QuoteType.CompareTo(other.QuoteType) with
                            | x when x < 0 -> -1
                            | x when x > 0 -> 1
                            | 0 -> this.OpenTimestamp.CompareTo(other.OpenTimestamp)

    override this.ToString() : string =
        sprintf "QuoteCandleStick (Contract: %s, QuoteType: %s, High: %s, Low: %s, Close: %s, Open: %s, OpenTimestamp: %s, CloseTimestamp: %s, Change: %s, Complete: %s)"
            this.Contract
            (this.QuoteType.ToString())
            (this.High.ToString("f3"))
            (this.Low.ToString("f3"))
            (this.Close.ToString("f3"))
            (this.Open.ToString("f3"))
            (this.OpenTimestamp.ToString("f6"))
            (this.CloseTimestamp.ToString("f6"))
            (this.Change.ToString("f6"))
            (this.Complete.ToString())
            
    member this.Merge(candle: QuoteCandleStick) : unit =
        this.High <- if this.High > candle.High then this.High else candle.High
        this.Low <- if this.Low < candle.Low then this.Low else candle.Low
        this.Close <- if this.LastTimestamp > candle.LastTimestamp then this.Close else candle.Close
        this.Open <- if this.FirstTimestamp < candle.FirstTimestamp then this.Open else candle.Open
        this.FirstTimestamp <- if candle.FirstTimestamp < this.FirstTimestamp then candle.FirstTimestamp else this.FirstTimestamp
        this.LastTimestamp <- if candle.LastTimestamp > this.LastTimestamp then candle.LastTimestamp else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
            
    member this.Update(price: float, time: float) : unit = 
        this.High <- if price > this.High then price else this.High
        this.Low <- if price < this.Low then price else this.Low
        this.Close <- if time > this.LastTimestamp then price else this.Close
        this.Open <- if time < this.FirstTimestamp then price else this.Open
        this.FirstTimestamp <- if time < this.FirstTimestamp then time else this.FirstTimestamp
        this.LastTimestamp <- if time > this.LastTimestamp then time else this.LastTimestamp
        this.Change <- (this.Close - this.Open) / this.Open
        
    member internal this.MarkComplete() : unit =
        this.Complete <- true
        
    member internal this.MarkIncomplete() : unit =
        this.Complete <- false
        
type Tick(
    timeReceived : DateTime,
    trade: Option<Trade>,
    quote : Option<Quote>,
    refresh : Option<Refresh>,
    unusualActivity : Option<UnusualActivity>) =
    
    let getTradeBytes(trade : Trade) : byte[] =
        let contractBytes : byte[] = Encoding.ASCII.GetBytes(trade.Contract);
        let contractLength : byte = System.Convert.ToByte(contractBytes.Length);
        let contractLengthInt32 : int = System.Convert.ToInt32 contractLength;
        let exchangeChar : char = LanguagePrimitives.EnumToValue trade.Exhange;
        let exchangeByte : byte = ((byte) exchangeChar);
        let priceBytes : byte[] = BitConverter.GetBytes(trade.Price); // 8 byte float
        let sizeBytes : byte[] = BitConverter.GetBytes(trade.Size); // 4 byte uint32
        let timestampBytes : byte[] = BitConverter.GetBytes(trade.Timestamp); // 8 byte float
        let totalVolumeBytes : byte[] = BitConverter.GetBytes(trade.TotalVolume); // 8 byte uint64
        let struct(qualifiersItem1, qualifiersItem2, qualifiersItem3, qualifiersItem4) = trade.Qualifiers;
        let askPriceAtExecutionBytes : byte[] = BitConverter.GetBytes(trade.AskPriceAtExecution); // 8 byte float
        let bidPriceAtExecutionBytes : byte[] = BitConverter.GetBytes(trade.BidPriceAtExecution); // 8 byte float
        let underlyingPriceAtExecutionBytes : byte[] = BitConverter.GetBytes(trade.UnderlyingPriceAtExecution); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next byte    | exchange | char
        // next 8 bytes | price | float64
        // next 4 bytes | size | uint32
        // next 8 bytes | timestamp | float64
        // next 8 bytes | totalvolume | uint64
        // next 4 bytes | qualifiers | 4 byte struct tuple
        // next 8 bytes | askpriceatexecution | float64
        // next 8 bytes | bidpriceatexecution | float64
        // next 8 bytes | underlyingpriceatexecution | float64
        let messageLength : byte = 60uy + contractLength;
        
        let bytes : byte[] = Array.zeroCreate (System.Convert.ToInt32(messageLength));
        bytes[0] <- System.Convert.ToByte((int)(MessageType.Trade));
        bytes[1] <- messageLength;
        bytes[2] <- contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        bytes[3 + contractLengthInt32] <- exchangeByte;
        Array.Copy(priceBytes, 0, bytes, 4 + contractLengthInt32, priceBytes.Length);
        Array.Copy(sizeBytes, 0, bytes, 12 + contractLengthInt32, sizeBytes.Length);
        Array.Copy(timestampBytes, 0, bytes, 16 + contractLengthInt32, timestampBytes.Length);
        Array.Copy(totalVolumeBytes, 0, bytes, 24 + contractLengthInt32, totalVolumeBytes.Length);
        bytes[32 + contractLengthInt32] <- qualifiersItem1;
        bytes[33 + contractLengthInt32] <- qualifiersItem2;
        bytes[34 + contractLengthInt32] <- qualifiersItem3;
        bytes[35 + contractLengthInt32] <- qualifiersItem4;
        Array.Copy(askPriceAtExecutionBytes, 0, bytes, 36 + contractLengthInt32, askPriceAtExecutionBytes.Length);
        Array.Copy(bidPriceAtExecutionBytes, 0, bytes, 44 + contractLengthInt32, bidPriceAtExecutionBytes.Length);
        Array.Copy(underlyingPriceAtExecutionBytes, 0, bytes, 52 + contractLengthInt32, underlyingPriceAtExecutionBytes.Length);
        
        bytes;
        
    let getQuoteBytes(quote : Quote) : byte[] =
        let contractBytes : byte[] = Encoding.ASCII.GetBytes(quote.Contract);
        let contractLength : byte = System.Convert.ToByte(contractBytes.Length);
        let contractLengthInt32 : int = System.Convert.ToInt32 contractLength;
        let askPriceBytes : byte[] = BitConverter.GetBytes(quote.AskPrice); // 8 byte float
        let askSizeBytes : byte[] = BitConverter.GetBytes(quote.AskSize); // 4 byte uint32
        let bidPriceBytes : byte[] = BitConverter.GetBytes(quote.BidPrice); // 8 byte float
        let bidSizeBytes : byte[] = BitConverter.GetBytes(quote.BidSize); // 4 byte uint32
        let timestampBytes : byte[] = BitConverter.GetBytes(quote.Timestamp); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next 8 bytes | askPrice | float64
        // next 4 bytes | askSize | uint32
        // next 8 bytes | bidPrice | float64
        // next 4 bytes | bidSize | uint32
        // next 8 bytes | timestamp | float64
        let messageLength : byte = 35uy + contractLength;
        
        let bytes : byte[] = Array.zeroCreate (System.Convert.ToInt32(messageLength));
        bytes[0] <- System.Convert.ToByte((int)(MessageType.Quote));
        bytes[1] <- messageLength;
        bytes[2] <- contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        Array.Copy(askPriceBytes, 0, bytes, 3 + contractLengthInt32, askPriceBytes.Length);
        Array.Copy(askSizeBytes, 0, bytes, 11 + contractLengthInt32, askSizeBytes.Length);
        Array.Copy(bidPriceBytes, 0, bytes, 15 + contractLengthInt32, bidPriceBytes.Length);
        Array.Copy(bidSizeBytes, 0, bytes, 23 + contractLengthInt32, bidSizeBytes.Length);
        Array.Copy(timestampBytes, 0, bytes, 27 + contractLengthInt32, timestampBytes.Length);
        
        bytes;
        
    let getRefreshBytes(refresh : Refresh) : byte[] =
        let contractBytes : byte[] = Encoding.ASCII.GetBytes(refresh.Contract);
        let contractLength : byte = System.Convert.ToByte(contractBytes.Length);
        let contractLengthInt32 : int = System.Convert.ToInt32 contractLength
        let openInterestBytes : byte[] = BitConverter.GetBytes(refresh.OpenInterest); // 4 byte uint32
        let openPriceBytes : byte[] = BitConverter.GetBytes(refresh.OpenPrice); // 8 byte float
        let closePriceBytes : byte[] = BitConverter.GetBytes(refresh.ClosePrice); // 8 byte float
        let highPriceBytes : byte[] = BitConverter.GetBytes(refresh.HighPrice); // 8 byte float
        let lowPriceBytes : byte[] = BitConverter.GetBytes(refresh.LowPrice); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next 4 bytes | openInterest | uint32
        // next 8 bytes | openPrice | float64
        // next 8 bytes | closePrice | float64
        // next 8 bytes | highPrice | float64
        // next 8 bytes | lowPrice | float64
        let messageLength : byte = 39uy + contractLength;
        
        let bytes : byte[] = Array.zeroCreate (System.Convert.ToInt32(messageLength));
        bytes[0] <- System.Convert.ToByte((int)(MessageType.Refresh));
        bytes[1] <- messageLength;
        bytes[2] <- contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        Array.Copy(openInterestBytes, 0, bytes, 3 + contractLengthInt32, openInterestBytes.Length);
        Array.Copy(openPriceBytes, 0, bytes, 7 + contractLengthInt32, openPriceBytes.Length);
        Array.Copy(closePriceBytes, 0, bytes, 15 + contractLengthInt32, closePriceBytes.Length);
        Array.Copy(highPriceBytes, 0, bytes, 23 + contractLengthInt32, highPriceBytes.Length);
        Array.Copy(lowPriceBytes, 0, bytes, 31 + contractLengthInt32, lowPriceBytes.Length);
        
        bytes;
        
    let getUnusualActivityBytes(unusualActivity : UnusualActivity) : byte[] =
        let contractBytes : byte[] = Encoding.ASCII.GetBytes(unusualActivity.Contract);
        let contractLength : byte = System.Convert.ToByte(contractBytes.Length);
        let contractLengthInt32 : int = System.Convert.ToInt32 contractLength;
        let unusualActivityTypeInt32 : int = LanguagePrimitives.EnumToValue unusualActivity.UnusualActivityType;
        let unusualActivityTypeByte : byte = ((byte) unusualActivityTypeInt32);
        let sentimentInt32 : int = LanguagePrimitives.EnumToValue unusualActivity.Sentiment;
        let sentimentByte : byte = ((byte) sentimentInt32);        
        let totalValueBytes : byte[] = BitConverter.GetBytes(unusualActivity.TotalValue); // 8 byte float
        let totalSizeBytes : byte[] = BitConverter.GetBytes(unusualActivity.TotalSize); // 4 byte uint32
        let averagePriceBytes : byte[] = BitConverter.GetBytes(unusualActivity.AveragePrice); // 8 byte float
        let askPriceAtExecutionBytes : byte[] = BitConverter.GetBytes(unusualActivity.AskPriceAtExecution); // 8 byte float
        let bidPriceAtExecutionBytes : byte[] = BitConverter.GetBytes(unusualActivity.BidPriceAtExecution); // 8 byte float
        let underlyingPriceAtExecutionBytes : byte[] = BitConverter.GetBytes(unusualActivity.UnderlyingPriceAtExecution); // 8 byte float
        let timestampBytes : byte[] = BitConverter.GetBytes(unusualActivity.Timestamp); // 8 byte float
        
        // byte 0       | type | byte
        // byte 1       | messageLength (includes bytes 0 and 1) | byte
        // byte 2       | contractLength | byte
        // bytes [3...] | contract | string (ascii)
        // next byte    | unusualActivityType | char
        // next byte    | sentiment | char
        // next 8 bytes | totalValue | float64
        // next 4 bytes | totalSize | uint32
        // next 8 bytes | averagePrice | float64
        // next 8 bytes | askPriceAtExecution | float64
        // next 8 bytes | bidPriceAtExecution | float64
        // next 8 bytes | underlyingPriceAtExecution | float64
        // next 8 bytes | timestamp | float64
        let messageLength : byte = 57uy + contractLength;
        
        let bytes : byte[] = Array.zeroCreate (System.Convert.ToInt32(messageLength));
        bytes[0] <- System.Convert.ToByte((int)(MessageType.UnusualActivity));
        bytes[1] <- messageLength;
        bytes[2] <- contractLength;
        Array.Copy(contractBytes, 0, bytes, 3, contractLengthInt32);
        bytes[3 + contractLengthInt32] <- unusualActivityTypeByte
        bytes[4 + contractLengthInt32] <- sentimentByte;
        Array.Copy(totalValueBytes, 0, bytes, 5 + contractLengthInt32, totalValueBytes.Length);
        Array.Copy(totalSizeBytes, 0, bytes, 13 + contractLengthInt32, totalSizeBytes.Length)
        Array.Copy(averagePriceBytes, 0, bytes, 17 + contractLengthInt32, averagePriceBytes.Length);
        Array.Copy(askPriceAtExecutionBytes, 0, bytes, 25 + contractLengthInt32, askPriceAtExecutionBytes.Length);
        Array.Copy(bidPriceAtExecutionBytes, 0, bytes, 33 + contractLengthInt32, bidPriceAtExecutionBytes.Length);
        Array.Copy(underlyingPriceAtExecutionBytes, 0, bytes, 41 + contractLengthInt32, underlyingPriceAtExecutionBytes.Length);
        Array.Copy(timestampBytes, 0, bytes, 49 + contractLengthInt32, timestampBytes.Length);        
        
        bytes;
        
    member _.TimeReceived() : DateTime = timeReceived
    
    member _.MessageType() : MessageType =
        match trade.IsSome with
        | true -> MessageType.Trade;
        | false ->
            match quote.IsSome with
            | true -> MessageType.Quote;
            | false ->
                match refresh.IsSome with
                | true -> MessageType.Refresh;
                | false -> MessageType.UnusualActivity;
        
    member _.Trade() : Trade =
        trade.Value
        
    member _.Quote() : Quote =
        quote.Value
        
    member _.Refresh() : Refresh =
        refresh.Value
        
    member _.UnusualActivity() : UnusualActivity =
        unusualActivity.Value
            
    member _.GetTimeReceivedBytes() : byte[] =
        BitConverter.GetBytes(System.Convert.ToUInt64((timeReceived - DateTime.UnixEpoch).Ticks) * 100UL)
        
    member _.GetEventBytes() : byte[] =
        match trade with
            | Some t -> getTradeBytes t
            | None ->
                match quote with
                    | Some q -> getQuoteBytes q
                    | None ->
                        match refresh with
                            | Some r -> getRefreshBytes r
                            | None ->
                                match unusualActivity with
                                    | Some u -> getUnusualActivityBytes u
                                    | None -> Array.Empty<byte>()

type ClientStats (
    socketDataMessages : uint64,
    socketTextMessages : uint64,
    queueDepth : int,
    eventCount : uint64,
    tradeCount : uint64,
    quoteCount : uint64,
    refreshCount : uint64,
    unusualActivityCount : uint64) =
    
    member _.SocketDataMessages() : uint64 =
        socketDataMessages
        
    member _.SocketTextMessages() : uint64 =
        socketTextMessages
        
    member _.QueueDepth() : int =
        queueDepth
        
    member _.EventCount() : uint64 =
        eventCount
        
    member _.TradeCount() : uint64 =
        tradeCount
        
    member _.QuoteCount() : uint64 =
        quoteCount
        
    member _.RefreshCount() : uint64 =
        refreshCount
        
    member _.UnusualActivityCount() : uint64 =
        unusualActivityCount
                    
type public IOptionsWebSocketClient =
    abstract member Join : unit -> unit
    abstract member Join : string -> unit
    abstract member Join : string[] -> unit
    abstract member JoinLobby : unit -> unit
    abstract member Leave : unit -> unit
    abstract member Leave : string -> unit
    abstract member Leave : string[] -> unit
    abstract member LeaveLobby : unit -> unit
    abstract member Stop : unit -> unit
    abstract member GetStats : unit -> ClientStats
    abstract member Log : string * [<ParamArray>] propertyValues:obj[] -> unit