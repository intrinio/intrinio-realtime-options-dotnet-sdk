# intrinio-realtime-options-dotnet-sdk
SDK for working with Intrinio's realtime options feed via WebSocket

[Intrinio](https://intrinio.com/) provides real-time stock option prices via a two-way WebSocket connection. To get started, [subscribe to a real-time data feed](https://intrinio.com/financial-market-data/options-data) and follow the instructions below.

## Requirements

- .NET 8+

## Docker
Add your API key to the config.json file in SampleApp, then
```
docker compose build
docker compose run example
```

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
using System.Collections;
using System.Threading;
using Intrinio;


namespace SampleApp
{
	class Program
	{
		private static Client _client = null;
		private static CandleStickClient _candleStickClient = null;
		private static Timer _timer = null;
		private static UInt64 _tradeCount = 0UL;
		private static UInt64 _quoteCount = 0UL;
		private static UInt64 _refreshCount = 0UL;
		private static UInt64 _blockCount = 0UL;
		private static UInt64 _sweepCount = 0UL;
		private static UInt64 _largeTradeCount = 0UL;
		private static UInt64 _unusualSweepCount = 0UL;
		private static UInt64 _tradeCandleStickCount = 0UL;
		private static UInt64 _tradeCandleStickCountIncomplete = 0UL;
		private static UInt64 _AskCandleStickCount = 0UL;
		private static UInt64 _AskCandleStickCountIncomplete = 0UL;
		private static UInt64 _BidCandleStickCount = 0UL;
		private static UInt64 _BidCandleStickCountIncomplete = 0UL;
		private static bool _useTradeCandleSticks = false;
		private static bool _useQuoteCandleSticks = false;

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
			switch (unusualActivity.UnusualActivityType)
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
					Client.Log("Invalid UA type detected: {0}", unusualActivity.UnusualActivityType);
					break;
			}
		}
		
		static void OnTradeCandleStick(TradeCandleStick tradeCandleStick)
		{
			if (tradeCandleStick.Complete)
			{
				Interlocked.Increment(ref _tradeCandleStickCount);
			}
			else
			{
				Interlocked.Increment(ref _tradeCandleStickCountIncomplete);
			}
		}
		
		static void OnQuoteCandleStick(QuoteCandleStick quoteCandleStick)
		{
			if (quoteCandleStick.QuoteType == QuoteType.Ask)
				if (quoteCandleStick.Complete)
					Interlocked.Increment(ref _AskCandleStickCount);
				else
					Interlocked.Increment(ref _AskCandleStickCountIncomplete);
			else
				if (quoteCandleStick.Complete)
					Interlocked.Increment(ref _BidCandleStickCount);
				else
					Interlocked.Increment(ref _BidCandleStickCountIncomplete);
		}

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<UInt64, UInt64, int> stats = client.GetStats();
			Client.Log("CLIENT STATS - Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			Client.Log("EVENT STATS - Trades = {0}, Quotes = {1}, Refreshes = {2}, Blocks = {3}, Sweeps = {4}, Large Trades = {5}, UnusualSweeps = {6}", _tradeCount, _quoteCount, _refreshCount, _blockCount, _sweepCount, _largeTradeCount, _unusualSweepCount);
			if (_useTradeCandleSticks)
				Client.Log("TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _tradeCandleStickCount, _tradeCandleStickCountIncomplete);
			if (_useQuoteCandleSticks)
				Client.Log("QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _AskCandleStickCount, _BidCandleStickCount, _AskCandleStickCountIncomplete, _BidCandleStickCountIncomplete);
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Client.Log("Stopping sample app");
			_timer.Dispose();
			_client.Stop();
			if (_useTradeCandleSticks || _useQuoteCandleSticks)
			{
				_candleStickClient.Stop();
			}

			Environment.Exit(0);
		}
		
		static void Main(string[] args)
		{
			Client.Log("Starting sample app");
			Action<Trade> onTrade = OnTrade;
			Action<Quote> onQuote = OnQuote;
			
			// Subscribe the candlestick client to trade and/or quote events as well.  It's important any method subscribed this way handles exceptions so as to not cause issues for other subscribers!
			//_useTradeCandleSticks = true;
			//_useQuoteCandleSticks = true;
			//_candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, IntervalType.OneMinute, true);
			//onTrade += _candleStickClient.OnTrade;
			//onQuote += _candleStickClient.OnQuote;
			//_candleStickClient.Start();
			
			// Register only the callbacks that you want.
			// Take special care when registering the 'OnQuote' handler as it will increase throughput by ~10x
			_client = new Client(onTrade: onTrade, onQuote: onQuote, onRefresh: OnRefresh, onUnusualActivity: OnUnusualActivity);
            
            // Alternatively, you can programmatically make your configuration if you don't want to use a config.json file
			// Config.Config config = new Config.Config()
			// {
			// 	ApiKey = "",
			// 	Delayed = false, //Use this to specify that even though you have realtime access, for this connection you want delayed 15minute
			// 	NumThreads = 8,
			// 	Provider = Provider.OPRA,
			// 	Symbols = new string[]{"AAPL", "MSFT"}
			// };
			// _client = new Client(onTrade: onTrade, onQuote: onQuote, onRefresh: OnRefresh, onUnusualActivity: OnUnusualActivity, config: config);
			
			_timer = new Timer(TimerCallback, _client, 10_000, 10_000);

			// Use this to subscribe to a static list of symbols (option contracts) provided in config.json
			_client.Join();

			// Use this to subscribe to the entire universe of symbols (option contracts). This requires special permission.
			//_client.JoinLobby();

			// Use this to subscribe, dynamically, to an option chain (all option contracts for a given underlying symbol).
			//_client.Join("AAPL");

			// Use this to subscribe, dynamically, to a specific option contract.
			//_client.Join("AAPL_230616P250.000");

			// Use this to subscribe, dynamically, a list of specific option contracts or option chains.
			//string[] contracts = { "GOOG__220408C02870000", "MSFT__220408C00315000", "AAPL__220414C00180000", "TSLA", "GE" };
            //_client.Join(contracts);

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
* **Exchange** - Enum identifying the specific exchange through which the trade occurred
* **Price** - the price in USD
* **Size** - the size of the last trade in hundreds (each contract is for 100 shares).
* **TotalVolume** - The number of contracts traded so far today.
* **Timestamp** - a Unix timestamp (with microsecond precision)
* **Qualifiers** - a 4-byte tuple: each byte represents one trade qualifier. See list of possible [Trade Qualifiers](#trade-qualifiers), below. 
* **AskPriceAtExecution** - the best last ask price in USD
* **BidPriceAtExecution** - the best last bid price in USD
* **UnderlyingPriceAtExecution** - the price of the underlying security in USD


### Trade Qualifiers

The trade qualifiers field is represented by a tuple containing 4 integers. Each integer can take one of the following values:
* **`0`** - Regular transaction
* **`2`** - Cancel
* **`3`** - This is the last price and it's cancelled
* **`4`** - Late but in sequence / sold last late
* **`5`** - This was the open price and it's cancelled
* **`6`** - Late report of opening trade and is out of sequence: or set the open
* **`7`** - Cancel only trade reported
* **`8`** - Transaction was executed electronically
* **`9`** - Reopen of a previously halted contract
* **`11`** - Spread
* **`23`** - Intermarket Sweep
* **`30`** - Extended hours
* **`33`** - Crossed trade including Request For Cross RFC
* **`87`** - Complex trade with equity leg
* **`107`** - Auction
* **`123`** - Stock option trade
* **`136`** - Ex-Pit trade
* **`192`** - Message received locally out-of-sequence
* **`222`** - Combo trade
* **`0`** - Blank

Each trade can be qualified by a maximum of 4(four) values. The combination of these values can have special values. These special values are:

* **`107, 23`** - Single leg auction ISO
* **`23, 33`** - Single leg cross ISO
* **`8, 11`** - Multi leg auto-electronic trade
* **`107, 11`** - Multi leg auction
* **`11, 33`** - Multi leg cross
* **`136, 11`** - Multi leg floor trade
* **`8, 11, 87`** - Multi leg auto-electronic trade against single leg(s)
* **`107, 123`** - Stock options auction
* **`107, 11, 87`** - Multi leg auction against single leg(s)
* **`136, 11, 87`** - Multi leg floor trade against single leg(s)
* **`8, 123`** - Stock options auto-electronic trade
* **`123, 33`** - Stock options cross
* **`136, 123`** - Stock options floor trade
* **`8, 87, 123`** - Stock options auto-electronic trade against single leg(s)
* **`107, 87, 123`** - Stock options auction against single leg(s)
* **`136, 87, 123`** - Stock options floor trade against single leg(s)
* **`136, 11, 222`** - Multi leg floor trade of proprietary products
* **`222, 30`** - Multilateral Compression Trade of Proprietary Data Products


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
		"Provider": "OPRA",
		"Symbols": [ "GOOG__210917C01040000", "MSFT__210917C00180000", "AAPL__210917C00130000", "SPY" ], //This is a list of individual contracts (or option chains) to subscribe to.
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