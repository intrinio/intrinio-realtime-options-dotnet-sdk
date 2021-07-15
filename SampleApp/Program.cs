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
		private static readonly ConcurrentDictionary<string, int> symbols = new ConcurrentDictionary<string, int>(5, 1_500_000);
		private static readonly ConcurrentDictionary<string, int> openInterest = new ConcurrentDictionary<string, int>(5, 1_500_000);
		private static int maxCount = 0;
		private static Quote maxCountQuote;

		private static readonly object obj = new object();

		static void OnMessage(SocketMessage socketMessage)
		{
			if (socketMessage.IsQuote)
			{
				string key = socketMessage.quote.Symbol + ":" + socketMessage.quote.Type;
				if (!symbols.ContainsKey(key)) {
					symbols[key] = 1;
				} else
				{
					symbols[key]++;
				}
				if (symbols[key] > maxCount)
				{
					lock (obj)
					{
						maxCount++;
						maxCountQuote = socketMessage.quote;
					}
				}
			}
			else if (socketMessage.IsTrade)
			{
				string key = socketMessage.trade.Symbol + ":trade";
				if (!symbols.ContainsKey(key)) {
					symbols[key] = 1;
				} else
				{
					symbols[key]++;
				}
			}
			else if (socketMessage.IsOpenInterest)
			{
				string key = socketMessage.openInterest.Symbol;

				symbols[key] = socketMessage.openInterest.OpenInterest;
				Client.Log("{0} openInterest updated to {1}", socketMessage.openInterest.Symbol,
					socketMessage.openInterest.OpenInterest);
				
			}
		}
		

		static void TimerCallback(object obj)
		{
			Client client = (Client) obj;
			Tuple<Int64, Int64, int> stats = client.GetStats();
			Client.Log("Data Messages = {0}, Text Messages = {1}, Queue Depth = {2}", stats.Item1, stats.Item2, stats.Item3);
			if (maxCount > 0)
			{
				Client.Log("Most active symbol: {0}:{1} ({2} updates)", maxCountQuote.Symbol, maxCountQuote.Type, maxCount);
			}
			if (!maxCountQuote.Equals(new Quote())) {
				Quote quote = maxCountQuote;
				Client.Log("{0} (type = {1}, strike price = {2}, isPut = {3}, isCall = {4}, expiration = {5})", quote.Symbol, quote.Type, quote.GetStrikePrice(), quote.IsPut(), quote.IsCall(), quote.GetExpirationDate());
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
			client = new Client(OnMessage);
			timer = new Timer(TimerCallback, client, 10000, 10000);
			client.Join();
			Console.CancelKeyPress += new ConsoleCancelEventHandler(Cancel);
		}

		
	}
}
