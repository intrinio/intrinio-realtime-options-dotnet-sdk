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
		private static int refreshCount = 0;
		private static int blockCount = 0;
		private static int sweepCount = 0;
		private static int largeTradeCount = 0;
		private static int goldenCount = 0;

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

		static void OnRefresh(Refresh refresh)
		{
			Interlocked.Increment(ref refreshCount);
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
            } else if (unusualActivity.Type == UAType.Golden)
			{
				Interlocked.Increment(ref goldenCount);
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
			Client.Log("PROGRAM STATS - Trades = {0}, Asks = {1}, Bids = {2}, OIs = {3}, Blocks = {4}, Sweeps = {5}, Large Trades = {6}, Golden = {7}", tradeCount, askCount, bidCount, refreshCount, blockCount, sweepCount, largeTradeCount, goldenCount);
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
			client = new Client(onTrade: OnTrade, onQuote: OnQuote, onOpenInterest: OnRefresh, onUnusualActivity: OnUnusualActivity);
			
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
