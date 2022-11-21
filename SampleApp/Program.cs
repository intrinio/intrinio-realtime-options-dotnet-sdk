using System;
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
		private static UInt64 _AskCandleStickCount = 0UL;
		private static UInt64 _BidCandleStickCount = 0UL;
		private static bool _useCandleSticks = false;

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
			Interlocked.Increment(ref _tradeCandleStickCount);
		}
		
		static void OnQuoteCandleStick(QuoteCandleStick quoteCandleStick)
		{
			if (quoteCandleStick.QuoteType == QuoteType.Ask)
				Interlocked.Increment(ref _AskCandleStickCount);
			else
				Interlocked.Increment(ref _BidCandleStickCount);
		}

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<UInt64, UInt64, int> stats = client.GetStats();
			Client.Log("CLIENT STATS - Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			Client.Log("EVENT STATS - Trades = {0}, Quotes = {1}, Refreshes = {2}, Blocks = {3}, Sweeps = {4}, Large Trades = {5}, UnusualSweeps = {6}", _tradeCount, _quoteCount, _refreshCount, _blockCount, _sweepCount, _largeTradeCount, _unusualSweepCount);
			if (_useCandleSticks)
				Client.Log("CANDLESTICK STATS - TradeCandleSticks = {0}, AskCandleSticks = {1}, BidCandleSticks = {2}", _tradeCandleStickCount, _AskCandleStickCount, _BidCandleStickCount);
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
			Action<Trade> onTrade = OnTrade;
			Action<Quote> onQuote = OnQuote;
			
			//Subscribe the candlestick client to trade and quote events as well.  It's important any method subscribed this way handle exceptions so as to not cause issues for other subscribers!
			_useCandleSticks = true;
			_candleStickClient = new CandleStickClient(OnTradeCandleStick, OnQuoteCandleStick, 60.0);
			onTrade += _candleStickClient.OnTrade;
			onQuote += _candleStickClient.OnQuote;
			

			// Register only the callbacks that you want.
			// Take special care when registering the 'OnQuote' handler as it will increase throughput by ~10x
			_client = new Client(onTrade: onTrade, onQuote: onQuote, onRefresh: OnRefresh, onUnusualActivity: OnUnusualActivity);
			
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
			//string[] clients = { "GOOG_220408C2870.000", "MSFT_220408C315.000", "AAPL_220414C180.000", "TSLA", "GE" };
            //_client.Join(clients);

			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}
