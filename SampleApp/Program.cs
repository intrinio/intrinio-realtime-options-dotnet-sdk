using System;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
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
		private static int oiCount = 0;
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
			Interlocked.Increment(ref oiCount);
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
			Client.Log("PROGRAM STATS - Trades = {0}, Asks = {1}, Bids = {2}, OIs = {3}, Blocks = {4}, Sweeps = {5}, Large Trades = {6}", tradeCount, askCount, bidCount, oiCount, blockCount, sweepCount, largeTradeCount);
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
			client = new Client(onTrade: OnTrade, onQuote: OnQuote, onOpenInterest: OnOpenInterest, onUnusualActivity: OnUnusualActivity);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			//client.Join();
			client.Join("MSFT");
			//client.Join("AAP___230616P00250000");
			//string[] clients = { "GOOG__220408C02870000", "MSFT__220408C00315000", "AAPL__220414C00180000" };
            //client.Join(clients, false);
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}		
	}
}
