using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MachinaTrader.Models;
using ExchangeSharp;
using Mynt.Core.Enums;
using Newtonsoft.Json.Linq;

namespace MachinaTrader.Controllers
{

    [Route("api/mynt/")]
    public class MyntApiController : Controller
    {
        [HttpGet]
        [Route("trading/GetExchangePairsExchangeSymbols")]
        public async Task<ActionResult> Get(string exchange)
        {
            JArray symbolArray = new JArray();
            IExchangeAPI api = ExchangeAPI.GetExchangeAPI(exchange.ToLower());
            var exchangeCoins = await api.GetSymbolsAsync();
            foreach (var coin in exchangeCoins)
            {
                symbolArray.Add(coin);
            }
            return new JsonResult(symbolArray);
        }

        [HttpGet]
        [Route("trading/GetBalance")]
        public async Task<IActionResult> GetBalance()
        {
            var fullApi = Runtime.GlobalExchangeApi.GetFullApi().Result;
            var balance = await fullApi.GetAmountsAvailableToTradeAsync();
            return new JsonResult(balance);
        }

        [HttpGet]
        [Route("trading/GetHistory")]
        public async Task<IActionResult> GetHistory()
        {
            var fullApi = Runtime.GlobalExchangeApi.GetFullApi().Result;
            var balance = await fullApi.GetCompletedOrderDetailsAsync("ETHBTC");
            return new JsonResult(balance);
        }

        [HttpGet]
        [Route("trading/Trade/{tradeId}")]
        public async Task<IActionResult> TradingTrade(string tradeId)
        {
            var activeTrade = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            var trade = activeTrade.FirstOrDefault(x => x.TradeId == tradeId);
            if (trade == null)
            {
                var closedTrades = await Runtime.GlobalDataStore.GetClosedTradesAsync();
                trade = closedTrades.FirstOrDefault(x => x.TradeId == tradeId);
            }

            return new JsonResult(trade);
        }

        [HttpGet]
        [Route("trading/GetWebSocketValues")]
        public IActionResult GetWebSocketValues()
        {
            return new JsonResult(Runtime.WebSocketTickers);

        }

        [HttpGet]
        [Route("trading/SellNow/{tradeId}")]
        public async Task TradingSellNow(string tradeId)
        {
            var activeTrade = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            var trade = activeTrade.FirstOrDefault(x => x.TradeId == tradeId);

            if (trade == null)
            {
                return;
            }

            var orderId = Runtime.Configuration.TradeOptions.PaperTrade ? Guid.NewGuid().ToString().Replace("-", "") : await Runtime.GlobalExchangeApi.Sell(trade.Market, trade.Quantity, trade.TickerLast.Bid);
            trade.CloseRate = trade.TickerLast.Bid;
            trade.OpenOrderId = orderId;
            trade.SellOrderId = orderId;
            trade.SellType = SellType.Manually;
            trade.IsSelling = true;

            await Runtime.GlobalDataStore.SaveTradeAsync(trade);
            await Runtime.GlobalHubMyntTraders.Clients.All.SendAsync("Send", "Set " + tradeId + " to SellNow");
        }

        [HttpGet]
        [Route("trading/CancelOrder/{tradeId}")]
        public async Task TradingCancelOrder(string tradeId)
        {
            var activeTrade = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            var trade = activeTrade.FirstOrDefault(x => x.TradeId == tradeId);

            if (trade == null)
            {
                return;
            }

            if (trade.IsBuying)
            {
                await Runtime.GlobalExchangeApi.CancelOrder(trade.BuyOrderId, trade.Market);
                trade.IsBuying = false;
                trade.OpenOrderId = null;
                trade.IsOpen = false;
                trade.SellType = SellType.Cancelled;
                trade.CloseDate = DateTime.UtcNow;
                await Runtime.GlobalDataStore.SaveTradeAsync(trade);
            }

            if (trade.IsSelling)
            {
                //Reenable in active trades
                await Runtime.GlobalExchangeApi.CancelOrder(trade.SellOrderId, trade.Market);
                trade.IsSelling = false;
                trade.OpenOrderId = null;
                //trade.IsOpen = false;
                //trade.SellType = SellType.Cancelled;
                //trade.CloseDate = DateTime.UtcNow;
                await Runtime.GlobalDataStore.SaveTradeAsync(trade);
            }

            await Runtime.GlobalHubMyntTraders.Clients.All.SendAsync("Send", "Set " + tradeId + " to SellNow");
        }


