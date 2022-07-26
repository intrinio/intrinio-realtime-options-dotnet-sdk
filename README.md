# intrinio-realtime-options-dotnet-sdk
SDK for working with Intrinio's realtime options feed via WebSocket

[Intrinio](https://intrinio.com/) provides real-time stock option prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/financial-market-data/options-data) and follow the instructions below.

## Requirements

- .NET 6+

## Installation

Go to [Release](https://github.com/intrinio/intrinio-realtime-options-dotnet-sdk/releases/), download the DLLs, reference it in your project. The DLLs contains dependencies necessary to the SDK.

## Sample Project

For a sample .NET project see: [intrinio-realtime-options-dotnet-sdk](https://github.com/intrinio/intrinio-realtime-options-dotnet-sdk)

## Features

* Receive streaming, real-time option price updates:
	* every trade
	* conflated bid and ask
	* open interest
	* unusual activity(block trades, sweeps, whale trades)
* Subscribe to updates from individual options contracts (or option chains)
* Subscribe to updates for the entire univers of option contracts (~1.5M option contracts)

## Example Usage
```csharp
using System;
using System.Threading;
using Intrinio;

namespace SampleApp
{
	class Program
	{
		private static Client client = null;
		private static Timer timer = null;
		private static int tradeCount = 0;
		private static int askCount = 0;
		private static int bidCount = 0;
		private static int openInterestCount = 0;
		private static int blockCount = 0;
		private static int sweepCount = 0;
		private static int largeTradeCount = 0;

		private static readonly object obj = new object();

		static void OnQuote(Quote quote)
		{
			if (quote.Type == QuoteType.Ask)
			{
				Interlocked.Increment(ref askCount);
			}
			else if (quote.Type == QuoteType.Bid)
			{
				Interlocked.Increment(ref bidCount);
			}
			else
            {
				Client.Log("Invalid quote type detected: {0}", quote.Type);
            }
		}

		static void OnTrade(Trade trade)
		{
			Interlocked.Increment(ref tradeCount);
		}

		static void OnOpenInterest(OpenInterest openInterest)
		{
			Interlocked.Increment(ref openInterestCount);
		}

		static void OnUnusualActivity(UnusualActivity unusualActivity)
		{
			if (unusualActivity.Type == UAType.Block)
            {
				Interlocked.Increment(ref blockCount);
            } else if (unusualActivity.Type == UAType.Sweep)
            {
				Interlocked.Increment(ref sweepCount);
            } else if (unusualActivity.Type == UAType.Large)
            {
				Interlocked.Increment(ref largeTradeCount);
            } else
            {
				Client.Log("Invalid UA type detected: {0}", unusualActivity.Type);
			}
		}

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<Int64, Int64, int> stats = client.GetStats();
			Client.Log("CLIENT STATS - Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			Client.Log("PROGRAM STATS - Trades = {0}, Asks = {1}, Bids = {2}, OIs = {3}, Blocks = {4}, Sweeps = {5}, Large Trades = {6}", tradeCount, askCount, bidCount, openInterestCount, blockCount, sweepCount, largeTradeCount);
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
			
			// Register only the callbacks that you want.
			// Take special care when registering the 'OnQuote' handler as it will increase throughput by ~10x
			client = new Client(onTrade: OnTrade, onQuote: OnQuote, onOpenInterest: OnOpenInterest, onUnusualActivity: OnUnusualActivity);
			
			timer = new Timer(TimerCallback, client, 10000, 10000);

			// Use this to subscribe to a static list of symbols (option contracts) provided in config.json
			//client.Join();

			// Use this to subscribe to the entire univers of symbols (option contracts). This requires special permission.
			//client.JoinLobby();

			// Use this to subscribe, dynamically, to an option chain (all option contracts for a given underlying symbol).
			//client.Join("AAPL");

			// Use this to subscribe, dynamically, to a specific option contract.
			//client.Join("AAP___230616P00250000");

			// Use this to subscribe, dynamically, a list of specific option contracts or option chains.
			//string[] clients = { "GOOG__220408C02870000", "MSFT__220408C00315000", "AAPL__220414C00180000", "TSLA", "GE" };
            //client.Join(clients);

			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}


```

## Handling Quotes

There are millions of options contracts, each with their own feed of activity.
We highly encourage you to make your OnTrade, OnQuote, OnUnusualActivity, and OnOpenInterest methods has short as possible and follow a queue pattern so your app can handle the large volume of activity.
Note that quotes (ask and bid updates) comprise 99% of the volume of the entire feed. Be cautious when deciding to receive quote updates.

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


### Open Interest Message

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

### Unusual Activity Message

```fsharp
type [<Struct>] UnusualActivity =
    {
        Symbol : string
        Type : UAType
        Sentiment : UASentiment
        TotalValue : single
        TotalSize : uint32
        AveragePrice : single
        AskAtExecution : single
        BidAtExecution : single
        PriceAtExecution : single
        Timestamp : double
    }
```

* **Symbol** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Type** - The type of unusual activity that was detected
  *    **`Block`** - represents an 'block' trade
  *    **`Sweep`** - represents an intermarket sweep
  *    **`Large`** - represents a trade of at least $100,000
* **Sentiment** - The sentiment of the unusual activity event
  *    **`Neutral`** - 
  *    **`Bullish`** - 
  *    **`Bearish`** - 
* **TotalValue** - The total value of the trade in USD. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the value of the entire event.
* **TotalValue** - The total size of the trade in number of contracts. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the total number of contracts exchanged during the event.
* **AveragePrice** - The average price at which the trade was executed. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the average trade price for the entire event.
* **AskAtExecution** - The 'ask' price of the underlying at execution of the trade event.
* **BidAtExecution** - The 'bid' price of the underlying at execution of the trade event.
* **PriceAtExecution** - The last trade price of the underlying at execution of the trade event.
* **Timestamp** - a Unix timestamp (with microsecond precision).

## API Keys

You will receive your Intrinio API Key after [creating an account](https://intrinio.com/signup). You will need a subscription to a [realtime data feed](https://intrinio.com/financial-market-data/options-data) as well.

## Documentation

### Overview

The Intrinio Realtime Client will handle authorization as well as establishment and management of all necessary WebSocket connections. All you need to get started is your API key.
The first thing that you'll do is create a new `Client` object, passing in a series of callbacks. These callback methods tell the client what types of subscriptions you will be setting up.
Creating a `Client` object will immediately attempt to authorize your API key (provided in the config.json file). If authoriztion is successful, the object constructor will, also, open up the necessary connections.
After a `Client` object has been created, you may subscribe to receive feed updates from the server.
You may subscribe to static list of symbols (a mixed list of option contracts and/or option chains). 
Or, you may subscribe, dynamically, to option contracts, option chains, or a mixed list thereof.
It is also possible to subscribe to the entire universe of option contracts by switching the `Provider` to "OPRA_FIREHOSE" (in config.json) and calling `JoinLobby`.
The volume of data provided by the `Firehose` exceeds 100Mbps and requires special authorization.
If you are using the non-firehose feed, you may update your subscriptions on the fly, using the `Join` and `Leave` methods.
The WebSocket client is designed for near-indefinite operation. It will automatically reconnect if a connection drops/fails and when then servers turn on every morning.
If you wish to perform a graceful shutdown of the application, please call the `Stop` method.

### Methods

`Client client = new Client(OnTrade, OnQuote, OnOpenInterest, OnUnusualActivity);` - Creates an Intrinio Real-Time client. The provided actions implement OnTrade, OnQuote, OnOpenInterest, and OnUnusualActivity which handle what happens when the associated event happens.
* **Parameter** `onTrade`: The Action accepting trades. If no `onTrade` callback is provided, you will not receive trade updates from the server.
* **Parameter** `onQuote`: The Action accepting quotes. If no `onQuote` callback is provided, you will not receive quote (ask, bid) updates from the server.
* **Parameter** `onOpenInterest`: The Action accepting open interest messages. If no `onOpenInterest` callback is provided, you will not receive open interest data from the server. Note: open interest data is only updated at the beginning of every trading day. If this callback is provided you will recieve an update immediately, as well as every 15 minutes (approx).
* **Parameter** `onUnusualActivity`: The Action accepting unusual activity events. If no `onUnusualActivity` callback is provided, you will not receive unusual activity updates from the server.

---------

`client.Join();` - Joins channel(s) configured in config.json.
`client.Join(String channel)` - Joins the provided channel. E.g. "AAPL" or "GOOG__210917C01040000"
`client.Join(String[] channels)` - Joins the provided channels. E.g. [ "AAPL", "MSFT__210917C00180000", "GOOG__210917C01040000" ]
`client.JoinLobby()` - Joins the 'lobby' (aka. firehose) channel. The provider must be set to `OPRA_FIREHOSE` for this to work. This requires special account permissions.

---------

`client.Leave();` - Leaves all joined channels/subscriptions, including `lobby`.
`client.Leave(String channel)` - Leaves the specified channel. E.g. "AAPL" or "GOOG__210917C01040000"
`client.Leave(String[] channels)` - Leaves the specified channels. E.g. [ "AAPL", "MSFT__210917C00180000", "GOOG__210917C01040000" ]

---------

`client.Stop();` - Stops the Intrinio Realtime WebSocket Client. This method will leave all joined channels, stop all threads, and gracefully close the websocket connection(s).

## Configuration

### config.json
```json
{
	"Config": {
		"ApiKey": "", //Your Intrinio API key.
		"Provider": "OPRA", //This is the mode for subscribing to individual contracts.
		"Symbols": [ "GOOG__210917C01040000", "MSFT__210917C00180000", "AAPL__210917C00130000", "SPY" ], //This is a list of individual contracts (or option chains) to subscribe to.
		//"Provider": "OPRA_FIREHOSE", //This is the mode for subscribing to all contracts. Note: You cannot connect to both `OPRA` and `OPRA_FIREHOSE` providers from the same process. Start another process with another provider if you wish to have both modes running.
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
			// Uncomment to log to file
			//{
			//	"Name": "File",
			//	"Args": {
			//		"path" : ""
			//	}
			//}
		]
	}
}
```