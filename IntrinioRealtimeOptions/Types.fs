namespace Intrinio

open System
open System.Globalization

type Provider =
    | NONE = 0
    | OPRA = 1
    | MANUAL = 2

module private TypesInline =
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
        
    let inline internal ScaleTimestamp (timestamp : UInt64) : double =
        (double timestamp) / 1_000_000_000.0

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
    member _.BidPrice with get() =
        if (bPrice = Int32.MaxValue) || (bPrice = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(bPrice, pt)
    member _.BidSize with get() : uint32 = bSize
    member _.Timestamp with get() : float = TypesInline.ScaleTimestamp(ts)

    member this.GetStrikePrice() : float32 =
        let i: int = this.Contract.IndexOf('_')
        let chunk: ReadOnlySpan<char> = this.Contract.AsSpan((i + 8), (this.Contract.Length - (i + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(this.Contract.IndexOf('_') + 1, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, this.Contract.IndexOf('_')).TrimEnd('_')

    override this.ToString() : string =
        sprintf "Quote (Symbol: %s, AskPrice: %s, AskSize: %s, BidPrice: %s, BidSize: %s, Timestamp: %s)"
            this.Contract
            (this.AskPrice.ToString("f3"))
            (this.AskSize.ToString())
            (this.BidPrice.ToString("f3"))
            (this.BidSize.ToString())
            (this.Timestamp.ToString("f6"))

/// <summary>
/// A 'Trade' is a unit of data representing an individual market trade event.
/// </summary>
/// <param name="Symbol">The id of the option contract (e.g. AAPL_210305C350.00).</param>
/// <param name="Price">The dollar price of the last trade.</param>
/// <param name="Size">The number of contacts for the trade.</param>
/// <param name="Timestamp">The time that the trade was executed (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
/// <param name="TotalVolume">The running total trade volume for this contract today.</param>
/// <param name="AskPriceAtExecution">The dollar price of the best ask at execution.</param>
/// <param name="BidPriceAtExecution">The dollar price of the best bid at execution.</param>
/// <param name="UnderlyingPriceAtExecution">The dollar price of the underlying security at the time of execution.</param>
type Trade internal
    (cont: string,
     pt: uint8,
     upt: uint8,
     p: int32,
     s: uint32,
     ts: uint64,
     tv: uint64,
     ape: int32,
     bpe: int32,
     upe: int32) =
    member _.Contract with get() : string = cont
    member _.Price with get() : float =
        if (p = Int32.MaxValue) || (p = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(p, pt)
    member _.Size with get() : uint32 = s
    member _.Timestamp with get() : float = TypesInline.ScaleTimestamp(ts)
    member _.TotalVolume with get() : uint64 = tv
    member _.AskPriceAtExecution with get() : float =
        if (ape = Int32.MaxValue) || (ape = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(ape, pt)
    member _.BidPriceAtExecution with get() : float =
        if (bpe = Int32.MaxValue) || (bpe = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(bpe, pt)
    member _.UnderlyingPriceAtExecution with get() : float =
        if (upe = Int32.MaxValue) || (upe = Int32.MinValue) then Double.NaN else TypesInline.ScaleInt32Price(upe, upt)
    
    member this.GetStrikePrice() : float32 =
        let i: int = this.Contract.IndexOf('_')
        let chunk: ReadOnlySpan<char> = this.Contract.AsSpan((i + 8), (this.Contract.Length - (i + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(this.Contract.IndexOf('_') + 1, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, this.Contract.IndexOf('_')).TrimEnd('_')

    override this.ToString() : string =
        sprintf "Trade (Symbol: %s, Price: %s, Size: %s, Timestamp: %s, TotalVolume: %s, AskPriceAtExecution: %s, BidPriceAtExecution: %s, UnderlyingPrice: %s)"
            this.Contract
            (this.Price.ToString("f3"))
            (this.Size.ToString())
            (this.Timestamp.ToString("f6"))
            (this.TotalVolume.ToString())
            (this.AskPriceAtExecution.ToString("f3"))
            (this.BidPriceAtExecution.ToString("f3"))
            (this.UnderlyingPriceAtExecution.ToString("f3"))

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
        let i: int = this.Contract.IndexOf('_')
        let chunk: ReadOnlySpan<char> = this.Contract.AsSpan((i + 8), (this.Contract.Length - (i + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(this.Contract.IndexOf('_') + 1, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, this.Contract.IndexOf('_')).TrimEnd('_')
    
    override this.ToString() : string =
        sprintf "Refresh (Symbol: %s, OpenInterest: %s, OpenPrice: %s, ClosePrice: %s, HighPrice: %s, LowPrice: %s)"
            this.Contract
            (this.OpenInterest.ToString())
            (this.OpenPrice.ToString("f3"))
            (this.ClosePrice.ToString("f3"))
            (this.HighPrice.ToString("f3"))
            (this.LowPrice.ToString("f3"))

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
/// <param name="Type">The type of unusual activity.</param>
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
    member _.Type with get() : UAType = uat
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
    member _.Timestamp with get() : float = TypesInline.ScaleTimestamp(t)

    member this.GetStrikePrice() : float32 =
        let i: int = this.Contract.IndexOf('_')
        let chunk: ReadOnlySpan<char> = this.Contract.AsSpan((i + 8), (this.Contract.Length - (i + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Contract.[this.Contract.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Contract.Substring(this.Contract.IndexOf('_') + 1, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Contract.Substring(0, this.Contract.IndexOf('_')).TrimEnd('_')

    override this.ToString() : string =
        sprintf "UnusualActivity (Symbol: %s, Type: %s, Sentiment: %s, TotalValue: %s, TotalSize: %s, AveragePrice: %s, AskAtExecution: %s, BidAtExecution: %s, UnderlyingPriceAtExecution: %s, Timestamp: %s)"
            this.Contract
            (this.Type.ToString())
            (this.Sentiment.ToString())
            (this.TotalValue.ToString("f3"))
            (this.TotalSize.ToString())
            (this.AveragePrice.ToString("f3"))
            (this.AskPriceAtExecution.ToString("f3"))
            (this.BidPriceAtExecution.ToString("f3"))
            (this.UnderlyingPriceAtExecution.ToString("f3"))
            (this.Timestamp.ToString("f6"))