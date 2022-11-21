namespace Intrinio

open Intrinio
open Serilog
open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open WebSocket4Net
open Intrinio.Config
open FSharp.NativeInterop
open System.Runtime.CompilerServices

module private CandleStickClientInline =

    [<SkipLocalsInit>]
    let inline private stackalloc<'a when 'a: unmanaged> (length: int): Span<'a> =
        let p = NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr
        Span<'a>(p, length)
        
type CandleStickClient(
    [<Optional; DefaultParameterValue(null:Action<TradeCandleStick>)>] onTradeCandleStick : Action<TradeCandleStick>,
    [<Optional; DefaultParameterValue(null:Action<QuoteCandleStick>)>] onQuoteCandleStick : Action<QuoteCandleStick>,
    CandleStickSeconds : float) =
    
    let tLock : ReaderWriterLockSlim = new ReaderWriterLockSlim()
    
    member _.OnTrade(trade: Trade) : unit = ()
    
    member _.OnQuote(quote: Quote) : unit = ()
        