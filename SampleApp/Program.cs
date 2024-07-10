using System;
using System.Threading;
using Intrinio.Realtime.Options;
using Serilog;
using Serilog.Core;

namespace SampleApp
{
	class Program
	{
		private static IOptionsWebSocketClient _client = null;
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
					Log("Invalid UA type detected: {0}", unusualActivity.UnusualActivityType);
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
			IOptionsWebSocketClient client = (IOptionsWebSocketClient) obj;
			ClientStats stats = client.GetStats();
			Log("CLIENT STATS - Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}, Individual Events = {3}, Trades = {4}, Quotes = {5}", stats.SocketDataMessages(), stats.SocketTextMessages(), stats.QueueDepth(), stats.EventCount(), stats.TradeCount(), stats.QuoteCount());
			Log("EVENT STATS - Trades = {0}, Quotes = {1}, Refreshes = {2}, Blocks = {3}, Sweeps = {4}, Large Trades = {5}, UnusualSweeps = {6}", _tradeCount, _quoteCount, _refreshCount, _blockCount, _sweepCount, _largeTradeCount, _unusualSweepCount);
			if (_useTradeCandleSticks)
				Log("TRADE CANDLESTICK STATS - TradeCandleSticks = {0}, TradeCandleSticksIncomplete = {1}", _tradeCandleStickCount, _tradeCandleStickCountIncomplete);
			if (_useQuoteCandleSticks)
				Log("QUOTE CANDLESTICK STATS - Asks = {0}, Bids = {1}, AsksIncomplete = {2}, BidsIncomplete = {3}", _AskCandleStickCount, _BidCandleStickCount, _AskCandleStickCountIncomplete, _BidCandleStickCountIncomplete);
		}

		static void Cancel(object sender, ConsoleCancelEventArgs args)
		{
			Log("Stopping sample app");
			_timer.Dispose();
			_client.Stop();
			if (_useTradeCandleSticks || _useQuoteCandleSticks)
			{
				_candleStickClient.Stop();
			}

			Environment.Exit(0);
		}
		
		[MessageTemplateFormatMethod("messageTemplate")]
		static void Log(string messageTemplate, params object[] propertyValues)
		{
			Serilog.Log.Information(messageTemplate, propertyValues);
		}
		
		static void Main(string[] args)
		{
			Action<Trade> onTrade = OnTrade;
			Action<Quote> onQuote = OnQuote;
			
			// //You can either automatically load the config.json by doing nothing, or you can specify your own config and pass it in.
			// //If you don't have a config.json, don't forget to also give Serilog a config so it can write to console
			// Serilog.Log.Logger = new LoggerConfiguration().WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information).CreateLogger();
			// Config.Config config = new Config.Config();
			// config.Provider = Provider.OPRA;
			// config.ApiKey = "";
			// config.Symbols = new string[] { };
			// config.NumThreads = 32;
			Log("Starting sample app");
			
			// // Subscribe the candlestick client to trade and/or quote events as well.  It's important any method subscribed this way handles exceptions so as to not cause issues for other subscribers!
			// _useTradeCandleSticks = true;
			// _useQuoteCandleSticks = false;
			// _candleStickClient = new CandleStickClient(OnTradeCandleStick, null, IntervalType.OneMinute, true, null, null, 0.0);
			// onTrade += _candleStickClient.OnTrade;
			// onQuote += _candleStickClient.OnQuote;
			// _candleStickClient.Start();
			
			//_client = new Client(onTrade, onQuote, OnRefresh, OnUnusualActivity, config);

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
			
			_timer = new Timer(TimerCallback, _client, 30_000, 30_000);

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
