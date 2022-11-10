namespace Intrinio

module Config =

    open System
    open Serilog
    open System.IO
    open Microsoft.Extensions.Configuration

    type Config () =
        member val ApiKey : string = null with get, set
        member val Provider : Provider = Provider.NONE with get, set
        member val IPAddress : string = null with get, set
        member val Symbols: string[] = [||] with get, set
        member val NumThreads: int = 4 with get, set
        
    let inline internal TranslateContract(contract: string) : string =
        if ((contract.Length <= 9) || (contract.IndexOf(".")>=9))
        then
            contract
        else //this is of the old format and we need to translate it. ex: AAPL__220101C00140000, TSLA__221111P00195000
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

    let LoadConfig() =
        Log.Information("Loading application configuration")
        let _config = ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json").Build()
        Log.Logger <- LoggerConfiguration().ReadFrom.Configuration(_config).CreateLogger()
        let mutable config = new Config()
        for (KeyValue(key,value)) in _config.AsEnumerable() do Log.Debug("Key: {0}, Value:{1}", key, value)
        _config.Bind("Config", config)
        if String.IsNullOrWhiteSpace(config.ApiKey)
        then failwith "You must provide a valid API key"
        if (config.Provider = Provider.NONE)
        then failwith "You must specify a valid 'provider'"
        if (config.Provider = Provider.MANUAL) && (String.IsNullOrWhiteSpace(config.IPAddress))
        then failwith "You must specify an IP address for manual configuration"
        for i = 0 to config.Symbols.Length-1 do
            config.Symbols[i] <- TranslateContract(config.Symbols[i])
        config