        [HttpGet]
        [Route("trading/Hold/{tradeId}/{holdBoolean}")]
        public async Task TradingHold(string tradeId, bool holdBoolean)
        {
            var activeTrades = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            var tradeToUpdate = activeTrades.FirstOrDefault(x => x.TradeId == tradeId);
            if (tradeToUpdate != null)
            {
                tradeToUpdate.SellNow = false;
                tradeToUpdate.HoldPosition = holdBoolean;
                await Runtime.GlobalDataStore.SaveTradeAsync(tradeToUpdate);
            }

            await Runtime.GlobalHubMyntTraders.Clients.All.SendAsync("Send", "Set " + tradeId + " to Hold");
        }

        [HttpGet]
        [Route("trading/SellOnProfit/{tradeId}/{profitPercentage}")]
        public async Task TradingSellOnProfit(string tradeId, decimal profitPercentage)
        {
            var activeTrades = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            var tradeToUpdate = activeTrades.FirstOrDefault(x => x.TradeId == tradeId);
            if (tradeToUpdate != null)
            {
                tradeToUpdate.SellNow = false;
                tradeToUpdate.HoldPosition = false;
                tradeToUpdate.SellOnPercentage = profitPercentage;

                await Runtime.GlobalDataStore.SaveTradeAsync(tradeToUpdate);
            }

            await Runtime.GlobalHubMyntTraders.Clients.All.SendAsync("Send", "Set " + tradeId + " to Hold");
        }

        [HttpGet]
        [Route("trading/TradersTester")]
        public IActionResult MyntTradersTester()
        {
            JObject testJson = JObject.Parse(System.IO.File.ReadAllText("wwwroot/views/mynt_traders.json"));
            return new JsonResult(testJson);
        }

        [HttpGet]
        [Route("trading/Traders")]
        public async Task<IActionResult> Traders()
        {
            var traders = await Runtime.GlobalDataStore.GetTradersAsync();
            return new JsonResult(traders);
        }

        [HttpGet]
        [Route("trading/ActiveTradesWithTrader")]
        public async Task<IActionResult> GetActiveTradesWithTrader()
        {
            // Get trades
            var activeTrades = await Runtime.GlobalDataStore.GetActiveTradesAsync();

            JObject activeTradesJson = new JObject();

            // Get information for active trade
            foreach (var activeTrade in activeTrades)
            {
                activeTradesJson[activeTrade.TraderId] = JObject.FromObject(activeTrade);
            }

            return new JsonResult(activeTradesJson);
        }

        [HttpGet]
        [Route("trading/ActiveTrades")]
        public async Task<IActionResult> GetActiveTrades()
        {
            // Get trades
            var activeTrades = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            return new JsonResult(activeTrades);
        }

        [HttpGet]
        [Route("trading/OpenTrades")]
        public async Task<IActionResult> GetOpenTrades()
        {
            // Get trades
            var activeTrades = await Runtime.GlobalDataStore.GetActiveTradesAsync();
            return new JsonResult(activeTrades.Where(x => x.IsSelling || x.IsBuying));
        }

        [HttpGet]
        [Route("trading/ClosedTrades")]
        public async Task<IActionResult> GetClosedTrades()
        {
            // Get trades
            var closedTrades = await Runtime.GlobalDataStore.GetClosedTradesAsync();
            return new JsonResult(closedTrades);
        }

        [HttpGet]
        [Route("trading/statistics")]
        public async Task<IActionResult> Statistics()
        {
            // Create Statistic model
            var stat = new Statistics();

            // Get winner/loser currencies
            var coins = new Dictionary<string, decimal?>();
            foreach (var cT in await Runtime.GlobalDataStore.GetClosedTradesAsync())
            {
                if (cT.SellOrderId != null)
                {
                    // Get profit per currency
                    if (coins.ContainsKey(cT.Market))
                        coins[cT.Market] = coins[cT.Market].Value + cT.CloseProfitPercentage;
                    else
                        coins.Add(cT.Market, cT.CloseProfitPercentage);

                    // Profit-loss
                    if (cT.CloseProfit != null) stat.ProfitLoss = stat.ProfitLoss + cT.CloseProfit.Value;
                    if (cT.CloseProfitPercentage != null)
                        stat.ProfitLossPercentage = stat.ProfitLossPercentage + cT.CloseProfitPercentage.Value;
                }

            }

            // Coin performance
            stat.CoinPerformance = coins.ToList().OrderByDescending(c => c.Value);

            // Create some viewbags
            ViewBag.tradeOptions = Runtime.Configuration.TradeOptions;
            ViewBag.stat = stat;

            return new JsonResult(ViewBag);
        }

        /// <summary>
        /// Logs
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("logs")]
        public IActionResult Logs()
        {
            var log = Log.ReadTail("Logs/Mynt-" + DateTime.Now.ToString("yyyyMMdd") + ".log", 500);
            return new JsonResult(log);
        }
    }
}