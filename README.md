# intrinio-realtime-options-dotnet-sdk
SDK for working with Intrinio's realtime options feed

[Intrinio](https://intrinio.com/) provides real-time stock option prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/financial-market-data/options-data) and follow the instructions below.

## Requirements

- .NET 5+

## Installation

Go to [Release](https://github.com/intrinio/intrinio-realtime-options-dotnet-sdk/releases/), download the DLLs, reference it in your project. The DLLs contains dependencies necessary to the SDK.

## Sample Project

For a sample .NET project see: [intrinio-realtime-options-dotnet-sdk](https://github.com/intrinio/intrinio-realtime-options-dotnet-sdk)

## Features

* Receive streaming, real-time option price quotes (last trade, bid, ask)
* Subscribe to updates from individual options contracts
* Subscribe to updates for all options contracts

## Example Usage
```csharp
using System;
using System.Threading;
using System.Collections.Concurrent;
using Intrinio;

namespace SampleApp
{
	class Program
	{
		private static Client client = null;
		private static Timer timer = null;
		private static readonly ConcurrentDictionary<string, int> trades = new ConcurrentDictionary<string, int>(5, 1_500_000);
		private static readonly ConcurrentDictionary<string, int> quotes = new ConcurrentDictionary<string, int>(5, 1_500_000);
		private static int maxTradeCount = 0;
		private static int maxQuoteCount = 0;
		private static int openInterestCount = 0;
		private static Trade maxCountTrade;
		private static Quote maxCountQuote;
		private static OpenInterest maxOpenInterest;

		private static readonly object obj = new object();

		static void OnQuote(Quote quote)
		{
			string key = quote.Symbol + ":" + quote.Type;
			if (!quotes.ContainsKey(key))
			{
				quotes[key] = 1;
			}
			else
			{
				quotes[key]++;
			}
			if (quotes[key] > maxQuoteCount)
			{
				lock (obj)
				{
					maxQuoteCount++;
					maxCountQuote = quote;
				}
			}
		}

		static void OnTrade(Trade trade)
		{
			string key = trade.Symbol + ":trade";
			if (!trades.ContainsKey(key))
			{
				trades[key] = 1;
			}
			else
			{
				trades[key]++;
			}
			if (trades[key] > maxTradeCount)
			{
				lock (obj)
				{
					maxTradeCount++;
					maxCountTrade = trade;
				}
			}
		}

		static void OnOpenInterest(OpenInterest openInterest)
		{
			openInterestCount++;
			if (openInterest.OpenInterest > maxOpenInterest.OpenInterest)
			{
				maxOpenInterest = openInterest;
			}
		}

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<Int64, Int64, int> stats = client.GetStats();
			Client.Log("Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			if (maxTradeCount > 0)
			{
				Client.Log("Most active trade symbol: {0:l} ({1} updates)", maxCountTrade.Symbol, maxTradeCount);
			}
			if (maxQuoteCount > 0)
			{
				Client.Log("Most active quote symbol: {0:l}:{1} ({2} updates)", maxCountQuote.Symbol, maxCountQuote.Type, maxQuoteCount);
			}
			if (openInterestCount > 0)
			{
				Client.Log("{0} open interest updates. Highest open interest symbol: {1:l} ({2})", openInterestCount, maxOpenInterest.Symbol, maxOpenInterest.OpenInterest);
			}
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Client.Log("Stopping sample app");
			timer.Dispose();
			client.Stop();
			Environment.Exit(0);
		}

		static void Main(string[] args)
		{
			Client.Log("Starting sample app");
			client = new Client(OnTrade, OnQuote, OnOpenInterest);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join();
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}


```

## Handling Quotes

There are millions of options contracts, each with their own feed of activity.  We highly encourage you to make your OnTrade, OnQuote, and OnOpenInterest methods has short as possible and follow a queue pattern so your app can handle the volume of activity options data has.

## Providers

Currently, Intrinio offers realtime data for this SDK from the following providers:

* OPRA - [Homepage](https://www.opraplan.com/)


## Data Format

### Trade Message

```fsharp
type [<Struct>] Trade =
    {
        Symbol : string
        Price : float
        Size : uint32
        TotalVolume : uint64
        Timestamp : float
    }
```

* **Symbol** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Price** - the price in USD
* **Size** - the size of the last trade in hundreds (each contract is for 100 shares).
* **TotalVolume** - The number of contracts traded so far today.
* **Timestamp** - a Unix timestamp (with microsecond precision)


### Quote Message

```fsharp
type [<Struct>] Quote =
    {
        Type : QuoteType 
        Symbol : string
        Price : float
        Size : uint32
        Timestamp : float
    }
```

* **Type** - the quote type
  *    **`Ask`** - represents an ask type
  *    **`Bid`** - represents a bid type  
* **Symbol** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Price** - the price in USD
* **Size** - the size of the last ask or bid in hundreds (each contract is for 100 shares).
* **Timestamp** - a Unix timestamp (with microsecond precision)


### Open Interst Message

```fsharp
type [<Struct>] OpenInterest =
    {
        Symbol : string
        OpenInterest : int32
        Timestamp : float
    }
```

* **Symbol** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Timestamp** - a Unix timestamp (with microsecond precision)
* **OpenInterest** - the total quantity of opened contracts as reported at the start of the trading day


## API Keys

You will receive your Intrinio API Key after [creating an account](https://intrinio.com/signup). You will need a subscription to a [realtime data feed](https://intrinio.com/financial-market-data/options-data) as well.

## Documentation

### Methods

`Client client = new Client(OnTrade, OnQuote, OnOpenInterest);` - Creates an Intrinio Real-Time client. The provided actions implement OnTrade, OnQuote, and OnOpenInterest which handle what happens when the associated event happens.
* **Parameter** `onTrade`: The Action accepting trades.
* **Parameter** `onQuote`: The Action accepting quotes.
* **Parameter** `onOpenInterest`: The Action accepting open interests.

---------

`client.Join();` - Joins channel(s) configured in config.json.

## Configuration

### config.json
```json
{
	"Config": {
		"ApiKey": "", //Your Intrinio API key.
		"Provider": "OPRA", //This is the mode for subscribing to individual contracts.
		"Symbols": [ "GOOG__210917C01040000", "MSFT__210917C00180000", "AAPL__210917C00130000" ], //This is a list of individual contracts to subscribe to.
		//"Provider": "OPRA_FIREHOSE", //This is the mode for subscribing to all contracts.  Can not do both providers from the same process.  Start another process with another provider if you with do have both modes running.
		//"Symbols": [ "lobby" ], //This will include all trades, quotes, and open interest events for all contracts
		//"Symbols": [ "lobby_trades_only" ], //This will include only trade events for all contracts
		"TradesOnly": true, //In either firehose or selective mode (provider), this indicates whether you only want trade events (true) or you want trades, quotes, and open interest events (false)
		"NumThreads": 8 //The number of threads to use for processing events.
	},
	"Serilog": {
		"Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
		"MinimumLevel": {
			"Default": "Information",
			"Override": {
				"Microsoft": "Warning",
				"System": "Warning"
			}
		},
		"WriteTo": [
			{ "Name": "Console" }
		]
	}
}
```