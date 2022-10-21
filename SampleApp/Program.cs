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
		private static UInt64 _goldenCount = 0L;

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
				case UAType.Golden:
					Interlocked.Increment(ref _goldenCount);
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
			Client.Log("PROGRAM STATS - Trades = {0}, Quotes = {1}, Refreshes = {2}, Blocks = {3}, Sweeps = {4}, Large Trades = {5}, Golden = {6}", _tradeCount, _quoteCount, _refreshCount, _blockCount, _sweepCount, _largeTradeCount, _goldenCount);
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
			//client.Join();

			// Use this to subscribe to the entire universe of symbols (option contracts). This requires special permission.
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
