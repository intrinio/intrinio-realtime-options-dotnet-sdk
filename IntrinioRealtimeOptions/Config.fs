namespace Intrinio.Realtime.Options

module Config =

    open System
    open Serilog
    open System.IO
    open Microsoft.Extensions.Configuration
    
    let inline internal TranslateContract(contract: string) : string =
        if ((contract.Length <= 9) || (contract.IndexOf(".")>=9))
        then
            contract
        else //this is of the old format and we need to translate it to what the server understands. input: AAPL__220101C00140000, TSLA__221111P00195000 
            let symbol : string = contract.Substring(0, 6).TrimEnd('_')
            let date : string = contract.Substring(6, 6)
            let callPut : char = contract[12]
            let mutable wholePrice : string = contract.Substring(13, 5).TrimStart('0')
            if wholePrice = String.Empty
            then wholePrice <- "0" 
            let mutable decimalPrice : string = contract.Substring(18)
            if decimalPrice[2] = '0'
            then
                decimalPrice <- decimalPrice.Substring(0, 2)
            String.Format($"{symbol}_{date}{callPut}{wholePrice}.{decimalPrice}")

    type Config () =
        let mutable apiKey : string = String.Empty
        let mutable provider : Provider = Provider.NONE        
        let mutable ipAddress : string = String.Empty
        let mutable numThreads : int = 4
        let mutable symbols : string[] = [||]
        
        member this.ApiKey with get () : string = apiKey and set (value : string) = apiKey <- value
        member this.Provider with get () : Provider = provider and set (value : Provider) = provider <- value
        member this.IPAddress with get () : string = ipAddress and set (value : string) = ipAddress <- value
        member this.Symbols with get () : string[] = symbols and set (value : string[]) = symbols <- value
        member this.NumThreads with get () : int = numThreads and set (value : int) = numThreads <- value
        
        member _.Validate() : unit =
            if String.IsNullOrWhiteSpace(apiKey)
            then failwith "You must provide a valid API key"
            if (provider = Provider.NONE)
            then failwith "You must specify a valid 'provider'"
            if ((provider = Provider.MANUAL) && (String.IsNullOrWhiteSpace(ipAddress)))
            then failwith "You must specify an IP address for manual configuration"
            if (numThreads <= 0)
            then failwith "You must specify a valid 'NumThreads'"
            for i = 0 to symbols.Length-1 do
                symbols[i] <- TranslateContract(symbols[i])
    
    let LoadConfig() =
        Log.Information("Loading application configuration")
        let _config = ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json").Build()
        Log.Logger <- LoggerConfiguration().ReadFrom.Configuration(_config).CreateLogger()
        let mutable config = new Config()
        for (KeyValue(key,value)) in _config.AsEnumerable() do Log.Debug("Key: {0}, Value:{1}", key, value)
        _config.Bind("Config", config)
        config.Validate()
        config