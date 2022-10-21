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

/// A 'Quote' is a unit of data representing an individual market bid or ask event.
/// Symbol: the id of the option contract (e.g. AAPL_210305C00070000)
/// AskPrice: the dollar price of the last ask
/// AskSize: the number of contacts for the ask
/// BidPrice: the dollars price of the last bid.
/// BidSize: the number of contacts for the bid
/// Timestamp: the time that the trade was executed (a unix timestamp representing the number of seconds (or better) since the unix epoch)
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
        let whole : uint16 = (uint16 this.Symbol.[13] - uint16 '0') * 10_000us + (uint16 this.Symbol.[14] - uint16 '0') * 1000us + (uint16 this.Symbol.[15] - uint16 '0') * 100us + (uint16 this.Symbol.[16] - uint16 '0') * 10us + (uint16 this.Symbol.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Symbol.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Symbol.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Symbol.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Symbol.[12] = 'P'

    member this.IsCall() : bool = this.Symbol.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        "Quote (" +
        ", Symbol: " + this.Symbol +
        ", AskPrice: " + this.AskPrice.ToString("f2") +
        ", AskSize: " + this.AskSize.ToString() +
        ", BidPrice: " + this.BidPrice.ToString("f2") +
        ", BidSize: " + this.BidSize.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("f6") +
        ")"

type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : float
        TotalVolume : uint64
        AskPriceAtExecution: float
        BidPriceAtExecution: float
        UnderlyingPrice: float
    }
    member this.GetStrikePrice() : float32 =
        let whole : uint16 = (uint16 this.Symbol.[13] - uint16 '0') * 10_000us + (uint16 this.Symbol.[14] - uint16 '0') * 1000us + (uint16 this.Symbol.[15] - uint16 '0') * 100us + (uint16 this.Symbol.[16] - uint16 '0') * 10us + (uint16 this.Symbol.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Symbol.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Symbol.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Symbol.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Symbol.[12] = 'P'

    member this.IsCall() : bool = this.Symbol.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        "Trade (" +
        "Symbol: " + this.Symbol +
        ", Price: " + this.Price.ToString("f2") +
        ", Size: " + this.Size.ToString() +
        ", TotalVolume: " + this.TotalVolume.ToString() +
        ", Timestamp: " + this.Timestamp.ToString("f6") +
        ")"

type [<Struct>] Refresh =
    {
        Symbol : string
        OpenInterest : uint32
        OpenPrice : float
        ClosePrice : float
        HighPrice : float
        LowPrice : float
    }

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
        let whole : uint16 = (uint16 this.Symbol.[13] - uint16 '0') * 10_000us + (uint16 this.Symbol.[14] - uint16 '0') * 1000us + (uint16 this.Symbol.[15] - uint16 '0') * 100us + (uint16 this.Symbol.[16] - uint16 '0') * 10us + (uint16 this.Symbol.[17] - uint16 '0')
        let part : float32 = (float32 (uint8 this.Symbol.[18] - uint8 '0')) * 0.1f + (float32 (uint8 this.Symbol.[19] - uint8 '0')) * 0.01f + (float32 (uint8 this.Symbol.[20] - uint8 '0')) * 0.001f
        (float32 whole) + part

    member this.IsPut() : bool = this.Symbol.[12] = 'P'

    member this.IsCall() : bool = this.Symbol.[12] = 'C'

    member this.GetExpirationDate() : DateTime = DateTime.ParseExact(this.Symbol.Substring(6, 6), "yyMMdd", CultureInfo.InvariantCulture)

    member this.GetUnderlyingSymbol() : string = this.Symbol.Substring(0, 6).TrimEnd('_')

    override this.ToString() : string =
        "Unusual Activity (" +
        "Symbol: " + this.Symbol +
        ", Activity Type: " + this.Type.ToString() +
        ", Sentiment: " + this.Sentiment.ToString() +
        ", Total Value: " + this.TotalValue.ToString("f2") +
        ", Total Size: " + this.TotalSize.ToString() +
        ", Average Price: " + this.AveragePrice.ToString("f2") +
        ", Ask Price at Execution: " + this.AskAtExecution.ToString("f2") +
        ", Bid Price at Execution: " + this.BidAtExecution.ToString("f2") +
        ", Underlying Price at Execution: " + this.UnderlyingPriceAtExecution.ToString("f2") +
        ", Timestamp: " + this.Timestamp.ToString("f6") +
        ")"