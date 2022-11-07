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
	* open interest, open, close, high, low
	* unusual activity(block trades, sweeps, whale trades, unusual sweeps)
* Subscribe to updates from individual options contracts (or option chains)
* Subscribe to updates for the entire universe of option contracts (~1.5M option contracts)

## Example Usage
```csharp
using System;
using System.Threading;
using Intrinio;

namespace SampleApp
{
	class Program
	{
		private static Client _client = null;
		private static Timer _timer = null;
		private static UInt64 _tradeCount = 0L;
		private static UInt64 _quoteCount = 0L;
		private static UInt64 _refreshCount = 0L;
		private static UInt64 _blockCount = 0L;
		private static UInt64 _sweepCount = 0L;
		private static UInt64 _largeTradeCount = 0L;
		private static UInt64 _unusualSweepCount = 0L;

		static void OnQuote(Quote quote)
		{
			Interlocked.Increment(ref _quoteCount);
		}

		static void OnTrade(Trade trade)
		{
			Interlocked.Increment(ref _tradeCount);
		}

		static void OnRefresh(Refresh refresh)
		{
			Interlocked.Increment(ref _refreshCount);
		}

		static void OnUnusualActivity(UnusualActivity unusualActivity)
		{
			switch (unusualActivity.Type)
			{
				case UAType.Block:
					Interlocked.Increment(ref _blockCount);
					break;
				case UAType.Sweep:
					Interlocked.Increment(ref _sweepCount);
					break;
				case UAType.Large:
					Interlocked.Increment(ref _largeTradeCount);
					break;
				case UAType.UnusualSweep:
					Interlocked.Increment(ref _unusualSweepCount);
					break;
				default:
					Client.Log("Invalid UA type detected: {0}", unusualActivity.Type);
					break;
			}
		}

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<UInt64, UInt64, int> stats = client.GetStats();
			Client.Log("CLIENT STATS - Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			Client.Log("PROGRAM STATS - Trades = {0}, Quotes = {1}, Refreshes = {2}, Blocks = {3}, Sweeps = {4}, Large Trades = {5}, UnusualSweeps = {6}", _tradeCount, _quoteCount, _refreshCount, _blockCount, _sweepCount, _largeTradeCount, _unusualSweepCount);
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Client.Log("Stopping sample app");
			_timer.Dispose();
			_client.Stop();
			Environment.Exit(0);
		}

		static void Main(string[] args)
		{
			Client.Log("Starting sample app");

			// Register only the callbacks that you want.
			// Take special care when registering the 'OnQuote' handler as it will increase throughput by ~10x
			_client = new Client(onTrade: OnTrade, onQuote: OnQuote, onRefresh: OnRefresh, onUnusualActivity: OnUnusualActivity);
			
			_timer = new Timer(TimerCallback, _client, 10_000, 10_000);

			// Use this to subscribe to a static list of symbols (option contracts) provided in config.json
			//_client.Join();

			// Use this to subscribe to the entire universe of symbols (option contracts). This requires special permission.
			//_client.JoinLobby();

			// Use this to subscribe, dynamically, to an option chain (all option contracts for a given underlying symbol).
			//_client.Join("AAPL");

			// Use this to subscribe, dynamically, to a specific option contract.
			//_client.Join("AAPL_230616P250.000");

			// Use this to subscribe, dynamically, a list of specific option contracts or option chains.
			//string[] clients = { "GOOG_220408C2870.000", "MSFT_220408C315.000", "AAPL_220414C180.000", "TSLA", "GE" };
            //_client.Join(clients);

			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}
```

## Handling Quotes

There are millions of options contracts, each with their own feed of activity.
We highly encourage you to make your OnTrade, OnQuote, OnUnusualActivity, and OnRefresh methods has short as possible and follow a queue pattern so your app can handle the large volume of activity.
Note that quotes (ask and bid updates) comprise 99% of the volume of the entire feed. Be cautious when deciding to receive quote updates.

## Providers

Currently, Intrinio offers realtime data for this SDK from the following providers:

* OPRA - [Homepage](https://www.opraplan.com/)


## Data Format

### Trade Message

```fsharp
type Trade
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Price** - the price in USD
* **Size** - the size of the last trade in hundreds (each contract is for 100 shares).
* **TotalVolume** - The number of contracts traded so far today.
* **Timestamp** - a Unix timestamp (with microsecond precision)
* **AskPriceAtExecution** - the best last ask price in USD
* **BidPriceAtExecution** - the best last bid price in USD
* **UnderlyingPriceAtExecution** - the price of the underlying security in USD


### Quote Message

```fsharp
type Quote
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **AskPrice** - the last best ask price in USD
* **AskSize** - the last best ask size in hundreds (each contract is for 100 shares).
* **BidPrice** - the last best bid price in USD
* **BidSize** - the last best bid size in hundreds (each contract is for 100 shares).
* **Timestamp** - a Unix timestamp (with microsecond precision)


### Refresh Message

```fsharp
type Refresh
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **OpenInterest** - the total quantity of opened contracts as reported at the start of the trading day
* **OpenPrice** - the open price price in USD
* **ClosePrice** - the close price in USD
* **HighPrice** - the current high price in USD
* **LowPrice** - the current low price in USD

### Unusual Activity Message

```fsharp
type UnusualActivity
```

* **Contract** - Identifier for the options contract.  This includes the ticker symbol, put/call, expiry, and strike price.
* **Type** - The type of unusual activity that was detected
  * **`Block`** - represents an 'block' trade
  * **`Sweep`** - represents an intermarket sweep
  * **`Large`** - represents a trade of at least $100,000
  * **`Unusual Sweep`** - represents an unusually large sweep near market open.
* **Sentiment** - The sentiment of the unusual activity event
  *    **`Neutral`** - 
  *    **`Bullish`** - 
  *    **`Bearish`** - 
* **TotalValue** - The total value of the trade in USD. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the value of the entire event.
* **TotalSize** - The total size of the trade in number of contracts. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the total number of contracts exchanged during the event.
* **AveragePrice** - The average price at which the trade was executed. 'Sweeps' and 'blocks' can be comprised of multiple trades. This is the average trade price for the entire event.
* **AskPriceAtExecution** - The 'ask' price of the underlying at execution of the trade event.
* **BidPriceAtExecution** - The 'bid' price of the underlying at execution of the trade event.
* **UnderlyingPriceAtExecution** - The last trade price of the underlying at execution of the trade event.
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

`Client client = new Client(OnTrade, OnQuote, OnRefresh, OnUnusualActivity);` - Creates an Intrinio Real-Time client. The provided actions implement OnTrade, OnQuote, OnRefresh, and OnUnusualActivity which handle what happens when the associated event happens.
* **Parameter** `onTrade`: The Action accepting trades. If no `onTrade` callback is provided, you will not receive trade updates from the server.
* **Parameter** `onQuote`: The Action accepting quotes. If no `onQuote` callback is provided, you will not receive quote (ask, bid) updates from the server.
* **Parameter** `onRefresh`: The Action accepting refresh messages. If no `onRefresh` callback is provided, you will not receive open interest, open, close, high, low data from the server. Note: open interest data is only updated at the beginning of every trading day. If this callback is provided you will recieve an update immediately, as well as every 15 minutes (approx).
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