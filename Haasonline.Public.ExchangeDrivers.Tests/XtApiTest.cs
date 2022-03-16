using System;
using Haasonline.Public.ExchangeDriver.Xt;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TradeServer.ScriptingDriver.ScriptApi.DataObjects;
using TradeServer.ScriptingDriver.ScriptApi.Interfaces;

namespace Haasonline.Public.ExchangeDriver.Tests
{
    [TestClass]
    public sealed class XtApiTest : TestBase
    {
        protected override IScriptApi Api { get; set; }

        protected override string PublicKey { get; set; }
        protected override string PrivateKey { get; set; }
        protected override string ExtraKey { get; set; }

        protected override IScriptMarket Market { get; set; }

        protected override bool AllowZeroPriceDecimals { get; set; } = false;
        protected override bool AllowZeroAmountDecimals { get; set; } = true;
        protected override bool AllowZeroFee { get; set; } = false;

        public XtApiTest() : base()
        {
            PublicKey = "d9039d04-536c-4a34-96c0-3013e8a2424e";
            PrivateKey = "80f4225b555fd3a97a1d5dab5a9f45f2aa151886";
            ExtraKey = "";

            Api = new XtApi();

            Api.SetCredentials(PublicKey, PrivateKey, ExtraKey);

            Market = new ScriptMarket("bnb", "usdt", "");

            Start();
        }
    }
}
