using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web;
using Newtonsoft.Json.Linq;
using RestSharp;
using TradeServer.ScriptingDriver.ScriptApi.Enums;
using TradeServer.ScriptingDriver.ScriptApi.Interfaces;

namespace Haasonline.Public.ExchangeDriver.Xt
{
    public class XtApi : IScriptApi
    {
        // NOTA: CAMBIAR A USDT/TLW -- TLW/USDT, algunas llamadas del api solo funcionarán para este mercado
        private const string DEFAULT_MARKET = "bnb_usdt";

        private string _publicKey;
        private string _privateKey;
        private string _extra;

        private long _lastNonce;
        private readonly string _apiUrl = "https://api.xt.com";
        private HMACSHA256 _hmac = new HMACSHA256();

        public string PingAddress { get; set; }

        public int PollingSpeed { get; set; }
        public ScriptedExchangeType PlatformType { get; set; }

        public bool HasTickerBatchCalls { get; set; }
        public bool HasOrderbookBatchCalls { get; set; }
        public bool HasLastTradesBatchCalls { get; set; }

        public bool HasPrivateKey { get; set; }
        public bool HasExtraPrivateKey { get; set; }

        public event EventHandler<string> Error;
        public event EventHandler<IScriptTick> PriceUpdate;
        public event EventHandler<IScriptOrderbook> OrderbookUpdate;
        public event EventHandler<IScriptOrderbook> OrderbookCorrection;
        public event EventHandler<IScriptLastTrades> LastTradesUpdate;
        public event EventHandler<Dictionary<string, decimal>> WalletUpdate;
        public event EventHandler<Dictionary<string, decimal>> WalletCorrection;
        public event EventHandler<List<IScriptPosition>> PositionListUpdate;
        public event EventHandler<List<IScriptPosition>> PositionCorrection;
        public event EventHandler<List<IScriptOrder>> OpenOrderListUpdate;
        public event EventHandler<List<IScriptOrder>> OpenOrderCorrection;

        private readonly object _lockObject = new object();

        public XtApi()
        {
            PingAddress = "https://www.xt.com:80";
            PollingSpeed = 30;
            PlatformType = ScriptedExchangeType.Spot;

            HasTickerBatchCalls = true;
            HasOrderbookBatchCalls = false;
            HasLastTradesBatchCalls = false;

            HasPrivateKey = true;
            HasExtraPrivateKey = false;
        }

        public void SetCredentials(string publicKey, string privateKey, string extra)
        {
            _publicKey = publicKey;
            _privateKey = privateKey;
            _extra = extra;

            _hmac.Key = Encoding.UTF8.GetBytes(privateKey);
        }

        public void Connect()
        {
            // Start websocket is needed
        }

        public void Disconnect()
        {
            // Start websocket is needed
        }

        #region Public API
        public List<IScriptMarket> GetMarkets()
        {
            List<IScriptMarket> markets = null;
            try
            {
                var response = Query(false, "/data/api/v1/getMarketConfig");

                Console.Out.WriteLine("Message sended");

                if (response != null)
                {
                    markets = new List<IScriptMarket>();

                    foreach (JToken item in response)
                    {
                        string aux = item.ToString();
                        int index = aux.IndexOf(":");
                        aux = aux.Substring(0, index);
                        aux = aux.Replace("\"", String.Empty);

                        index = aux.IndexOf("_");
                        string primaryCurrency = aux.Substring(0, index);
                        string secondaryCurrency = aux.Substring(index + 1, aux.Length - primaryCurrency.Length - 1);

                        JToken token = item.First;
                        string minAmount = token.Value<string>("minAmount");
                        string minMoney = token.Value<string>("minMoney");
                        string pricePoint = token.Value<string>("pricePoint");
                        string coinPoint = token.Value<string>("coinPoint");
                        

                        //decimal feeMaker = Decimal.Parse(makerFee, NumberStyles.Float, CultureInfo.InvariantCulture);
                        //decimal feeTaker = Decimal.Parse(takerFee, NumberStyles.Float, CultureInfo.InvariantCulture);
                        //int priceDecimals = int.Parse(pricePoint, CultureInfo.InvariantCulture);
                        //int amountDecimals = int.Parse(coinPoint, CultureInfo.InvariantCulture);

                        markets.Add(new Market(primaryCurrency, secondaryCurrency, minAmount, 
                            minMoney, pricePoint, coinPoint));
                    }
                }
            }
            catch (Exception ex)
            {
                OnError(ex.Message);
            }

            return markets;
        }

