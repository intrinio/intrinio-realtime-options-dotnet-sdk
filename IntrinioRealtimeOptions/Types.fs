namespace Intrinio

open System
open System.Globalization
open Intrinio

type Provider =
    | NONE = 0
    | OPRA = 1
    | OPRA_FIREHOSE = 2
    | MANUAL = 3
    | MANUAL_FIREHOSE = 4

type QuoteType =
    | Ask = 1
    | Bid = 2

/// <summary>
/// A 'Quote' is a unit of data representing an individual market bid or ask event.
/// </summary>
/// <param name="Symbol">The id of the option contract (e.g. AAPL_210305C00070000).</param>
/// <param name="AskPrice">The dollar price of the last ask.</param>
/// <param name="AskSize">The number of contacts for the ask.</param>
/// <param name="BidPrice">The dollars price of the last bid.</param>
/// <param name="BidSize">The number of contacts for the bid.</param>
/// <param name="Timestamp">The time that the trade was executed (a unix timestamp representing the number of seconds (or better) since the unix epoch).</param>
type [<Struct>] Quote =
    {
        Symbol : string
        AskPrice : float
        AskSize : uint32
        BidPrice : float
        BidSize : uint32
        Timestamp : float
    }

    member this.GetStrikePrice() : float32 =
        let chunk: ReadOnlySpan<char> = this.Symbol.AsSpan((this.Symbol.IndexOf('_') + 8), (this.Symbol.Length - (this.Symbol.IndexOf('_') + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(this.Symbol.IndexOf('_'), 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, this.Symbol.IndexOf('_')).TrimEnd('_')

    override this.ToString() : string =
        sprintf "Quote (Symbol: %s, AskPrice: %s, AskSize: %s, BidPrice: %s, BidSize: %s, Timestamp: %s)"
            this.Symbol
            (this.AskPrice.ToString("f3"))
            (this.AskSize.ToString())
            (this.BidPrice.ToString("f3"))
            (this.BidSize.ToString())
            (this.Timestamp.ToString("f6"))

type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : float
        TotalVolume : uint64
        AskPriceAtExecution: float
        BidPriceAtExecution: float
        UnderlyingPriceAtExecution: float
    }
    member this.GetStrikePrice() : float32 =
        let chunk: ReadOnlySpan<char> = this.Symbol.AsSpan((this.Symbol.IndexOf('_') + 8), (this.Symbol.Length - (this.Symbol.IndexOf('_') + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(this.Symbol.IndexOf('_'), 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, this.Symbol.IndexOf('_')).TrimEnd('_')

    override this.ToString() : string =
        sprintf "Trade (Symbol: %s, Price: %s, Size: %s, Timestamp: %s, TotalVolume: %s, AskPriceAtExecution: %s, BidPriceAtExecution: %s, UnderlyingPrice: %s)"
            this.Symbol
            (this.Price.ToString("f3"))
            (this.Size.ToString())
            (this.Timestamp.ToString("f6"))
            (this.TotalVolume.ToString())
            (this.AskPriceAtExecution.ToString("f3"))
            (this.BidPriceAtExecution.ToString("f3"))
            (this.UnderlyingPriceAtExecution.ToString("f3"))

type [<Struct>] Refresh =
    {
        Symbol : string
        OpenInterest : uint32
        OpenPrice : float
        ClosePrice : float
        HighPrice : float
        LowPrice : float
    }
    member this.GetStrikePrice() : float32 =
        let chunk: ReadOnlySpan<char> = this.Symbol.AsSpan((this.Symbol.IndexOf('_') + 8), (this.Symbol.Length - (this.Symbol.IndexOf('_') + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(this.Symbol.IndexOf('_'), 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, this.Symbol.IndexOf('_')).TrimEnd('_')
    
    override this.ToString() : string =
        sprintf "Refresh (Symbol: %s, OpenInterest: %s, OpenPrice: %s, ClosePrice: %s, HighPrice: %s, LowPrice: %s)"
            this.Symbol
            (this.OpenInterest.ToString())
            (this.OpenPrice.ToString("f3"))
            (this.ClosePrice.ToString("f3"))
            (this.HighPrice.ToString("f3"))
            (this.LowPrice.ToString("f3"))

type UAType =
    | Block = 3
    | Sweep = 4
    | Large = 5
    | Golden = 6

type UASentiment =
    | Neutral = 0
    | Bullish = 1
    | Bearish = 2
    
type PriceType =
    | One               = 0x00
    | Ten               = 0x01
    | Hundred           = 0x02
    | Thousand          = 0x03
    | TenThousand       = 0x04
    | HundredThousand   = 0x05
    | Million           = 0x06
    | TenMillion        = 0x07
    | HundredMillion    = 0x08
    | Billion           = 0x09
    | FiveHundredTwelve = 0x0A
    | Zero              = 0x0F
    
type [<Struct>] UnusualActivity =
    {
        Symbol : string
        Type : UAType
        Sentiment : UASentiment
        TotalValue : float
        TotalSize : uint32
        AveragePrice : float
        AskAtExecution : float
        BidAtExecution : float
        UnderlyingPriceAtExecution : float
        Timestamp : float
    }
    member this.GetStrikePrice() : float32 =
        let chunk: ReadOnlySpan<char> = this.Symbol.AsSpan((this.Symbol.IndexOf('_') + 8), (this.Symbol.Length - (this.Symbol.IndexOf('_') + 8)))
        Single.Parse(chunk)

    member this.IsPut() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'P'

    member this.IsCall() : bool = this.Symbol.[this.Symbol.IndexOf('_') + 7] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(this.Symbol.IndexOf('_'), 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, this.Symbol.IndexOf('_')).TrimEnd('_')

    override this.ToString() : string =
        sprintf "UnusualActivity (Symbol: %s, Type: %s, Sentiment: %s, TotalValue: %s, TotalSize: %s, AveragePrice: %s, AskAtExecution: %s, BidAtExecution: %s, UnderlyingPriceAtExecution: %s, Timestamp: %s)"
            this.Symbol
            (this.Type.ToString())
            (this.Sentiment.ToString())
            (this.TotalValue.ToString("f3"))
            (this.TotalSize.ToString())
            (this.AveragePrice.ToString("f3"))
            (this.AskAtExecution.ToString("f3"))
            (this.BidAtExecution.ToString("f3"))
            (this.UnderlyingPriceAtExecution.ToString("f3"))
            (this.Timestamp.ToString("f6"))