        public List<IScriptMarket> GetMarginMarkets()
        {
            return null;
        }

        public IScriptTick GetTicker(IScriptMarket market)
        {
            IScriptTick ticker = null;

            try
            {
                var response = Query(false, "/data/api/v1/getTicker?", new Dictionary<string, string>()
                {
                    {"market",market.PrimaryCurrency.ToLower() + "_" + market.SecondaryCurrency.ToLower()},
                });

                if (response != null)
                {
                    decimal close = 0.0M, buyPrice = 0.0M, sellPrice = 0.0M;

                    string price = response.Value<string>("price");
                    string ask = response.Value<string>("ask");
                    string bid = response.Value<string>("bid");

                    if (price != null)
                        close = Decimal.Parse(price, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (ask != null)
                        buyPrice = Decimal.Parse(ask, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (bid != null)
                        sellPrice = Decimal.Parse(bid, NumberStyles.Float, CultureInfo.InvariantCulture);

                    if (close != 0.0M)
                        ticker = new Ticker(close, buyPrice, sellPrice, market.PrimaryCurrency, market.SecondaryCurrency);
                }
            }
            catch (Exception ex)
            {
                OnError(ex.Message);
            }
            return ticker;
        }

        public List<IScriptTick> GetAllTickers()
        {
            List<IScriptTick> tickers = null;

            try
            {
                var response = Query(false, "/data/api/v1/getTickers");

                if (response != null)
                {
                    tickers = new List<IScriptTick>();

                    foreach (JToken item in response)
                    {
                        decimal close = 0.0M, buyPrice = 0.0M, sellPrice = 0.0M;

                        string aux = item.ToString();
                        int index = aux.IndexOf(":");
                        aux = aux.Substring(0, index);
                        aux = aux.Replace("\"", String.Empty);

                        index = aux.IndexOf("_");
                        string primaryCurrency = aux.Substring(0, index);
                        string secondaryCurrency = aux.Substring(index + 1, aux.Length - primaryCurrency.Length - 1);

                        JToken token = item.First;
                        string price = token.Value<string>("price");
                        string ask = token.Value<string>("ask");
                        string bid = token.Value<string>("bid");

                        if (price != null)
                            close = Decimal.Parse(price, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (ask != null)
                            buyPrice = Decimal.Parse(ask, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (bid != null)
                            sellPrice = Decimal.Parse(bid, NumberStyles.Float, CultureInfo.InvariantCulture);

                        if (close != 0.0M)
                            tickers.Add(new Ticker(close, buyPrice, sellPrice, primaryCurrency, secondaryCurrency));
                    }
                }
            }
            catch (Exception ex)
            {
                OnError(ex.Message);
            }

            return tickers;
        }

        public IScriptOrderbook GetOrderbook(IScriptMarket market)
        {
            IScriptOrderbook orderbook = null;

            try
            {
                var response = Query(false, "/data/api/v1/getDepth?", new Dictionary<string, string>()   //getBatchOrders?
                {
                    {"market", market.PrimaryCurrency.ToLower() + "_" + market.SecondaryCurrency.ToLower() },
                });

                if (response != null)
                    orderbook = new Orderbook(response.Value<JArray>("bids"), response.Value<JArray>("asks"));
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return orderbook;
        }

        public List<IScriptOrderbook> GetAllOrderbooks()
        {
            return null;
        }

        public IScriptLastTrades GetLastTrades(IScriptMarket market)
        {
            LastTradesContainer trades = null;

            try
            {
                var response = Query(false, "/data/api/v1/getTrades?", new Dictionary<string, string>()
                    {
                        {"market",market.PrimaryCurrency.ToLower() + "_" + market.SecondaryCurrency.ToLower()},
                    });

                if (response != null)
                {
                    trades = new LastTradesContainer();
                    trades.Market = market;
                    List<IScriptOrder> tradeList = new List<IScriptOrder>();

                    JArray arr = response.Value<JArray>();
                    foreach (var transaction in arr)
                        tradeList.Add(Trade.ParsePublicTrade(market, transaction.Value<JArray>()));

                    trades.Trades = tradeList;
                }
            }
            catch (Exception e)
            {
                OnError(e.Message
                    + "\n\t" + market.PrimaryCurrency + "_" + market.SecondaryCurrency);
            }

            return trades;
        }

        public List<IScriptLastTrades> GetAllLastTrades()
        {
            return null;
        }
        #endregion

        #region Private API
        public Dictionary<string, decimal> GetWallet()
        {
            Dictionary<string, decimal> wallet = null;

            try
            {
                var response = Query(true, "/trade/api/v1/getBalance?", new Dictionary<string, string>()
                {
                    { "accesskey", _publicKey},
                    { "nonce", CurrentTimeMillis().ToString()},
                });

                if (response != null)
                {
                    wallet = new Dictionary<string, decimal>();
                    JObject data = response.Value<JObject>("data");

                    foreach (var entry in data)
                        wallet.Add(entry.Key,
                            Decimal.Parse(entry.Value.Value<string>("available"), NumberStyles.Float, CultureInfo.InvariantCulture));
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return wallet;
        }

        public IScriptMarginWallet GetMarginWallet()
        {
            return null;
        }

        public List<IScriptOrder> GetOpenOrders()
        {
            List<IScriptOrder> orders = null;

            try
            {
                var responseA = Query(true, "/trade/api/v1/getOpenOrders?", new Dictionary<string, string>()
                {
                    { "accesskey", _publicKey },
                    { "market", DEFAULT_MARKET },
                    { "nonce", CurrentTimeMillis().ToString()},
                    { "page", "1"},
                    { "pageSize", "50" }
                });

                orders = new List<IScriptOrder>();

                if (responseA != null)
                {
                    JArray arrA = responseA.Value<JArray>("data");
                    foreach (var order in arrA)
                    {
                        orders.Add(Order.ParseOpenOrder(order as JObject, DEFAULT_MARKET));
                    }
                }

                if (orders.Count == 0)
                    return null;
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }


            return orders;
        }

        public List<IScriptPosition> GetPositions()
        {
            return null;
        }

        public List<IScriptOrder> GetTradeHistory()
        {
            List<IScriptOrder> trades = null;

            try
            {
                var parameters = new Dictionary<string, string>() {
                    { "accesskey", _publicKey },
                    { "market", DEFAULT_MARKET},
                    { "nonce", CurrentTimeMillis().ToString()},
                };

                var response = Query(true, "/trade/api/v1/myTrades?", parameters);

                if (response != null)
                {
                    trades = new List<IScriptOrder>();
                    JArray arr = response.Value<JArray>("data");

                    foreach (var trade in arr)
                    {
                        trades.Add(Trade.ParsePrivateTrade(trade as JObject, DEFAULT_MARKET));
                    }
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return trades;
        }

        public string PlaceOrder(IScriptMarket market, ScriptedOrderType direction, decimal price, decimal amount, bool isMarketOrder, string template = "", bool hiddenOrder = false)
        {
            var result = "";

            if (Monitor.TryEnter(_lockObject, 30000))
                try
                {
                    Dictionary<string, string> args;
                    if (isMarketOrder)
                    {
                        args = new Dictionary<string, string>
                        {
                            { "accesskey", _publicKey },
                            { "entrustType", "1" },     // 0 -> limitPrice ..... 1 -> marketPrice
                            { "market", market.PrimaryCurrency.ToLower() + "_" + market.SecondaryCurrency.ToLower() },
                            { "nonce", CurrentTimeMillis().ToString() },
                            { "number", price.ToString().Replace(',', '.') },
                            { "type", direction == ScriptedOrderType.Buy ? "1" : "0" }  // 1 -> buy ..... 0 -> sell
                        };
                    }
                    else // limit order
                    {
                        args = new Dictionary<string, string>()
                        {
                            { "accesskey", _publicKey },
                            { "entrustType", "0" },     // 0 -> limitPrice ..... 1 -> marketPrice
                            { "market", market.PrimaryCurrency.ToLower() + "_" + market.SecondaryCurrency.ToLower() },
                            { "nonce", CurrentTimeMillis().ToString() },
                            { "number", amount.ToString().Replace(',', '.') },
                            { "price", price.ToString().Replace(',', '.') },  // only requiered with limitPrice
                            { "type", direction == ScriptedOrderType.Buy ? "1" : "0" }  // 1 -> buy ..... 0 -> sell
                        };
                    }

                    RestClient client = new RestClient(_apiUrl + "/trade/api/v1/order");
                    RestRequest request = new RestRequest(Method.POST);

                    foreach (var arg in args)
                        request.AddParameter(arg.Key, arg.Value);

                    var dataStr = BuildPostDataNoUrlEncode(args);
                    string signature = ByteToString(_hmac.ComputeHash(Encoding.UTF8.GetBytes(dataStr.ToString()))).ToLower();

                    request.AddParameter("signature", signature);

                    var responseRaw = client.Execute(request).Content;
                    var response = JToken.Parse(responseRaw);

                    if (response != null && response.Value<int>("code") == 200)
                        result = response.Value<JObject>("data").Value<string>("id");
                }
                catch (Exception ex)
                {
                    OnError(ex.Message);
                }
                finally
                {
                    Monitor.Exit(_lockObject);
                }
            return result;
        }

        public string PlaceOrder(IScriptMarket market, ScriptedLeverageOrderType direction, decimal price, decimal amount, decimal leverage, bool isMarketOrder, string template = "", bool isHiddenOrder = false)
        {
            return null;
        }

        public bool CancelOrder(IScriptMarket market, string orderId, bool isBuyOrder)
        {
            var result = false;

            if (Monitor.TryEnter(_lockObject, 30000))
                try
                {
                    var args = new Dictionary<string, string>()
                    {
                        { "accesskey", _publicKey },
                        { "id", orderId },
                        { "market", market.PrimaryCurrency.ToLower() + "_" + market.SecondaryCurrency.ToLower()},
                        { "nonce", CurrentTimeMillis().ToString()},
                    };

                    RestClient client = new RestClient(_apiUrl + "/trade/api/v1/cancel/");
                    RestRequest request = new RestRequest(Method.POST);

                    foreach (var arg in args)
                        request.AddParameter(arg.Key, arg.Value);

                    var dataStr = BuildPostData(args);
                    string signature = ByteToString(_hmac.ComputeHash(Encoding.UTF8.GetBytes(dataStr.ToString()))).ToLower();

                    request.AddParameter("signature", signature);

                    var responseRaw = client.Execute(request).Content;
                    var response = JToken.Parse(responseRaw);

                    result = response != null && response.Value<int>("code") == 200;
                }
                catch (Exception ex)
                {
                    OnError(ex.Message);
                }
                finally
                {
                    Monitor.Exit(_lockObject);
                }
            return result;
        }

        public ScriptedOrderStatus GetOrderStatus(string orderId, IScriptMarket scriptMarket, decimal price, decimal amount, bool isBuyOrder)
        {
            var status = ScriptedOrderStatus.Unkown;

            try
            {
                var response = Query(true, "/trade/api/v1/getOrder?", new Dictionary<string, string>()
                {
                    { "accesskey", _publicKey },
                    { "id", orderId },
                    { "market", scriptMarket.PrimaryCurrency.ToLower() + "_" + scriptMarket.SecondaryCurrency.ToLower() },
                    { "nonce", CurrentTimeMillis().ToString()}
                });

                if (response != null)
                {
                    JObject o = response.Value<JObject>("data");
                    status = Order.GetStatusByXtStatusId(o.Value<int>("status"));
                }
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return status;
        }
        public IScriptOrder GetOrderDetails(string orderId, IScriptMarket market, decimal price, decimal amount, bool isBuyOrder)
        {
            // This might be called even when the order is in the open orders.
            // Make sure that the order is not open.

            Order order = null;

            try
            {
                var status = GetOrderStatus(orderId, market, price, amount, isBuyOrder);
                if (status != ScriptedOrderStatus.Completed && status != ScriptedOrderStatus.Cancelled)
                {
                    order = new Order();
                    order.Status = status;
                    return order;
                }

                Thread.Sleep(500);

                var history = GetTradeHistory();
                if (history == null)
                    return null;

                var trades = history
                    .Where(c => c.OrderId == orderId)
                    .ToList();

                order = new Order();
                GetOrderDetailsFromTrades(market, orderId, amount, order, trades);
            }
            catch (Exception e)
            {
                OnError(e.Message);
            }

            return order;
        }
        #endregion

        #region Helpers
        public decimal GetContractValue(IScriptMarket pair, decimal price)
        {
            return 1;
        }

        public decimal GetMaxPositionAmount(IScriptMarket pair, decimal tickClose, Dictionary<string, decimal> wallet, decimal leverage, ScriptedLeverageSide scriptedLeverageSide)
        {
            return 1;
        }


        private void GetOrderDetailsFromTrades(IScriptMarket market, string orderId, decimal amount, Order order, List<IScriptOrder> trades)
        {
            order.OrderId = orderId;
            order.Market = market;

            if (order.Status == ScriptedOrderStatus.Unkown)
                order.Status = trades.Sum(c => c.AmountFilled) >= amount
                    ? ScriptedOrderStatus.Completed
                    : ScriptedOrderStatus.Cancelled;

            order.Price = GetAveragePrice(trades.ToList());
            order.Amount = amount;
            order.AmountFilled = trades.Sum(c => c.AmountFilled);
            order.AmountFilled = Math.Min(order.Amount, order.AmountFilled);

            order.FeeCost = trades.Sum(c => c.FeeCost);

            if (!trades.Any())
                return;

            order.Timestamp = trades.First().Timestamp;
            order.FeeCurrency = trades[0].FeeCurrency;
            order.IsBuyOrder = trades[0].IsBuyOrder;
        }

        private decimal GetAveragePrice(List<IScriptOrder> trades)
        {
            var totalVolume = trades.Sum(c => c.AmountFilled * c.Price);
            if (totalVolume == 0 || trades.Sum(c => c.AmountFilled) == 0M)
                return 0M;

            return totalVolume / trades.Sum(c => c.AmountFilled);
        }
        #endregion

        #region Events
        private void OnError(string exMessage)
        {
            if (Error != null)
                Error(this, exMessage);
        }

        private void OnPriceUpdate(IScriptTick e)
        {
            if (PriceUpdate != null)
                PriceUpdate(this, e);
        }
        private void OnOrderbookUpdate(IScriptOrderbook e)
        {
            if (OrderbookUpdate != null)
                OrderbookUpdate(this, e);
        }
        private void OnOrderbookCorrection(IScriptOrderbook e)
        {
            if (OrderbookCorrection != null)
                OrderbookCorrection(this, e);
        }
        private void OnLastTradesUpdate(IScriptLastTrades e)
        {
            if (LastTradesUpdate != null)
                LastTradesUpdate(this, e);
        }

        private void OnWalletUpdate(Dictionary<string, decimal> e)
        {
            if (WalletUpdate != null)
                WalletUpdate(this, e);
        }
        private void OnWalletCorrection(Dictionary<string, decimal> e)
        {
            if (WalletCorrection != null)
                WalletCorrection(this, e);
        }

        private void OnOpenOrderListUpdate(List<IScriptOrder> e)
        {
            if (OpenOrderListUpdate != null)
                OpenOrderListUpdate(this, e);
        }
        private void OnOpenOrderCorrection(List<IScriptOrder> e)
        {
            if (OpenOrderCorrection != null)
                OpenOrderCorrection(this, e);
        }

        private void OnPositionListUpdate(List<IScriptPosition> e)
        {
            if (PositionListUpdate != null)
                PositionListUpdate(this, e);
        }
        private void OnPositionCorrection(List<IScriptPosition> e)
        {
            if (PositionCorrection != null)
                PositionCorrection(this, e);
        }
        #endregion

        #region Reset API Functions
        private JToken Query(bool authenticate, string methodName, Dictionary<string, string> args = null)
        {
            if (args == null)
                args = new Dictionary<string, string>();

            var dataStr = BuildPostData(args);

            if (Monitor.TryEnter(_lockObject, 30000))
                try
                {
                    if (authenticate)
                    {
                        // Add extra nonce-header
                        if (!args.ContainsKey("nonce"))
                        {
                            args.Add("nonce", CurrentTimeMillis().ToString());
                            dataStr = BuildPostData(args);
                        }
                        string signature = ByteToString(_hmac.ComputeHash(Encoding.UTF8.GetBytes(dataStr.ToString()))).ToLower();
                        args.Add("signature", signature);
                        dataStr = BuildPostData(args);
                    }

                    string url = _apiUrl + methodName + dataStr;

                    RestClient client = new RestClient(url);
                    RestRequest request = new RestRequest();

                    var response = client.Execute(request).Content;
                    return JToken.Parse(response);
                }
                catch (Exception ex)
                {
                    OnError(ex.Message);
                }
                finally
                {
                    Monitor.Exit(_lockObject);
                }
            return null;
        }

        private object BuildPostDataNoUrlEncode(Dictionary<string, string> args)
        {
            string s = "";
            for (int i = 0; i < args.Count; i++)
            {
                var item = args.ElementAt(i);
                var key = item.Key;
                var val = item.Value;

                s += key + "=" + val;

                if (i != args.Count - 1)
                    s += "&";
            }
            return s;
        }

        private static string BuildPostData(Dictionary<string, string> d)
        {
            string s = "";
            for (int i = 0; i < d.Count; i++)
            {
                var item = d.ElementAt(i);
                var key = item.Key;
                var val = item.Value;

                s += key + "=" + HttpUtility.UrlEncode(val);

                if (i != d.Count - 1)
                    s += "&";
            }
            return s;
        }

        private static readonly DateTime Jan1st1970 = new DateTime
            (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long CurrentTimeMillis()
        {
            return (long)(DateTime.UtcNow - Jan1st1970).TotalMilliseconds;
        }
        private Int64 GetNonce()
        {
            var temp = DateTime.UtcNow.Ticks;
            if (temp <= _lastNonce)
                temp = _lastNonce + 1;
            _lastNonce = temp;
            return _lastNonce;
        }

        private static string ByteToString(byte[] buff)
        {
            return buff.Aggregate("", (current, t) => current + t.ToString("X2"));
        }
        public static byte[] StringToByteArray(String hex)
        {
            var numberChars = hex.Length;
            var bytes = new byte[numberChars / 2];
            for (var i = 0; i < numberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        
        #endregion
    }

    public class Market : IScriptMarket
    {
        public string PrimaryCurrency { get; set; }
        public string SecondaryCurrency { get; set; }
        public decimal Fee { get; set; }
        public int PriceDecimals { get; set; }
        public int AmountDecimals { get; set; }
        public decimal MinimumTradeAmount { get; set; }
        public decimal MinimumTradeVolume { get; set; }

        // Not relevant for spot TODO: Implement if not spot
        public DateTime SettlementDate { get; set; }
        public List<decimal> Leverage { get; set; }
        public string UnderlyingCurrency { get; set; }
        public string ContractName { get; set; }

        public Market(string primaryCurrency, string secondaryCurrency, string minAmount,
                string minMoney, string pricePoint, string coinPoint)
        {
            try
            {
                SettlementDate = DateTime.Now;
                Leverage = new List<decimal>();
                ContractName = "";

                PrimaryCurrency = primaryCurrency;
                SecondaryCurrency = secondaryCurrency;
                UnderlyingCurrency = PrimaryCurrency;

                MinimumTradeVolume = Decimal.Parse(minMoney, NumberStyles.Float, CultureInfo.InvariantCulture); //0.0005M;
                MinimumTradeAmount = Decimal.Parse(minAmount, NumberStyles.Float, CultureInfo.InvariantCulture);

                Fee = 0.0M; //0.25M;

                PriceDecimals = int.Parse(pricePoint, CultureInfo.InvariantCulture); //8;
                if (PriceDecimals == 0.0M)
                    PriceDecimals = BitConverter.GetBytes(decimal.GetBits(MinimumTradeVolume)[3])[2];

                AmountDecimals = int.Parse(coinPoint, CultureInfo.InvariantCulture);
                if (AmountDecimals == 0.0M)
                    AmountDecimals = BitConverter.GetBytes(decimal.GetBits(MinimumTradeAmount)[3])[2]; ;
            }
            catch (Exception ex)
            {
                Console.Write(ex);
                throw;
            }
        }

        public Market(string primaryCurrency, string secondaryCurrency)
        {
            PrimaryCurrency = primaryCurrency;
            SecondaryCurrency = secondaryCurrency;
        }

        public virtual decimal ParsePrice(decimal price)
        {
            return Math.Round(price, PriceDecimals);
        }

        public virtual decimal ParseAmount(decimal price)
        {
            return Math.Round(price, AmountDecimals);
        }

        public virtual int GetPriceDecimals(decimal price)
        {
            return PriceDecimals;
        }

        public virtual int GetAmountDecimals(decimal price)
        {
            return AmountDecimals;
        }

        public bool IsAmountEnough(decimal price, decimal amount)
        {
            return amount < MinimumTradeAmount && amount * price >= MinimumTradeVolume;
        }
    }

    public class Ticker : IScriptTick
    {
        public IScriptMarket Market { get; set; }
        public decimal Close { get; set; }
        public decimal BuyPrice { get; set; }
        public decimal SellPrice { get; set; }

        public Ticker(decimal close, decimal buyPrice, decimal sellPrice,
                string primairy = "", string secondairy = "")
        {
            Market = new Market(primairy, secondairy);

            Close = close;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
        }
    }

    public class Orderbook : IScriptOrderbook
    {
        public List<IScriptOrderbookRecord> Asks { get; set; }
        public List<IScriptOrderbookRecord> Bids { get; set; }

        public Orderbook(JArray bids, JArray asks)
        {
            List<IScriptOrderbookRecord> bidList = new List<IScriptOrderbookRecord>();
            List<IScriptOrderbookRecord> askList = new List<IScriptOrderbookRecord>();

            if (bids != null)
                foreach (var b in bids)
                {
                    decimal price = 0.0M, amount = 0.0M;
                    JArray values = b.Value<JArray>();
                    string sPrice = values[0].Value<string>();
                    string sAmount = values[1].Value<string>();

                    if (sPrice != null)
                        price = Decimal.Parse(sPrice, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (sAmount != null)
                        amount = Decimal.Parse(sAmount, NumberStyles.Float, CultureInfo.InvariantCulture);

                    bidList.Add(new OrderInfo(price, amount));
                }
            Bids = bidList;

            if (asks != null)
                foreach (var a in asks)
                {
                    decimal price = 0.0M, amount = 0.0M;
                    JArray values = a.Value<JArray>();
                    string sPrice = values[0].Value<string>();
                    string sAmount = values[1].Value<string>();

                    if (sPrice != null)
                        price = Decimal.Parse(sPrice, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (sAmount != null)
                        amount = Decimal.Parse(sAmount, NumberStyles.Float, CultureInfo.InvariantCulture);

                    askList.Add(new OrderInfo(price, amount));
                }
            Asks = askList;
        }
    }

    public class OrderInfo : IScriptOrderbookRecord
    {
        public decimal Price { get; set; }
        public decimal Amount { get; set; }

        public OrderInfo(decimal price, decimal amount)
        {
            Price = price;
            Amount = amount;
        }
    }

    public class LastTradesContainer : IScriptLastTrades
    {
        public IScriptMarket Market { get; set; }
        public List<IScriptOrder> Trades { get; set; }
    }

    public class Order : IScriptOrder
    {
        public IScriptMarket Market { get; set; }
        public string OrderId { get; set; }     // id
        public string ExecutingId { get; set; } 
        public DateTime Timestamp { get; set; } // time
        public decimal Price { get; set; }      // price
        public decimal Amount { get; set; }     //number
        public decimal AmountFilled { get; set; }   // completeNumber
        public decimal FeeCost { get; set; }        // fee
        public string FeeCurrency { get; set; }     
        public bool IsBuyOrder { get; set; }        // type
        public ScriptedLeverageOrderType Direction { get; set; }
        public ScriptedOrderStatus Status { get; set; } // status
        public string ExtraInfo1 { get; set; }

        public Order()
        {
            Status = ScriptedOrderStatus.Unkown;
        }

        public static Order ParseOpenOrder(JObject o, string market)
        {
            if (o == null)
                return null;

            var r = new Order();
            r.OrderId = o.Value<string>("id");
            r.Price = Convert.ToDecimal(o.Value<string>("price"), CultureInfo.InvariantCulture);
            r.Amount = Convert.ToDecimal(o.Value<string>("number"), CultureInfo.InvariantCulture);
            r.AmountFilled = Convert.ToDecimal(o.Value<string>("completeNumber"), CultureInfo.InvariantCulture);
            r.Status = GetStatusByXtStatusId(Convert.ToInt32(o.Value<string>("status")));
            r.IsBuyOrder = Convert.ToInt32(o.Value<string>("type")) == 1;

            int index = market.IndexOf("_");
            string primaryCurrency = market.Substring(0, index);
            string secondaryCurrency = market.Substring(index + 1, market.Length - primaryCurrency.Length - 1);

            r.Market = new Market(primaryCurrency, secondaryCurrency);

            return r;
        }

        public static ScriptedOrderStatus GetStatusByXtStatusId(int id)
        {
            switch (id)
            {
                default:
                case 0: // submision not matched
                    return ScriptedOrderStatus.Unkown;
                case 1: // unsettled or partially completed
                    return ScriptedOrderStatus.Executing;
                case 2: // completed
                    return ScriptedOrderStatus.Completed;
                case 3: // cancelled
                    return ScriptedOrderStatus.Cancelled;
                case 4: // matched but in settlement
                    return ScriptedOrderStatus.Executing;
            }
        }

        //TODO: Delete those two methods?

        public static Order ParseOpenOrder(JObject o)
        {
            if (o == null)
                return null;

            string[] pair = o.Value<string>("Exchange").Split('-');

            var r = new Order()
            {
                Market = new Market(pair[1], pair[0]),

                OrderId = o.Value<string>("OrderUuid"),

                Price = Convert.ToDecimal(o.Value<string>("Limit"), CultureInfo.InvariantCulture),
                Amount = Convert.ToDecimal(o.Value<string>("Quantity"), CultureInfo.InvariantCulture),
                AmountFilled = Convert.ToDecimal(o.Value<string>("Quantity"), CultureInfo.InvariantCulture) -
                               Convert.ToDecimal(o.Value<string>("QuantityRemaining"), CultureInfo.InvariantCulture),

                Status = ScriptedOrderStatus.Executing
            };

            if (o.Value<string>("OrderType") != null)
                r.IsBuyOrder = o.Value<string>("OrderType").ToLower() == "limit_buy";


            return r;
        }

        public static Order ParseSingle(JObject o)
        {
            if (o == null)
                return null;

            Order order = ParseOpenOrder(o);
            order.IsBuyOrder = o.Value<string>("Type").IndexOf("limit_buy", StringComparison.Ordinal) > 0;

            if (Convert.ToDecimal(o.Value<string>("QuantityRemaining"), CultureInfo.InvariantCulture) == 0.0M)
                order.Status = ScriptedOrderStatus.Completed;

            else if (!o.Value<bool>("IsOpen"))
                order.Status = ScriptedOrderStatus.Cancelled;

            else if (o.Value<bool>("IsOpen"))
                order.Status = ScriptedOrderStatus.Executing;

            if (o.Property("CancelInitiated") != null && o.Value<bool>("CancelInitiated"))
                order.Status = ScriptedOrderStatus.Cancelled;

            return order;
        }
    }

    public class Trade : IScriptOrder
    {
        public IScriptMarket Market { get; set; }
        public string OrderId { get; set; }
        public string ExecutingId { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountFilled { get; set; }
        public decimal FeeCost { get; set; }
        public string FeeCurrency { get; set; }
        public bool IsBuyOrder { get; set; }
        public ScriptedLeverageOrderType Direction { get; set; }
        public ScriptedOrderStatus Status { get; set; }

        public static Trade ParsePrivateTrade(JObject o, string market)
        {
            if (o == null)
                return null;

            int index = market.IndexOf("_");
            string primaryCurrency = market.Substring(0, index);
            string secondaryCurrency = market.Substring(index + 1, market.Length - primaryCurrency.Length - 1);

            var r = new Trade();
            r.Market = new Market(primaryCurrency, secondaryCurrency);
            r.OrderId = o.Value<string>("id");
            r.Timestamp = UnixTimeStampToDateTime
                (
                    double.Parse(o.Value<string>("time"), NumberStyles.Float, CultureInfo.InvariantCulture)
                );
            r.Price = Decimal.Parse(o.Value<string>("price"), NumberStyles.Float, CultureInfo.InvariantCulture);
            r.Amount = Decimal.Parse(o.Value<string>("amount"), NumberStyles.Float, CultureInfo.InvariantCulture);
            r.AmountFilled = Decimal.Parse(o.Value<string>("amount"), NumberStyles.Float, CultureInfo.InvariantCulture);    //TODO ??? Isn't this suppossed to be filled?
            r.FeeCurrency = r.Market.PrimaryCurrency;
            r.FeeCost = Decimal.Parse(o.Value<string>("fee"), NumberStyles.Float, CultureInfo.InvariantCulture);
            r.IsBuyOrder = o.Value<string>("type") == "1";

            return r;
        }

        public static Trade ParsePublicTrade(IScriptMarket market, JArray arr)
        {
            if (arr == null)
                return null;

            Trade r = new Trade();
            r.Market = market;
            r.Timestamp = UnixTimeStampToDateTime
                (
                    double.Parse(arr[0].Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture)
                );

            r.Price = Decimal.Parse(arr[1].Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture);
            r.Amount = Decimal.Parse(arr[2].Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture);
            r.IsBuyOrder = arr[3].Value<string>().ToLower() == "ask";

            return r;
        }
        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddMilliseconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }
    }

}